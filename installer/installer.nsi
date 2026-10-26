; ══════════════════════════════════════════════════════════════
; 七子麻将 · Tiisu Mahjong 安装包（NSIS 3.13）
;
; 口径（北极星 2026-10-26）：
;   - 框架依赖发布（用户机器需 .NET 10 Desktop Runtime）
;   - **按现代 Windows 安装**：装到 %LOCALAPPDATA%\Programs\TiisuMahjong，
;     写 HKCU 卸载项（设置 → 应用 可卸载）、开始菜单 + 桌面快捷方式、自带 uninstall.exe
;   - 免管理员（RequestExecutionLevel user）
;
; 用法：
;   makensis /DSRC="<publish 目录>" /DOUTFILE="<输出 exe>" /DVERSION="261026-xxxx" installer.nsi
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
!ifndef VERSION
  !define VERSION "dev"
!endif

!define APPNAME "七子麻将 Tiisu Mahjong"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\TiisuMahjong"

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\TiisuMahjong"
InstallDirRegKey HKCU "Software\TiisuMahjong" "InstallDir"
RequestExecutionLevel user
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "0.0.0.0"
VIAddVersionKey "ProductName" "Tiisu Mahjong"
VIAddVersionKey "FileDescription" "Tiisu Mahjong installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Pub-Polaris"

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安装 ${APPNAME}"
!define MUI_WELCOMEPAGE_TEXT "安装到当前用户目录，无需管理员权限。$\r$\n$\r$\n会写入卸载信息（设置 → 应用 可卸载），并创建开始菜单与桌面快捷方式。$\r$\n$\r$\n需要 .NET 10 Desktop Runtime（安装程序会检测）。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APPEXE}"
!define MUI_FINISHPAGE_RUN_TEXT "立即启动 七子麻将"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

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

  ; 升级：清掉旧版本遗留的业务 JS（新版本判役/听牌/AI 全在 C#，这些文件不再发布）
  Delete "$INSTDIR\Web\src\engine.js"
  Delete "$INSTDIR\Web\src\winchecker.js"
  Delete "$INSTDIR\Web\src\aiPlayer.js"
  Delete "$INSTDIR\Web\src\tenpai.js"
  Delete "$INSTDIR\Web\src\handAnalyzer.js"

  ; 旧版便携目录里的卸载脚本
  Delete "$INSTDIR\uninstall.cmd"

  File /r "${SRC}\*.*"

  ; 真正的卸载器（写进安装目录，注册表指向它）
  WriteUninstaller "$INSTDIR\uninstall.exe"

  ; 快捷方式：中文显示名
  CreateShortCut "$DESKTOP\七子麻将.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0
  CreateShortCut "$SMPROGRAMS\七子麻将.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0

  ; 旧版 ASCII 快捷方式（若是便携版留下的）也清掉，避免两个图标
  Delete "$DESKTOP\TiisuMahjong.lnk"
  Delete "$SMPROGRAMS\TiisuMahjong.lnk"

  ; 安装位置（供 InstallDirRegKey 记住上次目录）
  WriteRegStr HKCU "Software\TiisuMahjong" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\TiisuMahjong" "Version" "${VERSION}"

  ; 卸载信息：设置 → 应用 里能看到并卸载
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "Pub-Polaris"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APPEXE}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\七子麻将.lnk"
  Delete "$SMPROGRAMS\七子麻将.lnk"
  Delete "$DESKTOP\TiisuMahjong.lnk"
  Delete "$SMPROGRAMS\TiisuMahjong.lnk"

  ; 程序若在运行，先结束（否则 exe 被占用会留下残余）
  nsExec::Exec 'taskkill /IM Tiisu.Mahjong.exe /F'
  Sleep 300

  ; 卸载器自己在 $INSTDIR 里运行时，先把工作目录挪走，否则目录删不掉
  SetOutPath "$TEMP"
  Delete "$INSTDIR\uninstall.exe"
  RMDir /r "$INSTDIR"
  IfFileExists "$INSTDIR\*.*" 0 +3
    Delete /REBOOTOK "$INSTDIR\*.*"
    RMDir /REBOOTOK "$INSTDIR"

  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKCU "Software\TiisuMahjong"
SectionEnd
