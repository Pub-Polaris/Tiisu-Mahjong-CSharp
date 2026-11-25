# Blender 牌桌皮肤（Polaris_3D）

不导出 FBX；运行时只吃 **PNG + skin.json**，源文件保留 `.blend`。

## 产物

| 文件 | 作用 |
|---|---|
| `Assets/Table.blend` | 牌桌源文件（木框 + 绿呢），可继续在 Blender 里改 |
| `make_table_skin.py` | 顶视正交相机渲染 2048×2048 透明底 `table.png` |
| `make_ui_panels.py` | 用 Pillow 生成中央板底板（纯 2D） |
| `table.png` | 桌面整图，`scale = 1.4971` |
| `center-plate.png` | 中央信息板底板，2× 交付 |
| `skin.json` | 皮肤清单 |
| `preview.png` | 接进 `TableSceneSkia` 后的 1400×1400 预览 |
| `render_4x3_reference.py` | 按实验相机 25° 出 800×600 预览（参考图本身无 FOV 信息） |
| `preview-4x3.png` | 4:3 透视预览 |
| `soft3d-preview.png` | Soft-3D 桌面实验预览（四边形投影，避免三角接缝） |
| `soft3d-tile-preview.png` | `--render-soft3d-tile` 的单张牌小样 |
| `REFERENCE.md` | 参考图测点与角度推导 |

## 重新生成

```powershell
blender --background Assets\Table.blend --python art\table\skin-blender\make_table_skin.py
python art\table\skin-blender\make_ui_panels.py
blender --background Assets\Table.blend --python art\table\skin-blender\render_4x3_reference.py
```

## 预览 / 接入

```powershell
$env:TIISU_SKIN = "$PWD\art\table\skin-blender"
tools\Tiisu.DevTools\bin\Debug\net10.0-windows10.0.19041\Tiisu.DevTools.exe `
  --render-table-png artifacts\3d\table-skin-flat.png --variant flat

# 单张牌 Soft-3D 小样
tools\Tiisu.DevTools\bin\Debug\net10.0-windows10.0.19041\Tiisu.DevTools.exe `
  --render-soft3d-tile artifacts\3d\tile.png 1m
```

随包启用时，把 `skin.json`、`table.png`、`center-plate.png` 放到
`src/Tiisu.App/Assets/ui/`。

## 边界

- 代码仍是 Skia 2D / 软渲染；Blender 只负责离线出图，不引入运行时 3D 引擎。
- `TableRenderStyle.Soft3D` 是运行时的软 3D 桌面实验：`Soft3DTable` 只投影一次，`StaticLayerCache` 缓存；
  动态牌仍是 2D。25° 只是实验相机，不是参考图测出的角度；测点见 `REFERENCE.md`。
- 当前替换 `table` / `center-plate`；牌河底暂不做整块深色垫布（空牌河会出现大块深色矩形）。
- `shadow` / `frame` 整层压边试过，会让 CPU 光栅基准从 4.64ms 升到约 4.9ms；先不随皮启用。
- 不产出 FBX；需要交换格式时用 glTF 2.0（JSON/二进制，不带脚本），不用 FBX。

By DSH
