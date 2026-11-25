# 参考图透视测算（800×600，4:3）

源图：`G:\IGJMJ02\map\map01.bmp`（800×600 RGB，2008-06-05）。

## 测点

把紫色桌面区域按颜色分割后，取两侧边界做直线拟合：

- 左边界：`x ≈ -1.065·y + 443.3`
- 右边界：`x ≈ 1.024·y + 372.7`
- 两线交点（灭点）：约 `(407, 34)`
- 桌面顶边：约 `y = 232`
- 侧边在画面中的斜率：约 `46.8°`（相对水平线）

## 角度结论

画面中心 `y = 300`，灭点在 `y ≈ 34`，水平线在中心上方约 266px。

焦距取决于视场角假设：

| 假设 | 焦距 | 俯视角 |
|---|---:|---:|
| 垂直 FOV 55° | 576px | **24.8°** |
| 垂直 FOV 50° | 643px | 22.5° |
| 水平 FOV 60°（垂直 46.8°） | 691px | 21.1° |

**采用 25° 俯角**（垂直 FOV 55° 假设）。`Soft3DTable` 与 `render_4x3_reference.py` 都用这个值；
如果后续确定实际 FOV，再按同表换算。

## 复现

```powershell
blender --background Assets\Table.blend --python art\table\skin-blender\render_4x3_reference.py
```

输出 `preview-4x3.png`；`Soft3DTable` 使用 `CameraPitchDegrees = 25f`、`CameraFovDegrees = 55f`。

By DSH
