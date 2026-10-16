#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDirectory
  #define SourceDirectory "..\artifacts\win-x64\Bundled"
#endif
#ifndef PackageFlavor
  #define PackageFlavor "Bundled"
#endif
[Setup]
AppId={{0B4D76D1-EC84-430F-BC31-BDDA2C3D0D43}
AppName=Lantern
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Lantern
DefaultGroupName=Lantern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=Lantern-{#AppVersion}-{#PackageFlavor}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Lantern.exe
[Files]
Source: "{#SourceDirectory}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDirectory}\cli\*"; DestDir: "{app}\cli"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Lantern"; Filename: "{app}\Lantern.exe"
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Lantern"; Flags: uninsdeletevalue
; User data, paired identities, receive directories and firewall policy are preserved on uninstall.
