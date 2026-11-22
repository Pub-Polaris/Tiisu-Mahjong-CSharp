using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;

namespace Tiisu.App.Runtime;

/// <summary>
/// 本机玩家档案。根目录仍是 <c>%APPDATA%\TiisuMahjong</c>；每个档案在
/// <c>profiles\&lt;guid&gt;\</c> 下拥有独立的 settings / stats / state / logs / layout。
/// <para>
/// 同一时刻只有一个 active 档案；<c>profiles.json</c> 是索引缓存，每档案内的
/// <c>profile.json</c> 才是权威，索引损坏时可以扫描重建。
/// </para>
/// </summary>
public sealed class PlayerProfileStore
{
    public const string IndexFileName = "profiles.json";
    public const string ProfileFileName = "profile.json";
    public const string ProfilesDirectoryName = "profiles";
    public const int IndexVersion = 1;
    public const int MaxNameLength = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex GuidPattern = new("^[0-9a-fA-F]{32}$", RegexOptions.Compiled);

    private readonly object _gate = new();
    private readonly List<PlayerProfile> _profiles = new();
    private readonly string _root;
    private readonly string _profilesRoot;
    private readonly string _trashRoot;
    private string _activeId = string.Empty;

    private PlayerProfileStore(string root)
    {
        _root = Path.GetFullPath(root);
        _profilesRoot = Path.Combine(_root, ProfilesDirectoryName);
        _trashRoot = Path.Combine(_profilesRoot, "_trash");
    }

    public string RootDirectory => _root;
    public string ProfilesDirectory => _profilesRoot;
    public string IndexPath => Path.Combine(_root, IndexFileName);

    public IReadOnlyList<PlayerProfile> Profiles
    {
        get
        {
            lock (_gate)
            {
                return _profiles.ToArray();
            }
        }
    }

    public PlayerProfile Active
    {
        get
        {
            lock (_gate)
            {
                return _profiles.FirstOrDefault(p => p.Id == _activeId) ?? _profiles[0];
            }
        }
    }

    /// <summary>读取索引；索引缺失/损坏时扫描目录；再不行建默认档案（并迁移旧平铺数据）。</summary>
    public static PlayerProfileStore LoadOrCreate(string? root = null)
    {
        var store = new PlayerProfileStore(root ?? AppData.Root);
        store.Initialize();
        return store;
    }

    private void Initialize()
    {
        Directory.CreateDirectory(_profilesRoot);

        List<PlayerProfile>? fromIndex = TryReadIndex(out string? active);
        List<PlayerProfile> fromDisk = ScanProfiles();

        var merged = new List<PlayerProfile>();
        foreach (PlayerProfile profile in fromIndex ?? new List<PlayerProfile>())
        {
            if (Directory.Exists(profile.Directory))
            {
                merged.Add(profile);
            }
        }

        foreach (PlayerProfile profile in fromDisk)
        {
            if (merged.All(p => p.Id != profile.Id))
            {
                merged.Add(profile);
            }
        }

        if (merged.Count == 0 && HasLegacyData())
        {
            PlayerProfile migrated = CreateLegacyProfile();
            merged.Add(migrated);
            active = migrated.Id;
        }

        if (merged.Count == 0)
        {
            PlayerProfile created = CreateDefaultProfile();
            merged.Add(created);
            active = created.Id;
        }

        lock (_gate)
        {
            _profiles.Clear();
            _profiles.AddRange(merged.OrderBy(p => p.CreatedAt).ThenBy(p => p.DisplayName, StringComparer.CurrentCulture));
            _activeId = !string.IsNullOrWhiteSpace(active) && _profiles.Any(p => p.Id == active)
                ? active!
                : _profiles[0].Id;
        }

        SaveIndex();
    }

    /// <summary>重新读索引与目录（档案页进入时调用，吸收其它实例的改动）。</summary>
    public void Reload()
    {
        List<PlayerProfile>? fromIndex = TryReadIndex(out string? active);
        List<PlayerProfile> fromDisk = ScanProfiles();
        var merged = new List<PlayerProfile>();
        foreach (PlayerProfile profile in fromIndex ?? new List<PlayerProfile>())
        {
            if (Directory.Exists(profile.Directory))
            {
                merged.Add(profile);
            }
        }

        foreach (PlayerProfile profile in fromDisk)
        {
            if (merged.All(p => p.Id != profile.Id))
            {
                merged.Add(profile);
            }
        }

        if (merged.Count == 0)
        {
            merged.Add(CreateDefaultProfile());
        }

        lock (_gate)
        {
            _profiles.Clear();
            _profiles.AddRange(merged.OrderBy(p => p.CreatedAt).ThenBy(p => p.DisplayName, StringComparer.CurrentCulture));
            if (string.IsNullOrWhiteSpace(active) || _profiles.All(p => p.Id != active))
            {
                active = _profiles[0].Id;
            }

            _activeId = active!;
        }

        SaveIndex();
    }

    public PlayerProfile? Find(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return null;
        }

        string key = idOrName.Trim();
        lock (_gate)
        {
            PlayerProfile? byId = _profiles.FirstOrDefault(p =>
                string.Equals(p.Id, key, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }

            return _profiles.FirstOrDefault(p =>
                string.Equals(p.DisplayName, key, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    public PlayerProfile? FindBySteamId(string? steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return null;
        }

        lock (_gate)
        {
            return _profiles.FirstOrDefault(p =>
                string.Equals(p.SteamId, steamId.Trim(), StringComparison.Ordinal));
        }
    }

    /// <summary>启动参数 <c>--profile</c>：命中 id/名字就用；没有则新建（不抢当前 active）。</summary>
    public PlayerProfile ResolveLaunchProfile(string? nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            return Active;
        }

        PlayerProfile? found = Find(nameOrId);
        if (found is not null)
        {
            return found;
        }

        return Create(nameOrId, activate: false);
    }

    public PlayerProfile Create(string? name = null, bool activate = true, Settings? seedSettings = null)
    {
        return WithProfileLock(() =>
        {
            string normalized = NormalizeName(name, _profiles.Count + 1);
            string id = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(_profilesRoot, id);
            Directory.CreateDirectory(directory);

            Settings seed = seedSettings?.Clone() ?? LoadSeedSettings();
            seed.Save(directory);

            var profile = new PlayerProfile(id, normalized, directory, DateTimeOffset.Now);
            WriteProfileFile(profile);

            lock (_gate)
            {
                _profiles.Add(profile);
                if (activate || string.IsNullOrWhiteSpace(_activeId))
                {
                    _activeId = id;
                }
            }

            SaveIndex();
            return profile;
        });
    }

    public PlayerProfile Rename(string id, string? name)
    {
        return WithProfileLock(() =>
        {
            PlayerProfile profile = Require(id);
            string normalized = NormalizeName(name, _profiles.Count + 1);
            PlayerProfile updated = profile with { DisplayName = normalized };
            WriteProfileFile(updated);

            lock (_gate)
            {
                int index = _profiles.FindIndex(p => p.Id == id);
                if (index >= 0)
                {
                    _profiles[index] = updated;
                }
            }

            SaveIndex();
            return updated;
        });
    }

    public PlayerProfile SetActive(string id)
    {
        return WithProfileLock(() =>
        {
            PlayerProfile profile = Require(id);
            lock (_gate)
            {
                _activeId = profile.Id;
            }

            SaveIndex();
            return profile;
        });
    }

    /// <summary>删除非当前档案；目录移到 <c>profiles\_trash\</c>，可人工恢复。</summary>
    public void Delete(string id)
    {
        WithProfileLock(() =>
        {
            PlayerProfile profile = Require(id);
            lock (_gate)
            {
                if (_profiles.Count <= 1)
                {
                    throw new InvalidOperationException("至少保留一个本地玩家档案");
                }

                if (string.Equals(_activeId, id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("不能删除当前玩家档案");
                }
            }

            Directory.CreateDirectory(_trashRoot);
            string target = Path.Combine(_trashRoot, profile.Id + "-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            if (Directory.Exists(profile.Directory))
            {
                Directory.Move(profile.Directory, target);
            }

            lock (_gate)
            {
                _profiles.RemoveAll(p => p.Id == id);
            }

            SaveIndex();
            return true;
        });
    }

    /// <summary>未来 Steam 登录时用：同 SteamID 复用，没有则建 Steam 来源档案。</summary>
    public PlayerProfile EnsureSteamProfile(string steamId, string? displayName, bool activate = false)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            throw new ArgumentException("steamId is required", nameof(steamId));
        }

        return WithProfileLock(() =>
        {
            PlayerProfile? existing = FindBySteamId(steamId);
            if (existing is not null)
            {
                return existing;
            }

            string normalized = NormalizeName(displayName, _profiles.Count + 1);
            string id = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(_profilesRoot, id);
            Directory.CreateDirectory(directory);

            Settings seed = LoadSeedSettings();
            seed.Save(directory);
            var profile = new PlayerProfile(id, normalized, directory, DateTimeOffset.Now, steamId.Trim(), ProfileSource.Steam);
            WriteProfileFile(profile);

            lock (_gate)
            {
                _profiles.Add(profile);
                if (activate)
                {
                    _activeId = id;
                }
            }

            SaveIndex();
            return profile;
        });
    }

    /// <summary>名字归一化：Trim、压缩空白、去控制字符、截断；空名回退。</summary>
    public static string NormalizeName(string? name, int fallbackIndex = 1)
    {
        string value = (name ?? string.Empty).Trim();
        if (value.Length > 0)
        {
            value = Whitespace.Replace(value, " ");
            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (!char.IsControl(c))
                {
                    builder.Append(c);
                }
            }

            value = builder.ToString().Trim();
        }

        if (value.Length == 0)
        {
            value = "玩家 " + Math.Max(1, fallbackIndex).ToString(CultureInfo.InvariantCulture);
        }

        return value.Length <= MaxNameLength ? value : value[..MaxNameLength];
    }

    private Settings LoadSeedSettings()
    {
        try
        {
            PlayerProfile active = Active;
            return Settings.Load(active.Directory).Clone();
        }
        catch
        {
            return new Settings();
        }
    }

    private PlayerProfile Require(string id)
    {
        PlayerProfile? profile;
        lock (_gate)
        {
            profile = _profiles.FirstOrDefault(p => p.Id == id);
        }

        return profile ?? throw new InvalidOperationException("找不到玩家档案：" + id);
    }

    private bool HasLegacyData() =>
        File.Exists(Path.Combine(_root, "settings.json"))
        || File.Exists(Path.Combine(_root, "stats.json"))
        || File.Exists(Path.Combine(_root, "state.json"))
        || File.Exists(Path.Combine(_root, "layout-custom.json"))
        || Directory.Exists(Path.Combine(_root, "logs"));

    private PlayerProfile CreateLegacyProfile()
    {
        string name = NormalizeName(LegacyPlayerName() ?? Environment.UserName, 1);
        string id = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(_profilesRoot, id);
        Directory.CreateDirectory(directory);

        CopyIfExists("settings.json");
        CopyIfExists("stats.json");
        CopyIfExists("state.json");
        CopyIfExists("layout-custom.json");
        CopyIfExists("steam-pending.json");
        CopyLogs();

        void CopyIfExists(string fileName)
        {
            string source = Path.Combine(_root, fileName);
            if (!File.Exists(source))
            {
                return;
            }

            try
            {
                File.Copy(source, Path.Combine(directory, fileName), overwrite: true);
            }
            catch
            {
                // 单个旧文件复制失败不阻断迁移。
            }
        }

        void CopyLogs()
        {
            string source = Path.Combine(_root, "logs");
            if (!Directory.Exists(source))
            {
                return;
            }

            try
            {
                string target = Path.Combine(directory, "logs");
                Directory.CreateDirectory(target);
                foreach (string file in Directory.EnumerateFiles(source))
                {
                    try
                    {
                        File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
                    }
                    catch
                    {
                        // 跳过坏文件。
                    }
                }
            }
            catch
            {
                // 日志目录整体复制失败时保留已有档案。
            }
        }

        if (!File.Exists(Path.Combine(directory, "settings.json")))
        {
            new Settings().Save(directory);
        }

        var profile = new PlayerProfile(id, name, directory, DateTimeOffset.Now);
        WriteProfileFile(profile);
        return profile;
    }

    private string? LegacyPlayerName()
    {
        string path = Path.Combine(_root, "settings.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (document.RootElement.TryGetProperty("playerName", out JsonElement name)
                && name.ValueKind == JsonValueKind.String)
            {
                return name.GetString();
            }
        }
        catch
        {
            // 迁移读不到名字就用系统用户名。
        }

        return null;
    }

    private PlayerProfile CreateDefaultProfile()
    {
        string name = NormalizeName(Environment.UserName, 1);
        string id = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(_profilesRoot, id);
        Directory.CreateDirectory(directory);
        new Settings().Save(directory);
        var profile = new PlayerProfile(id, name, directory, DateTimeOffset.Now);
        WriteProfileFile(profile);
        return profile;
    }

    private List<PlayerProfile>? TryReadIndex(out string? activeId)
    {
        activeId = null;
        string path = IndexPath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            IndexDto? index = JsonSerializer.Deserialize<IndexDto>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
            if (index is null)
            {
                return null;
            }

            activeId = index.Active;
            var result = new List<PlayerProfile>();
            foreach (ProfileDto entry in index.Profiles ?? new List<ProfileDto>())
            {
                PlayerProfile? profile = FromDto(entry);
                if (profile is not null)
                {
                    result.Add(profile);
                }
            }

            return result;
        }
        catch
        {
            try
            {
                File.Move(path, path + ".bak-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), overwrite: true);
            }
            catch
            {
                // 备份失败也不阻断扫描。
            }

            return null;
        }
    }

    private List<PlayerProfile> ScanProfiles()
    {
        var result = new List<PlayerProfile>();
        if (!Directory.Exists(_profilesRoot))
        {
            return result;
        }

        foreach (string directory in Directory.EnumerateDirectories(_profilesRoot))
        {
            string name = Path.GetFileName(directory);
            if (string.Equals(name, "_trash", StringComparison.OrdinalIgnoreCase) || !GuidPattern.IsMatch(name))
            {
                continue;
            }

            string profilePath = Path.Combine(directory, ProfileFileName);
            ProfileDto? dto = null;
            if (File.Exists(profilePath))
            {
                try
                {
                    dto = JsonSerializer.Deserialize<ProfileDto>(File.ReadAllText(profilePath, Encoding.UTF8), JsonOptions);
                }
                catch
                {
                    dto = null;
                }
            }

            if (dto is null)
            {
                dto = new ProfileDto
                {
                    Id = name,
                    Name = NormalizeName(null, result.Count + 1),
                    CreatedAt = Directory.GetCreationTime(directory),
                };
            }

            dto.Id = name;
            PlayerProfile? profile = FromDto(dto);
            if (profile is not null)
            {
                result.Add(profile);
            }
        }

        return result;
    }

    private PlayerProfile? FromDto(ProfileDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Id) || !GuidPattern.IsMatch(dto.Id))
        {
            return null;
        }

        string directory = Path.Combine(_profilesRoot, dto.Id);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        string name = NormalizeName(dto.Name, 1);
        return new PlayerProfile(
            dto.Id,
            name,
            directory,
            dto.CreatedAt == default ? DateTimeOffset.Now : dto.CreatedAt,
            dto.SteamId,
            string.Equals(dto.Source, "Steam", StringComparison.OrdinalIgnoreCase) ? ProfileSource.Steam : ProfileSource.Local);
    }

    private void WriteProfileFile(PlayerProfile profile)
    {
        Directory.CreateDirectory(profile.Directory);
        ProfileDto dto = ToDto(profile);
        WriteJson(Path.Combine(profile.Directory, ProfileFileName), dto);
    }

    private void SaveIndex()
    {
        List<PlayerProfile> snapshot;
        string active;
        lock (_gate)
        {
            snapshot = _profiles.ToList();
            active = _activeId;
        }

        var index = new IndexDto
        {
            Version = IndexVersion,
            Active = active,
            Profiles = snapshot.Select(ToDto).ToList(),
        };
        WriteJson(IndexPath, index);
    }

    private static ProfileDto ToDto(PlayerProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.DisplayName,
        CreatedAt = profile.CreatedAt,
        SteamId = profile.SteamId,
        Source = profile.Source == ProfileSource.Steam ? "Steam" : "Local",
    };

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions), Utf8NoBom);
        File.Move(temp, path, overwrite: true);
    }

    private T WithProfileLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, @"Local\TiisuMahjong.Profiles");
        bool owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            if (!owned)
            {
                throw new TimeoutException("等待玩家档案锁超时");
            }

            return action();
        }
        finally
        {
            if (owned)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private sealed class IndexDto
    {
        public int Version { get; set; } = IndexVersion;
        public string? Active { get; set; }
        public List<ProfileDto>? Profiles { get; set; }
    }

    private sealed class ProfileDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public string? SteamId { get; set; }
        public string? Source { get; set; }
    }
}
