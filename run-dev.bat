@echo off
if not exist "Assets\ffmpeg\ffmpeg.exe" (
    echo [ERROR] Assets\ffmpeg\ffmpeg.exe not found.
    echo Run fetch-ffmpeg.bat first, then try again.
    pause & exit /b 1
)
echo Starting VideoPress in development mode...
dotnet run --project VideoPress.csproj -c Debug 2>&1
pause
