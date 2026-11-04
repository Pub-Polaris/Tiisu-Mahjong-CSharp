"""7Neuu v2：场面特征训练（181 维）。

特征布局（Python 与 C# 必须一致）：
  0..33    手牌计数
  34..169  四家牌河计数（seat0..3 各 34，除以 4）
  170..173 四家副露数（除以 4）
  174      牌山 / 83
  175      自己巡目 / 18
  176      本场 / 8
  177      是否庄家
  178..180 座次 one-hot（南/西/北）

数据：dist/dataset/guobiao_state.jsonl（国标，带四家牌河）
      dist/dataset/discards.jsonl（人类，只有自己牌河）

用法：python ml/train_policy_v2.py --out ml/policy_weights_v2.json --epochs 2
"""

import argparse
import json
import os

import numpy as np
import torch
import torch.nn as nn

FEATURES = 181
HIDDEN = 256


def hand_counts(hand):
    counts = np.zeros(34, dtype=np.float32)
    num = ""
    for ch in hand:
        if ch.isdigit():
            num += ch
            continue
        base = {"m": 0, "p": 9, "s": 18, "z": 27}.get(ch)
        if base is not None:
            for n in num:
                idx = base + int(n) - 1
                if 0 <= idx < 34:
                    counts[idx] += 1
        num = ""
    return counts


def featurize(obj):
    x = np.zeros(FEATURES, dtype=np.float32)
    x[0:34] = hand_counts(obj.get("hand", ""))

    rivers = obj.get("rivers")
    if isinstance(rivers, list) and len(rivers) == 4:
        for seat in range(4):
            row = rivers[seat]
            if isinstance(row, list) and len(row) == 34:
                x[34 + seat * 34:34 + (seat + 1) * 34] = np.asarray(row, dtype=np.float32) / 4.0
    else:
        # 老格式（人类日志）：只有自己的弃牌列表
        own = obj.get("discards") or []
        seat = {"south": 1, "west": 2, "north": 3}.get(str(obj.get("seat", "")), 0)
        for t in own:
            try:
                idx = {"m": 0, "p": 9, "s": 18, "z": 27}[t[-1]] + int(t[0]) - 1
            except (KeyError, ValueError):
                continue
            if 0 <= idx < 34:
                x[34 + seat * 34 + idx] += 1 / 4.0

    meld_counts = obj.get("meldCounts")
    if isinstance(meld_counts, list) and len(meld_counts) == 4:
        x[170:174] = np.asarray(meld_counts, dtype=np.float32) / 4.0
    else:
        melds = obj.get("melds") or []
        seat = {"south": 1, "west": 2, "north": 3}.get(str(obj.get("seat", "")), 0)
        x[170 + seat] = min(len(melds), 4) / 4.0

    x[174] = min(float(obj.get("wall", 0)), 83.0) / 83.0
    x[175] = min(float(obj.get("turn", 0)), 18.0) / 18.0
    x[176] = min(float(obj.get("h", 0)), 8.0) / 8.0
    x[177] = 1.0 if obj.get("dealer") else 0.0
    seat_name = str(obj.get("seat", ""))
    if seat_name in ("south", "west", "north"):
        x[178 + ["south", "west", "north"].index(seat_name)] = 1.0
    return x


def load(paths):
    xs, ys, srcs = [], [], []
    for path, weight in paths:
        if not os.path.exists(path):
            print("missing", path)
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
                if x[idx] <= 0:
                    continue
                xs.append(x)
                ys.append(idx)
                srcs.append(os.path.basename(path).replace(".jsonl", ""))
    return np.stack(xs), np.array(ys, dtype=np.int64), np.array(srcs)


class Policy(nn.Module):
    def __init__(self):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(FEATURES, HIDDEN), nn.ReLU(),
            nn.Linear(HIDDEN, HIDDEN), nn.ReLU(),
            nn.Linear(HIDDEN, 34),
        )

    def forward(self, x):
        return self.net(x)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="dist/dataset")
    ap.add_argument("--out", default="ml/policy_weights_v2.json")
    ap.add_argument("--epochs", type=int, default=2)
    ap.add_argument("--batch", type=int, default=4096)
    ap.add_argument("--lr", type=float, default=1e-3)
    args = ap.parse_args()

    paths = [
        (os.path.join(args.data, "guobiao_state.jsonl"), 1.0),
        (os.path.join(args.data, "discards.jsonl"), 5.0),
    ]
    print("loading...")
    X, y, src = load(paths)
    print("samples", len(y), "features", X.shape[1], {s: int((src == s).sum()) for s in set(src)})

    rng = np.random.default_rng(42)
    perm = rng.permutation(len(y))
    cut = int(len(y) * 0.95)
    tr, va = perm[:cut], perm[cut:]

    device = torch.device("xpu" if hasattr(torch, "xpu") and torch.xpu.is_available() else "cpu")
    model = Policy().to(device)
    opt = torch.optim.Adam(model.parameters(), lr=args.lr)
    loss_fn = nn.CrossEntropyLoss()
    Xt, yt = torch.from_numpy(X), torch.from_numpy(y)

    print("device", device)
    for epoch in range(args.epochs):
        model.train()
        order = rng.permutation(tr)
        total = 0.0
        for i in range(0, len(order), args.batch):
            idx = order[i:i + args.batch]
            xb = Xt[idx].to(device)
            yb = yt[idx].to(device)
            out = model(xb).masked_fill(~(xb[:, :34] > 0), -1e9)
            loss = loss_fn(out, yb)
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

    sd = {k: v.detach().cpu().numpy().astype(np.float32) for k, v in model.state_dict().items()}
    payload = {
        "version": 2,
        "features": FEATURES,
        "hidden": HIDDEN,
        "layers": [
            {"w": sd["net.0.weight"].tolist(), "b": sd["net.0.bias"].tolist()},
            {"w": sd["net.2.weight"].tolist(), "b": sd["net.2.bias"].tolist()},
            {"w": sd["net.4.weight"].tolist(), "b": sd["net.4.bias"].tolist()},
        ],
        "val_top1": float(hit.mean()),
        "samples": int(len(y)),
    }
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(payload, f, separators=(",", ":"))
    print("saved", args.out)


if __name__ == "__main__":
    main()
