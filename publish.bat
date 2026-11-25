@echo off
cd /d %~dp0
echo ==========================================
echo   Building Hufra.exe (single file)
echo ==========================================
echo.

dotnet publish Hufra.App\Hufra.App.csproj -c Release -r win-x64 --self-contained true -o dist

if errorlevel 1 (
    echo.
    echo ERROR: Build failed. Make sure .NET 8 SDK is installed:
    echo https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo.
echo Done! The app folder is at: dist\
echo Run dist\Hufra.exe directly, or copy the WHOLE "dist" folder
echo (not just the exe alone) if you move it elsewhere.
echo.
pause
