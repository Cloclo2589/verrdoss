#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif

#define MyAppName "VerrDoss"
#define MyAppExe "Verrdoss.exe"
#define MyAppId "{{8F3C1A2E-6B47-4D91-9C2A-5E7B0D4F8A16}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppName}
DefaultDirName={localappdata}\Programs\Verrdoss
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=VerrdossSetup
SetupIconFile=..\src\Verrdoss.App\Assets\verrdoss.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
CloseApplications=force
AppMutex=Local\Verrdoss.SingleInstance
VersionInfoVersion={#MyAppVersion}.0
WizardStyle=modern

[Files]
Source: "staging\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"

[Run]
Filename: "{app}\{#MyAppExe}"; Parameters: "--register-shell"; StatusMsg: "Ajout au menu contextuel..."; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExe}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExe}"; Parameters: "--unregister"; Flags: runhidden; RunOnceId: "UnregisterVerrdoss"
