from PIL import Image, ImageDraw
import os

out = os.path.dirname(os.path.abspath(__file__))

# 中央信息板底板：2x 交付，400x400 逻辑。
# 文字、点数、余牌仍由代码逐帧绘制，这里只做底板。
w = h = 800
img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((10, 10, w - 11, h - 11), radius=72,
                    fill=(54, 64, 80, 255), outline=(18, 23, 30, 255), width=6)
d.rounded_rectangle((18, 18, w - 19, h - 19), radius=64,
                    outline=(105, 120, 138, 120), width=2)
# 底部轻微压暗，避免整块纯色。
shade = Image.new("RGBA", (w, h), (0, 0, 0, 0))
sd = ImageDraw.Draw(shade)
for y in range(h):
    sd.line((0, y, w, y), fill=(0, 0, 0, int(28 * (y / (h - 1)))))
img = Image.alpha_composite(img, shade)
img.save(os.path.join(out, "center-plate.png"))
print("center-plate.png written")
