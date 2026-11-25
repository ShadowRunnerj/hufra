; سكربت Inno Setup لبرنامج "حُفرة"
; ==========================================
; يحوّل ملف Hufra.exe (الناتج من publish.bat) إلى ملف تنصيب احترافي
; باسم Hufra-Setup.exe مع أيقونة، اختصار سطح المكتب، وقائمة ابدأ.
;
; المتطلبات قبل التشغيل:
; 1) نفّذ publish.bat أولاً (لازم يكون عندك ..\dist\Hufra.exe موجود)
; 2) ثبّت Inno Setup من https://jrsoftware.org/isdl.php (مجاني)
; 3) شغّل build-installer.bat، أو افتح هذا الملف بـ Inno Setup Compiler واضغط Compile

#define MyAppName "حُفرة"
#define MyAppVersion "2.6.2"
#define MyAppPublisher "Hufra"
#define MyAppExeName "Hufra.exe"

[Setup]
AppId={{8F2B1E4A-6C3D-4B7A-9E1F-3A5C7D9B2E4F}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\Output
OutputBaseFilename=Hufra-Setup
SetupIconFile=hufra_icon_small.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; ملاحظة: Inno Setup ما يدعم RTL كامل بشكل رسمي مضمّن، فواجهة التنصيب
; نفسها تطلع LTR حتى لو حطينا لغة عربية. هذا لا يؤثر على واجهة البرنامج
; نفسه بعد التثبيت (اللي مبنية RTL بالكامل في WPF).
; لو تبي ترجمة عربية لخطوات التنصيب، نزّل ملف Arabic.isl من:
; https://github.com/jrsoftware/issrc/tree/main/Files/Languages/Unofficial
; وحطه بمجلد Languages داخل تثبيت Inno Setup، ثم فعّل السطر تحت:
; [Languages]
; Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent
; بعد التحديث التلقائي الصامت: شغّل النسخة الجديدة تلقائياً
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: WizardSilent
