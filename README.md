# Tiisu-Mahjong-CSharp
七子麻将的 C# 实现（原 Web 版：`Pub-Polaris/Tiisu-Mahjong-main`）。

> 本目录放 **C# 实现**；原 HTML/CSS/JS 版在 `..\Web\`，作为**行为基准（oracle）**，不改动。
> 外部知识库在 `..\Web\docs\knowledge\`（随 Web 仓库版本化）。

## 结构

```
NET\
  global.json          # 固定 SDK 10.0.402（避免误用 11 RC）
  Tiisu.slnx           # 新解决方案格式（.NET 10 SDK 生成）
  src\Tiisu.Core\      # 纯逻辑核心（无 HTTP / DOM / 时间 / 随机）
  tests\Tiisu.Core.Tests\  # xUnit
  tools\Tiisu.Tools\   # golden 生成 / 对比 / fuzz 控制台
```

## 常用命令

```powershell
cd E:\工程文件\TiisuMahjong\NET
dotnet build                 # 构建
dotnet test                  # 跑测试
dotnet run --project tools/Tiisu.Tools
```

## 约束

- **不修改 `..\Web\` 下的产品代码**（它是基准）。
- Core 内禁止出现 HTTP、文件、DOM、时间、随机数（用接口注入）。
- 目标框架 `net10.0`；SDK 由 `global.json` 固定在 10.0.402。
- 迁移策略：先用现有 JS 生成 **golden corpus**，C# 逐字段对比，**0 差异**才继续。
