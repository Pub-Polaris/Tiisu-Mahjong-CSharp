"""国标和牌牌型统计（按 Botzone 协议正确还原）。

协议要点（Botzone Wiki Chinese-Standard-Mahjong 输出消息）：
- PENG Card1：碰上一回合的弃牌，并**打出 Card1**
- CHI Card1 Card2：吃成顺子，中间牌 Card1，并**打出 Card2**
- GANG：上一条是 DRAW → 暗杠；否则明杠（杠上一回合弃牌）
- BUGANG Card1：补杠 Card1
- PLAY Card1：弃牌；DRAW：摸牌

校验：每局 HU 时 手牌 + 副露 + 牌河 + 牌山 == 136，否则整局丢弃。

用法：python tools/guobiao_win_stats.py <final2_all.jsonl.gz> [out.md]
"""

import gzip
import json
import sys
from collections import Counter

BOTZONE = {"W": "m", "T": "p", "B": "s"}
SUIT_ORDER = {"m": 0, "p": 9, "s": 18, "z": 27}


def tid(tok):
    if not tok:
        return None
    if tok in ("F1", "F2", "F3", "F4"):
        return tok[1] + "z"
    if tok in ("J1", "J2", "J3"):
        return str(int(tok[1]) + 4) + "z"
    if tok[0] in BOTZONE and tok[1:].isdigit():
        return tok[1:] + BOTZONE[tok[0]]
    return None


def tile_index(t):
    return SUIT_ORDER[t[1]] + int(t[0]) - 1


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
    for t in hand:
        grid[{"m": 0, "p": 1, "s": 2, "z": 3}[t[-1]]][int(t[0])] += 1

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


def content_card(entry, key):
    content = ((entry.get("output") or {}).get("content")) or {}
    if not isinstance(content, dict) or not content:
        return []
    text = next(iter(content.values()), "")
    parts = text.split()
    marker = key
    if marker in parts:
        return parts[parts.index(marker) + 1:]
    return []


def stats_for_game(obj):
    hands = None
    rivers = [[], [], [], []]
    meld_counts = [0, 0, 0, 0]
    meld_tiles = [0, 0, 0, 0]
    wall = 0
    last_discard = None
    last_owner = None
    prev = None  # (action, player)

    for e in obj.get("logs") or []:
        disp = ((e.get("output") or {}).get("display")) or {}
        act = disp.get("action")
        p = disp.get("player")
        if act == "DEAL":
            hands = [[tid(t) for t in h if t] for h in disp.get("hand") or []]
            hands = [[t for t in h if t] for h in hands]
            rivers = [[], [], [], []]
            meld_counts = [0, 0, 0, 0]
            meld_tiles = [0, 0, 0, 0]
            wall = sum(disp.get("tileCnt") or [0, 0, 0, 0])
            last_discard = None
            last_owner = None
            prev = (act, None)
            continue
        if hands is None:
            continue

        if "tileCnt" in disp:
            wall = sum(disp.get("tileCnt") or [0, 0, 0, 0])

        if act == "DRAW":
            t = tid(disp.get("tile"))
            if p is not None and t:
                hands[p].append(t)
            prev = (act, p)
        elif act == "PLAY":
            t = tid(disp.get("tile"))
            if p is not None and t in hands[p]:
                hands[p].remove(t)
                rivers[p].append(t)
                last_discard = t
                last_owner = p
            prev = (act, p)
        elif act == "PENG":
            cards = content_card(e, "PENG")
            discard = tid(cards[0]) if cards else None
            if p is not None and last_discard:
                if last_owner is not None and last_discard in rivers[last_owner]:
                    rivers[last_owner].remove(last_discard)
                for _ in range(2):
                    if last_discard in hands[p]:
                        hands[p].remove(last_discard)
                meld_counts[p] += 1
                meld_tiles[p] += 3
                if discard:
                    if discard in hands[p]:
                        hands[p].remove(discard)
                    rivers[p].append(discard)
                    last_discard = discard
                    last_owner = p
            prev = (act, p)
        elif act == "CHI":
            cards = content_card(e, "CHI")
            if p is not None and len(cards) >= 2 and last_discard:
                if last_owner is not None and last_discard in rivers[last_owner]:
                    rivers[last_owner].remove(last_discard)
                mid = tid(cards[0])
                discard = tid(cards[1])
                if mid:
                    num = int(mid[0])
                    suit = mid[1]
                    seq = [f"{n}{suit}" for n in (num - 1, num, num + 1) if 1 <= n <= 9]
                    # 顺子里除「claimed」外的两张从手牌扣；若信息不全则回退扣两张同花色邻牌
                    claimed = last_discard
                    need = [t for t in seq if t != claimed]
                    removed = 0
                    for t in need:
                        if t in hands[p]:
                            hands[p].remove(t)
                            removed += 1
                    while removed < 2:
                        cand = next((t for t in hands[p] if t in seq), None)
                        if not cand:
                            break
                        hands[p].remove(cand)
                        removed += 1
                    meld_counts[p] += 1
                    meld_tiles[p] += 3
                if discard:
                    if discard in hands[p]:
                        hands[p].remove(discard)
                    rivers[p].append(discard)
                    last_discard = discard
                    last_owner = p
            prev = (act, p)
        elif act == "GANG":
            t = tid(disp.get("tile")) or last_discard
            concealed = prev and prev[0] == "DRAW" and prev[1] == p
            take = 4 if concealed else 3
            if p is not None and t:
                if not concealed and last_owner is not None and t in rivers[last_owner]:
                    rivers[last_owner].remove(t)
                for _ in range(take):
                    if t in hands[p]:
                        hands[p].remove(t)
                meld_counts[p] += 1
                meld_tiles[p] += 4 if concealed else 4  # 杠面子算 4 张
                if not concealed:
                    last_discard = None
            prev = (act, p)
        elif act == "BUGANG":
            cards = content_card(e, "BUGANG")
            t = tid(cards[0]) if cards else None
            if p is not None and t and t in hands[p]:
                hands[p].remove(t)
                meld_tiles[p] += 1
            prev = (act, p)
        elif act == "HU":
            if p is None:
                return None
            hand = list(hands[p])
            # 期望暗牌数 = 14 - 3×面子 + 杠数（每个杠多一张）
            expected = 14 - 3 * meld_counts[p]
            if len(hand) == expected - 1 and last_discard:
                hand.append(last_discard)  # 荣和：铳张补进手牌
            total = sum(len(h) for h in hands) + sum(meld_tiles) + sum(len(r) for r in rivers) + wall
            if total != 136:
                return ("__invalid__", total)
            shape = classify(hand, meld_counts[p])
            return (shape, total)
        else:
            prev = (act, p)

    return None


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    out = sys.argv[2] if len(sys.argv) > 2 else "dist/dataset/guobiao-shape-stats.md"
    stats = Counter()
    invalid = 0
    no_hu = 0
    games = 0

    with gzip.open(sys.argv[1], "rt", encoding="utf-8") as f:
        for line in f:
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            games += 1
            res = stats_for_game(obj)
            if res is None:
                no_hu += 1
                continue
            shape, _ = res
            if shape == "__invalid__":
                invalid += 1
            else:
                stats[shape] += 1

    total = sum(stats.values())
    hu_games = games - no_hu
    lines = [
        "# 国标和牌牌型统计（协议正确还原）",
        "",
        f"- 局数：{games}（有和牌 {hu_games}，荒庄/未和 {no_hu}）",
        f"- 有效和牌：{total}，还原失败丢弃：{invalid}（占和牌局 {invalid / max(hu_games, 1):.2%}）",
        "",
        "| 形态 | 数量 | 占比 |",
        "|---|---:|---:|",
    ]
    for k, v in stats.most_common():
        lines.append(f"| {k} | {v} | {v / max(total, 1):.1%} |")
    lines += ["", "校验：手牌 + 副露 + 牌河 + 牌山 == 136，不满足整局丢弃。"]
    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main())
