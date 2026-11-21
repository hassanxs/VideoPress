using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace VideoPress.Models;

public enum FileStatus { Waiting, Running, Done, Error, Cancelled }

public partial class VideoFile : ObservableObject
{
    public Guid   Id           { get; } = Guid.NewGuid();
    public string FullPath     { get; set; } = "";
    public string Name         { get; set; } = "";
    public long   OriginalSize { get; set; }

    public string OriginalSizeStr => FormatBytes(OriginalSize);

    [ObservableProperty] private FileStatus _status      = FileStatus.Waiting;
    [ObservableProperty] private double     _progress;
    [ObservableProperty] private string     _statusText  = "Waiting";
    [ObservableProperty] private string     _errorMessage = "";
    [ObservableProperty] private string     _outputPath  = "";
    [ObservableProperty] private TimeSpan   _elapsed;
    [ObservableProperty] private string     _savingsText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputSizeStr))]
    [NotifyPropertyChangedFor(nameof(SavingsPct))]
    private long _outputSize;

    // ── Basic metadata (populated by ffprobe after file is added) ───────────
    [ObservableProperty] private string _duration    = "";   // e.g. "0:02:34"
    [ObservableProperty] private string _resolution  = "";   // e.g. "1920×1080"
    [ObservableProperty] private string _videoCodec  = "";   // e.g. "h264"
    [ObservableProperty] private string _fps         = "";   // e.g. "30 fps"
    [ObservableProperty] private string _videoBitrate = "";  // e.g. "4.2 Mbps"

    // ── Rich metadata (populated by MediaInfo after file is added) ───────────
    [ObservableProperty] private string _container          = "";  // e.g. "MPEG-4"
    [ObservableProperty] private string _videoProfile       = "";  // e.g. "High@L4.0"
    [ObservableProperty] private string _bitDepth           = "";  // e.g. "8-bit"
    [ObservableProperty] private string _chromaSubsampling  = "";  // e.g. "4:2:0"
    [ObservableProperty] private string _colorRange         = "";  // e.g. "Limited"
    [ObservableProperty] private string _colorPrimaries     = "";  // e.g. "BT.709"
    [ObservableProperty] private string _transferChar       = "";  // e.g. "BT.709"
    [ObservableProperty] private string _scanType           = "";  // e.g. "Progressive"
    [ObservableProperty] private string _aspectRatio        = "";  // e.g. "16:9"
    [ObservableProperty] private string _frameRateMode      = "";  // e.g. "CFR"
    [ObservableProperty] private string _hdrFormat          = "";  // e.g. "HDR10" or ""
    [ObservableProperty] private string _overallBitrate     = "";  // e.g. "4.46 Mbps"
    [ObservableProperty] private string _writingApp         = "";  // e.g. "Lavf61.1.100"
    [ObservableProperty] private bool   _isHdr;

    // Audio tracks summary list (each entry = one track)
    [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<VideoPress.Services.AudioTrackInfo>
        _audioTracks = new();

    // Subtitle tracks
    [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<VideoPress.Services.SubtitleTrackInfo>
        _subtitleTracks = new();

    // True once MediaInfo has finished populating rich metadata
    [ObservableProperty] private bool _mediaInfoLoaded;

    // ── Output file MediaInfo (populated after compression completes) ────────
    [ObservableProperty] private VideoPress.Services.MediaInfoResult? _outputMediaInfo;
    [ObservableProperty] private bool _outputMediaInfoLoaded;

    // Thumbnail as WPF BitmapSource (extracted via ffmpeg)
    [ObservableProperty] private System.Windows.Media.Imaging.BitmapSource? _thumbnail;

    // Whether the card is expanded to show metadata
    [ObservableProperty] private bool _isExpanded;

    // ── Live compression stats ────────────────────────────────────────────────
    /// e.g. "2.8×"
    [ObservableProperty] private string _speed   = "";
    /// e.g. "1.4 Mbps"
    [ObservableProperty] private string _bitrate = "";
    /// e.g. "0:42"
    [ObservableProperty] private string _eta     = "";

    /// True while Running AND we have at least one parsed stats line
    [ObservableProperty] private bool _hasStats;

    // ── Computed ─────────────────────────────────────────────────────────────
    public string Ext           => Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();
    public string OutputSizeStr => FormatBytes(OutputSize);
    public int    SavingsPct    => OriginalSize > 0 && OutputSize > 0
                                       ? (int)((1.0 - (double)OutputSize / OriginalSize) * 100)
                                       : 0;

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)            return "0 B";
        if (bytes < 1_024)         return $"{bytes} B";
        if (bytes < 1_048_576)     return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1_073_741_824) return $"{bytes / 1_048_576.0:F1} MB";
        return $"{bytes / 1_073_741_824.0:F2} GB";
    }
}
