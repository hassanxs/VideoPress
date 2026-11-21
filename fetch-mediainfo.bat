@echo off
setlocal
echo ============================================================
echo  VideoPress — Fetch MediaInfo CLI
echo ============================================================
echo.

set DEST=Assets\mediainfo
set EXE=%DEST%\MediaInfo.exe

if exist "%EXE%" (
    echo [OK] MediaInfo.exe already present at %EXE%
    echo      Delete it and re-run this script to force a fresh download.
    goto :done
)

if not exist "%DEST%" mkdir "%DEST%"

echo [1/2] Downloading MediaInfo CLI (x64)...
echo.

set URL=https://mediaarea.net/download/binary/mediainfo/24.11/MediaInfo_CLI_24.11_Windows_x64.zip
set ZIP=%DEST%\MediaInfo_CLI.zip

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri '%URL%' -OutFile '%ZIP%'"

if not exist "%ZIP%" (
    echo [ERROR] Download failed. Check your internet connection.
    pause & exit /b 1
)

echo [2/2] Extracting MediaInfo.exe...

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Expand-Archive -Path '%ZIP%' -DestinationPath '%DEST%\extracted' -Force"

if exist "%DEST%\extracted\MediaInfo.exe" (
    move /Y "%DEST%\extracted\MediaInfo.exe" "%EXE%" > nul
) else (
    for /R "%DEST%\extracted" %%F in (MediaInfo.exe) do (
        move /Y "%%F" "%EXE%" > nul
    )
)

if exist "%DEST%\extracted" rmdir /S /Q "%DEST%\extracted"
if exist "%ZIP%" del /F /Q "%ZIP%"

if exist "%EXE%" (
    echo.
    echo [OK] MediaInfo.exe saved to %EXE%
    goto :done
) else (
    echo [ERROR] Could not find MediaInfo.exe in the downloaded archive.
    pause & exit /b 1
)

:done
echo.
echo ============================================================
echo  Now run build.bat or make-installer.bat to bundle it.
echo ============================================================
echo.
pause
