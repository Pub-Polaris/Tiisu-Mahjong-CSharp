"""自博弈弃牌策略训练：读取 `--export-selfplay` 数据集，按奖励函数加权 BC。

用法：
    dotnet ... --export-selfplay dist/selfplay.jsonl --games 500 --mode 4
    python ml/train_policy_selfplay.py --data dist/selfplay.jsonl --out ml/policy_weights_v3.json --epochs 8

奖励（可在 RewardConfig 调）：
- 终局点数差（/1000）
- 一位大幅领先时，正收益按领先幅度衰减
- 吃碰和牌、向听变差、长期不降向听、错和、放铳扣分
- 四位劣势时弃和/不冒进给极小正分
"""

from __future__ import annotations

import argparse
import json
import math
import random
from dataclasses import dataclass

import numpy as np


@dataclass
class RewardConfig:
    points_scale: float = 1000.0
    call_win_penalty: float = 0.30
    shanten_worse_penalty: float = 0.02
    shanten_better_bonus: float = 0.002
    no_progress_penalty: float = 0.004
    no_progress_after: int = 3
    deal_in_extra: float = 0.50
    misfire_penalty: float = 0.50
    defensive_bonus: float = 0.006
    lead_decay: float = 50000.0


def compute_rewards(rows: list[dict], cfg: RewardConfig) -> list[float]:
    rewards = []
    for r in rows:
        base = (r["FinalScore"] - r["InitialScore"]) / cfg.points_scale
        reward = base
        if r.get("FinalRank") == 1 and base > 0 and r.get("Lead", 0) > 0:
            reward = base / (1.0 + r["Lead"] / cfg.lead_decay)
        reward -= cfg.call_win_penalty * r.get("CallWins", 0)
        reward -= cfg.misfire_penalty * r.get("Misfires", 0)
        if r.get("DealsIn", 0) > 0:
            reward -= cfg.deal_in_extra * abs(base)
        if r["ShantenAfter"] > r["ShantenBefore"]:
            reward -= cfg.shanten_worse_penalty
        elif r["ShantenAfter"] < r["ShantenBefore"]:
            reward += cfg.shanten_better_bonus
        if r.get("FinalRank") == 4 and base < 0 and r["ShantenAfter"] >= r["ShantenBefore"]:
            reward += cfg.defensive_bonus
        rewards.append(reward)

    # 长期不降向听：同 game/player 按 step 顺序，连续 3 次没有刷新最好向听就扣分。
    order = sorted(range(len(rows)), key=lambda i: (rows[i].get("Game", 0), rows[i]["Player"], rows[i].get("Step", 0)))
    best: dict[tuple[int, int], int] = {}
    stale: dict[tuple[int, int], int] = {}
    for i in order:
        r = rows[i]
        key = (r.get("Game", 0), r["Player"])
        cur = r["ShantenAfter"]
        old = best.get(key, 99)
        if cur < old:
            best[key] = cur
            stale[key] = 0
        else:
            stale[key] = stale.get(key, 0) + 1
            if stale[key] >= cfg.no_progress_after:
                rewards[i] -= cfg.no_progress_penalty
    return rewards


def load_rows(path: str) -> list[dict]:
    rows = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def one_hot(y: np.ndarray, classes: int = 34) -> np.ndarray:
    out = np.zeros((len(y), classes), dtype=np.float32)
    out[np.arange(len(y)), y] = 1.0
    return out


def softmax(z: np.ndarray) -> np.ndarray:
    z = z - z.max(axis=1, keepdims=True)
    e = np.exp(z)
    return e / e.sum(axis=1, keepdims=True)


def init_params(feature_count: int, hidden: int, rng_np: np.random.Generator,
                init_path: str | None) -> list[np.ndarray]:
    w1 = rng_np.normal(0, math.sqrt(2.0 / feature_count), (hidden, feature_count)).astype(np.float32)
    b1 = np.zeros(hidden, dtype=np.float32)
    w2 = rng_np.normal(0, math.sqrt(2.0 / hidden), (hidden, hidden)).astype(np.float32)
    b2 = np.zeros(hidden, dtype=np.float32)
    w3 = rng_np.normal(0, math.sqrt(1.0 / hidden), (34, hidden)).astype(np.float32)
    b3 = np.zeros(34, dtype=np.float32)
    if not init_path:
        return [w1, b1, w2, b2, w3, b3]

    with open(init_path, encoding="utf-8") as f:
        old = json.load(f)
    layers = old.get("layers") or []
    if len(layers) < 3:
        raise SystemExit(f"init weights 缺层: {init_path}")
    ow1 = np.asarray(layers[0]["w"], dtype=np.float32)
    ob1 = np.asarray(layers[0]["b"], dtype=np.float32)
    ow2 = np.asarray(layers[1]["w"], dtype=np.float32)
    ob2 = np.asarray(layers[1]["b"], dtype=np.float32)
    ow3 = np.asarray(layers[2]["w"], dtype=np.float32)
    ob3 = np.asarray(layers[2]["b"], dtype=np.float32)
    if ow1.shape[1] != feature_count:
        raise SystemExit(f"init features 不匹配: {ow1.shape[1]} != {feature_count}")
    old_hidden = min(hidden, ow1.shape[0], ow2.shape[0], ow2.shape[1], ow3.shape[1])
    w1[:] = 0
    w2[:] = 0
    w3[:] = 0
    b1[:] = 0
    b2[:] = 0
    b3[:] = 0
    w1[:old_hidden] = ow1[:old_hidden]
    b1[:old_hidden] = ob1[:old_hidden]
    w2[:old_hidden, :old_hidden] = ow2[:old_hidden, :old_hidden]
    b2[:old_hidden] = ob2[:old_hidden]
    w3[:, :old_hidden] = ow3[:, :old_hidden]
    b3[:] = ob3
    print(f"warm-start {init_path}: old_hidden={old_hidden} new_hidden={hidden}")
    return [w1, b1, w2, b2, w3, b3]


def train(args: argparse.Namespace) -> None:
    rows = load_rows(args.data)
    if not rows:
        raise SystemExit("数据集为空")
    feature_count = len(rows[0]["Features"])
    bad = [i for i, r in enumerate(rows) if len(r["Features"]) != feature_count]
    if bad:
        raise SystemExit(f"特征长度不一致，首行 {feature_count}，第 {bad[0]} 行 {len(rows[bad[0]]['Features'])}")

    raw_rewards = np.asarray(compute_rewards(rows, RewardConfig()), dtype=np.float32)
    # 数值归一化：tanh 压到 [-1,1]，不把 -80 这种极值直接喂给权重。
    rewards = np.tanh(raw_rewards / max(1.0, float(args.reward_scale)))
    x = np.asarray([r["Features"] for r in rows], dtype=np.float32)
    y = np.asarray([r["Action"] for r in rows], dtype=np.int64)
    if not np.isfinite(x).all():
        bad = int(np.argwhere(~np.isfinite(x))[0][0])
        raise SystemExit(f"特征含 NaN/Inf，第 {bad} 行")
    if y.min() < 0 or y.max() >= 34:
        raise SystemExit(f"Action 越界: [{y.min()}, {y.max()}]")
    feature_mean = x.mean(axis=0)
    feature_std = x.std(axis=0)
    feature_std[feature_std < 1e-6] = 1.0
    x = (x - feature_mean) / feature_std
    if not np.isfinite(x).all():
        raise SystemExit("标准化后出现 NaN/Inf")
    weights = np.exp(np.clip(rewards, -2.0, 2.0)).astype(np.float32)
    weights /= weights.mean()

    games = sorted({r.get("Game", 0) for r in rows})
    rng = random.Random(args.seed)
    rng.shuffle(games)
    val_games = set(games[: max(1, len(games) // 10)])
    val = np.array([r.get("Game", 0) in val_games for r in rows])
    tr = ~val
    print(f"samples={len(rows)} features={feature_count} train={int(tr.sum())} val={int(val.sum())} "
          f"reward=[{rewards.min():.3f},{rewards.max():.3f}] raw=[{raw_rewards.min():.1f},{raw_rewards.max():.1f}]")

    rng_np = np.random.default_rng(args.seed)
    hidden = args.hidden
    params = init_params(feature_count, hidden, rng_np, args.init_weights)
    w1, b1, w2, b2, w3, b3 = params
    m = [np.zeros_like(p) for p in params]
    v = [np.zeros_like(p) for p in params]
    beta1, beta2, eps = 0.9, 0.999, 1e-8
    t = 0
    batch = args.batch

    best_acc = 0.0
    best_params: list[np.ndarray] | None = None
    stale_epochs = 0
    for epoch in range(args.epochs):
        idx = np.where(tr)[0]
        rng_np.shuffle(idx)
        for start in range(0, len(idx), batch):
            sel = idx[start:start + batch]
            xb = x[sel]
            yb = y[sel]
            wb = weights[sel][:, None]

            h1 = np.maximum(xb @ w1.T + b1, 0)
            h2 = np.maximum(h1 @ w2.T + b2, 0)
            logits = h2 @ w3.T + b3
            p = softmax(logits)
            one = one_hot(yb)
            if args.label_smoothing > 0:
                one = one * (1.0 - args.label_smoothing) + args.label_smoothing / 34.0
            loss = -(np.log(np.clip(p[np.arange(len(sel)), yb], 1e-8, 1.0)) * wb[:, 0]).mean()

            dlogits = (p - one) * wb / len(sel)
            dw3 = dlogits.T @ h2
            db3 = dlogits.sum(axis=0)
            dh2 = (dlogits @ w3) * (h2 > 0)
            dw2 = dh2.T @ h1
            db2 = dh2.sum(axis=0)
            dh1 = (dh2 @ w2) * (h1 > 0)
            dw1 = dh1.T @ xb
            db1 = dh1.sum(axis=0)
            grads = [dw1, db1, dw2, db2, dw3, db3]
            if not all(np.isfinite(g).all() for g in grads):
                raise SystemExit(f"梯度出现 NaN/Inf（epoch={epoch + 1}, step={t + 1}）")

            t += 1
            for i in range(len(params)):
                g = np.clip(grads[i], -args.grad_clip, args.grad_clip)
                m[i] = beta1 * m[i] + (1 - beta1) * g
                v[i] = beta2 * v[i] + (1 - beta2) * (g ** 2)
                mh = m[i] / (1 - beta1 ** t)
                vh = v[i] / (1 - beta2 ** t)
                params[i] -= args.lr * mh / (np.sqrt(vh) + eps)
            if not all(np.isfinite(p).all() for p in params):
                raise SystemExit(f"参数出现 NaN/Inf（epoch={epoch + 1}, step={t}）")

        if val.any():
            hv1 = np.maximum(x[val] @ w1.T + b1, 0)
            hv2 = np.maximum(hv1 @ w2.T + b2, 0)
            pred = (hv2 @ w3.T + b3).argmax(axis=1)
            acc = float((pred == y[val]).mean())
        else:
            acc = 0.0
        print(f"epoch {epoch + 1}/{args.epochs} loss={loss:.4f} val_top1={acc:.4f}")
        if acc > best_acc:
            best_acc = acc
            best_params = [p.copy() for p in params]
            stale_epochs = 0
        else:
            stale_epochs += 1
            if stale_epochs >= args.patience:
                print(f"early stop: val_top1 连续 {stale_epochs} 轮未提升")
                break

    if best_params is not None:
        w1, b1, w2, b2, w3, b3 = best_params
        print(f"restore best val_top1={best_acc:.4f}")

    out = {
        "version": 4,
        "model_version": "0.0.7-pre",
        "features": feature_count,
        "hidden": hidden,
        "layers": [
            {"w": w1.tolist(), "b": b1.tolist()},
            {"w": w2.tolist(), "b": b2.tolist()},
            {"w": w3.tolist(), "b": b3.tolist()},
        ],
        "val_top1": best_acc,
        "samples": len(rows),
        "feature_mean": feature_mean.tolist(),
        "feature_std": feature_std.tolist(),
        "selfplay": True,
        "training": {
            "grad_clip": args.grad_clip,
            "patience": args.patience,
            "init_weights": args.init_weights,
        },
    }
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False)
    print(f"saved {args.out}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data", required=True)
    parser.add_argument("--out", default="ml/policy_weights_v3.json")
    parser.add_argument("--epochs", type=int, default=8)
    parser.add_argument("--hidden", type=int, default=128)
    parser.add_argument("--batch", type=int, default=256)
    parser.add_argument("--lr", type=float, default=1e-3)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--reward-scale", type=float, default=20.0)
    parser.add_argument("--label-smoothing", type=float, default=0.02)
    parser.add_argument("--grad-clip", type=float, default=5.0)
    parser.add_argument("--patience", type=int, default=2)
    parser.add_argument("--init-weights", default=None)
    train(parser.parse_args())


if __name__ == "__main__":
    main()
