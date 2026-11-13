# Tiisu.AudioGen — 音频合成工具

把所有音效和报牌语音**一次性生成**到 `src/Tiisu.App/Assets/`，运行时程序只读 WAV。

## 用法

```powershell
cd E:\工程文件\TiisuMahjong\NET

dotnet run --project tools/Tiisu.AudioGen            # 音效 + 语音（默认输出到 App/Assets）
dotnet run --project tools/Tiisu.AudioGen -- --list  # 只列会生成哪些文件
dotnet run --project tools/Tiisu.AudioGen -- --sfx   # 只生成音效（不需要 espeak-ng）
dotnet run --project tools/Tiisu.AudioGen -- --out D:\tmp\audio
```

语音部分需要 **espeak-ng**（默认路径 `C:\Program Files\eSpeak NG\espeak-ng.exe`，可用 `--espeak` 指定）。
没有它时只跑 `--sfx` 也能生成全部音效。

## 分发说明

报牌语音**不随安装包分发**：游戏首次播放语音时用本机 Windows SAPI 中文语音生成到
`%LOCALAPPDATA%\TiisuMahjong\voices`。`--voices` 仅供本机调试/开发，语音受本机 Windows 许可约束。

## 生成内容

| 类别 | 文件 | 特征 |
|---|---|---|
| 音效 | draw / discard / call / kan / win / flow / roundend | 程序合成，几十毫秒 |
| 音效 | **deal** | 20 次碰撞、间隔 40–90ms、约 1.2s |
| 音效 | **shuffle** | 约 1.35s 连续摩擦 |
| 语音 | chi 吃 / peng 碰 / gang 杠 / hepai 和牌 / zimo 自摸 / ronghe 荣和 / liuju 流局 / tingpai 听牌 | espeak-ng `cmn+f4 / rate180 / pitch85`，F0≈256Hz（参考 261） |

## 参数来源

音效参数按桌面麻将常见音效的总长、密度与包络特征设计；语音参数在
`(变体 × rate × pitch)` 网格里按目标音高与时长打分选出。全部音频由本工具
从零合成，不使用任何第三方录音采样。

## 运行时行为

`Ui/SoundEffects.cs` 优先读 `Assets/sounds/<cue>.wav`；
文件缺失才退回内联合成（保证打包漏文件也不会静音崩）。语音读 `Assets/voices/<name>.wav`，缺失静默。

## 校验

音效与语音的 SHA256 见仓库根目录 `ASSET-HASHES.md`。音效可用 `--sfx --out <目录>` 重新生成并逐字节对比；语音需相同版本 espeak-ng。

```powershell
dotnet run --project tools/Tiisu.AudioGen -- --sfx --out D:\tmp\audio
```
