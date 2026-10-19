@echo off
setlocal
echo FSR 4.1.1 Native AA - standalone installer
echo Close the game before continuing.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -PluginDll "BelmontsCurseGraphics.dll" %*
set "install_result=%errorlevel%"
pause
exit /b %install_result%
