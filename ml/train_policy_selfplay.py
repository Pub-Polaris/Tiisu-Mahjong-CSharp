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
    feature_mean = x.mean(axis=0)
    feature_std = x.std(axis=0)
    feature_std[feature_std < 1e-6] = 1.0
    x = (x - feature_mean) / feature_std
    y = np.asarray([r["Action"] for r in rows], dtype=np.int64)
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
    w1 = rng_np.normal(0, math.sqrt(2.0 / feature_count), (hidden, feature_count)).astype(np.float32)
    b1 = np.zeros(hidden, dtype=np.float32)
    w2 = rng_np.normal(0, math.sqrt(2.0 / hidden), (hidden, hidden)).astype(np.float32)
    b2 = np.zeros(hidden, dtype=np.float32)
    w3 = rng_np.normal(0, math.sqrt(1.0 / hidden), (34, hidden)).astype(np.float32)
    b3 = np.zeros(34, dtype=np.float32)

    params = [w1, b1, w2, b2, w3, b3]
    m = [np.zeros_like(p) for p in params]
    v = [np.zeros_like(p) for p in params]
    beta1, beta2, eps = 0.9, 0.999, 1e-8
    t = 0
    batch = args.batch

    best_acc = 0.0
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

            t += 1
            for i in range(len(params)):
                m[i] = beta1 * m[i] + (1 - beta1) * grads[i]
                v[i] = beta2 * v[i] + (1 - beta2) * (grads[i] ** 2)
                mh = m[i] / (1 - beta1 ** t)
                vh = v[i] / (1 - beta2 ** t)
                params[i] -= args.lr * mh / (np.sqrt(vh) + eps)

        if val.any():
            hv1 = np.maximum(x[val] @ w1.T + b1, 0)
            hv2 = np.maximum(hv1 @ w2.T + b2, 0)
            pred = (hv2 @ w3.T + b3).argmax(axis=1)
            acc = float((pred == y[val]).mean())
        else:
            acc = 0.0
        print(f"epoch {epoch + 1}/{args.epochs} loss={loss:.4f} val_top1={acc:.4f}")
        best_acc = max(best_acc, acc)

    out = {
        "version": 4,
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
    train(parser.parse_args())


if __name__ == "__main__":
    main()
