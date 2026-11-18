#!/usr/bin/env python3
"""Tiisu 牌桌皮肤示例图集生成器（美工交接用，不参与构建）。

产出：
  skin-atlas.png  —— 2× 交付图集（RGBA，1024×1024），六块区域按下面 REGIONS 的坐标排布
  skin.json       —— 与图集配套的清单（本脚本只写图片，清单是手写的，见同目录 skin.json）

美术换皮时不需要改这个脚本：直接按 skin.json 的区域名交付 PNG/图集即可。
这里生成一张"可看的示例"，用来验证 TableSkin / SkinStore / 静态层缓存链路
（预览：TIISU_SKIN=<本目录> Tiisu.DevTools --render-table-png ...）。
"""

from __future__ import annotations

import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT_DIR = os.path.dirname(os.path.abspath(__file__))
SIZE = 1024
LOGICAL_SCALE = 2  # 图集是 2× 交付：清单里 scale = 2


def rounded_mask(w: int, h: int, radius: int, supersample: int = 4) -> Image.Image:
    """抗锯齿圆角矩形遮罩。"""
    mask = Image.new("L", (w * supersample, h * supersample), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (0, 0, w * supersample - 1, h * supersample - 1),
        radius=radius * supersample,
        fill=255,
    )
    return mask.resize((w, h), Image.LANCZOS)


def gradient(w: int, h: int, top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    """竖直线性渐变（用 numpy 广播，PIL 没有内置）。"""
    t = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None, None]
    a = np.array(top, dtype=np.float32)[None, None, :]
    b = np.array(bottom, dtype=np.float32)[None, None, :]
    rgb = (a * (1.0 - t) + b * t)
    rgb = np.repeat(rgb, w, axis=1)
    return Image.fromarray(rgb.clip(0, 255).astype(np.uint8), mode="RGB")


def add_grain(img: Image.Image, amount: float = 7.0, blur: float = 0.6) -> Image.Image:
    """细颗粒 + 大块斑驳：让台面不呆板（九宫格拉伸后中间会重复，所以噪声要低频）。"""
    arr = np.asarray(img, dtype=np.float32)
    rng = np.random.default_rng(20261118)
    fine = rng.normal(0.0, amount, (arr.shape[0], arr.shape[1], 1)).astype(np.float32)
    coarse = rng.normal(0.0, amount * 1.6, (arr.shape[0], arr.shape[1], 1)).astype(np.float32)
    coarse_img = Image.fromarray(
        np.repeat(coarse.clip(-64, 64) + 128, 3, axis=2).astype(np.uint8)
    ).filter(ImageFilter.GaussianBlur(18))
    coarse = np.asarray(coarse_img, dtype=np.float32)[:, :, :1] - 128.0
    out = np.clip(arr + fine + coarse * 0.6, 0, 255).astype(np.uint8)
    return Image.fromarray(out, mode="RGB")


def paste(region: Image.Image, box: tuple[int, int, int, int]) -> None:
    # alpha_composite 保留区域自身的 alpha（用 paste+mask 会把 alpha 再乘一次，半透明区域会变淡）。
    atlas.alpha_composite(region, box[:2])


def draw_table() -> Image.Image:
    """桌面主体：绿呢台面 + 内圈压暗 + 左上受光边。九宫格边距留 96px（=48 逻辑）。"""
    w = h = 512
    base = gradient(w, h, (58, 92, 70), (34, 60, 46))
    base = add_grain(base)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.paste(base, (0, 0), rounded_mask(w, h, 96))

    # 内圈压暗（做旧 / 收边感）：边到内侧 48px 的暗渐变。
    shade = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(shade)
    for i in range(48):
        alpha = int(64 * (1.0 - i / 48.0) ** 1.6)
        d.rounded_rectangle((i, i, w - 1 - i, h - 1 - i), radius=96 - i // 2, outline=(0, 0, 0, alpha), width=1)
    canvas = Image.alpha_composite(canvas, shade)

    # 左上受光：沿内缘一圈细亮边（九宫格拉伸时直边不弯，圆角落在角块里，形状不会走样）。
    light = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    dl = ImageDraw.Draw(light)
    dl.rounded_rectangle((16, 16, w - 17, h - 17), radius=80, outline=(186, 224, 192, 70), width=4)
    dl.rounded_rectangle((34, 34, w - 35, h - 35), radius=62, outline=(24, 44, 32, 70), width=3)
    canvas = Image.alpha_composite(canvas, light.filter(ImageFilter.GaussianBlur(1.0)))
    return canvas


def draw_center_plate() -> Image.Image:
    """中央信息板：深板岩色 + 内描边 + 顶部微光。九宫格边距 40px（=20 逻辑）。"""
    w, h = 320, 200
    base = gradient(w, h, (62, 72, 86), (40, 48, 58))
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.paste(base, (0, 0), rounded_mask(w, h, 40))

    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle((3, 3, w - 4, h - 4), radius=38, outline=(18, 24, 30, 255), width=4)
    d.rounded_rectangle((7, 5, w - 8, h - 10), radius=34, outline=(120, 150, 158, 46), width=2)
    return canvas


def draw_river_bed() -> Image.Image:
    """牌河底：比台面略深的实色垫布 + 内圈压暗（九宫格拉伸，实色不会有半透明接缝）。"""
    w, h = 240, 160
    base = gradient(w, h, (36, 62, 46), (28, 50, 37))
    base = add_grain(base, amount=5.0)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.paste(base, (0, 0), rounded_mask(w, h, 24))
    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle((0, 0, w - 1, h - 1), radius=24, outline=(18, 34, 24, 220), width=3)
    d.rounded_rectangle((5, 5, w - 6, h - 6), radius=20, outline=(70, 104, 82, 90), width=2)
    return canvas


def draw_button() -> Image.Image:
    """按钮九宫格：暖金描边的深色胶囊，用在牌桌内的小面板（报听标 / 档位标 / 风位方块）。"""
    w, h = 160, 64
    base = gradient(w, h, (78, 58, 30), (46, 34, 16))
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.paste(base, (0, 0), rounded_mask(w, h, 20))
    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle((1, 1, w - 2, h - 2), radius=19, outline=(214, 178, 104, 235), width=3)
    d.rounded_rectangle((7, 6, w - 8, h // 2), radius=14, outline=(255, 232, 176, 40), width=2)
    return canvas


def draw_shadow() -> Image.Image:
    """桌面投影：内圈最暗、向外散开的软阴影。九宫格边距 80px（=40 逻辑），清单里 inflate = 40。"""
    w = h = 256
    inset = 80
    alpha = np.zeros((h, w), dtype=np.float32)
    yy, xx = np.mgrid[0:h, 0:w]
    dx = np.maximum(np.maximum(inset - xx, xx - (w - 1 - inset)), 0)
    dy = np.maximum(np.maximum(inset - yy, yy - (h - 1 - inset)), 0)
    dist = np.sqrt(dx.astype(np.float32) ** 2 + dy.astype(np.float32) ** 2)
    alpha = np.clip(1.0 - dist / inset, 0.0, 1.0) ** 1.5 * 150.0
    channel = alpha.astype(np.uint8)
    rgba = np.zeros((h, w, 4), dtype=np.uint8)
    rgba[:, :, 3] = channel
    return Image.fromarray(rgba, mode="RGBA")


def draw_frame() -> Image.Image:
    """外框：深木圈 + 内侧一条金线，套在整张画布上。九宫格边距 64px（=32 逻辑）。"""
    w = h = 320
    ring = 64
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    wood = add_grain(gradient(w, h, (44, 34, 24), (26, 20, 14)), amount=10.0)
    ring_mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(ring_mask).rectangle((0, 0, w - 1, h - 1), fill=255)
    hole = Image.new("L", (w, h), 0)
    ImageDraw.Draw(hole).rounded_rectangle((ring, ring, w - 1 - ring, h - 1 - ring), radius=24, fill=255)
    ring_mask = Image.fromarray(np.clip(np.asarray(ring_mask, np.int16) - np.asarray(hole, np.int16), 0, 255).astype(np.uint8))
    canvas.paste(wood, (0, 0), ring_mask)
    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle((ring - 3, ring - 3, w - 1 - ring + 3, h - 1 - ring + 3), radius=26, outline=(206, 170, 96, 210), width=3)
    d.rectangle((0, 0, w - 1, h - 1), outline=(12, 9, 6, 200), width=2)
    return canvas


REGIONS = {
    # name: (x, y, w, h)
    "table": (0, 0, 512, 512),
    "center-plate": (520, 0, 320, 200),
    "river-bed": (520, 208, 240, 160),
    "button": (520, 376, 160, 64),
    "shadow": (520, 448, 256, 256),
    "frame": (0, 520, 320, 320),
}


def main() -> None:
    global atlas
    atlas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    paste(draw_table(), REGIONS["table"])
    paste(draw_center_plate(), REGIONS["center-plate"])
    paste(draw_river_bed(), REGIONS["river-bed"])
    paste(draw_button(), REGIONS["button"])
    paste(draw_shadow(), REGIONS["shadow"])
    paste(draw_frame(), REGIONS["frame"])

    out = os.path.join(OUT_DIR, "skin-atlas.png")
    atlas.save(out)
    print(f"wrote {out}  {SIZE}x{SIZE}  scale={LOGICAL_SCALE}")


if __name__ == "__main__":
    main()
