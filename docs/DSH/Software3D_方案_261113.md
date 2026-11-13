# Software 3D 备用方案（2026-11-13）

## 为什么先记下来

用户要求：写一个简单的 Software 3D 实现模块，但**暂不启用**。

## 事实：Skia 不支援 3D

当前 UI 渲染用 SkiaSharp（`Tiisu.App.Rendering.SkiaCanvas`）。Skia 是 2D 绘图 API：

- 只有矩阵变换、路径、位图；没有 GPU 3D 管线、深度缓冲、透视纹理映射。
- `ICanvas.ConcatPerspective` 只是给牌桌桌面做**透视矩阵**，不是 3D 场景。
- 所以「3D」只能自己在 CPU 上做投影 + 光栅化，再作为 2D 多边形交给 Skia 填充。

## 实现

`src/Tiisu.App/Rendering/Software3D.cs`：

- `Vertex3` / `Triangle3D` / `Camera3D`
- `TryProject`：单点透视投影（测试 / 命中测试）
- `Render`：视空间变换 → 近平面剔除 → 背面剔除 → 按平均深度排序（画家算法）→ 平面着色 → `ICanvas.FillPolygon`
- 已知限制：无 z-buffer（穿插三角形会错）、无纹理、无裁剪（近平面整片丢弃）、纯 CPU

## 性能

单三角形投影 + 排序是 O(n log n)。几百个三角形可以接受；上千个会明显吃 CPU。
**不要放进每帧主循环**，只在需要静态 3D 预览时调用，并且不要和 Skia 的桌面透视叠加。

## 不启用

- 没有任何调用点；程序启动、对局、渲染都不引用它。
- 以后真要做 3D（例如牌桌 3D 视角）再单独评估：优先考虑换 GPU 后端，而不是这个 CPU 版。
