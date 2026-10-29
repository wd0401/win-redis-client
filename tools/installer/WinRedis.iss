; WinRedis Inno Setup 安装脚本（Inno Setup 6+）
; 用法：在 Inno Setup Compiler 中打开本文件并 Build，
; 或命令行：iscc tools\installer\WinRedis.iss
; 前置：先执行 README 中的单文件发布命令，生成 publish\win-x64\WinRedisClient.exe

#define AppName "WinRedis"
#define AppVersion "1.0.0"
#define AppPublisher "WinRedis Contributors"
#define AppExe "WinRedisClient.exe"
#define ProjectRoot "..\.."

[Setup]
AppId={{6E5D6A2B-53B3-4E7B-9F1A-6B0C9C1E2A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; 按当前用户安装，免管理员权限；如需装到 Program Files 改为 admin
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#ProjectRoot}\publish\installer
OutputBaseFilename={#AppName}-{#AppVersion}-win-x64-setup
SetupIconFile={#ProjectRoot}\src\WinRedisClient.App\Assets\winredis-icon.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#ProjectRoot}\publish\win-x64\WinRedisClient.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 仅删除程序目录；用户数据保留在 %AppData%\WinRedis，卸载后重新安装仍可继续使用
Type: filesandordirs; Name: "{app}"
