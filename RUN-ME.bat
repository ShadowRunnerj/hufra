@echo off
setlocal enabledelayedexpansion
cd /d %~dp0

echo ==========================================
echo   Hufra Setup (Automatic)
echo   This will install prerequisites if needed,
echo   build the app, package it, and launch it.
echo   First run may take a few minutes.
echo ==========================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 goto need_dotnet
echo [1/4] .NET SDK found - OK
goto check_innosetup

:need_dotnet
echo [1/4] .NET SDK not found. Installing via winget...
winget install Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
if errorlevel 1 goto dotnet_fail
echo.
echo NOTE: .NET SDK was just installed.
echo Please CLOSE this window and run RUN-ME.bat again
echo so Windows can detect the newly installed tool.
pause
exit /b 0

:dotnet_fail
echo.
echo ERROR: Could not install .NET SDK automatically.
echo Possible reasons: winget is not available on this Windows version,
echo or administrator rights are required.
echo Please install manually from: https://dotnet.microsoft.com/download
echo Then run this file again.
pause
exit /b 1

:check_innosetup
call :find_iscc
if defined ISCC goto innosetup_ok

echo [2/4] Inno Setup not found. Installing via winget...
winget install JRSoftware.InnoSetup -e --silent --accept-source-agreements --accept-package-agreements

call :find_iscc
if defined ISCC goto innosetup_ok

echo.
echo ERROR: Inno Setup was installed but could not be located automatically.
echo Please open File Explorer, search for "ISCC.exe", and tell the developer
echo the full path it shows - this helps fix the script for good.
echo As a workaround for now, you can also try: install Inno Setup manually
echo from https://jrsoftware.org/isdl.php using default settings, then run
echo this file again.
pause
exit /b 1

:innosetup_ok
echo [2/4] Inno Setup found - OK
echo       Location: %ISCC%

echo [3/4] Building the app, this may take a minute or two...
dotnet publish Hufra.App\Hufra.App.csproj -c Release -r win-x64 --self-contained true -o dist
if errorlevel 1 goto build_fail
echo Build succeeded - OK

echo [4/4] Packaging the installer...
"%ISCC%" "installer\Hufra.iss"
if not exist "Output\Hufra-Setup.exe" goto package_fail

echo.
echo ==========================================
echo Done! Launching Hufra now...
echo ==========================================
timeout /t 2 >nul
"Output\Hufra-Setup.exe" /VERYSILENT /SUPPRESSMSGBOXES
exit /b 0

:build_fail
echo.
echo ERROR: Build failed. Run publish.bat by itself to see the full error.
pause
exit /b 1

:package_fail
echo.
echo ERROR: Failed to create the installer.
pause
exit /b 1

:find_iscc
set "ISCC="
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC (
    for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do set "ISCC=%%I"
)
exit /b 0
