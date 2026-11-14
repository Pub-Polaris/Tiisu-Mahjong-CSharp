# 本地化盘点与方案

## 现状

`--audit-ui-strings` 扫描结果：**611 个中文 UI 字面量，35 个文件**。

| 文件 | 数量 |
|---|---:|
| SettlementOverlay.cs | 74 |
| MatchRuntime.cs | 69 |
| LicensesScreen.cs | 67 |
| SettingsScreen.cs | 48 |
| SvgExporter.cs | 34 |
| Achievements.cs | 30 |
| Screens.cs | 30 |
| TableDemoModel.cs | 29 |
| RoundLogReader.cs | 27 |
| TableLayoutSpec.cs | 22 |
| TutorialScreen.cs | 21 |
| SnapshotBuilder.cs | 21 |

重新盘点：

```powershell
dotnet run --project tools/Tiisu.DevTools -- --audit-ui-strings
```

CSV 输出：`artifacts/localization/ui-strings.csv`。

## 方案

**外置 JSON 资源，不内嵌代码。**

```
src/Tiisu.App/Assets/i18n/zh-CN.json
src/Tiisu.App/Assets/i18n/en-US.json
```

- `UiText` 读取当前语言；缺失 key 回退 `zh-CN`，再缺失返回 key 本身（开发期可见）
- key 用命名空间：`menu.play` / `settings.volume.bgm` / `stats.rounds`
- 不翻译文档正文与许可全文；它们保持原文，商店/README 另做英文
- `Settings.Language` 默认 `zh-CN`；设置页显示语言切换，但**只在核心页完成翻译后开放**

## 分阶段

1. **盘点与框架**（已完成）：CSV、key 规范、`UiText` 设计；不开放语言切换
2. **核心导航**（已完成）：菜单、设置、新手引导、战绩、成就、牌谱回看；设置页可切 English
3. **对局内**（未做）：动作栏、提示、结算、牌桌 HUD
4. **规则/帮助/许可**（未做）：规则速查、许可页、Markdown 文档导航
5. **英文验收**（部分）：`--render-screen <screen>-en` 可出英文截图；英文 key 反向检查未做

## 当前状态

- 已翻译：菜单、设置、新手引导、战绩、成就、牌谱回看
- 已翻译：对局内动作按钮、牌桌中央 HUD、结算关键标签、流局原因；规则页正文、许可页条目与导航、Markdown 标题/正文/导航
- 未翻译：动作栏外的长提示；许可全文保留原文
- 切换入口：设置 →「语言 / Language」→ English

## 风险

- 611 条字面量里有日志/调试文本，不应全部翻译；阶段 2 先排除 `Runtime` 日志与 `TableDemoModel`
- 字体子集需要补英文/ASCII；当前已包含 ASCII
- 牌桌列宽、设置行宽对大字号英文更敏感，必须出图验收
- 不在本轮开放语言开关，避免出现半中半英的界面
