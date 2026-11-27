#ifndef AppVersion
  #error Pass AppVersion from build-installer.ps1.
#endif
#ifndef AppFileVersion
  #error Pass AppFileVersion from build-installer.ps1.
#endif
#ifndef PublishDir
  #error Pass PublishDir from build-installer.ps1.
#endif
#ifndef LegacyRuntimeDeleteScript
  #error Pass LegacyRuntimeDeleteScript from build-installer.ps1.
#endif

#include LegacyRuntimeDeleteScript
#include "LegacyRuntimeCleanup.iss"
#include "RuntimeDependencies.iss"

[Setup]
AppId={{B7F4D8C1-6A2E-4C9B-8F13-4E9A2D5C8B71}
AppName=HNAUnlocker
AppVersion={#AppVersion}
AppVerName=HNAUnlocker {#AppVersion}
AppPublisher=Schrodingers-Neko
AppPublisherURL=https://github.com/Schrodingers-Neko/HNAUnlocker
AppSupportURL=https://github.com/Schrodingers-Neko/HNAUnlocker/issues
AppUpdatesURL=https://github.com/Schrodingers-Neko/HNAUnlocker/releases
DefaultDirName={autopf}\HNAUnlocker
DefaultGroupName=HNAUnlocker
UsePreviousAppDir=yes
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
SetupArchitecture=x64
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
AppMutex=Global\HNAUnlocker.Running,HNAUnlocker
CloseApplications=no
RestartApplications=no
UninstallDisplayName=HNAUnlocker
UninstallDisplayIcon={app}\HNAUnlocker.exe
SetupIconFile={#PublishDir}\Assets\AppIcon.ico
LicenseFile={#PublishDir}\LICENSE
VersionInfoVersion={#AppFileVersion}
VersionInfoProductVersion={#AppFileVersion}
OutputBaseFilename=HNAUnlocker-{#AppVersion}-win-x64-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\HNAUnlocker"; Filename: "{app}\HNAUnlocker.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,HNAUnlocker}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\HNAUnlocker"; Filename: "{app}\HNAUnlocker.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\HNAUnlocker.exe"; WorkingDir: "{app}"; Flags: nowait runasoriginaluser skipifdoesntexist; Check: IsUpdaterInstall

[Code]
function IsUpdaterInstall: Boolean;
begin
  Result := (ExpandConstant('{param:HNAUPDATE|0}') = '1') and RuntimeCanLaunchApplication;
end;
