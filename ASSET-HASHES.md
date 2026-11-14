# 资源文件校验和（SHA256）

生成时间：2026-11-14；用于核对发布资源与仓库内容是否一致。

> 报牌语音不随安装包分发，由本机 Windows SAPI 在首次使用时生成；本清单不包含语音，也不保证跨机器哈希一致。

## 音效（由 Tiisu.AudioGen 可复现生成）

```
b4df516425824700afb528c110768a0e57c434dc9277bb3699d9cf99d1bf8481  sounds/call.wav
87a6461b63be4f4afaad3c873d98d3818c0d92e5cb9fea922cc6ddde67e218e5  sounds/deal.wav
443967caa6a57221b9878d8d3e923dbbaf31ef9f86b7f17d9cf25200bfad94dc  sounds/discard.wav
aa8e15320d7ea38ca3adfa31d666097d208e6acdcca8e0363c0e6382fc8924aa  sounds/draw.wav
012f2f4faf433a74ac1872f6b0745414bcb2ef241b2692bc918e498133b97c58  sounds/flow.wav
ba05d31cc3ae486ceb6caca5401fa78bf73ac53a90690d62a9c076a72b988204  sounds/kan.wav
04cdf53e63d684748df9f128eb1ec389b269597d68d1d128c156dcba1ccd4eff  sounds/roundend.wav
eeb197d04e0bd88b44a845eef8d16f4e80389a0d3cce7a38d2a0ef118fcbe8a5  sounds/shuffle.wav
e15b2903fc4f02b219cb63e6e4c62e25b3f77d096dced84d062b8a35f61a2dde  sounds/win.wav
```

## BGM（第三方，不由 AudioGen 生成）

```
f9c200fac374da23267d2c5b67ba2e880d97715659177be5a24690d11e334e2a  bgm/lobby.wav
```

## 字体子集（由文渊圆体 v1.110 提取并重命名）

```
62cbd48bbafa2eba834840afc03f35674f36201223d169df3937de90e38ef591  fonts/TiisuRoundedSC-subset.ttf
```

## 重建与核对

```powershell
cd E:\工程文件\TiisuMahjong\NET
dotnet run --project tools/Tiisu.AudioGen -- --sfx --out D:\tmp\audio
Get-ChildItem src\Tiisu.App\Assets\sounds\*.wav | ForEach-Object {
  $a = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
  $b = (Get-FileHash (Join-Path D:\tmp\audio "sounds\$($_.Name)") -Algorithm SHA256).Hash
  if ($a -ne $b) { "DIFF $($_.Name)" } else { "OK   $($_.Name)" }
}
```

音效与仓库内文件应逐字节一致。语音在本机生成，不参与发布哈希核对。
