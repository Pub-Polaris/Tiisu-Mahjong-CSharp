#!/usr/bin/env python3
"""Stream shard JSONL -> memmap arrays for XPU/CPU training.

Does not load a whole shard's JSON objects into memory. Each input shard becomes:
  X.npy  (N, 339) float32
  y.npy  (N,)     int8
  g.npy  (N,)     int32  global game id
  shard.json
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np


def count_lines(path: Path) -> int:
    n = 0
    with path.open("rb") as f:
        for _ in f:
            n += 1
    return n


def convert_shard(src: Path, out_dir: Path, game_offset: int, sample_every: int = 1) -> dict:
    n = count_lines(src)
    out_n = (n + sample_every - 1) // sample_every
    out_dir.mkdir(parents=True, exist_ok=True)
    x_path = out_dir / "X.npy"
    y_path = out_dir / "y.npy"
    g_path = out_dir / "g.npy"

    first = None
    with src.open("r", encoding="utf-8") as f:
        for line in f:
            if line.strip():
                first = json.loads(line)
                break
    if first is None:
        raise SystemExit(f"empty shard: {src}")
    features = len(first["Features"])
    if features != 339:
        raise SystemExit(f"{src}: features={features}, expected 339")

    x = np.lib.format.open_memmap(x_path, mode="w+", dtype=np.float32, shape=(out_n, features))
    y = np.lib.format.open_memmap(y_path, mode="w+", dtype=np.int8, shape=(out_n,))
    g = np.lib.format.open_memmap(g_path, mode="w+", dtype=np.int32, shape=(out_n,))

    i = 0
    read = 0
    with src.open("r", encoding="utf-8") as f:
        for line in f:
            if not line.strip():
                continue
            if read % sample_every == 0:
                row = json.loads(line)
                feat = np.asarray(row["Features"], dtype=np.float32)
                if feat.shape != (features,) or not np.isfinite(feat).all():
                    raise SystemExit(f"{src}: bad features at sample {i}")
                action = int(row["Action"])
                if action < 0 or action >= 34:
                    raise SystemExit(f"{src}: bad action {action} at sample {i}")
                x[i] = feat
                y[i] = action
                g[i] = game_offset + int(row.get("Game", 0))
                i += 1
                if i % 500_000 == 0:
                    print(f"{src.name}: {i:,}/{out_n:,}", flush=True)
            read += 1

    if i != out_n:
        raise SystemExit(f"{src}: expected {out_n} sampled rows, wrote {i}")
    x.flush(); y.flush(); g.flush()
    meta = {
        "source": str(src),
        "samples": out_n,
        "sourceLines": n,
        "sampleEvery": sample_every,
        "features": features,
        "gameOffset": game_offset,
        "x": str(x_path),
        "y": str(y_path),
        "g": str(g_path),
    }
    (out_dir / "shard.json").write_text(
        json.dumps(meta, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"done {src.name}: {out_n:,} samples (every {sample_every})", flush=True)
    return meta


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True, help="selfplay-100k directory")
    ap.add_argument("--out", required=True, help="dataset output directory")
    ap.add_argument("--game-offset", type=int, default=0)
    ap.add_argument("--sample-every", type=int, default=1)
    args = ap.parse_args()
    sample_every = max(1, args.sample_every)

    root = Path(args.root)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    shards = sorted(root.glob("shard-*/selfplay.jsonl"))
    if not shards:
        raise SystemExit(f"no shards under {root}")
    metas = []
    for idx, src in enumerate(shards):
        print(f"[{idx + 1}/{len(shards)}] {src}", flush=True)
        metas.append(convert_shard(src, out / src.parent.name,
                                   args.game_offset + idx * 10_000, sample_every))
    (out / "manifest.json").write_text(
        json.dumps(metas, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"dataset: {len(metas)} shards, {sum(m['samples'] for m in metas):,} samples")


if __name__ == "__main__":
    main()
