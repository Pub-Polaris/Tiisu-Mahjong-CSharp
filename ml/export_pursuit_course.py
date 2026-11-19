#!/usr/bin/env python3
"""21k 稀有课程导出（A 期，原型；不训练、不替换 weights）。

从 `--export-selfplay` 产出的 JSONL 里挑出「追役原型会进入的目标形态」决策，
给每条样本打上 `PursuitTarget` + `SampleWeight = 1.1`，供 Phase 2 的训练脚本按权重采样。
本轮只产出数据文件与接口，不跑正式训练、不改 `policy_weights_v4.json`。

用法::

    dotnet ...\\Tiisu.DevTools.dll --export-selfplay dist/selfplay.jsonl --games 500 --mode 4
    python ml/export_pursuit_course.py --data dist/selfplay.jsonl --out dist/pursuit_course.jsonl

筛选口径（与 C# `AiPursuit.AssessTarget` 近似，特征布局见 docs/SELFPLAY_TRAINING.md）：

- 七对子：手牌 pairUnits = sum(count//2) >= 4 且没有明副露。
- 混/清一色：主花色（手牌+副露）占比 >= 0.55。
- 字一色：字牌（手牌+副露）占比 >= 0.55 且 >= 8 张。
- 大七星：七种字牌里 count==2 的 >= 3 且没有副露。
- 九莲宝灯：门清、主花色 >= 9 张、该花色占比 >= 0.65、有 1 或 9。
- 四暗杠：门清、至少 1 个暗杠、进度 >= 0.4。

注意：自博弈特征里**没有牌山剩余**，所以本脚本只做「结构形态」筛选；
「早期/中期、非关键局」的运行时门槛仍在 `AiPursuit.Observe` 里判断，课程数据不替代它。
"""

from __future__ import annotations

import argparse
import json
import random
from collections import Counter

# SelfPlayFeatures v4 布局（与 src/Tiisu.Engine/SelfPlay/SelfPlayFeatures.cs 一致）
HAND_OFFSET = 0
MELD_OFFSET = 170
MELD_COUNT_OFFSET = 306
FEATURE_COUNT = 339
HONOR_START = 27
HONOR_KINDS = 7

TARGET_ORDER = [
    "SevenPairs",
    "Flush",
    "AllHonors",
    "BigSevenStars",
    "NineGates",
    "FourAnkan",
]
TARGET_VALUE = {
    "SevenPairs": 0.45,
    "Flush": 0.80,
    "AllHonors": 1.20,
    "BigSevenStars": 1.50,
    "NineGates": 1.50,
    "FourAnkan": 1.30,
}
ENTRY_MIN_PROGRESS = 0.50
SAMPLE_WEIGHT = 1.1


def _counts(features: list[float], offset: int, scale: float) -> list[int]:
    return [int(round(features[offset + i] * scale)) for i in range(34)]


def _assess(features: list[float], player: int) -> dict[str, float]:
    """返回每个目标的进度（0 = 不可行）。与 C# 口径近似。"""
    hand = _counts(features, HAND_OFFSET, 1.0)
    meld_seat = MELD_OFFSET + max(0, min(3, player)) * 34
    meld = _counts(features, meld_seat, 4.0)
    meld_count = int(round(features[MELD_COUNT_OFFSET + max(0, min(3, player))] * 4.0))

    hand_tiles = sum(hand)
    meld_tiles = sum(meld)
    total = hand_tiles + meld_tiles

    suit_hand = [0, 0, 0]
    suit_all = [0, 0, 0]
    for index in range(27):
        suit_hand[index // 9] += hand[index]
        suit_all[index // 9] += hand[index] + meld[index]
    honor_hand = sum(hand[HONOR_START : HONOR_START + HONOR_KINDS])
    honor_all = honor_hand + sum(meld[HONOR_START : HONOR_START + HONOR_KINDS])

    pair_units = 0
    pair_kinds = 0
    triplet_kinds = 0
    seven_honor_pairs = 0
    for index in range(34):
        count = hand[index]
        if count >= 2:
            pair_kinds += 1
            pair_units += count // 2
            if index >= HONOR_START and count == 2:
                seven_honor_pairs += 1
        if count >= 3:
            triplet_kinds += 1

    # 「明副露」近似：副露组数 - 暗杠数。特征里没有暗杠数，按门清目标只接受 0 组副露。
    open_meld = meld_count

    progress: dict[str, float] = {}

    progress["SevenPairs"] = (
        min(1.0, pair_units / 7.0) if open_meld == 0 and pair_kinds >= 3 else 0.0
    )

    dominant = max(range(3), key=lambda s: suit_all[s])
    flush_share = (suit_all[dominant] + honor_all) / total if total else 0.0
    progress["Flush"] = flush_share if flush_share >= 0.55 else 0.0

    honor_share = honor_all / total if total else 0.0
    progress["AllHonors"] = honor_share if honor_all >= 8 and honor_share >= 0.55 else 0.0

    progress["BigSevenStars"] = (
        seven_honor_pairs / 7.0 if meld_count == 0 and seven_honor_pairs >= 3 else 0.0
    )

    dominant_hand = max(range(3), key=lambda s: suit_hand[s])
    purity = suit_hand[dominant_hand] / hand_tiles if hand_tiles else 0.0
    has_one = hand[dominant_hand * 9] > 0
    has_nine = hand[dominant_hand * 9 + 8] > 0
    nine_gates = (
        meld_count == 0
        and suit_hand[dominant_hand] >= 9
        and purity >= 0.65
        and (has_one or has_nine)
    )
    progress["NineGates"] = (
        min(1.0, 0.65 * purity + 0.35 * (1.0 if has_one and has_nine else 0.55))
        if nine_gates
        else 0.0
    )

    # 四暗杠：特征里没有 AnkanCount，用「副露组数为 0 + 至少两组刻子」近似（保守，宁缺勿滥）。
    four_ankan = meld_count == 0 and triplet_kinds >= 2
    progress["FourAnkan"] = (
        min(1.0, 0.75 * triplet_kinds / 4.0) if four_ankan else 0.0
    )

    return progress


def pick_target(features: list[float], player: int) -> str | None:
    progress = _assess(features, player)
    best: tuple[float, str] | None = None
    for target in TARGET_ORDER:
        value = progress[target]
        if value < ENTRY_MIN_PROGRESS:
            continue
        score = 0.6 * value + 0.1 * TARGET_VALUE[target]
        if best is None or score > best[0]:
            best = (score, target)
    return None if best is None else best[1]


def export(args: argparse.Namespace) -> None:
    """单遍扫描 + 每目标蓄水池采样：内存有界、结果由 --seed 决定。"""
    per_target_cap = max(1, args.max // len(TARGET_ORDER))
    reservoirs: dict[str, list[dict]] = {target: [] for target in TARGET_ORDER}
    seen: Counter = Counter()
    scanned = 0
    candidates = 0

    rng = random.Random(args.seed)
    with open(args.data, encoding="utf-8") as source:
        for line in source:
            line = line.strip()
            if not line:
                continue
            scanned += 1
            row = json.loads(line)
            features = row.get("Features")
            if not isinstance(features, list) or len(features) != FEATURE_COUNT:
                continue
            target = pick_target(features, int(row.get("Player", 0)))
            if target is None:
                continue
            candidates += 1
            seen[target] += 1
            bucket = reservoirs[target]
            if len(bucket) < per_target_cap:
                bucket.append(row)
            else:
                # 蓄水池采样：后面的候选等概率替换已有样本。
                j = rng.randrange(seen[target])
                if j < per_target_cap:
                    bucket[j] = row

    rows: list[dict] = []
    for target in TARGET_ORDER:
        rows.extend(reservoirs[target])
    rng.shuffle(rows)
    rows = rows[: args.max]

    written = 0
    with open(args.out, "w", encoding="utf-8") as sink:
        for row in rows:
            target = pick_target(row["Features"], int(row.get("Player", 0)))
            if target is None:
                continue
            out = dict(row)
            out["PursuitTarget"] = target
            out["SampleWeight"] = SAMPLE_WEIGHT
            sink.write(json.dumps(out, ensure_ascii=False) + "\n")
            written += 1

    print(f"scanned={scanned} candidates={candidates} written={written} "
          f"sample_weight={SAMPLE_WEIGHT} per_target_cap={per_target_cap}")
    for target in TARGET_ORDER:
        print(f"  {target}: candidates={seen[target]} picked={len(reservoirs[target])}")
    print(f"wrote {args.out}")


def main() -> None:
    parser = argparse.ArgumentParser(description="导出 21k 稀有追役课程（不训练）")
    parser.add_argument("--data", required=True, help="自博弈导出的 JSONL")
    parser.add_argument("--out", default="dist/pursuit_course.jsonl", help="输出 JSONL")
    parser.add_argument("--max", type=int, default=21000, help="总样本上限（默认 21000）")
    parser.add_argument("--seed", type=int, default=20261119, help="打乱种子")
    export(parser.parse_args())


if __name__ == "__main__":
    main()
