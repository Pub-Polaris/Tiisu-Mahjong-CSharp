#!/usr/bin/env python3
"""Train the 339-dim policy MLP on Intel XPU (fallback CPU).

Data is read from the memmap shards produced by ml/build_dataset.py, not loaded
into a Python list. Production v4 weights are never overwritten.
"""
from __future__ import annotations

import argparse
import json
import math
import time
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F


class PolicyNet(nn.Module):
    def __init__(self, features: int, hidden: int):
        super().__init__()
        self.fc1 = nn.Linear(features, hidden)
        self.fc2 = nn.Linear(hidden, hidden)
        self.fc3 = nn.Linear(hidden, 34)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        x = F.relu(self.fc1(x))
        x = F.relu(self.fc2(x))
        return self.fc3(x)


def load_shards(root: Path):
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    shards = []
    for meta in manifest:
        shards.append({
            "x": np.load(meta["x"], mmap_mode="r"),
            "y": np.load(meta["y"], mmap_mode="r"),
            "g": np.load(meta["g"], mmap_mode="r"),
        })
    return shards


def normalize_stats(shards, features: int) -> tuple[np.ndarray, np.ndarray]:
    total = 0
    s = np.zeros(features, dtype=np.float64)
    ss = np.zeros(features, dtype=np.float64)
    for sh in shards:
        x = sh["x"]
        for start in range(0, len(x), 200_000):
            chunk = np.asarray(x[start:start + 200_000], dtype=np.float64)
            s += chunk.sum(axis=0)
            ss += (chunk * chunk).sum(axis=0)
            total += len(chunk)
    mean = s / total
    var = np.maximum(ss / total - mean * mean, 0.0)
    std = np.sqrt(var)
    std[std < 1e-6] = 1.0
    return mean.astype(np.float32), std.astype(np.float32)


def sample_batch(shards, indices, mean, std):
    xs = []
    ys = []
    for idx in indices:
        shard_index, local = idx
        x = shards[shard_index]["x"]
        xs.append(x[local])
        ys.append(int(shards[shard_index]["y"][local]))
    x = (np.asarray(xs, dtype=np.float32) - mean) / std
    return torch.from_numpy(x), torch.from_numpy(np.asarray(ys, dtype=np.int64))


def evaluate(model, shards, indices, mean, std, batch_size, device):
    correct = 0
    total = 0
    with torch.no_grad():
        for start in range(0, len(indices), batch_size):
            part = indices[start:start + batch_size]
            x, y = sample_batch(shards, part, mean, std)
            x = x.to(device, non_blocking=True)
            y = y.to(device, non_blocking=True)
            pred = model(x).argmax(dim=1)
            correct += int((pred == y).sum().item())
            total += len(part)
    return correct / max(1, total)


def warm_start(model: PolicyNet, path: str) -> None:
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    layers = data.get("layers") or []
    if len(layers) < 3:
        raise SystemExit(f"init weights missing layers: {path}")
    with torch.no_grad():
        for target, layer in ((model.fc1, layers[0]), (model.fc2, layers[1]),
                              (model.fc3, layers[2])):
            w = torch.as_tensor(layer["w"], dtype=torch.float32)
            b = torch.as_tensor(layer["b"], dtype=torch.float32)
            out_dim = min(target.weight.shape[0], w.shape[0])
            in_dim = min(target.weight.shape[1], w.shape[1])
            target.weight.zero_()
            target.bias.zero_()
            target.weight[:out_dim, :in_dim] = w[:out_dim, :in_dim]
            target.bias[:out_dim] = b[:out_dim]
    print(f"warm-start {path}")


def export(model, features, hidden, mean, std, out_path: Path, args, val_acc):
    def to_list(t):
        return t.detach().to("cpu", dtype=torch.float32).numpy().tolist()

    data = {
        "version": 4,
        "model_version": "0.0.7-pre",
        "features": features,
        "hidden": hidden,
        "layers": [
            {"w": to_list(model.fc1.weight), "b": to_list(model.fc1.bias)},
            {"w": to_list(model.fc2.weight), "b": to_list(model.fc2.bias)},
            {"w": to_list(model.fc3.weight), "b": to_list(model.fc3.bias)},
        ],
        "val_top1": val_acc,
        "feature_mean": mean.tolist(),
        "feature_std": std.tolist(),
        "selfplay": True,
        "training": {
            "device": args.device,
            "epochs": args.epochs,
            "batch": args.batch,
            "lr": args.lr,
            "grad_clip": args.grad_clip,
            "init_weights": args.init_weights,
        },
    }
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(data, ensure_ascii=False) + "\n", encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dataset", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--init-weights", default=None)
    ap.add_argument("--hidden", type=int, default=384)
    ap.add_argument("--epochs", type=int, default=3)
    ap.add_argument("--batch", type=int, default=4096)
    ap.add_argument("--lr", type=float, default=3e-4)
    ap.add_argument("--grad-clip", type=float, default=5.0)
    ap.add_argument("--val-fraction", type=float, default=0.05)
    ap.add_argument("--max-samples", type=int, default=0)
    ap.add_argument("--device", default="auto", choices=["auto", "xpu", "cpu"])
    args = ap.parse_args()

    if args.device == "auto":
        args.device = "xpu" if torch.xpu.is_available() else "cpu"
    if args.device == "xpu" and not torch.xpu.is_available():
        raise SystemExit("xpu requested but torch.xpu is unavailable")
    device = torch.device(args.device)
    if args.device == "xpu":
        torch.xpu.manual_seed(1)
    else:
        torch.manual_seed(1)
    print(f"torch={torch.__version__} device={args.device} "
          f"xpu_available={torch.xpu.is_available()}", flush=True)

    root = Path(args.dataset)
    shards = load_shards(root)
    lengths = [len(s["x"]) for s in shards]
    total = sum(lengths)
    features = shards[0]["x"].shape[1]
    if features != 339:
        raise SystemExit(f"dataset features={features}, expected 339")
    print(f"dataset shards={len(shards)} samples={total:,}", flush=True)

    # Global game ids are already unique across shards.
    rng = np.random.default_rng(1)
    max_game = max(int(s["g"].max()) for s in shards if len(s["g"]))
    val_games = set(rng.choice(np.arange(max_game + 1),
                               size=max(1, int((max_game + 1) * args.val_fraction)),
                               replace=False).tolist())
    train = []
    val = []
    for si, sh in enumerate(shards):
        g = sh["g"]
        for start in range(0, len(g), 200_000):
            chunk = g[start:start + 200_000]
            vals = np.fromiter((bool(v in val_games) for v in chunk), dtype=bool, count=len(chunk))
            local = np.arange(start, start + len(chunk))
            val.extend((si, int(i)) for i in local[vals])
            train.extend((si, int(i)) for i in local[~vals])
    if args.max_samples > 0:
        rng.shuffle(train)
        train = train[:args.max_samples]
        val = val[:max(1, args.max_samples // 20)]
    print(f"train={len(train):,} val={len(val):,} val_games={len(val_games)}", flush=True)

    mean, std = normalize_stats(shards, features)
    model = PolicyNet(features, args.hidden).to(device)
    if args.init_weights:
        warm_start(model, args.init_weights)
    opt = torch.optim.AdamW(model.parameters(), lr=args.lr, weight_decay=1e-4)
    best = 0.0
    stale = 0
    start = time.time()
    for epoch in range(args.epochs):
        rng.shuffle(train)
        model.train()
        running = 0.0
        for step, begin in enumerate(range(0, len(train), args.batch), 1):
            part = train[begin:begin + args.batch]
            x, y = sample_batch(shards, part, mean, std)
            x = x.to(device, non_blocking=True)
            y = y.to(device, non_blocking=True)
            logits = model(x)
            loss = F.cross_entropy(logits, y, label_smoothing=0.02)
            if not torch.isfinite(loss):
                raise SystemExit(f"non-finite loss epoch={epoch + 1} step={step}")
            opt.zero_grad(set_to_none=True)
            loss.backward()
            torch.nn.utils.clip_grad_norm_(model.parameters(), args.grad_clip)
            for p in model.parameters():
                if not torch.isfinite(p).all():
                    raise SystemExit(f"non-finite parameter epoch={epoch + 1} step={step}")
            opt.step()
            running += float(loss.item())
            if step % 100 == 0:
                print(f"epoch {epoch + 1} step {step} loss={running / 100:.4f} "
                      f"elapsed={time.time() - start:.0f}s", flush=True)
                running = 0.0
        val_acc = evaluate(model, shards, val, mean, std, args.batch, device)
        print(f"epoch {epoch + 1}/{args.epochs} val_top1={val_acc:.4f} "
              f"elapsed={time.time() - start:.0f}s", flush=True)
        if val_acc > best:
            best = val_acc
            stale = 0
        else:
            stale += 1
            if stale >= 1:
                print("early stop")
                break

    export(model, features, args.hidden, mean, std, Path(args.out), args, best)
    print(f"saved {args.out}")


if __name__ == "__main__":
    main()
