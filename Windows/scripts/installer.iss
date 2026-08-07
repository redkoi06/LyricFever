; LyricFever Windows 安装脚本（Inno Setup 6）
; 用法: ISCC.exe scripts\installer.iss
; 产物: publish\LyricFeverSetup-1.0.0.exe（基于 publish.ps1 生成的 portable 目录）
;
; 设计：per-user 安装（%LOCALAPPDATA%\Programs\LyricFever），无需管理员权限；
; DB/设置写入 %APPDATA%\LyricFever；此版本不再部署任何本地翻译模型。

#define MyAppName "Lyric Fever"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "LyricFever"
#define MyAppExeName "LyricFever.exe"
#define SourceDir "..\publish\LyricFever"

[Setup]
AppId={{6C4E8F2A-9B1D-4E5A-8C3F-2A7D1B5E9C41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\LyricFever
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\publish
OutputBaseFilename=LyricFeverSetup-{#MyAppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\LyricFever.Windows.App\Assets\LyricFever.ico

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; 1.1.0 起停用本地机器翻译，升级时移除旧模型和原生推理运行库。
Type: filesandordirs; Name: "{app}\models"
Type: files; Name: "{app}\LyricFeverTranslation.dll"
Type: files; Name: "{app}\dnnl.dll"

[Icons]
Name: "{autoprograms}\Lyric Fever"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Lyric Fever"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 Lyric Fever"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 清理历史模型缓存（保留设置/DB，卸载不删除用户数据）
Type: filesandordirs; Name: "{userappdata}\LyricFever\models"
