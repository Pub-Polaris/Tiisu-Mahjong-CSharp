"""牌型统计：标准型 vs 特殊形（1102工程）。

口径（北极星 2026-11-02）：
- 副露在所有麻将里都算一个面子；
- 特殊形 = 七对 / 十三幺 / 四子七对；其余 4 面子 + 1 雀头 = 标准型（含碰碰胡）；
- 七子以人类为准，AI 仅参考。

数据源：
- 七子：%APPDATA%/TiisuMahjong/logs/roundlog_*.jsonl 的 win_* 事件（hand + melds）
- 国标：final2_all.jsonl.gz 的 HU 事件（跟踪手牌、副露、最后一张弃牌）
- 日麻：特征 shard 无直接和牌手牌字段，本次不计

用法：python tools/shape_stats.py <guobiao.jsonl.gz> [out.md]
"""

import glob
import gzip
import json
import os
import sys
from collections import Counter

BOTZONE = {"W": "m", "T": "p", "B": "s"}


def botzone_to_id(tok):
    if not tok:
        return None
    if tok in ("F1", "F2", "F3", "F4"):
        return tok[1] + "z"
    if tok in ("J1", "J2", "J3"):
        return str(int(tok[1]) + 4) + "z"
    if tok[0] in BOTZONE and tok[1:].isdigit():
        return tok[1:] + BOTZONE[tok[0]]
    return None


def parse_hand(text):
    tiles, num = [], ""
    for ch in text:
        if ch.isdigit():
            num += ch
        else:
            for n in num:
                tiles.append(f"{n}{ch}")
            num = ""
    return tiles


def classify(hand, meld_count):
    counts = Counter(hand)
    if meld_count == 0 and len(hand) == 14:
        if all(c == 2 for c in counts.values()) and len(counts) == 7:
            return "七对"
        if len(counts) == 13 and all(t[-1] == "z" or t[0] in "19" for t in counts):
            return "十三幺"
        if sum(1 for c in counts.values() if c == 4) == 1 and sum(
            1 for c in counts.values() if c == 2
        ) == 5:
            return "四子七对"

    need = 4 - meld_count
    if need < 0:
        return "未知"
    grid = [[0] * 10 for _ in range(4)]
    order = {"m": 0, "p": 1, "s": 2, "z": 3}
    for t in hand:
        grid[order[t[-1]]][int(t[0])] += 1

    def sets(k):
        if k == 0:
            return all(all(v == 0 for v in row) for row in grid)
        for s in range(4):
            for n in range(1, 10):
                if grid[s][n] == 0:
                    continue
                if grid[s][n] >= 3:
                    grid[s][n] -= 3
                    if sets(k - 1):
                        grid[s][n] += 3
                        return True
                    grid[s][n] += 3
                if s < 3 and n <= 7 and grid[s][n + 1] and grid[s][n + 2]:
                    grid[s][n] -= 1
                    grid[s][n + 1] -= 1
                    grid[s][n + 2] -= 1
                    if sets(k - 1):
                        grid[s][n] += 1
                        grid[s][n + 1] += 1
                        grid[s][n + 2] += 1
                        return True
                    grid[s][n] += 1
                    grid[s][n + 1] += 1
                    grid[s][n + 2] += 1
        return False

    for s in range(4):
        for n in range(1, 10):
            if grid[s][n] >= 2:
                grid[s][n] -= 2
                ok = sets(need)
                grid[s][n] += 2
                if ok:
                    return "标准型"
    return "未知"


def qizi_stats(paths):
    stats = {"human": Counter(), "ai": Counter()}
    for path in paths:
        for line in open(path, encoding="utf-8"):
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            if obj.get("type") != "step" or not str(obj.get("a", "")).startswith("win"):
                continue
            hand = parse_hand(obj.get("hand") or "")
            if not hand:
                continue
            src = "human" if int(obj.get("p", 0)) == 0 else "ai"
            stats[src][classify(hand, len(obj.get("melds") or []))] += 1
    return stats


def guobiao_stats(path):
    stats = Counter()
    with gzip.open(path, "rt", encoding="utf-8") as f:
        for line in f:
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            hands = None
            melds = [0, 0, 0, 0]
            last_discard = None
            for e in obj.get("logs") or []:
                disp = ((e.get("output") or {}).get("display")) or {}
                act = disp.get("action")
                p = disp.get("player")
                if act == "DEAL":
                    hands = [[botzone_to_id(t) for t in h if t] for h in disp.get("hand") or []]
                    hands = [[t for t in h if t] for h in hands]
                    melds = [0, 0, 0, 0]
                elif not hands:
                    continue
                elif act == "DRAW" and p is not None:
                    tid = botzone_to_id(disp.get("tile"))
                    if tid:
                        hands[p].append(tid)
                elif act == "PLAY" and p is not None:
                    tid = botzone_to_id(disp.get("tile"))
                    if tid in hands[p]:
                        hands[p].remove(tid)
                        last_discard = tid
                elif act == "PENG" and p is not None:
                    tid = botzone_to_id(disp.get("tile"))
                    for _ in range(2):
                        if tid in hands[p]:
                            hands[p].remove(tid)
                    melds[p] += 1
                elif act == "CHI" and p is not None:
                    content = ((e.get("output") or {}).get("content")) or {}
                    text = next(iter(content.values()), "") if isinstance(content, dict) else ""
                    for tok in text.split("CHI", 1)[-1].split()[:2]:
                        tid = botzone_to_id(tok)
                        if tid in hands[p]:
                            hands[p].remove(tid)
                    melds[p] += 1
                elif act == "GANG" and p is not None:
                    tid = botzone_to_id(disp.get("tile"))
                    for _ in range(3):
                        if tid in hands[p]:
                            hands[p].remove(tid)
                    melds[p] += 1
                elif act == "HU" and p is not None:
                    hand = list(hands[p])
                    if (len(hand) + melds[p] * 3) % 3 == 1 and last_discard:
                        hand.append(last_discard)
                    stats[classify(hand, melds[p])] += 1
                    break
    return stats


def pct(counter):
    total = sum(counter.values())
    if total == 0:
        return "—"
    return " · ".join(f"{k} {v} ({v / total:.1%})" for k, v in counter.most_common())


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    out = sys.argv[2] if len(sys.argv) > 2 else "dist/dataset/shape-stats.md"

    logs = glob.glob(os.path.join(
        os.path.expanduser("~"), "AppData", "Roaming", "TiisuMahjong", "logs", "roundlog_*.jsonl"))
    qizi = qizi_stats(logs)
    guo = guobiao_stats(sys.argv[1])

    lines = [
        "# 牌型统计：标准型 vs 特殊形",
        "",
        "| 来源 | 样本 | 分布 |",
        "|---|---:|---|",
        f"| 七子·人类 | {sum(qizi['human'].values())} | {pct(qizi['human'])} |",
        f"| 七子·AI | {sum(qizi['ai'].values())} | {pct(qizi['ai'])} |",
        f"| 国标 | {sum(guo.values())} | {pct(guo)} |",
        "",
        "规则：副露算一个面子；特殊形 = 七对 / 十三幺 / 四子七对；其余按标准型。",
        "",
        "说明：日麻特征 shard 没有直接的和牌手牌字段，本次未计入。",
    ]
    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main())
