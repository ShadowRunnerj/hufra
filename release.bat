@echo off
chcp 65001 >nul
cd /d %~dp0
rem ===== نشر إصدار جديد: يبني ويرفع Hufra-Setup.exe كـ GitHub Release =====
rem المتطلبات (مرة وحدة): .NET 8 SDK + Inno Setup 6 + GitHub CLI (gh) وتسجيل الدخول: gh auth login
rem الاستخدام:  release.bat 2.6.2
if "%~1"=="" (
    echo Usage: release.bat VERSION   e.g. release.bat 2.6.2
    exit /b 1
)
set VER=%~1
for /f "usebackq eol=# tokens=*" %%R in ("Hufra.App\update-repo.txt") do set REPO=%%R
if /i "%REPO%"=="OWNER/REPO" (
    echo ERROR: set your GitHub repo in Hufra.App\update-repo.txt  ^(owner/repo^)
    exit /b 1
)
where gh >nul 2>nul || (echo ERROR: install GitHub CLI: https://cli.github.com & exit /b 1)

echo Updating version to %VER% ...
powershell -NoProfile -Command "$v='%VER%'; (Get-Content installer\Hufra.iss -Raw) -replace '(?m)^(#define\s+MyAppVersion\s+\")[\d.]+(\")','${1}'+$v+'${2}' | Set-Content installer\Hufra.iss -Encoding UTF8; (Get-Content Hufra.App\MainWindow.xaml.cs -Raw) -replace 'CurrentVersion = \"[\d.]+\"','CurrentVersion = \"'+$v+'\"' | Set-Content Hufra.App\MainWindow.xaml.cs -Encoding UTF8"

call publish.bat || exit /b 1
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Hufra.iss || exit /b 1

gh release create v%VER% Output\Hufra-Setup.exe --repo %REPO% --title "Hufra %VER%" --notes "تحديث %VER%" || exit /b 1
echo.
echo Done: v%VER% published. Users get the update banner automatically.
