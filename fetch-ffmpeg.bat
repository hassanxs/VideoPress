@echo off
setlocal
echo ============================================================
echo  VideoPress — Fetch FFmpeg Binaries
echo  Run this ONCE before building. Downloads ~110 MB.
echo ============================================================
echo.

where curl >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] curl not found. Install curl or use Windows 10+.
    pause & exit /b 1
)
where tar >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] tar not found. Requires Windows 10 build 17063+.
    pause & exit /b 1
)

if not exist "Assets\ffmpeg" mkdir "Assets\ffmpeg"

set ZIP="%TEMP%\ffmpeg-build.zip"
set URL=https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip

echo [1/3] Downloading FFmpeg static build (~110 MB)...
curl -L --progress-bar -o %ZIP% "%URL%"
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Download failed. Check internet connection.
    pause & exit /b 1
)

echo.
echo [2/3] Extracting...
set EXTRACT=%TEMP%\ffmpeg-extract
if exist "%EXTRACT%" rmdir /s /q "%EXTRACT%"
mkdir "%EXTRACT%"
tar -xf %ZIP% -C "%EXTRACT%"
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Extraction failed.
    pause & exit /b 1
)

echo [3/3] Copying ffmpeg.exe and ffprobe.exe to Assets\ffmpeg\...
for /r "%EXTRACT%" %%F in (ffmpeg.exe) do (
    copy /Y "%%F" "Assets\ffmpeg\ffmpeg.exe" >nul
    echo     Copied: %%F
)
for /r "%EXTRACT%" %%F in (ffprobe.exe) do (
    copy /Y "%%F" "Assets\ffmpeg\ffprobe.exe" >nul
    echo     Copied: %%F
)

del %ZIP%
rmdir /s /q "%EXTRACT%"

if not exist "Assets\ffmpeg\ffmpeg.exe" (
    echo [ERROR] ffmpeg.exe not found after extraction.
    pause & exit /b 1
)

echo.
echo ============================================================
echo  Done! Assets\ffmpeg\ now contains:
dir /b "Assets\ffmpeg\"
echo.
echo  You can now run build.bat to produce a fully self-contained exe.
echo ============================================================
pause
