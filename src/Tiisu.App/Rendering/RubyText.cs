using System;
using System.Collections.Generic;
using System.Text;

namespace Tiisu.App.Rendering;

/// <summary>振り仮名の1区間。<c>{漢字|かんじ}</c> の記法を描画時に分解する。</summary>
public readonly record struct RubySegment(string Base, string? Ruby);

/// <summary>
/// 軽量なルビ記法パーサー。対応形式は <c>{漢字|かんじ}</c>。
/// 不正な括弧はそのまま文字として扱う（翻訳ファイルの破損で描画が消えないように）。
/// </summary>
public static class RubyText
{
    public static bool HasMarkup(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains('{') && text.Contains('|') && text.Contains('}');

    public static string StripMarkup(string? text)
    {
        if (!HasMarkup(text))
        {
            return text ?? string.Empty;
        }

        var sb = new StringBuilder(text!.Length);
        foreach (var segment in Parse(text))
        {
            sb.Append(segment.Base);
        }

        return sb.ToString();
    }

    public static IReadOnlyList<RubySegment> Parse(string? text)
    {
        var result = new List<RubySegment>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        var plain = new StringBuilder();
        for (int i = 0; i < text.Length;)
        {
            if (text[i] != '{')
            {
                plain.Append(text[i++]);
                continue;
            }

            int pipe = text.IndexOf('|', i + 1);
            int close = pipe > i ? text.IndexOf('}', pipe + 1) : -1;
            if (pipe <= i + 1 || close <= pipe + 1)
            {
                plain.Append(text[i++]);
                continue;
            }

            if (plain.Length > 0)
            {
                result.Add(new RubySegment(plain.ToString(), null));
                plain.Clear();
            }

            result.Add(new RubySegment(text[(i + 1)..pipe], text[(pipe + 1)..close]));
            i = close + 1;
        }

        if (plain.Length > 0)
        {
            result.Add(new RubySegment(plain.ToString(), null));
        }

        return result;
    }
}
