@echo off
setlocal

echo ============================================================
echo  VideoPress — Build Script
echo ============================================================
echo.

where dotnet >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] .NET SDK not found.
    echo Please install .NET 8 SDK from: https://dotnet.microsoft.com/download
    pause & exit /b 1
)

:: Check FFmpeg binaries are present
if not exist "Assets\ffmpeg\ffmpeg.exe" (
    echo [ERROR] Assets\ffmpeg\ffmpeg.exe not found.
    echo.
    echo Run fetch-ffmpeg.bat first to download the FFmpeg binaries.
    echo They will be embedded into the final .exe automatically.
    pause & exit /b 1
)

echo [1/3] Restoring NuGet packages...
dotnet restore VideoPress.csproj
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] Restore failed. & pause & exit /b 1 )

echo.
echo [2/3] Building release...
dotnet build VideoPress.csproj -c Release --no-restore
if %ERRORLEVEL% NEQ 0 ( echo [ERROR] Build failed. & pause & exit /b 1 )

echo.
echo [3/3] Publishing self-contained .exe (this embeds FFmpeg — may take a minute)...
dotnet publish VideoPress.csproj -c Release -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o ./publish/VideoPress

if %ERRORLEVEL% NEQ 0 ( echo [ERROR] Publish failed. & pause & exit /b 1 )

echo.
echo ============================================================
echo  BUILD SUCCESSFUL
echo  Output: .\publish\VideoPress\VideoPress.exe
echo  Size includes embedded FFmpeg (~110 MB total — expected)
echo ============================================================
echo.

set /p RUN="Run VideoPress now? (y/n): "
if /i "%RUN%"=="y" start "" ".\publish\VideoPress\VideoPress.exe"

pause
