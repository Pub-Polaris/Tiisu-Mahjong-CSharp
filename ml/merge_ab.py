#!/usr/bin/env python3
"""Merge parallel --ab-ai shard JSON reports into one Markdown/CSV summary."""
from __future__ import annotations

import argparse
import json
import math
import random
import statistics
from pathlib import Path


def pct(values, q):
    if not values:
        return 0.0
    s = sorted(values)
    idx = min(len(s) - 1, max(0, int(q * (len(s) - 1))))
    return s[idx]


def merge(root: Path):
    files = sorted(root.glob("shard-*/ab-ai_*.json"))
    if not files:
        raise SystemExit(f"no ab-ai json under {root}")
    reports = [json.loads(p.read_text(encoding="utf-8")) for p in files]
    variants = {}
    for report in reports:
        for v in report["variants"]:
            item = variants.setdefault(v["variant"], {
                "model": v.get("model"), "games": [], "rounds": []
            })
            item["model"] = v.get("model") or item["model"]
            item["games"].extend(v.get("gameRows") or [])
            item["rounds"].extend(v.get("roundRows") or [])
    out = {}
    for name, item in variants.items():
        gs = item["games"]
        rs = item["rounds"]
        rounds = sum(g.get("Rounds", 0) for g in gs)
        focus_wins = sum(g.get("FocusWins", 0) for g in gs)
        focus_deals = sum(g.get("FocusDealIns", 0) for g in gs)
        focus_melds = sum(g.get("FocusMelds", 0) for g in gs)
        ranks = [g["Rank"] for g in gs]
        seats = {g["Seed"]: g["Seat"] for g in gs}
        focus_turns = []
        win_turns = []
        draw_turns = []
        yaku = {}
        for r in rs:
            if r["Winners"]:
                win_turns.append(r["DiscardTurn"])
                draw_turns.append(r["DrawTurn"])
                if seats.get(r["Seed"]) in r["Winners"]:
                    focus_turns.append(r["DiscardTurn"])
            for y in r.get("Yaku") or []:
                yaku[y] = yaku.get(y, 0) + 1
        out[name] = {
            "variant": name,
            "model": item["model"],
            "games": len(gs),
            "rounds": rounds,
            "avgRank": statistics.mean(ranks) if ranks else 0,
            "top1Rate": sum(1 for r in ranks if r == 1) / max(1, len(ranks)),
            "top2Rate": sum(1 for r in ranks if r <= 2) / max(1, len(ranks)),
            "roundWinRate": focus_wins / max(1, rounds),
            "dealInRate": focus_deals / max(1, rounds),
            "meldsPerRound": focus_melds / max(1, rounds),
            "winTurn": {"mean": statistics.mean(win_turns) if win_turns else 0,
                        "p50": pct(win_turns, .5), "p90": pct(win_turns, .9)},
            "drawTurn": {"mean": statistics.mean(draw_turns) if draw_turns else 0,
                         "p50": pct(draw_turns, .5), "p90": pct(draw_turns, .9)},
            "focusWinTurn": {"mean": statistics.mean(focus_turns) if focus_turns else 0,
                             "p50": pct(focus_turns, .5), "p90": pct(focus_turns, .9)},
            "yakuCounts": yaku,
            "yakuCoverage": len(yaku),
            "focusWinTurns": focus_turns,
            "seedRanks": {g["Seed"]: g["Rank"] for g in gs},
            "seedWins": {g["Seed"]: g.get("FocusWins", 0) / max(1, g.get("Rounds", 0)) for g in gs},
            "seedDeals": {g["Seed"]: g.get("FocusDealIns", 0) / max(1, g.get("Rounds", 0)) for g in gs},
        }
    return out, files


def paired(base, wide, key):
    seeds = sorted(set(base[key]) & set(wide[key]))
    if key == "seedRanks":
        a = [1 if base[key][s] == 1 else 0 for s in seeds]
        b = [1 if wide[key][s] == 1 else 0 for s in seeds]
    else:
        a = [base[key][s] for s in seeds]
        b = [wide[key][s] for s in seeds]
    d = [y - x for x, y in zip(a, b)]
    if not d:
        return 0, 0, 0
    random.seed(1)
    boots = sorted(statistics.mean([d[random.randrange(len(d))] for _ in d]) for _ in range(3000))
    return statistics.mean(d), boots[int(.025 * len(boots))], boots[int(.975 * len(boots))]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    root = Path(args.root)
    out = Path(args.out)
    variants, files = merge(root)
    lines = ["# A/B 汇总", "", f"- shards: {len(files)}"]
    lines.append("| 组 | 局数 | 平均顺位 | top1 | top2 | 和牌率 | 放铳率 | 副露/局 | discardTurn | drawTurn | 模型 |")
    lines.append("|---|---:|---:|---:|---:|---:|---:|---:|---|---|---|")
    for name in ("baseline", "pursuit"):
        v = variants.get(name)
        if not v:
            continue
        lines.append(
            f"| {name} | {v['games']} | {v['avgRank']:.3f} | {v['top1Rate']:.3f} | {v['top2Rate']:.3f} | "
            f"{v['roundWinRate']:.3f} | {v['dealInRate']:.3f} | {v['meldsPerRound']:.3f} | "
            f"{v['winTurn']['mean']:.2f}/{v['winTurn']['p50']:.0f}/{v['winTurn']['p90']:.0f} | "
            f"{v['drawTurn']['mean']:.2f}/{v['drawTurn']['p50']:.0f}/{v['drawTurn']['p90']:.0f} | {v['model']} |")
    if "baseline" in variants and "pursuit" in variants:
        lines += ["", "## 配对差异（pursuit − baseline）", "", "| 指标 | 差值 | 95% CI |", "|---|---:|---:|"]
        for key, label in (("seedRanks", "top1"), ("seedWins", "和牌率"), ("seedDeals", "放铳率")):
            mean, lo, hi = paired(variants["baseline"], variants["pursuit"], key)
            lines.append(f"| {label} | {mean:+.4f} | {lo:+.4f} ~ {hi:+.4f} |")
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    (out.with_suffix(".json")).write_text(json.dumps({"variants": variants, "files": [str(f) for f in files]},
                                                       ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(out)


if __name__ == "__main__":
    main()
