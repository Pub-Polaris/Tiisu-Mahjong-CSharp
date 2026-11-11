@echo off
chcp 65001 >nul
cd /d "%~dp0"
set GAMES=%1
if "%GAMES%"=="" set GAMES=500
echo 自对弈 %GAMES% 局：神经 AI 对 TieredAi（日志追加到 %APPDATA%\TiisuMahjong\logs）
dotnet run --project tools\Tiisu.DevTools\Tiisu.DevTools.csproj -c Release -- --selfplay %GAMES%
echo.
echo 完成。日志在 %APPDATA%\TiisuMahjong\logs\selfplay_*.jsonl
pause
