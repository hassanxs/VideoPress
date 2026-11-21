@echo off
setlocal
echo ============================================================
echo  VideoPress — Build Installer
echo ============================================================
echo.

:: ── Step 1: Check Inno Setup is installed ───────────────────
set ISCC=""
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
    set ISCC="%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
) else if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" (
    set ISCC="%ProgramFiles%\Inno Setup 6\ISCC.exe"
)

if %ISCC%=="" (
    echo [ERROR] Inno Setup 6 not found.
    echo.
    echo Please download and install it from:
    echo   https://jrsoftware.org/isdl.php
    echo.
    echo Then run this script again.
    pause & exit /b 1
)

:: ── Step 2: Check FFmpeg assets exist ───────────────────────
if not exist "Assets\ffmpeg\ffmpeg.exe" (
    echo [ERROR] Assets\ffmpeg\ffmpeg.exe not found.
    echo Run fetch-ffmpeg.bat first.
    pause & exit /b 1
)

:: ── Step 3: Build the app ────────────────────────────────────
echo [1/3] Building VideoPress (Release + publish)...
dotnet publish VideoPress.csproj -c Release -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o ./publish/VideoPress

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed.
    pause & exit /b 1
)

:: ── Step 4: Create dist folder ──────────────────────────────
if not exist "dist" mkdir dist

:: ── Step 5: Compile installer ────────────────────────────────
echo.
echo [2/3] Compiling installer with Inno Setup...
%ISCC% "installer\VideoPress.iss"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Inno Setup compilation failed.
    pause & exit /b 1
)

:: ── Step 6: Done ─────────────────────────────────────────────
echo.
echo [3/3] Done!
echo.
echo ============================================================
echo  OUTPUT: dist\VideoPress-Setup-1.0.3.exe
echo ============================================================
echo.
echo The installer:
echo   - Installs VideoPress to Program Files
echo   - Creates Start Menu shortcut
echo   - Offers optional Desktop shortcut
echo   - Registers in Add/Remove Programs
echo   - Offers to launch app when done
echo   - Includes a clean uninstaller
echo.
for %%F in ("dist\VideoPress-Setup-*.exe") do (
    echo  File: %%F
    echo  Size: %%~zF bytes
)

pause
