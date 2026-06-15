@echo off
setlocal

cd /d "%~dp0"

set "VERSION=%~1"
if not defined VERSION (
    for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "([xml](Get-Content -Raw 'DNFWeeklyWidget.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"`) do set "VERSION=%%V"
)

if not defined VERSION (
    echo Failed to determine the release version.
    pause
    exit /b 1
)

echo Building DNFWeeklyWidget %VERSION%...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-Release.ps1" -Version "%VERSION%"

if errorlevel 1 (
    echo.
    echo Release build failed.
    pause
    exit /b 1
)

echo.
echo Release build completed.
echo Output: %~dp0artifacts\release
pause

