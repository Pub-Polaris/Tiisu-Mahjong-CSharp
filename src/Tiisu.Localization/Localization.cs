using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tiisu.Localization;

/// <summary>
/// 共享语言目录。语言文件是嵌入资源：lang/zh-CN.json / en-US.json / ja-JP.json。
/// 既可读嵌套 JSON（ui.menu.play），也兼容旧的中文 key（七子麻将）。
/// </summary>
public static class L
{
    public const string DefaultLanguage = "zh-CN";
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new(StringComparer.Ordinal);
    private static readonly Regex Placeholder = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);
    private static string _language = DefaultLanguage;

    public static string Language => _language;
    public static event Action? LanguageChanged;

    public static IReadOnlyList<string> SupportedLanguages { get; } = new[] { "zh-CN", "en-US", "ja-JP" };

    public static string Normalize(string? language) =>
        language is not null && language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US"
        : language is not null && (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                                   || language.StartsWith("jp", StringComparison.OrdinalIgnoreCase)) ? "ja-JP"
        : DefaultLanguage;

    public static void SetLanguage(string? language)
    {
        string next = Normalize(language);
        bool changed = !string.Equals(_language, next, StringComparison.Ordinal);
        _language = next;
        EnsureCatalog(DefaultLanguage);
        EnsureCatalog(next);
        if (changed)
        {
            LanguageChanged?.Invoke();
        }
    }

    public static bool HasKey(string key) =>
        !string.IsNullOrWhiteSpace(key)
        && (Catalog(_language).ContainsKey(key) || Catalog(DefaultLanguage).ContainsKey(key));

    public static string T(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (Catalog(_language).TryGetValue(key, out string? value)
            || Catalog(DefaultLanguage).TryGetValue(key, out value))
        {
            return value;
        }

        return key;
    }

    public static string T(string key, params (string Name, object? Value)[] args)
    {
        string value = T(key);
        if (args.Length == 0 || value.Length == 0)
        {
            return value;
        }

        var map = args
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .ToDictionary(a => a.Name, a => a.Value, StringComparer.Ordinal);
        return Placeholder.Replace(value, m =>
        {
            if (!map.TryGetValue(m.Groups[1].Value, out object? arg))
            {
                return m.Value;
            }

            return arg switch
            {
                null => string.Empty,
                IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
                _ => arg.ToString() ?? string.Empty,
            };
        });
    }

    private static Dictionary<string, string> Catalog(string language) =>
        Catalogs.TryGetValue(language, out var catalog) ? catalog : EmptyCatalog;

    private static readonly Dictionary<string, string> EmptyCatalog = new(StringComparer.Ordinal);

    private static void EnsureCatalog(string language)
    {
        if (Catalogs.ContainsKey(language))
        {
            return;
        }

        var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
        string resource = $"Tiisu.Localization.lang.{language}.json";
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(stream);
                Flatten(doc.RootElement, string.Empty, catalog);
            }
            catch (JsonException)
            {
                // 损坏的语言包不能影响游戏；保留空目录并回退 zh-CN。
            }
        }

        Catalogs[language] = catalog;
    }

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> target)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string key = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
                Flatten(property.Value, key, target);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.String && prefix.Length > 0)
        {
            target[prefix] = element.GetString() ?? string.Empty;
        }
    }
}
