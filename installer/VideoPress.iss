; ============================================================
;  VideoPress — Inno Setup Installer Script
;  Requires: Inno Setup 6+ (https://jrsoftware.org/isinfo.php)
;  Build:    Run make-installer.bat (or open this in Inno Setup IDE)
; ============================================================

#define AppName        "VideoPress"
; CI passes /DAppVersion=x.y.z from the git tag; this is the local fallback.
#ifndef AppVersion
  #define AppVersion   "1.0.4"
#endif
#define AppPublisher   "VideoPress"
#define AppURL         "https://github.com/hassanxs/VideoPress"
#define AppExeName     "VideoPress.exe"
#define AppDescription "Professional Video Compression Studio"
#define BuildDir       "..\publish\VideoPress"

[Setup]
; ── Identity ────────────────────────────────────────────────
AppId                    = {{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName                  = {#AppName}
AppVersion               = {#AppVersion}
AppVerName               = {#AppName} {#AppVersion}
AppPublisher             = {#AppPublisher}
AppPublisherURL          = {#AppURL}
AppSupportURL            = {#AppURL}
AppUpdatesURL            = {#AppURL}
AppCopyright             = Copyright (C) 2025 {#AppPublisher}

; ── Install location ────────────────────────────────────────
DefaultDirName           = {autopf}\{#AppName}
DefaultGroupName         = {#AppName}
AllowNoIcons             = yes
DisableProgramGroupPage  = yes

; ── Output ──────────────────────────────────────────────────
OutputDir                = ..\dist
OutputBaseFilename       = VideoPress-Setup-{#AppVersion}
SetupIconFile            = ..\Assets\icon.ico

; ── Compression ─────────────────────────────────────────────
Compression              = lzma2/ultra64
SolidCompression         = yes
LZMAUseSeparateProcess   = yes

; ── Appearance ──────────────────────────────────────────────
WizardStyle              = modern
WizardSizePercent        = 120
DisableWelcomePage       = no
ShowLanguageDialog       = no

; ── Privileges ──────────────────────────────────────────────
PrivilegesRequiredOverridesAllowed = dialog
PrivilegesRequired       = lowest

; ── Uninstall ───────────────────────────────────────────────
Uninstallable            = yes
UninstallDisplayIcon     = {app}\{#AppExeName}
UninstallDisplayName     = {#AppName}
CreateUninstallRegKey    = yes

; ── Misc ────────────────────────────────────────────────────
VersionInfoVersion       = {#AppVersion}
VersionInfoCompany       = {#AppPublisher}
VersionInfoDescription   = {#AppDescription}
ArchitecturesAllowed     = x64compatible
ArchitecturesInstallIn64BitMode = x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; \
     GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Main executable (self-contained — includes .NET runtime + FFmpeg)
Source: "{#BuildDir}\{#AppExeName}"; DestDir: "{app}"; \
        Flags: ignoreversion

; Any additional files alongside the exe
Source: "{#BuildDir}\*"; DestDir: "{app}"; \
        Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; \
        Excludes: "{#AppExeName}"

[Icons]
; Start Menu
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

; Desktop (only if task selected)
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; \
      Tasks: desktopicon

[Run]
; Offer to launch app after install finishes
Filename: "{app}\{#AppExeName}"; \
          Description: "Launch {#AppName}"; \
          Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove install folder on uninstall
; NOTE: %APPDATA%\VideoPress (presets, settings) is intentionally kept
Type: filesandordirs; Name: "{app}"

[Code]
// ── Require Windows 10+ ─────────────────────────────────────────────────────
function InitializeSetup(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  if Version.Major < 10 then
  begin
    MsgBox(
      'VideoPress requires Windows 10 or later.' + #13#10 +
      'Please upgrade Windows before installing.',
      mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;
