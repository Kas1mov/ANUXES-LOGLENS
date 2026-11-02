@echo off
setlocal
cd /d "%~dp0"

echo ==========================================
echo   ANUXES LOGLENS - Release Build
echo ==========================================

dotnet publish ANUXES.LOGLENS.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
if errorlevel 1 (
    echo.
    echo Build failed. Check that a compatible .NET SDK is installed and review the error above.
    pause
    exit /b 1
)

echo.
echo Build completed:
echo %~dp0bin\Release\net8.0-windows\win-x64\publish\ANUXES.LOGLENS.exe
pause
