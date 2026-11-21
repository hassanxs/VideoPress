using System.Text;

namespace VideoPress.Models;

public enum VideoCodec     { H264, H265, VP9, AV1 }
public enum QualityMode    { CRF, Bitrate, TargetSize }
public enum ResolutionMode { Original, Preset, Custom }
public enum AudioCodecType { AAC, MP3, Opus, Strip }

public class CompressionSettings
{
    // Codec
    public VideoCodec Codec { get; set; } = VideoCodec.H264;

    /// <summary>Optional GPU info — when set, hardware encoders are used for H.264/H.265.</summary>
    public VideoPress.Services.GpuInfo? Gpu { get; set; }

    // Quality
    public QualityMode QualityMode    { get; set; } = QualityMode.CRF;
    public int         CRF            { get; set; } = 23;
    public int         BitrateKbps    { get; set; } = 2000;
    public int         TargetSizeMB   { get; set; } = 50;

    // Resolution
    public ResolutionMode ResMode       { get; set; } = ResolutionMode.Original;
    public string         ResPreset     { get; set; } = "1920x1080";
    public int            CustomWidth   { get; set; } = 1920;
    public int            CustomHeight  { get; set; } = 1080;
    public bool           MaintainAspect { get; set; } = true;

    // Audio
    public AudioCodecType AudioCodec       { get; set; } = AudioCodecType.AAC;
    public int            AudioBitrateKbps { get; set; } = 128;

    // Batch
    public bool   AddSuffix    { get; set; } = true;
    public string OutputFolder { get; set; } = "";

    // Custom
    public string CustomFlags { get; set; } = "";

    /// <summary>
    /// Per-file: set when the source's MP4 edit list is broken (see
    /// FFmpegService.DetectBrokenEditListAsync). Adds -ignore_editlist 1 to the input.
    /// </summary>
    public bool IgnoreEditList { get; set; }

    // ── Codec helpers ────────────────────────────────────────────────────────
    public string GetCodecLib()
    {
        // Use hardware encoder when GPU is detected and codec supports it
        if (Gpu != null)
        {
            if (Codec == VideoCodec.H264 && Gpu.H264Encoder != null)
                return Gpu.H264Encoder;
            if (Codec == VideoCodec.H265 && Gpu.H265Encoder != null)
                return Gpu.H265Encoder;
        }

        return Codec switch
        {
            VideoCodec.H264 => "libx264",
            VideoCodec.H265 => "libx265",
            VideoCodec.VP9  => "libvpx-vp9",
            VideoCodec.AV1  => "libaom-av1",
            _               => "libx264",
        };
    }

    /// <summary>Input-side -hwaccel args matching the GPU encoder in use ("" for CPU).</summary>
    public string GetHwDecodeArgs()
    {
        if (!IsUsingGpu) return "";
        var enc = GetCodecLib();
        if (enc.Contains("nvenc")) return "-hwaccel cuda";
        if (enc.Contains("qsv"))   return "-hwaccel qsv";
        if (enc.Contains("amf"))   return "-hwaccel d3d11va";
        return "";
    }

    /// <summary>True when a hardware encoder will be used for this codec.</summary>
    public bool IsUsingGpu =>
        Gpu != null && (
            (Codec == VideoCodec.H264 && Gpu.H264Encoder != null) ||
            (Codec == VideoCodec.H265 && Gpu.H265Encoder != null));

    public string GetAudioCodecLib()
    {
        // WebM container (VP9/AV1) only supports Opus or Vorbis — never AAC/MP3
        bool isWebM = Codec is VideoCodec.VP9 or VideoCodec.AV1;

        if (isWebM)
        {
            // Force Opus for WebM regardless of user selection
            // (except Strip which is handled separately)
            return AudioCodec == AudioCodecType.Strip ? "" : "libopus";
        }

        return AudioCodec switch
        {
            AudioCodecType.AAC  => "aac",
            AudioCodecType.MP3  => "libmp3lame",
            AudioCodecType.Opus => "libopus",
            _                   => "",
        };
    }

    /// <summary>Display name for the audio codec actually being used (may differ from user selection for WebM).</summary>
    public string GetEffectiveAudioDescription()
    {
        bool isWebM = Codec is VideoCodec.VP9 or VideoCodec.AV1;
        if (isWebM && AudioCodec != AudioCodecType.Strip)
            return "Opus (forced for WebM)";
        return AudioCodec switch
        {
            AudioCodecType.AAC  => "AAC",
            AudioCodecType.MP3  => "MP3",
            AudioCodecType.Opus => "Opus",
            AudioCodecType.Strip => "none",
            _                   => "AAC",
        };
    }

    public string GetContainerExt() => Codec switch
    {
        VideoCodec.VP9 => ".webm",
        VideoCodec.AV1 => ".mkv",
        _              => ".mp4",
    };

    // ── FFmpeg arg builder ───────────────────────────────────────────────────
    /// <param name="durationSecs">
    ///   Actual video duration in seconds (used for 2-pass bitrate target).
    ///   Pass 0 to fall back to a 60-second estimate.
    /// </param>
    public string BuildFFmpegArgs(string input, string output,
                                  bool secondPass = false,
                                  double durationSecs = 0)
    {
        var a = new StringBuilder();

        // Hardware DECODE on the same GPU that encodes. Without this the CPU decodes
        // every frame and, on large/high-bitrate/4K sources, can't feed the GPU encoder
        // fast enough — GPU usage collapses after the first few seconds.
        // No -hwaccel_output_format: frames come back to system memory, so CPU filters
        // (scale, custom -vf) keep working, and FFmpeg silently falls back to software
        // decode for codecs the GPU can't decode.
        var hwDecode = GetHwDecodeArgs();
        if (hwDecode.Length > 0)
            a.Append(hwDecode + " ");

        if (IgnoreEditList)
            a.Append("-ignore_editlist 1 ");

        a.Append($"-i \"{input}\" ");

        if (QualityMode == QualityMode.TargetSize && !secondPass)
        {
            // First pass — encode to null output; no audio needed
            a.Append($"-c:v {GetCodecLib()} -b:v 0 -pass 1 -an -f null NUL");
            return a.ToString().Trim();
        }

        // ── Video ────────────────────────────────────────────────────────────
        a.Append($"-c:v {GetCodecLib()} ");

        switch (QualityMode)
        {
            case QualityMode.CRF:
                a.Append($"-crf {CRF} ");
                break;
            case QualityMode.Bitrate:
                a.Append($"-b:v {BitrateKbps}k ");
                break;
            case QualityMode.TargetSize:
                // second pass — calculate bitrate from target size and actual duration
                var secs     = durationSecs > 0 ? durationSecs : 60.0;
                var totalKb  = TargetSizeMB * 8_192.0;          // MB → kbits
                var audioBit = AudioCodec != AudioCodecType.Strip ? AudioBitrateKbps : 0;
                var videoBit = Math.Max(100, (int)((totalKb / secs) - audioBit));
                a.Append($"-b:v {videoBit}k -pass 2 ");
                break;
        }

        // Encoding preset (speed/quality trade-off)
        // Hardware encoders use a different preset scale — skip for GPU paths
        if (!IsUsingGpu && Codec is VideoCodec.H264 or VideoCodec.H265)
            a.Append("-preset medium ");

        // ── Scale filter ─────────────────────────────────────────────────────
        if (ResMode == ResolutionMode.Preset)
        {
            var parts = ResPreset.Split('x');
            if (parts.Length == 2)
                a.Append($"-vf scale={parts[0]}:{parts[1]} ");
        }
        else if (ResMode == ResolutionMode.Custom)
        {
            // Use -2 for the constrained dimension to keep aspect ratio
            if (MaintainAspect)
                a.Append($"-vf scale={CustomWidth}:-2 ");
            else
                a.Append($"-vf scale={CustomWidth}:{CustomHeight} ");
        }

        // ── Audio ─────────────────────────────────────────────────────────────
        if (AudioCodec == AudioCodecType.Strip)
            a.Append("-an ");
        else
            a.Append($"-c:a {GetAudioCodecLib()} -b:a {AudioBitrateKbps}k ");

        // ── Custom flags ──────────────────────────────────────────────────────
        var custom = CustomFlags?.Trim();
        if (!string.IsNullOrEmpty(custom))
            a.Append(custom + " ");

        // ── Output ────────────────────────────────────────────────────────────
        a.Append($"-y \"{output}\"");

        return a.ToString().Trim();
    }

    // Preview shown in the UI (no real paths)
    public string GetPreviewCommand()
    {
        var ext     = GetContainerExt();
        var outName = $"output{ext}";
        return "ffmpeg " + BuildFFmpegArgs("input.mp4", outName);
    }
}
