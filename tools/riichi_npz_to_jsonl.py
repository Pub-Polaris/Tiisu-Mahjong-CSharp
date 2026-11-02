"""日麻（Tenhou）特征牌谱 → 弃牌训练样本。

用途（北极星 2026-11-02）：作为「人类在不破坏牌效的情况下凑听牌形」的参考，
不参与七子判役/计分，只提供人类风格的弃牌样本。

输入：1nfrared218/tenhou-mjai-riichi-training 的 webdataset shard（tar 里是 .npz，
      每个 npz 是一局的特征样本：hand_counts / legal actions / expert_index）。
输出：与 --export-dataset 同 schema（src=riichi）。

用法：python tools/riichi_npz_to_jsonl.py <shard.tar> <out.jsonl>
"""

import io
import json
import sys
import tarfile

import numpy as np

SUITS = "mpsz"


def tile_id(idx):
    if idx < 0 or idx > 33:
        return None
    if idx < 9:
        return f"{idx + 1}m"
    if idx < 18:
        return f"{idx - 8}p"
    if idx < 27:
        return f"{idx - 17}s"
    return f"{idx - 26}z"


def hand_string(counts):
    groups = []
    for suit_index, suit in enumerate(SUITS):
        base = suit_index * 9
        nums = []
        max_num = 9 if suit_index < 3 else 7
        for n in range(1, max_num + 1):
            nums.extend([str(n)] * int(counts[base + n - 1]))
        if nums:
            groups.append("".join(nums) + suit)
    return "".join(groups)


def seat_name(wind_index):
    return ["east", "south", "west", "north"][wind_index % 4]


def convert_game(z, out, stats):
    if "hand_counts" not in z:
        return
    hand_counts = z["hand_counts"]
    offsets = z["legal_offsets"]
    expert = z["expert_index"]
    legal_type = z["legal_type"]
    legal_tiles = z["legal_tiles"]
    players = z["player"]
    seat_winds = z["seat_wind"]
    round_winds = z["round_wind"]
    dealers = z["dealer"]
    honbas = z["honba"]
    tiles_left = z["tiles_left"]

    for i in range(int(z["sample_count"][0])):
        action = int(offsets[i] + expert[i])
        if int(legal_type[action]) != 0:
            continue  # 只导出「打牌」决策；吃碰杠/立直等跳过

        # legal_tiles 用 1..34 编码（1=1m、34=7z），hand_counts 用 0..33；统一到 0..33。
        tile_idx = int(legal_tiles[action][0]) - 1
        if tile_idx < 0 or tile_idx > 33:
            continue
        label = tile_id(tile_idx)
        if label is None:
            continue

        player = int(players[i])
        seat = int(seat_winds[i]) - 27 if 27 <= int(seat_winds[i]) <= 30 else player
        rw = int(round_winds[i]) - 27 if 27 <= int(round_winds[i]) <= 30 else 0
        dealer = int(dealers[i])

        sample = {
            "v": 1,
            "src": "riichi",
            "p": player,
            "seat": seat_name(seat),
            "dealer": 1 if player == dealer else 0,
            "wall": int(tiles_left[i]),
            "h": int(honbas[i]),
            "rw": rw % 4,
            "dc": 0,
            "turn": 0,
            "hand": hand_string(hand_counts[i]),
            "melds": [],
            "discards": ["" for _ in range(4)],
            "label": label,
            "labelIdx": tile_idx,
        }
        out.write(json.dumps(sample, ensure_ascii=False, separators=(",", ":")) + "\n")
        stats["total"] += 1
        stats["perTile"][tile_idx] += 1


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    src, dst = sys.argv[1], sys.argv[2]
    stats = {"games": 0, "total": 0, "perTile": [0] * 34}

    with tarfile.open(src, "r") as tar, open(dst, "w", encoding="utf-8", newline="\n") as out:
        for member in tar:
            if not member.isfile() or not member.name.endswith(".npz"):
                continue
            f = tar.extractfile(member)
            if f is None:
                continue
            try:
                z = np.load(io.BytesIO(f.read()), allow_pickle=False)
            except Exception:
                continue
            convert_game(z, out, stats)
            stats["games"] += 1

    print(f"games={stats['games']} samples={stats['total']}")
    print("perTile=" + ",".join(str(n) for n in stats["perTile"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
