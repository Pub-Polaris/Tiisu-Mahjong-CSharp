using System;
using System.Collections.Generic;

namespace Tiisu.App.Runtime;

/// <summary>
/// 局末确认闸门：AI 自动确认，人类必须逐个确认；全确认前不推进下一局。
/// </summary>
public sealed class RoundConfirmationGate
{
    private readonly bool[] _confirmed = new bool[MahjongEngineSeatCount];

    private const int MahjongEngineSeatCount = 4;

    public bool IsConfirmed(int seat) =>
        seat >= 0 && seat < _confirmed.Length && _confirmed[seat];

    public void Reset()
    {
        Array.Clear(_confirmed);
    }

    public void Confirm(int seat)
    {
        if (seat >= 0 && seat < _confirmed.Length)
        {
            _confirmed[seat] = true;
        }
    }

    public void ConfirmAll()
    {
        for (int i = 0; i < _confirmed.Length; i++)
        {
            _confirmed[i] = true;
        }
    }

    public bool AllConfirmed
    {
        get
        {
            for (int i = 0; i < _confirmed.Length; i++)
            {
                if (!_confirmed[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>未确认的座位（用于提示 / 日志）。</summary>
    public IReadOnlyList<int> Pending
    {
        get
        {
            var list = new List<int>(_confirmed.Length);
            for (int i = 0; i < _confirmed.Length; i++)
            {
                if (!_confirmed[i])
                {
                    list.Add(i);
                }
            }

            return list;
        }
    }
}
