# 资源文件校验和（SHA256）

生成时间：2026-11-14；用于核对发布资源与仓库内容是否一致。

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

## 语音（由 Tiisu.AudioGen + espeak-ng 生成；版本不同可能不同）

```
2fad76bd0ff3f3d255849fd2ef2db4af2587b1815674c53d91758b70858cff9b  voices/chi.wav
3c0be35185ca3e072be009ecc0e4d2c6ec3dfd6b9126e2ad3ce318617beeaa78  voices/gang.wav
890c51d8929a9a1b3653dc6c18ac55ba1e202e006a32a505d3c649a4b4d4ae1a  voices/hepai.wav
ead4191927ae79e72045c65bce8904c9ca9c8718079e3ebf4ab5336c5fac7132  voices/liuju.wav
1804f37b4cfe16f4731d91e6ec204c24d1cafecf20e8bb6a09702df8fc56c53c  voices/peng.wav
b60b35993e8c2d1ed318d43632e58f7e6950aa204a3beb9db9646686a7709260  voices/ronghe.wav
dcc774bbbb7295599b0b0b14bf38952e629151cd21d4343274f092692bce7984  voices/tingpai.wav
5fe61b7cef0b88c42668ebe2a570b8514ab8bef130797aa27736e722d31df66a  voices/zimo.wav
```

## BGM（第三方，不由 AudioGen 生成）

```
f9c200fac374da23267d2c5b67ba2e880d97715659177be5a24690d11e334e2a  bgm/lobby.wav
```

## 字体子集（由文渊圆体 v1.110 提取，非 AudioGen）

```
bcaa354091eb8da6e9c0593542567b50cfcf2107510985b8c7f9ef3357c10791  fonts/WenYuanRoundedSC-subset.ttf
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

音效与仓库内文件应逐字节一致；语音需要相同版本 espeak-ng 才能复现。
