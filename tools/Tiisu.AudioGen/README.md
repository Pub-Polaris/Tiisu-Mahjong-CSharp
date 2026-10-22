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

## 生成内容

| 类别 | 文件 | 特征 |
|---|---|---|
| 音效 | draw / discard / call / kan / win / flow / roundend | 程序合成，几十毫秒 |
| 音效 | **deal** | 20 次碰撞、间隔 40–90ms、1.2s（对齐 IGS 参考 1.175s） |
| 音效 | **shuffle** | 1.35s 连续摩擦（对齐参考 1.567s） |
| 语音 | chi 吃 / peng 碰 / gang 杠 / hepai 和牌 / zimo 自摸 / ronghe 荣和 / liuju 流局 / tingpai 听牌 | espeak-ng `cmn+f4 / rate180 / pitch85`，F0≈256Hz（参考 261） |

## 参数来源

`C:\Users\rksk1\Desktop\voice_igsmj02_01.wav`（IGS 原声）分析：14.43s / 24kHz / 40 个有声段 / F0 中位 261Hz。
参数是在 `(变体 × rate × pitch)` 网格里按「F0 差 + 时长差」打分选出来的。

## 运行时行为

`Ui/SoundEffects.cs` 优先读 `Assets/sounds/<cue>.wav`；
文件缺失才退回内联合成（保证打包漏文件也不会静音崩）。语音读 `Assets/voices/<name>.wav`，缺失静默。
