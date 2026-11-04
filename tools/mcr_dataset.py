"""国标（Botzone MCR）→ 带场面特征的弃牌样本。

按官方协议还原：PENG Card1 = 碰上一回合弃牌并打出 Card1；CHI Card1 Card2 = 吃成顺子
（中间牌 Card1）并打出 Card2；GANG 按上一条是否 DRAW 区分暗/明；BUGANG = 补杠。

每条样本（打牌前状态）：
  hand 34 + rivers 4×34 + meldCounts 4 + wall/turn/honba/dealer/seat/rw

用法：python tools/mcr_dataset.py <final2_all.jsonl.gz> <out.jsonl>
"""

import gzip
import json
import sys
from collections import Counter

BOTZONE = {"W": "m", "T": "p", "B": "s"}
SUIT_BASE = {"m": 0, "p": 9, "s": 18, "z": 27}
WIND_NAMES = ["east", "south", "west", "north"]


def tile_id(tok):
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
    return SUIT_BASE[t[1]] + int(t[0]) - 1


def hand_string(tiles):
    groups = []
    for suit in "mpsz":
        nums = sorted(int(t[0]) for t in tiles if t and t[-1] == suit)
        if nums:
            groups.append("".join(str(n) for n in nums) + suit)
    return "".join(groups)


def content_card(entry, key):
    content = ((entry.get("output") or {}).get("content")) or {}
    if not isinstance(content, dict) or not content:
        return []
    text = next(iter(content.values()), "")
    parts = text.split()
    return parts[parts.index(key) + 1:] if key in parts else []


def river_counts(rivers):
    out = []
    for seat in rivers:
        counts = [0] * 34
        for t in seat:
            counts[tile_index(t)] += 1
        out.append(counts)
    return out


def emit(out, stats, hands, rivers, meld_counts, wall, honba, round_wind, dealer, player, discard):
    if discard is None or discard not in hands[player]:
        return
    sample = {
        "v": 2,
        "src": "guobiao",
        "p": player,
        "seat": WIND_NAMES[(player - (dealer or 0)) % 4],
        "dealer": 1 if player == (dealer or 0) else 0,
        "wall": wall,
        "h": honba,
        "rw": round_wind % 4,
        "dc": 0,
        "turn": len(rivers[player]),
        "hand": hand_string(hands[player]),
        "melds": [],
        "rivers": river_counts(rivers),
        "meldCounts": list(meld_counts),
        "label": discard,
        "labelIdx": tile_index(discard),
    }
    out.write(json.dumps(sample, ensure_ascii=False, separators=(",", ":")) + "\n")
    stats[0] += 1


def convert_game(obj, out, stats):
    hands = None
    rivers = [[], [], [], []]
    meld_counts = [0, 0, 0, 0]
    wall = 0
    honba = 0
    round_wind = 0
    dealer = None
    last_discard = None
    last_owner = None
    prev = None

    initdata = obj.get("initdata")
    if isinstance(initdata, str):
        try:
            initdata = json.loads(initdata)
        except json.JSONDecodeError:
            initdata = None
    if isinstance(initdata, dict):
        try:
            round_wind = int(initdata.get("quan", 0))
        except (TypeError, ValueError):
            round_wind = 0

    for e in obj.get("logs") or []:
        disp = ((e.get("output") or {}).get("display")) or {}
        act = disp.get("action")
        p = disp.get("player")
        if act == "DEAL":
            hands = [[tile_id(t) for t in h if t] for h in disp.get("hand") or []]
            hands = [[t for t in h if t] for h in hands]
            rivers = [[], [], [], []]
            meld_counts = [0, 0, 0, 0]
            wall = sum(disp.get("tileCnt") or [0, 0, 0, 0])
            prev = (act, None)
            continue
        if hands is None:
            continue

        if "tileCnt" in disp:
            wall = sum(disp.get("tileCnt") or [0, 0, 0, 0])

        if act == "DRAW":
            t = tile_id(disp.get("tile"))
            if p is not None and t:
                if dealer is None:
                    dealer = p
                hands[p].append(t)
            prev = (act, p)
        elif act == "PLAY":
            t = tile_id(disp.get("tile"))
            if p is None or t is None:
                continue
            emit(out, stats, hands, rivers, meld_counts, wall, honba, round_wind, dealer, p, t)
            if t in hands[p]:
                hands[p].remove(t)
                rivers[p].append(t)
                last_discard = t
                last_owner = p
            prev = (act, p)
        elif act == "PENG":
            cards = content_card(e, "PENG")
            discard = tile_id(cards[0]) if cards else None
            if p is None or not last_discard:
                continue
            if last_owner is not None and last_discard in rivers[last_owner]:
                rivers[last_owner].remove(last_discard)
            for _ in range(2):
                if last_discard in hands[p]:
                    hands[p].remove(last_discard)
            meld_counts[p] += 1
            if discard:
                emit(out, stats, hands, rivers, meld_counts, wall, honba, round_wind, dealer, p, discard)
                if discard in hands[p]:
                    hands[p].remove(discard)
                rivers[p].append(discard)
                last_discard = discard
                last_owner = p
            prev = (act, p)
        elif act == "CHI":
            cards = content_card(e, "CHI")
            if p is None or len(cards) < 2 or not last_discard:
                continue
            if last_owner is not None and last_discard in rivers[last_owner]:
                rivers[last_owner].remove(last_discard)
            mid = tile_id(cards[0])
            discard = tile_id(cards[1])
            if mid:
                num = int(mid[0])
                suit = mid[1]
                seq = [f"{n}{suit}" for n in (num - 1, num, num + 1) if 1 <= n <= 9]
                need = [t for t in seq if t != last_discard]
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
            if discard:
                emit(out, stats, hands, rivers, meld_counts, wall, honba, round_wind, dealer, p, discard)
                if discard in hands[p]:
                    hands[p].remove(discard)
                rivers[p].append(discard)
                last_discard = discard
                last_owner = p
            prev = (act, p)
        elif act == "GANG":
            t = tile_id(disp.get("tile")) or last_discard
            concealed = prev and prev[0] == "DRAW" and prev[1] == p
            take = 4 if concealed else 3
            if p is not None and t:
                if not concealed and last_owner is not None and t in rivers[last_owner]:
                    rivers[last_owner].remove(t)
                for _ in range(take):
                    if t in hands[p]:
                        hands[p].remove(t)
                meld_counts[p] += 1
                if not concealed:
                    last_discard = None
            prev = (act, p)
        elif act == "BUGANG":
            cards = content_card(e, "BUGANG")
            t = tile_id(cards[0]) if cards else None
            if p is not None and t and t in hands[p]:
                hands[p].remove(t)
            prev = (act, p)
        elif act in ("HU", "LIUJU"):
            return
        else:
            prev = (act, p)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    stats = [0]
    games = 0
    with gzip.open(sys.argv[1], "rt", encoding="utf-8") as fin, \
            open(sys.argv[2], "w", encoding="utf-8", newline="\n") as out:
        for line in fin:
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            convert_game(obj, out, stats)
            games += 1
    print(f"games={games} samples={stats[0]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
