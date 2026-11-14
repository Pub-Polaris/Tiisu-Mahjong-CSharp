using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tiisu.App.Runtime;

/// <summary>
/// Steam Cloud 的最小存储接口；真实实现接 Steam Remote Storage，
/// 测试/本地镜像用 <see cref="LocalCloudStorage"/>。
/// </summary>
public interface ICloudStorage
{
    bool IsAvailable { get; }
    byte[]? Read(string fileName);
    bool Write(string fileName, byte[] data);
    IReadOnlyList<string> List();
}

/// <summary>把本地目录当“云”用：本地镜像、离线备份与测试。</summary>
public sealed class LocalCloudStorage : ICloudStorage
{
    private readonly string _directory;

    public LocalCloudStorage(string directory)
    {
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
    }

    public bool IsAvailable => true;

    public byte[]? Read(string fileName)
    {
        string path = Path.Combine(_directory, Path.GetFileName(fileName));
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public bool Write(string fileName, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(Path.Combine(_directory, Path.GetFileName(fileName)), data);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_directory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(_directory)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToArray();
    }
}

public sealed record CloudSyncResult(
    int Uploaded,
    int Downloaded,
    int Skipped,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Errors.Count == 0;
}

/// <summary>
/// 同步策略：只处理 <see cref="SteamCloudPolicy"/> 允许的文件。
/// 云端缺失 → 上传；本地缺失 → 下载；两边都有但不同 → 本地优先上传（不覆盖本地）。
/// </summary>
public sealed class SteamCloudSync
{
    public CloudSyncResult Sync(string localDirectory, ICloudStorage cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        if (!cloud.IsAvailable)
        {
            return new CloudSyncResult(0, 0, 0, new[] { "cloud unavailable" });
        }

        var errors = new List<string>();
        int uploaded = 0;
        int downloaded = 0;
        int skipped = 0;

        foreach (string localPath in SteamCloudPolicy.ExistingFiles(localDirectory))
        {
            string name = Path.GetFileName(localPath);
            byte[] local = File.ReadAllBytes(localPath);
            byte[]? remote = cloud.Read(name);
            if (remote is not null && local.AsSpan().SequenceEqual(remote))
            {
                skipped++;
                continue;
            }

            if (cloud.Write(name, local))
            {
                uploaded++;
            }
            else
            {
                errors.Add("upload failed: " + name);
            }
        }

        foreach (string name in cloud.List())
        {
            if (!SteamCloudPolicy.ShouldSync(name) || File.Exists(Path.Combine(localDirectory, name)))
            {
                continue;
            }

            byte[]? remote = cloud.Read(name);
            if (remote is null)
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(localDirectory);
                File.WriteAllBytes(Path.Combine(localDirectory, name), remote);
                downloaded++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add("download failed: " + name);
            }
        }

        return new CloudSyncResult(uploaded, downloaded, skipped, errors);
    }
}
