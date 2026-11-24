# Blender 牌桌皮肤（Polaris_3D）

不导出 FBX；运行时只吃 **PNG + skin.json**，源文件保留 `.blend`。

## 产物

| 文件 | 作用 |
|---|---|
| `Assets/Table.blend` | 牌桌源文件（木框 + 绿呢），可继续在 Blender 里改 |
| `make_table_skin.py` | 顶视正交相机渲染 2048×2048 透明底 `table.png` |
| `table.png` | 皮肤图集：`table` 区域整图 |
| `skin.json` | 皮肤清单，`scale = 1.5` |
| `preview.png` | 接进 `TableSceneSkia` 后的 1400×1400 预览 |

## 重新生成

```powershell
blender --background Assets\Table.blend --python art\table\skin-blender\make_table_skin.py
```

## 预览 / 接入

```powershell
$env:TIISU_SKIN = "$PWD\art\table\skin-blender"
tools\Tiisu.DevTools\bin\Debug\net10.0-windows10.0.19041\Tiisu.DevTools.exe `
  --render-table-png artifacts\3d\table-skin-flat.png --variant flat
```

随包启用时，把 `skin.json` 和 `table.png` 放到 `src/Tiisu.App/Assets/ui/`。

## 边界

- 代码仍是 Skia 2D / 软渲染；Blender 只负责离线出图，不引入运行时 3D 引擎。
- 当前只替换 `table` 区域；中央板、牌河底、按钮仍走程序化绘制，后续可逐块加 `center-plate` / `river-bed` / `button`。
- 不产出 FBX；需要交换格式时用 glTF 2.0（JSON/二进制，不带脚本），不用 FBX。

By DSH
