@echo off
setlocal
REM Installs ClashGUID Visualizer for the current user (no administrator rights needed).
set "SRC=%~dp0bundle\ClashGuidVisualizer.bundle"
set "DST=%APPDATA%\Autodesk\ApplicationPlugins\ClashGuidVisualizer.bundle"

tasklist /FI "IMAGENAME eq Roamer.exe" 2>nul | find /I "Roamer.exe" >nul
if not errorlevel 1 (
    echo Navisworks is running. Close it and run install.bat again.
    goto :fail
)
if not exist "%SRC%\PackageContents.xml" (
    echo Add-in files not found: "%SRC%"
    echo Run install.bat from the extracted repository folder.
    goto :fail
)
if exist "%ProgramData%\Autodesk\ApplicationPlugins\ClashGuidVisualizer.bundle" (
    echo Another copy is installed in "%ProgramData%\Autodesk\ApplicationPlugins".
    echo Remove it first ^(it was installed by the old MSI^), then run install.bat again.
    goto :fail
)

if exist "%DST%" rmdir /s /q "%DST%"
xcopy "%SRC%" "%DST%\" /e /i /q /y >nul
if errorlevel 1 (
    echo Copy failed.
    goto :fail
)
REM Files from a downloaded ZIP are marked as coming from the internet; .NET will not load such a DLL.
powershell -NoProfile -Command "Get-ChildItem -LiteralPath $env:DST -Recurse | Unblock-File"

echo Installed to "%DST%"
echo Start Navisworks Manage 2025 and open Clash Detective ^> Results.
pause
exit /b 0

:fail
pause
exit /b 1
