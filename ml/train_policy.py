"""小策略网（弃牌）训练：局面特征 → 34 类 softmax。

数据：dist/dataset/{riichi,guobiao,discards}.jsonl（src 区分来源）
输出：ml/policy_weights.json（MLP 权重，供 C# 零依赖推理）

用法：
  python ml/train_policy.py --data dist/dataset --out ml/policy_weights.json --epochs 3
"""

import argparse
import json
import os
import random

import numpy as np
import torch
import torch.nn as nn

TILE_SUITS = "mpsz"
FEATURES = 34 + 7  # 手牌 34 + (wall, turn, honba, dealer, seat×3)


def hand_counts(hand):
    counts = np.zeros(34, dtype=np.float32)
    num = ""
    for ch in hand:
        if ch.isdigit():
            num += ch
            continue
        suit = {"m": 0, "p": 9, "s": 18, "z": 27}.get(ch)
        if suit is None:
            num = ""
            continue
        for n in num:
            idx = suit + int(n) - 1
            if 0 <= idx < 34:
                counts[idx] += 1
        num = ""
    return counts


def featurize(obj):
    x = np.zeros(FEATURES, dtype=np.float32)
    x[:34] = hand_counts(obj.get("hand", ""))
    x[34] = min(float(obj.get("wall", 0)), 83.0) / 83.0
    x[35] = min(float(obj.get("turn", 0)), 18.0) / 18.0
    x[36] = min(float(obj.get("h", 0)), 8.0) / 8.0
    x[37] = 1.0 if obj.get("dealer") else 0.0
    seat = str(obj.get("seat", ""))
    if seat in ("south", "west", "north"):
        x[38 + ["south", "west", "north"].index(seat)] = 1.0
    return x


def load(data_dir):
    files = [
        ("riichi.jsonl", 1.0),
        ("guobiao.jsonl", 1.0),
        ("discards.jsonl", 3.0),  # 你的对局，权重高一些
    ]
    xs, ys, srcs, ws = [], [], [], []
    for name, weight in files:
        path = os.path.join(data_dir, name)
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as f:
            for line in f:
                try:
                    obj = json.loads(line)
                except json.JSONDecodeError:
                    continue
                idx = int(obj.get("labelIdx", -1))
                if not 0 <= idx < 34:
                    continue
                x = featurize(obj)
                if x[:34][idx] <= 0:  # 标签必须在自己手里
                    continue
                xs.append(x)
                ys.append(idx)
                srcs.append(name.replace(".jsonl", ""))
                ws.append(weight)
    return np.stack(xs), np.array(ys, dtype=np.int64), np.array(srcs), np.array(ws, dtype=np.float32)


class Policy(nn.Module):
    def __init__(self, hidden=256):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(FEATURES, hidden), nn.ReLU(),
            nn.Linear(hidden, hidden), nn.ReLU(),
            nn.Linear(hidden, 34),
        )

    def forward(self, x):
        return self.net(x)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="dist/dataset")
    ap.add_argument("--out", default="ml/policy_weights.json")
    ap.add_argument("--epochs", type=int, default=3)
    ap.add_argument("--batch", type=int, default=4096)
    ap.add_argument("--lr", type=float, default=1e-3)
    ap.add_argument("--limit", type=int, default=0)
    args = ap.parse_args()

    print("loading...")
    X, y, src, w = load(args.data)
    if args.limit:
        X, y, src, w = X[:args.limit], y[:args.limit], src[:args.limit], w[:args.limit]
    print("samples", len(y), "features", X.shape[1], "src", {s: int((src == s).sum()) for s in set(src)})

    rng = np.random.default_rng(42)
    perm = rng.permutation(len(y))
    cut = int(len(y) * 0.95)
    tr, va = perm[:cut], perm[cut:]

    device = torch.device("xpu" if hasattr(torch, "xpu") and torch.xpu.is_available() else "cpu")
    print("device", device)

    model = Policy().to(device)
    opt = torch.optim.Adam(model.parameters(), lr=args.lr)
    loss_fn = nn.CrossEntropyLoss(reduction="none")

    Xt = torch.from_numpy(X)
    yt = torch.from_numpy(y)
    wt = torch.from_numpy(w)

    for epoch in range(args.epochs):
        model.train()
        order = rng.permutation(tr)
        total = 0.0
        for i in range(0, len(order), args.batch):
            idx = order[i:i + args.batch]
            xb = Xt[idx].to(device)
            yb = yt[idx].to(device)
            out = model(xb)
            # 只允许打自己手里的牌
            legal = xb[:, :34] > 0
            out = out.masked_fill(~legal, -1e9)
            loss = (loss_fn(out, yb) * wt[idx].to(device)).mean()
            opt.zero_grad()
            loss.backward()
            opt.step()
            total += float(loss.item()) * len(idx)
        print(f"epoch {epoch + 1}/{args.epochs} loss {total / len(order):.4f}")

    model.eval()
    with torch.no_grad():
        xb = Xt[va].to(device)
        out = model(xb).masked_fill(~(xb[:, :34] > 0), -1e9)
        pred = out.argmax(dim=1).cpu().numpy()
    hit = pred == y[va]
    print(f"val top-1 {hit.mean():.4f} ({len(va)} samples)")
    for s in sorted(set(src[va])):
        m = src[va] == s
        print(f"  {s}: {hit[m].mean():.4f} ({int(m.sum())})")

    # 导出 JSON 权重（float32 → list）
    sd = {k: v.detach().cpu().numpy().astype(np.float32) for k, v in model.state_dict().items()}
    payload = {
        "version": 1,
        "features": FEATURES,
        "hidden": 256,
        "layers": [
            {"w": sd["net.0.weight"].tolist(), "b": sd["net.0.bias"].tolist()},
            {"w": sd["net.2.weight"].tolist(), "b": sd["net.2.bias"].tolist()},
            {"w": sd["net.4.weight"].tolist(), "b": sd["net.4.bias"].tolist()},
        ],
        "val_top1": float(hit.mean()),
        "samples": int(len(y)),
    }
    os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(payload, f, separators=(",", ":"))
    print("saved", args.out)


if __name__ == "__main__":
    main()
