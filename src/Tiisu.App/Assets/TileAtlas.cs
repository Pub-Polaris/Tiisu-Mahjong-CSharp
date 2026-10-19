using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SkiaSharp;

namespace Tiisu.App.Assets;

/// <summary>
/// 牌面雪碧图（由 Web/scripts/export_tiles.py 生成）。
/// tiles.png：9 列 × 4 行，每格 cellW×cellH；tiles.json 记录格子尺寸与 id 顺序。
/// 只暴露「整图 + 每张牌的源矩形」，切片交给 SkiaCanvas 的 DrawImage。
/// </summary>
public sealed class TileAtlas : IDisposable
{
    private readonly Dictionary<string, SKRect> _sources = new();

    public TileAtlas(string assetsDir)
    {
        string jsonPath = Path.Combine(assetsDir, "tiles.json");
        string pngPath = Path.Combine(assetsDir, "tiles.png");
        if (!File.Exists(jsonPath) || !File.Exists(pngPath))
        {
            return;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(jsonPath), options);
        if (manifest?.Ids is null || manifest.Ids.Count == 0)
        {
            return;
        }

        Sheet = SKImage.FromEncodedData(pngPath);
        if (Sheet is null)
        {
            return;
        }

        for (int i = 0; i < manifest.Ids.Count; i++)
        {
            int col = i % manifest.Cols;
            int row = i / manifest.Cols;
            _sources[manifest.Ids[i]] = new SKRect(
                col * manifest.CellW, row * manifest.CellH,
                col * manifest.CellW + manifest.CellW, row * manifest.CellH + manifest.CellH);
        }

        IsLoaded = _sources.Count > 0;
    }

    public bool IsLoaded { get; }

    public SKImage? Sheet { get; }

    public bool TryGetSource(string tileId, out SKRect src) => _sources.TryGetValue(tileId, out src);

    public void Dispose() => Sheet?.Dispose();

    private sealed class Manifest
    {
        public int CellW { get; set; } = 72;
        public int CellH { get; set; } = 100;
        public int Cols { get; set; } = 9;
        public List<string>? Ids { get; set; }
    }
}
