"""自对弈日志统计。

读取 selfplay_*.jsonl（每局一行），输出神经座 / 基线座的平均顺位、一位率、局数。
用法：python tools/selfplay_report.py [日志文件或目录...]
"""

import glob
import json
import os
import sys
from collections import defaultdict


def load(paths):
    rows = []
    for p in paths:
        if os.path.isdir(p):
            files = sorted(glob.glob(os.path.join(p, "selfplay_*.jsonl")))
        else:
            files = [p]
        for f in files:
            with open(f, encoding="utf-8") as fh:
                for line in fh:
                    line = line.strip()
                    if not line:
                        continue
                    try:
                        rows.append(json.loads(line))
                    except json.JSONDecodeError:
                        pass
    return rows


def summarize(name, rows):
    if not rows:
        print(f"{name}: 0 局")
        return
    ranks = [r["rank"] for r in rows]
    wins = sum(1 for r in ranks if r == 1)
    print(f"{name}: {len(rows)} 局 · 平均顺位 {sum(ranks) / len(ranks):.2f} · 一位率 {wins / len(ranks):.1%}")
    per_seat = defaultdict(list)
    for r in rows:
        per_seat[r["seat"]].append(r["rank"])
    for seat in sorted(per_seat):
        rs = per_seat[seat]
        print(f"  座 {seat}: {len(rs)} 局 平均 {sum(rs) / len(rs):.2f}")


def main():
    args = sys.argv[1:]
    if not args:
        args = [os.path.join(os.path.expanduser("~"), "AppData", "Roaming", "TiisuMahjong", "logs")]
    rows = load(args)
    neural = [r for r in rows if r.get("neural")]
    base = [r for r in rows if not r.get("neural")]
    summarize("神经座", neural)
    summarize("基线座", base)
    if neural and base:
        print(f"合计: {len(rows)} 局")


if __name__ == "__main__":
    main()
