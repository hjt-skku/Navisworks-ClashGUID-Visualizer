@echo off
setlocal
REM Removes ClashGUID Visualizer installed by install.bat.
set "DST=%APPDATA%\Autodesk\ApplicationPlugins\ClashGuidVisualizer.bundle"

tasklist /FI "IMAGENAME eq Roamer.exe" 2>nul | find /I "Roamer.exe" >nul
if not errorlevel 1 (
    echo Navisworks is running. Close it and run uninstall.bat again.
    pause
    exit /b 1
)
if not exist "%DST%" (
    echo ClashGUID Visualizer is not installed.
    pause
    exit /b 0
)
rmdir /s /q "%DST%"
if exist "%DST%" (
    echo Could not remove "%DST%". Close Navisworks and try again.
    pause
    exit /b 1
)
echo ClashGUID Visualizer has been removed.
pause
exit /b 0
