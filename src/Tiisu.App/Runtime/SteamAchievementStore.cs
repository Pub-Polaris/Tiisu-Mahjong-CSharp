using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Tiisu.App.Runtime;

/// <summary>
/// Steam 离线成就队列（<c>%APPDATA%\TiisuMahjong\steam-pending.json</c>）。
/// 无 SDK 时把待上报 Steam ID 落盘；SDK 可用后由 <see cref="SteamAchievementSync"/> 补报。
/// </summary>
public sealed class SteamAchievementStore
{
    private readonly object _gate = new();
    private readonly string _path;

    public SteamAchievementStore(string? directory = null)
    {
        string dir = System.IO.Path.GetFullPath(directory ?? Settings.DefaultDirectory);
        Directory.CreateDirectory(dir);
        _path = System.IO.Path.Combine(dir, "steam-pending.json");
    }

    public string PendingPath => _path;

    public IReadOnlyList<string> Load()
    {
        lock (_gate)
        {
            return LoadCore();
        }
    }

    public void Add(IEnumerable<string> steamIds)
    {
        var ids = steamIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            var all = new HashSet<string>(LoadCore(), StringComparer.Ordinal);
            all.UnionWith(ids);
            SaveCore(all);
        }
    }

    public void Remove(IEnumerable<string> steamIds)
    {
        var remove = new HashSet<string>(steamIds, StringComparer.Ordinal);
        if (remove.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            var all = new HashSet<string>(LoadCore(), StringComparer.Ordinal);
            all.ExceptWith(remove);
            SaveCore(all);
        }
    }

    private IReadOnlyList<string> LoadCore()
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path, Encoding.UTF8))
                   ?? Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private void SaveCore(IEnumerable<string> ids)
    {
        string tmp = _path + ".tmp";
        string json = JsonSerializer.Serialize(ids.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        try
        {
            File.Move(tmp, _path, overwrite: true);
        }
        catch (IOException)
        {
            // Steam 队列写失败不影响单机；下次再试。
            try
            {
                File.Delete(tmp);
            }
            catch
            {
                // 忽略
            }
        }
    }
}
