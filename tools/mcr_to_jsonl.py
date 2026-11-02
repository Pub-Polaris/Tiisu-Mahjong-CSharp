"""国标（MCR）牌谱 → 弃牌训练样本。

输入：IJCAI-2026 MCR corpora 的 `final2_all.jsonl.gz`（Botzone Mahjong-GB 牌谱，一行一局）。
输出：与 `--export-dataset` 同 schema 的 jsonl（src=guobiao），供小 AI 预训练/对照。

用法：
  python tools/mcr_to_jsonl.py <in.jsonl.gz> <out.jsonl>
"""

import gzip
import json
import sys
from collections import Counter

SUITS = {"W": "m", "T": "p", "B": "s"}
HONORS = {"F1": "1z", "F2": "2z", "F3": "3z", "F4": "4z",
          "J1": "5z", "J2": "6z", "J3": "7z"}
ORDER = "mpsz"


def tile_id(tok):
    """Botzone 令牌 → 我们的牌 id；未知（花牌等）返回 None。"""
    if not tok:
        return None
    if tok in HONORS:
        return HONORS[tok]
    if len(tok) < 2 or tok[0] not in SUITS:
        return None
    try:
        num = int(tok[1:])
    except ValueError:
        return None
    if not 1 <= num <= 9:
        return None
    return f"{num}{SUITS[tok[0]]}"


def hand_string(tiles):
    """['1m','2m','1z'] → '12m1z'（同 Web/C# notation）。"""
    groups = []
    for suit in ORDER:
        nums = sorted(int(t[0]) for t in tiles if t[-1] == suit)
        if nums:
            groups.append("".join(str(n) for n in nums) + suit)
    return "".join(groups)


def label_index(tid):
    if tid is None:
        return -1
    num = int(tid[0])
    suit = tid[1]
    base = {"m": 0, "p": 9, "s": 18, "z": 27}[suit]
    return base + num - 1


def convert_game(obj, out, stats):
    hands = None
    discards = [[], [], [], []]
    turn = [0, 0, 0, 0]
    wall = 0

    for entry in obj.get("logs") or []:
        display = ((entry.get("output") or {}).get("display")) or {}
        action = display.get("action")
        if not action:
            continue

        if action == "DEAL":
            hands = [[tile_id(t) for t in h] for h in display.get("hand") or []]
            wall = sum(display.get("tileCnt") or [0, 0, 0, 0])
            continue

        if hands is None:
            continue

        player = display.get("player")
        tile = tile_id(display.get("tile"))

        if action == "DRAW":
            if player is None or tile is None:
                continue
            hands[player].append(tile)
            wall = max(0, wall - 1)
            continue

        if action == "PLAY":
            if player is None or tile is None:
                continue
            hand = [t for t in hands[player] if t is not None]
            if tile not in hand:
                continue
            sample = {
                "v": 1,
                "src": "guobiao",
                "p": player,
                "seat": "",
                "dealer": 0,
                "wall": wall,
                "h": 0,
                "rw": 0,
                "dc": 0,
                "turn": turn[player],
                "hand": hand_string(hand),
                "melds": [],
                "discards": [hand_string([]) for _ in range(4)],
                "label": tile,
                "labelIdx": label_index(tile),
            }
            out.write(json.dumps(sample, ensure_ascii=False, separators=(",", ":")) + "\n")
            stats["total"] += 1
            stats["perTile"][label_index(tile)] += 1

            hands[player].remove(tile)
            discards[player].append(tile)
            turn[player] += 1
            continue

        if action == "PENG":
            if player is None or tile is None:
                continue
            for _ in range(2):
                if tile in hands[player]:
                    hands[player].remove(tile)
            continue

        if action == "CHI":
            # content 形如 "3 0 CHI B8 F1"：CHI 后面两张是吃进的两张手牌。
            content = ((entry.get("output") or {}).get("content")) or {}
            text = next(iter(content.values()), "") if isinstance(content, dict) else ""
            parts = text.split("CHI", 1)[-1].split()
            for tok in parts[:2]:
                tid = tile_id(tok)
                if tid and tid in hands[player]:
                    hands[player].remove(tid)
            continue

        if action == "GANG":
            content = ((entry.get("output") or {}).get("content")) or {}
            text = next(iter(content.values()), "") if isinstance(content, dict) else ""
            parts = text.split("GANG", 1)[-1].split()
            for tok in parts:
                tid = tile_id(tok)
                if tid and tid in hands[player]:
                    hands[player].remove(tid)
            continue

        if action in ("HU", "LIUJU"):
            return


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    src, dst = sys.argv[1], sys.argv[2]
    stats = {"games": 0, "total": 0, "perTile": [0] * 34}

    with gzip.open(src, "rt", encoding="utf-8") as fin, open(dst, "w", encoding="utf-8", newline="\n") as out:
        for line in fin:
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            convert_game(obj, out, stats)
            stats["games"] += 1

    print(f"games={stats['games']} samples={stats['total']}")
    print("perTile=" + ",".join(str(n) for n in stats["perTile"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
