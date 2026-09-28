@echo off
setlocal enabledelayedexpansion

set ROOT=%~dp0

if "%CI%"=="true" (
    set BUILD_CONFIG=Release
) else (
    set BUILD_CONFIG=Release
)

echo === Publishing Zaya.DocumentScanner.Impl.Windows (%BUILD_CONFIG%) ===

dotnet publish "%ROOT%src\Zaya.DocumentScanner.Impl.Windows\Zaya.DocumentScanner.Impl.Windows.csproj" -c %BUILD_CONFIG% -o "%ROOT%out"
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%

echo === Detecting version ===

for /f "usebackq delims=" %%a in (`dotnet msbuild "%ROOT%src\Zaya.DocumentScanner.Impl.Windows\Zaya.DocumentScanner.Impl.Windows.csproj" -getProperty:Version -nologo -v:q`) do set VER=%%a
set VER=!VER: =!
if "!VER!"=="" set VER=1.0.0

for /f "tokens=1,2,3 delims=." %%a in ("!VER!") do (
    set VER_MAJOR=%%a
    set VER_MINOR=%%b
)
set CHANNEL=!VER_MAJOR!.!VER_MINOR!
echo   Version=!VER!  Channel=!CHANNEL!

echo !VER!>"%ROOT%out\version.txt"
echo !CHANNEL!>"%ROOT%out\channel.txt"

echo === Creating Zaya.DocumentScanner-!VER!.zip ===
powershell -NoProfile -Command "Compress-Archive -Path '%ROOT%out\Zaya.DocumentScanner.exe' -DestinationPath '%ROOT%out\Zaya.DocumentScanner-!VER!.zip' -Force"
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%

echo === Done: out\Zaya.DocumentScanner-!VER!.zip ===
