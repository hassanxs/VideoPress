using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using VideoPress.Models;
using VideoPress.Services;

namespace VideoPress.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly FFmpegService       _ffmpeg     = new();
    private readonly GpuDetectionService _gpuSvc;
    private readonly PresetService       _presetSvc  = new();
    private readonly ThemeService        _themeSvc   = new();
    private readonly MediaInfoService    _mediaInfo  = new();
    private CancellationTokenSource?    _cts;

    // ── Presets ───────────────────────────────────────────────────────────────
    public ObservableCollection<Models.Preset> UserPresets  { get; } = new();
    public IReadOnlyList<Models.Preset>        BuiltInPresets => PresetService.BuiltIn;

    [ObservableProperty] private bool      _showPresetsPanel;
    [ObservableProperty] private string    _themeChoice     = "Auto";
    [ObservableProperty] private VideoFile? _compareFile;
    [ObservableProperty] private string _activePresetName = "";
    [ObservableProperty] private bool   _presetModified;
    [ObservableProperty] private string _newPresetName   = "";

    // ── FFmpeg / startup state ────────────────────────────────────────────────
    [ObservableProperty] private bool   _ffmpegReady;
    [ObservableProperty] private string _ffmpegStatus      = "Starting…";
    [ObservableProperty] private System.Windows.Media.SolidColorBrush _ffmpegStatusBrush
        = MakeBrush("#F39C12");

    // Extraction progress (shown only on very first run)
    [ObservableProperty] private bool   _isExtracting;
    [ObservableProperty] private double _extractProgress;
    [ObservableProperty] private string _extractMessage    = "";

    // ── GPU state ────────────────────────────────────────────────────────────
    [ObservableProperty] private GpuInfo _gpu = GpuInfo.Detecting;

    // ── File queue ───────────────────────────────────────────────────────────
    public ObservableCollection<VideoFile> Files { get; } = new();

    [ObservableProperty] private bool   _isRunning;
    [ObservableProperty] private string _overallStatus  = "Ready — drop videos to begin.";
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private bool   _hasFiles;

    // ── Settings ─────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    [NotifyPropertyChangedFor(nameof(IsWebMCodec))]
    private VideoCodec _selectedCodec = VideoCodec.H264;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private QualityMode _selectedQualityMode = QualityMode.CRF;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private int _crfValue = 23;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private int _bitrateKbps = 2000;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private int _targetSizeMB = 50;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private ResolutionMode _resMode = ResolutionMode.Original;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private int _resPresetIndex = 1;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private int _customWidth = 1920;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private int _customHeight = 1080;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private bool _maintainAspect = true;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private int _audioCodecIndex = 0;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private int _audioBitrateKbps = 128;

    [ObservableProperty] private bool   _addSuffix       = true;
    [ObservableProperty] private bool   _autoOpenOutput  = true;
    [ObservableProperty] private string _outputFolder    = "";

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))]
    private string _customFlags = "";

    // ── Log ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _logText  = "";
    [ObservableProperty] private bool   _showLog;

    // ── Derived ──────────────────────────────────────────────────────────────
    public string CommandPreview => BuildSettings().GetPreviewCommand();

    public string AppVersion =>
        "v" + (System.Reflection.Assembly.GetExecutingAssembly()
                   .GetName().Version?.ToString(3) ?? "1.0.3");
    public bool   IsWebMCodec    => SelectedCodec is VideoCodec.VP9 or VideoCodec.AV1;

    // Called by CommunityToolkit whenever any [ObservableProperty] changes.
    // If a preset is active and user tweaks a setting, mark it modified.
    partial void OnSelectedCodecChanged(VideoCodec value)        => MarkModified();
    partial void OnSelectedQualityModeChanged(QualityMode value) => MarkModified();
    partial void OnCrfValueChanged(int value)                    => MarkModified();
    partial void OnBitrateKbpsChanged(int value)                 => MarkModified();
    partial void OnTargetSizeMBChanged(int value)                => MarkModified();
    partial void OnResModeChanged(ResolutionMode value)          => MarkModified();
    partial void OnResPresetIndexChanged(int value)              => MarkModified();
    partial void OnCustomWidthChanged(int value)                 => MarkModified();
    partial void OnCustomHeightChanged(int value)                => MarkModified();
    partial void OnAudioCodecIndexChanged(int value)             => MarkModified();
    partial void OnAudioBitrateKbpsChanged(int value)            => MarkModified();
    partial void OnCustomFlagsChanged(string value)              => MarkModified();

    private void MarkModified()
    {
        if (!string.IsNullOrEmpty(ActivePresetName))
            PresetModified = true;
    }
    public bool   CanCompress    =>
        FfmpegReady &&
        Files.Any(f => f.Status == FileStatus.Waiting) &&
        !IsRunning;

    private static readonly string[] ResPresets =
        ["3840x2160","1920x1080","1280x720","854x480","640x360"];
    private static readonly AudioCodecType[] AudioCodecMap =
        [AudioCodecType.AAC, AudioCodecType.MP3, AudioCodecType.Opus, AudioCodecType.Strip];

    // ── Init ─────────────────────────────────────────────────────────────────
    public MainViewModel()
    {
        _gpuSvc = new GpuDetectionService(_ffmpeg);

        Files.CollectionChanged += (_, _) =>
        {
            CompressAllCommand.NotifyCanExecuteChanged();
            HasFiles = Files.Count > 0;
        };

        // Fire-and-forget startup sequence on the UI thread
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(StartupAsync);
    }

    private async Task StartupAsync()
    {
        try
        {
        // ── Step 0: Apply saved / system theme ───────────────────────────
        var savedTheme = _themeSvc.Load();
        ThemeChoice = savedTheme;
        var theme = savedTheme switch
        {
            "Light" => AppTheme.Light,
            "Dark"  => AppTheme.Dark,
            _       => _themeSvc.DetectSystemTheme(),  // "Auto"
        };
        _themeSvc.ApplyTheme(theme);

        // ── Step 1: Extract embedded FFmpeg if needed ─────────────────────
        if (_ffmpeg.IsBundled)
        {
            IsExtracting = true;
            FfmpegStatus = "Preparing…";

            var progress = new Progress<(double pct, string msg)>(p =>
            {
                ExtractProgress = p.pct;
                ExtractMessage  = p.msg;
                FfmpegStatus    = p.msg;
            });

            try
            {
                await _ffmpeg.EnsureExtractedAsync(progress);
            }
            catch (Exception ex)
            {
                FfmpegStatus      = "Extraction failed";
                FfmpegStatusBrush = MakeBrush("#E74C3C");
                IsExtracting      = false;
                MessageBox.Show(
                    $"Failed to extract FFmpeg:\n\n{ex.Message}",
                    "VideoPress", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IsExtracting = false;
        }
        else if (!_ffmpeg.IsAvailable)
        {
            // Binaries not embedded and not on disk — shouldn't happen in release,
            // but give a clear message during development
            FfmpegStatus      = "FFmpeg missing — run fetch-ffmpeg.bat";
            FfmpegStatusBrush = MakeBrush("#E74C3C");
            return;
        }

        // ── Step 2: Mark FFmpeg ready ─────────────────────────────────────
        FfmpegReady       = true;
        FfmpegStatus      = "FFmpeg ready";
        FfmpegStatusBrush = MakeBrush("#2ECC71");
        CompressAllCommand.NotifyCanExecuteChanged();

        // ── Step 3: Detect GPU ────────────────────────────────────────────
        Gpu = GpuInfo.Detecting;
        try
        {
            Gpu = await _gpuSvc.DetectAsync();
            // Write detection result to log so user can verify via Log panel
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[GPU Detection] " + _gpuSvc.DetectionLog.TrimEnd());
            sb.AppendLine($"[GPU Detection] Encoder tests:");
            // DetectionLog already contains per-encoder results appended during detection
            sb.Append($"[GPU Detection] → Selected: {Gpu.DisplayName}");
            if (Gpu.H264Encoder != null) sb.Append($" | H.264={Gpu.H264Encoder}");
            if (Gpu.H265Encoder != null) sb.Append($" | H.265={Gpu.H265Encoder}");
            sb.AppendLine();
            LogText += sb.ToString();
        }
        catch (Exception ex)
        {
            Gpu = GpuInfo.CpuFallback;
            LogText += $"[GPU Detection] Failed: {ex.Message}\n";
        }

        // Refresh command preview now that GPU is known
        OnPropertyChanged(nameof(CommandPreview));

        // ── Step 4: Extract MediaInfo CLI if bundled ─────────────────────
        if (_mediaInfo.IsBundled)
        {
            try { await _mediaInfo.ExtractAsync(); }
            catch { /* non-fatal */ }
        }

        // ── Step 5: Load saved user presets ──────────────────────────────
        var saved = _presetSvc.LoadUserPresets();
        foreach (var p in saved) UserPresets.Add(p);
        }
        catch (Exception ex)
        {
            // Show error with full detail so startup failures are visible
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            MessageBox.Show(
                $"Startup failed:\n\n{inner.GetType().Name}:\n{inner.Message}\n\n{inner.StackTrace?[..Math.Min(400, inner.StackTrace?.Length ?? 0)]}",
                "VideoPress Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Theme command ─────────────────────────────────────────────────────────
    [RelayCommand]
    public void SetTheme(string choice)  // "Dark", "Light", "Auto"
    {
        ThemeChoice = choice;
        var theme = choice switch
        {
            "Light" => AppTheme.Light,
            "Dark"  => AppTheme.Dark,
            _       => _themeSvc.DetectSystemTheme(),
        };
        _themeSvc.ApplyTheme(theme);
        _themeSvc.Save(choice);
    }

    // ── Preset commands ───────────────────────────────────────────────────────
    [RelayCommand]
    public void TogglePresetsPanel() => ShowPresetsPanel = !ShowPresetsPanel;

    [RelayCommand]
    public void ResetToDefaults()
    {
        // Reset all settings to baseline
        SelectedCodec       = VideoCodec.H264;
        SelectedQualityMode = QualityMode.CRF;
        CrfValue            = 23;
        BitrateKbps         = 2000;
        TargetSizeMB        = 50;
        ResMode             = ResolutionMode.Original;
        ResPresetIndex      = 1;    // 1080p
        CustomWidth         = 1920;
        CustomHeight        = 1080;
        MaintainAspect      = true;
        AudioCodecIndex     = 0;    // AAC
        AudioBitrateKbps    = 128;
        CustomFlags         = "";

        // Clear preset label
        ActivePresetName = "";
        PresetModified   = false;
    }

    [RelayCommand]
    public void ApplyPreset(Models.Preset preset)
    {
        SelectedCodec       = preset.Codec;
        SelectedQualityMode = preset.QualityMode;
        CrfValue            = preset.CRF;
        BitrateKbps         = preset.BitrateKbps;
        TargetSizeMB        = preset.TargetSizeMB;
        ResMode             = preset.ResMode;
        ResPresetIndex      = preset.ResPresetIdx;
        CustomWidth         = preset.CustomWidth;
        CustomHeight        = preset.CustomHeight;
        MaintainAspect      = preset.MaintainAspect;
        AudioCodecIndex     = preset.AudioCodecIndex;
        AudioBitrateKbps    = preset.AudioBitrateKbps;
        CustomFlags         = preset.CustomFlags;

        ActivePresetName = preset.Name;
        PresetModified   = false;
        ShowPresetsPanel = false;
    }

    [RelayCommand]
    public void SavePreset()
    {
        var name = NewPresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var preset = new Models.Preset
        {
            Name            = name,
            IsBuiltIn       = false,
            Codec           = SelectedCodec,
            QualityMode     = SelectedQualityMode,
            CRF             = CrfValue,
            BitrateKbps     = BitrateKbps,
            TargetSizeMB    = TargetSizeMB,
            ResMode         = ResMode,
            ResPresetIdx    = ResPresetIndex,
            CustomWidth     = CustomWidth,
            CustomHeight    = CustomHeight,
            MaintainAspect  = MaintainAspect,
            AudioCodecIndex  = AudioCodecIndex,
            AudioBitrateKbps = AudioBitrateKbps,
            CustomFlags     = CustomFlags,
        };

        var list = UserPresets.ToList();
        _presetSvc.AddOrReplace(list, preset);

        // Refresh observable collection
        UserPresets.Clear();
        foreach (var p in list) UserPresets.Add(p);

        ActivePresetName = name;
        PresetModified   = false;
        NewPresetName    = "";
    }

    [RelayCommand]
    public void DeletePreset(Models.Preset preset)
    {
        var list = UserPresets.ToList();
        _presetSvc.Delete(list, preset);
        UserPresets.Remove(preset);
        if (ActivePresetName == preset.Name) ActivePresetName = "";
    }

    // ── File commands ─────────────────────────────────────────────────────────
    [RelayCommand]
    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (!File.Exists(p)) continue;
            var ext = Path.GetExtension(p).ToLowerInvariant();
            if (ext is not (".mp4" or ".mov" or ".mkv" or ".avi" or ".webm"
                        or ".m4v" or ".wmv" or ".flv" or ".ts" or ".3gp"))
                continue;
            if (Files.Any(f => f.FullPath == p)) continue;

            var info = new FileInfo(p);
            var vf = new VideoFile { FullPath = p, Name = info.Name, OriginalSize = info.Length };
            Files.Add(vf);
            // Probe metadata + thumbnail in background — non-blocking
            _ = ProbeFileAsync(vf);
        }
        CompressAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void RemoveFile(VideoFile file)
    {
        if (file.Status == FileStatus.Running) return;
        Files.Remove(file);
        CompressAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void MoveFile(int[] fromTo)  // [fromIndex, toIndex]
    {
        var from = fromTo[0];
        var to   = fromTo[1];
        if (from < 0 || from >= Files.Count) return;
        if (to   < 0 || to   >= Files.Count) return;
        if (from == to) return;
        Files.Move(from, to);
    }

    [RelayCommand]
    public void ClearQueue()
    {
        var toRemove = Files.Where(f => f.Status is FileStatus.Done or FileStatus.Error or FileStatus.Cancelled).ToList();
        foreach (var f in toRemove) Files.Remove(f);
        CompressAllCommand.NotifyCanExecuteChanged();
    }

    // ── Compression ───────────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanCompress))]
    public async Task CompressAllAsync()
    {
        // Blank OutputFolder means "next to each source file" — CompressFileAsync
        // resolves that per file. (It must not be overwritten here, or every later
        // batch would land in the first batch's folder.)

        _cts = new CancellationTokenSource();
        IsRunning = true;
        OverallProgress = 0;
        CompressAllCommand.NotifyCanExecuteChanged();

        var waiting = Files.Where(f => f.Status == FileStatus.Waiting).ToList();
        int processed = 0, succeeded = 0, failed = 0;
        VideoFile? firstDone = null;
        bool cancelled = false;

        foreach (var file in waiting)
        {
            if (_cts.Token.IsCancellationRequested) { cancelled = true; break; }
            await CompressFileAsync(file, _cts.Token);
            processed++;

            switch (file.Status)
            {
                case FileStatus.Done:
                    succeeded++;
                    firstDone ??= file;
                    break;
                case FileStatus.Error:
                    failed++;
                    break;
                case FileStatus.Cancelled:
                    cancelled = true;
                    break;
            }

            OverallProgress = (double)processed / waiting.Count * 100.0;
            if (cancelled) break;
        }

        IsRunning = false;
        _cts      = null;

        var parts = new List<string>();
        if (succeeded > 0) parts.Add($"{succeeded} file{(succeeded != 1 ? "s" : "")} compressed");
        if (failed    > 0) parts.Add($"{failed} failed");
        var summary = parts.Count > 0 ? string.Join(", ", parts) : "nothing compressed";
        OverallStatus = cancelled
            ? $"Cancelled — {summary}."
            : failed > 0 ? $"Finished with errors — {summary}. Check the log."
                         : $"Completed — {summary}.";
        OverallProgress = cancelled ? OverallProgress : 100;
        CompressAllCommand.NotifyCanExecuteChanged();

        if (AutoOpenOutput && firstDone != null && File.Exists(firstDone.OutputPath))
            System.Diagnostics.Process.Start("explorer.exe",
                $"/select,\"{firstDone.OutputPath}\"");
    }

    [RelayCommand]
    public void Cancel()
    {
        _cts?.Cancel();
        OverallStatus = "Cancelling…";
    }

    [RelayCommand]
    public void BrowseOutputFolder()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description        = "Select output folder for compressed videos",
            UseDescriptionForTitle = true,
        };
        if (!string.IsNullOrWhiteSpace(OutputFolder))
            dlg.InitialDirectory = OutputFolder;

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            OutputFolder = dlg.SelectedPath;
    }

    [RelayCommand]
    public void OpenOutput(VideoFile file)
    {
        if (File.Exists(file.OutputPath))
            System.Diagnostics.Process.Start("explorer.exe",
                $"/select,\"{file.OutputPath}\"");
    }

    [RelayCommand]
    public void ToggleLog() => ShowLog = !ShowLog;

    [RelayCommand]
    public void OpenCompare(VideoFile file) => CompareFile = file;

    [RelayCommand]
    public void CloseCompare() => CompareFile = null;

    [RelayCommand]
    public void ToggleExpand(VideoFile file) => file.IsExpanded = !file.IsExpanded;

    // ── Background metadata + thumbnail probe ─────────────────────────────────
    private async Task ProbeFileAsync(VideoFile file)
    {
        if (!FfmpegReady) return;
        try
        {
            // Probe basic metadata via ffprobe
            var meta = await _ffmpeg.ProbeAsync(file.FullPath);
            file.Duration     = meta.Duration;
            file.Resolution   = meta.Resolution;
            file.VideoCodec   = meta.VideoCodec;
            file.Fps          = meta.Fps;
            file.VideoBitrate = meta.Bitrate;

            // Extract thumbnail
            var thumbPath = await _ffmpeg.ExtractThumbnailAsync(file.FullPath);
            if (thumbPath != null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var bmp = new System.Windows.Media.Imaging.BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource        = new Uri(thumbPath);
                        bmp.CacheOption      = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 240;
                        bmp.EndInit();
                        bmp.Freeze();
                        file.Thumbnail = bmp;
                    }
                    catch { }
                    finally { try { File.Delete(thumbPath); } catch { } }
                });
            }

            // ── Rich metadata via MediaInfo (runs after thumbnail so UI feels fast) ──
            if (_mediaInfo.IsAvailable)
            {
                var info = await _mediaInfo.GetInfoAsync(file.FullPath);

                file.Container         = info.Container;
                file.VideoProfile      = info.VideoProfileStr;
                file.BitDepth          = info.BitDepthStr;
                file.ChromaSubsampling = info.ChromaStr;
                file.ColorRange        = info.ColorRange;
                file.ColorPrimaries    = info.ColorPrimaries;
                file.TransferChar      = info.TransferChar;
                file.ScanType          = info.ScanType;
                file.AspectRatio       = info.AspectRatio;
                file.FrameRateMode     = info.FrameRateMode;
                file.HdrFormat         = info.HdrBadge;
                file.IsHdr             = info.IsHdr;
                file.OverallBitrate    = info.OverallBitrateStr;
                file.WritingApp        = info.WritingApp;

                // Replace fps/resolution/bitrate with MediaInfo values if richer
                if (!string.IsNullOrEmpty(info.FrameRateStr))
                    file.Fps = info.FrameRateStr;
                if (!string.IsNullOrEmpty(info.ResolutionStr))
                    file.Resolution = info.ResolutionStr;
                if (!string.IsNullOrEmpty(info.BitrateStr))
                    file.VideoBitrate = info.BitrateStr;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    file.AudioTracks.Clear();
                    foreach (var t in info.AudioTracks)
                        file.AudioTracks.Add(t);
                    file.SubtitleTracks.Clear();
                    foreach (var t in info.SubtitleTracks)
                        file.SubtitleTracks.Add(t);
                    file.MediaInfoLoaded = true;
                });
            }
        }
        catch { /* probe failure is non-fatal */ }
    }

    // ── Core file compression ─────────────────────────────────────────────────
    private async Task CompressFileAsync(VideoFile file, CancellationToken ct)
    {
        var settings = BuildSettings();
        file.Status     = FileStatus.Running;
        file.StatusText = "Analysing…";
        file.Progress   = 0;
        OverallStatus   = $"Compressing: {file.Name}";

        var startTime = DateTime.Now;
        string? outPath = null;

        try
        {
            var duration = await _ffmpeg.GetDurationAsync(file.FullPath);

            // Broken MP4 edit list → FFmpeg would read only a few minutes of video,
            // then encode audio alone for the rest (GPU goes idle). Read all frames instead.
            var editListIssue = await _ffmpeg.DetectBrokenEditListAsync(file.FullPath);
            if (editListIssue != null)
            {
                settings.IgnoreEditList = true;
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    LogText += $"[WARN] {file.Name}: broken edit list ({editListIssue}). " +
                               "Ignoring it so the whole video is encoded.\n");
            }

            var baseName = Path.GetFileNameWithoutExtension(file.Name);
            var suffix   = settings.AddSuffix ? "_compressed" : "";
            var ext      = settings.GetContainerExt();
            var outDir   = string.IsNullOrWhiteSpace(OutputFolder)
                               ? Path.GetDirectoryName(file.FullPath)!
                               : OutputFolder;
            Directory.CreateDirectory(outDir);

            outPath = Path.Combine(outDir, $"{baseName}{suffix}{ext}");
            int n = 1;
            while (File.Exists(outPath))
                outPath = Path.Combine(outDir, $"{baseName}{suffix}_{n++}{ext}");

            file.OutputPath = outPath;

            void OnProgress(object? s, FFmpegProgressEventArgs e)
            {
                file.Progress   = e.Percent;
                file.StatusText = $"{e.Percent:F0}%";
                file.Elapsed    = DateTime.Now - startTime;

                if (e.HasStats)
                {
                    file.Speed    = e.Speed;
                    file.Bitrate  = e.Bitrate;
                    file.Eta      = e.Eta;
                    file.HasStats = true;
                }
            }
            void OnLog(object? s, string line)
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    LogText = (LogText.Length > 60_000 ? "" : LogText) + line + "\n");
            }

            // Subtitle codecs decide which subtitle streams the output can keep.
            settings.SourceSubtitleCodecs = await _ffmpeg.GetSubtitleCodecsAsync(file.FullPath);

            bool success;
            _ffmpeg.Progress += OnProgress;
            _ffmpeg.LogLine  += OnLog;
            try
            {
                if (settings.UsesTwoPass)
                {
                    // Pass stats go to a unique temp prefix, never the working folder.
                    settings.PassLogFile = Path.Combine(Path.GetTempPath(), $"vp_pass_{Guid.NewGuid():N}");

                    file.StatusText = "Pass 1 / 2…";
                    var args1 = settings.BuildFFmpegArgs(file.FullPath, outPath,
                                    secondPass: false, durationSecs: duration.TotalSeconds);
                    var ok1 = await _ffmpeg.RunAsync(args1, duration, ct);

                    if (!ok1 || ct.IsCancellationRequested)
                    {
                        Cleanup(outPath, file, ct.IsCancellationRequested);
                        if (!ok1 && !ct.IsCancellationRequested)
                            file.ErrorMessage = "FFmpeg pass 1 failed. Check the log.";
                        return;
                    }

                    file.StatusText = "Pass 2 / 2…";
                    var args2 = settings.BuildFFmpegArgs(file.FullPath, outPath,
                                    secondPass: true, durationSecs: duration.TotalSeconds);
                    success = await _ffmpeg.RunAsync(args2, duration, ct);
                }
                else
                {
                    var args = settings.BuildFFmpegArgs(file.FullPath, outPath,
                                    secondPass: true, durationSecs: duration.TotalSeconds);
                    success  = await _ffmpeg.RunAsync(args, duration, ct);
                }
            }
            finally
            {
                // Always detach, or a thrown run would keep updating this card
                // while the next file encodes.
                _ffmpeg.Progress -= OnProgress;
                _ffmpeg.LogLine  -= OnLog;
                DeletePassLogs(settings.PassLogFile);
            }

            if (ct.IsCancellationRequested) { Cleanup(outPath, file, true); return; }

            if (!success)
            {
                Cleanup(outPath, file, false);
                file.ErrorMessage = "FFmpeg returned a non-zero exit code. Check the log.";
                return;
            }

            var outInfo       = new FileInfo(outPath);
            file.OutputSize   = outInfo.Length;
            file.SavingsText  = file.SavingsPct > 0
                ? $"−{file.SavingsPct}%"
                : file.SavingsPct < -2
                    ? $"+{Math.Abs(file.SavingsPct)}% (already compressed)"
                    : "≈same";
            file.Progress     = 100;
            file.Status       = FileStatus.Done;
            file.StatusText   = "Done";
            file.Elapsed      = DateTime.Now - startTime;
            file.HasStats     = false;
            file.Speed        = "";
            file.Bitrate      = "";
            file.Eta          = "";

            // ── Read compressed file with MediaInfo ───────────────────────
            if (_mediaInfo.IsAvailable)
            {
                var capturedPath = outPath;
                var capturedFile = file;
                _ = Task.Run(async () =>
                {
                    var outMedia = await _mediaInfo.GetInfoAsync(capturedPath);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        capturedFile.OutputMediaInfo       = outMedia;
                        capturedFile.OutputMediaInfoLoaded = true;
                    });
                });
            }
        }
        catch (OperationCanceledException) { Cleanup(outPath, file, true); }
        catch (Exception ex)
        {
            Cleanup(outPath, file, false);
            file.ErrorMessage = ex.Message;
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                LogText += $"\n[ERROR] {ex.Message}\n");
        }
    }

    private static void DeletePassLogs(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return;
        try
        {
            var dir  = Path.GetDirectoryName(prefix)!;
            var name = Path.GetFileName(prefix);
            foreach (var f in Directory.GetFiles(dir, name + "*"))
                try { File.Delete(f); } catch { }
        }
        catch { }
    }

    private static void Cleanup(string? path, VideoFile file, bool cancelled)
    {
        if (path != null && File.Exists(path)) File.Delete(path);
        file.Status     = cancelled ? FileStatus.Cancelled : FileStatus.Error;
        file.StatusText = cancelled ? "Cancelled" : "Error";
    }

    private CompressionSettings BuildSettings() => new()
    {
        Codec            = SelectedCodec,
        QualityMode      = SelectedQualityMode,
        CRF              = CrfValue,
        BitrateKbps      = BitrateKbps,
        TargetSizeMB     = TargetSizeMB,
        ResMode          = ResMode,
        ResPreset        = ResPresetIndex >= 0 && ResPresetIndex < ResPresets.Length
                               ? ResPresets[ResPresetIndex] : "1920x1080",
        CustomWidth      = CustomWidth,
        CustomHeight     = CustomHeight,
        MaintainAspect   = MaintainAspect,
        AudioCodec       = AudioCodecIndex >= 0 && AudioCodecIndex < AudioCodecMap.Length
                               ? AudioCodecMap[AudioCodecIndex] : AudioCodecType.AAC,
        AudioBitrateKbps = AudioBitrateKbps,
        AddSuffix        = AddSuffix,
        OutputFolder     = OutputFolder,
        CustomFlags      = CustomFlags,
        Gpu              = Gpu.Vendor == GpuVendor.Cpu ? null : Gpu,
    };

    private static System.Windows.Media.SolidColorBrush MakeBrush(string hex)
    {
        var c = (System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return new System.Windows.Media.SolidColorBrush(c);
    }
}
