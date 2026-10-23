using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Tiisu.App.Rendering;

namespace Tiisu.App.Ui;

/// <summary>
/// 数据驱动布局：一屏一份 JSON，元素按顺序绘制。
/// 目的：改版式只改数据，不改渲染代码（北极星给设计后由此落地）。
/// </summary>
public sealed class UiLayout
{
    public float Width { get; set; } = Screen.Width;
    public float Height { get; set; } = Screen.Height;
    public List<LayoutElement> Elements { get; set; } = new();

    public static UiLayout Load(string path)
    {
        if (!File.Exists(path))
        {
            return new UiLayout();
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<UiLayout>(File.ReadAllText(path), options) ?? new UiLayout();
    }
}

/// <summary>布局元素。type：rect / text / tile / button / image（image 预留）。</summary>
public sealed class LayoutElement
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "rect";
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public string? Text { get; set; }
    public string? TileId { get; set; }
    public string? Action { get; set; }
    public string? Fill { get; set; }
    public string? Border { get; set; }
    public float Radius { get; set; }
    public float FontSize { get; set; } = 20f;
    public string? FontColor { get; set; }

    /// <summary>文字水平对齐：center（默认）/ left / right。</summary>
    public string? Align { get; set; }

    public RectF Rect => new(X, Y, W, H);
}
