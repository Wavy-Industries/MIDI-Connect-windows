; MIDI Connect Installer Script for Inno Setup 6.x
; Copyright (c) 2024 Wavy Industries AS
; Download Inno Setup from: https://jrsoftware.org/isinfo.php
;
; IMPORTANT LICENSING NOTE:
; This installer does NOT bundle loopMIDI/virtualMIDI driver.
; The virtualMIDI SDK license explicitly prohibits redistribution without permission.
; Instead, this installer:
;   1. Detects if loopMIDI is installed
;   2. If not installed, guides user to download from official website
;   3. Launches loopMIDI setup for the user to install
;
; For commercial distribution with bundled driver, contact: info@tobias-erichsen.de

#define MyAppName "MIDI Connect"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Wavy Industries AS"
#define MyAppURL "https://wavyindustries.com"
#define MyAppExeName "MIDIConnect.exe"
#define MyAppSupportEmail "hello@wavyindustries.com"

; loopMIDI download URL (check for updates at https://www.tobias-erichsen.de/software/loopmidi.html)
#define LoopMIDIWebsite "https://www.tobias-erichsen.de/software/loopmidi.html"

[Setup]
; App identity
AppId={{MIDI-CONNECT-WAVY-INDUSTRIES-2024}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppContact={#MyAppSupportEmail}
AppCopyright=Copyright (c) 2024 {#MyAppPublisher}

; Installation settings
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
InfoBeforeFile=
InfoAfterFile=
OutputDir=Output
OutputBaseFilename=MIDI_Connect_Setup_{#MyAppVersion}
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Version info
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCopyright=Copyright (c) 2024 {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Startup Options:"; Flags: unchecked

[Files]
; Main application files - adjust paths based on your publish output
Source: "..\bin\Release\net10.0-windows10.0.22621.0\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\net10.0-windows10.0.22621.0\publish\MIDIConnect.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\net10.0-windows10.0.22621.0\publish\MIDIConnect.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\net10.0-windows10.0.22621.0\publish\MIDIConnect.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\net10.0-windows10.0.22621.0\publish\Hardcodet.NotifyIcon.Wpf.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\net10.0-windows10.0.22621.0\publish\WinRT.Runtime.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\net10.0-windows10.0.22621.0\publish\Microsoft.Windows.SDK.NET.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\icon.ico"; DestDir: "{app}"; Flags: ignoreversion

; Include any additional DLLs from publish folder
Source: "..\bin\Release\net10.0-windows10.0.22621.0\publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Wavy Industries\{#MyAppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Wavy Industries\{#MyAppName}"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"

[Run]
; Launch app after installation (optional)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  LoopMIDIPage: TWizardPage;
  LoopMIDIStatusLabel: TLabel;
  LoopMIDIInstallButton: TButton;
  LoopMIDIWebsiteButton: TButton;
  LoopMIDISkipButton: TButton;
  LoopMIDIInstalled: Boolean;
  LoopMIDISkipped: Boolean;

// Check if loopMIDI/virtualMIDI driver is installed
function IsLoopMIDIInstalled: Boolean;
var
  VersionStr: String;
begin
  Result := False;
  
  // Check loopMIDI registry key
  if RegQueryStringValue(HKLM, 'SOFTWARE\Tobias Erichsen\loopMIDI', 'Version', VersionStr) then
  begin
    Result := True;
    Exit;
  end;
  
  // Check virtualMIDI registry key (driver might be installed via rtpMIDI or SDK)
  if RegQueryStringValue(HKLM, 'SOFTWARE\Tobias Erichsen\teVirtualMIDI', 'Version', VersionStr) then
  begin
    Result := True;
    Exit;
  end;
  
  // Check if driver DLL exists in System32
  if FileExists(ExpandConstant('{sys}\teVirtualMIDI64.dll')) then
  begin
    Result := True;
    Exit;
  end;
  
  // Also check Program Files for loopMIDI
  if DirExists(ExpandConstant('{pf}\Tobias Erichsen\loopMIDI')) then
  begin
    Result := True;
    Exit;
  end;
end;

procedure OpenLoopMIDIWebsite(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', '{#LoopMIDIWebsite}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure InstallLoopMIDIClick(Sender: TObject);
var
  ErrorCode: Integer;
begin
  // Open the loopMIDI website for manual download
  MsgBox('We will now open the loopMIDI download page.' + #13#10 + #13#10 +
         'Please download and install loopMIDI, then click "Re-check" to verify the installation.' + #13#10 + #13#10 +
         'loopMIDI is free software by Tobias Erichsen.', mbInformation, MB_OK);
  
  ShellExec('open', '{#LoopMIDIWebsite}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure RecheckLoopMIDIClick(Sender: TObject);
begin
  LoopMIDIInstalled := IsLoopMIDIInstalled;
  
  if LoopMIDIInstalled then
  begin
    LoopMIDIStatusLabel.Caption := 'loopMIDI driver: INSTALLED' + #13#10 + #13#10 +
                                    'The virtualMIDI driver is now available. Click Next to continue.';
    LoopMIDIStatusLabel.Font.Color := clGreen;
    LoopMIDIInstallButton.Caption := 'Re-check';
  end
  else
  begin
    LoopMIDIStatusLabel.Caption := 'loopMIDI driver: NOT INSTALLED' + #13#10 + #13#10 +
                                    'Please install loopMIDI to enable virtual MIDI port functionality.' + #13#10 +
                                    'Click "Download loopMIDI" to open the download page.';
    LoopMIDIStatusLabel.Font.Color := clRed;
  end;
end;

procedure SkipLoopMIDIClick(Sender: TObject);
begin
  if MsgBox('{#MyAppName} requires the virtualMIDI driver to create virtual MIDI ports.' + #13#10 + #13#10 +
            'Without it, the application will not function correctly.' + #13#10 + #13#10 +
            'Are you sure you want to skip loopMIDI installation?', mbConfirmation, MB_YESNO) = IDYES then
  begin
    LoopMIDISkipped := True;
    WizardForm.NextButton.OnClick(nil);
  end;
end;

procedure InitializeWizard;
begin
  // Create custom page for loopMIDI dependency
  LoopMIDIPage := CreateCustomPage(wpSelectDir, 'Required Dependency: loopMIDI',
    '{#MyAppName} requires the loopMIDI virtual MIDI driver.');
  
  // Status label
  LoopMIDIStatusLabel := TLabel.Create(LoopMIDIPage);
  LoopMIDIStatusLabel.Parent := LoopMIDIPage.Surface;
  LoopMIDIStatusLabel.Left := 0;
  LoopMIDIStatusLabel.Top := 0;
  LoopMIDIStatusLabel.Width := LoopMIDIPage.SurfaceWidth;
  LoopMIDIStatusLabel.Height := 100;
  LoopMIDIStatusLabel.AutoSize := False;
  LoopMIDIStatusLabel.WordWrap := True;
  
  // Download/Install button
  LoopMIDIInstallButton := TButton.Create(LoopMIDIPage);
  LoopMIDIInstallButton.Parent := LoopMIDIPage.Surface;
  LoopMIDIInstallButton.Left := 0;
  LoopMIDIInstallButton.Top := 110;
  LoopMIDIInstallButton.Width := 150;
  LoopMIDIInstallButton.Height := 30;
  LoopMIDIInstallButton.Caption := 'Download loopMIDI';
  LoopMIDIInstallButton.OnClick := @InstallLoopMIDIClick;
  
  // Re-check button
  LoopMIDIWebsiteButton := TButton.Create(LoopMIDIPage);
  LoopMIDIWebsiteButton.Parent := LoopMIDIPage.Surface;
  LoopMIDIWebsiteButton.Left := 160;
  LoopMIDIWebsiteButton.Top := 110;
  LoopMIDIWebsiteButton.Width := 100;
  LoopMIDIWebsiteButton.Height := 30;
  LoopMIDIWebsiteButton.Caption := 'Re-check';
  LoopMIDIWebsiteButton.OnClick := @RecheckLoopMIDIClick;
  
  // Skip button (install anyway)
  LoopMIDISkipButton := TButton.Create(LoopMIDIPage);
  LoopMIDISkipButton.Parent := LoopMIDIPage.Surface;
  LoopMIDISkipButton.Left := 270;
  LoopMIDISkipButton.Top := 110;
  LoopMIDISkipButton.Width := 120;
  LoopMIDISkipButton.Height := 30;
  LoopMIDISkipButton.Caption := 'Skip (Not Recommended)';
  LoopMIDISkipButton.OnClick := @SkipLoopMIDIClick;
  
  // Info label
  with TLabel.Create(LoopMIDIPage) do
  begin
    Parent := LoopMIDIPage.Surface;
    Left := 0;
    Top := 160;
    Width := LoopMIDIPage.SurfaceWidth;
    Height := 80;
    AutoSize := False;
    WordWrap := True;
    Caption := 'loopMIDI is free software by Tobias Erichsen that creates virtual MIDI ports on Windows. ' +
               '{#MyAppName} uses the virtualMIDI driver (included with loopMIDI) to create MIDI ports for your Bluetooth MIDI devices.' + #13#10 + #13#10 +
               'Website: www.tobias-erichsen.de/software/loopmidi.html';
  end;
  
  LoopMIDIInstalled := False;
  LoopMIDISkipped := False;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = LoopMIDIPage.ID then
  begin
    // Check current status
    LoopMIDIInstalled := IsLoopMIDIInstalled;
    
    if LoopMIDIInstalled then
    begin
      LoopMIDIStatusLabel.Caption := 'loopMIDI driver: INSTALLED' + #13#10 + #13#10 +
                                      'The virtualMIDI driver is already installed on your system. Click Next to continue with the installation.';
      LoopMIDIStatusLabel.Font.Color := clGreen;
      LoopMIDIInstallButton.Enabled := False;
      LoopMIDISkipButton.Enabled := False;
    end
    else
    begin
      LoopMIDIStatusLabel.Caption := 'loopMIDI driver: NOT INSTALLED' + #13#10 + #13#10 +
                                      '{#MyAppName} requires the virtualMIDI driver to function.' + #13#10 +
                                      'Please click "Download loopMIDI" to open the download page, install loopMIDI, then click "Re-check".';
      LoopMIDIStatusLabel.Font.Color := clRed;
      LoopMIDIInstallButton.Enabled := True;
      LoopMIDISkipButton.Enabled := True;
    end;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  
  if CurPageID = LoopMIDIPage.ID then
  begin
    if (not LoopMIDIInstalled) and (not LoopMIDISkipped) then
    begin
      // Re-check one more time
      LoopMIDIInstalled := IsLoopMIDIInstalled;
      
      if not LoopMIDIInstalled then
      begin
        if MsgBox('loopMIDI is not installed. {#MyAppName} will not function correctly without it.' + #13#10 + #13#10 +
                  'Do you want to continue anyway?', mbConfirmation, MB_YESNO) = IDNO then
        begin
          Result := False;
        end;
      end;
    end;
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
end;
