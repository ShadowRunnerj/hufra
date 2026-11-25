@echo off
cd /d %~dp0
echo ==========================================
echo   Building Hufra-Setup.exe installer
echo ==========================================
echo.

set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

if not exist "%ISCC%" (
    echo ERROR: Inno Setup not found. Download it here (free):
    echo https://jrsoftware.org/isdl.php
    echo Then run this file again.
    pause
    exit /b 1
)

if not exist "dist\Hufra.exe" (
    echo ERROR: dist\Hufra.exe not found.
    echo Run publish.bat first, before running this file.
    pause
    exit /b 1
)

"%ISCC%" "installer\Hufra.iss"

if errorlevel 1 (
    echo.
    echo ERROR: Failed to build the installer.
    pause
    exit /b 1
)

echo.
echo Done! The installer is at: Output\Hufra-Setup.exe
echo This is the file you share with other people.
echo.
pause
