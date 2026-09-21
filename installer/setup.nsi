; ==============================================================================
; AndroidSyncControl — Production NSIS Installer Script
; Invariant: Never deletes %LOCALAPPDATA%\AndroidSyncControl\agent-data on uninstall
; ==============================================================================

!include "MUI2.nsh"
!include "x64.nsh"

; --- General Attributes ---
Name "AndroidSyncControl"
OutFile "..\release\AndroidSyncControl-${APP_VERSION}-Setup.exe"
Unicode True

; Default installation folder
InstallDir "$PROGRAMFILES64\AndroidSyncControl"

; Get installation folder from registry if available
InstallDirRegKey HKLM "Software\AndroidSyncControl" "Install_Dir"

RequestExecutionLevel admin

; --- Interface Settings ---
!define MUI_ABORTWARNING
!define MUI_ICON "..\src\AndroidSyncControl\Resources\appicon.ico"
!define MUI_UNICON "..\src\AndroidSyncControl\Resources\appicon.ico"

; --- Pages ---
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

; Finish page with option to run application
!define MUI_FINISHPAGE_RUN "$INSTDIR\AndroidSyncControl.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch AndroidSyncControl"
!insertmacro MUI_PAGE_FINISH

; Uninstaller pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; Languages
!insertmacro MUI_LANGUAGE "English"

; ==============================================================================
; Installer Sections
; ==============================================================================
Section "AndroidSyncControl Core" SecCore
    SectionIn RO

    ; Ensure 64-bit redirection is disabled so we install cleanly to Program Files
    ${If} ${RunningX64}
        SetRegView 64
    ${EndIf}

    SetOutPath "$INSTDIR"

    ; Copy staged distribution files
    File /r "..\dist\AndroidSyncControl-${APP_VERSION}-win-x64\*.*"

    ; Write registry install key
    WriteRegStr HKLM "Software\AndroidSyncControl" "Install_Dir" "$INSTDIR"

    ; Register Uninstaller in Windows Add/Remove Programs
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AndroidSyncControl" "DisplayName" "AndroidSyncControl"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AndroidSyncControl" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AndroidSyncControl" "Publisher" "AndroidSyncControl"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AndroidSyncControl" "DisplayIcon" "$INSTDIR\AndroidSyncControl.exe,0"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AndroidSyncControl" "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AndroidSyncControl" "InstallLocation" "$INSTDIR"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AndroidSyncControl" "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AndroidSyncControl" "NoRepair" 1

    ; Create uninstaller
    WriteUninstaller "$INSTDIR\uninstall.exe"

    ; Create Start Menu Shortcuts
    CreateDirectory "$SMPROGRAMS\AndroidSyncControl"
    CreateShortcut "$SMPROGRAMS\AndroidSyncControl\AndroidSyncControl.lnk" "$INSTDIR\AndroidSyncControl.exe" "" "$INSTDIR\AndroidSyncControl.exe" 0
    CreateShortcut "$SMPROGRAMS\AndroidSyncControl\Uninstall AndroidSyncControl.lnk" "$INSTDIR\uninstall.exe" "" "$INSTDIR\uninstall.exe" 0

    ; Create Desktop Shortcut
    CreateShortcut "$DESKTOP\AndroidSyncControl.lnk" "$INSTDIR\AndroidSyncControl.exe" "" "$INSTDIR\AndroidSyncControl.exe" 0
SectionEnd

; ==============================================================================
; Uninstaller Section
; INVARIANT: Clean ONLY $INSTDIR (Program Files). NEVER delete %LOCALAPPDATA%
; ==============================================================================
Section "Uninstall"
    ${If} ${RunningX64}
        SetRegView 64
    ${EndIf}

    ; Remove Shortcuts
    Delete "$DESKTOP\AndroidSyncControl.lnk"
    Delete "$SMPROGRAMS\AndroidSyncControl\AndroidSyncControl.lnk"
    Delete "$SMPROGRAMS\AndroidSyncControl\Uninstall AndroidSyncControl.lnk"
    RMDir "$SMPROGRAMS\AndroidSyncControl"

    ; Remove Registry keys
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AndroidSyncControl"
    DeleteRegKey HKLM "Software\AndroidSyncControl"

    ; Delete Program Files files
    RMDir /r "$INSTDIR"
SectionEnd
