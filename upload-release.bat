@echo off
setlocal
cd /d "%~dp0"

echo.
echo === BackupSaves: pack bin\Release and upload to GitHub ===
echo     (no build - uses already compiled Release output)
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0upload-release.ps1" %*
set ERR=%ERRORLEVEL%

if %ERR% equ 0 (
  echo.
  echo OK.
  pause
  exit /b 0
)

echo.
if %ERR% equ 2 (
  echo GitHub login / gh is missing.
  echo See instructions above: open cmd and run  gh auth login
) else (
  echo FAILED with exit code %ERR%.
)
pause
exit /b %ERR%
