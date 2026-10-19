; ══════════════════════════════════════════════════════════════
; 七子麻将 · Tiisu Mahjong 安装包（NSIS 3.13）
;
; 约束（北极星 2026-10-20）：
;   - 框架依赖发布（用户机器需 .NET 10 Desktop Runtime）
;   - **不写注册表**：只复制文件 + .lnk 快捷方式 + 附带 uninstall.cmd
;   - 默认装到 %LOCALAPPDATA%\Programs\TiisuMahjong（免管理员）
;
; 注意：uninstall.cmd 必须是 **纯 ASCII**（NSIS 写出的含中文 .cmd 在 GBK 控制台会解析失败），
;       所以快捷方式文件名用 ASCII `TiisuMahjong.lnk`，路径用 %USERPROFILE% / %APPDATA% 展开。
;
; 用法：
;   makensis /DSRC="<publish 目录>" /DOUTFILE="<输出 exe>" installer.nsi
; ══════════════════════════════════════════════════════════════

Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"

!ifndef SRC
  !define SRC "..\dist\publish"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\dist\TiisuMahjong-Setup.exe"
!endif
!ifndef APPEXE
  !define APPEXE "Tiisu.Mahjong.exe"
!endif

Name "七子麻将 Tiisu Mahjong"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\TiisuMahjong"
RequestExecutionLevel user
ShowInstDetails show

VIProductVersion "0.1.0.0"
VIAddVersionKey "ProductName" "Tiisu Mahjong"
VIAddVersionKey "FileDescription" "Tiisu Mahjong installer"
VIAddVersionKey "FileVersion" "0.1.0"
VIAddVersionKey "LegalCopyright" "Pub-Polaris"

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安装 七子麻将"
!define MUI_WELCOMEPAGE_TEXT "本安装包只复制文件与创建快捷方式，$\r$\n不写入注册表，删目录即可卸载。$\r$\n$\r$\n需要 .NET 10 Desktop Runtime（安装程序会检测）。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APPEXE}"
!define MUI_FINISHPAGE_RUN_TEXT "立即启动 七子麻将"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"

; ── 运行时检测：dotnet --list-runtimes 里找 WindowsDesktop.App 10.x ──
Function CheckDotNet10
  nsExec::ExecToStack '"$SYSDIR\cmd.exe" /c dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.WindowsDesktop.App 10." >nul'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONEXCLAMATION|MB_YESNO "未检测到 .NET 10 Desktop Runtime。$\r$\n$\r$\n程序是「框架依赖」发布，必须先安装运行时。$\r$\n是否现在打开下载页？（安装仍会继续）" \
      IDYES openDl IDNO continueInstall
    openDl:
      ExecShell "open" "https://dotnet.microsoft.com/download/dotnet/10.0"
    continueInstall:
  ${EndIf}
FunctionEnd

Function .onInit
  Call CheckDotNet10
FunctionEnd

Section "主程序" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"
  File /r "${SRC}\*.*"

  ; 卸载脚本：纯 ASCII，路径用环境变量展开（避免中文 .cmd 在 GBK 控制台解析失败）
  FileOpen $9 "$INSTDIR\uninstall.cmd" w
  FileWrite $9 "@echo off$\r$\n"
  FileWrite $9 "echo Uninstalling Tiisu Mahjong ...$\r$\n"
  FileWrite $9 "del /q $\"%USERPROFILE%\Desktop\TiisuMahjong.lnk$\" 2>nul$\r$\n"
  FileWrite $9 "del /q $\"%APPDATA%\Microsoft\Windows\Start Menu\Programs\TiisuMahjong.lnk$\" 2>nul$\r$\n"
  FileWrite $9 "cd /d $\"%TEMP%$\"$\r$\n"
  FileWrite $9 "rmdir /s /q $\"$INSTDIR$\" 2>nul$\r$\n"
  FileWrite $9 "echo Done.$\r$\n"
  FileWrite $9 "pause$\r$\n"
  FileClose $9

  ; 快捷方式：文件名 ASCII
  CreateShortCut "$DESKTOP\TiisuMahjong.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0
  CreateShortCut "$SMPROGRAMS\TiisuMahjong.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0
SectionEnd
