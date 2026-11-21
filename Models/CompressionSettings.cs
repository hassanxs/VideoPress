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

    // ── Per-file inputs (set by the view model before building args) ─────────

    /// <summary>
    /// Codec names of the source's subtitle streams, in order (from ffprobe).
    /// Used to keep subtitles the output container can hold.
    /// </summary>
    public IReadOnlyList<string> SourceSubtitleCodecs { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Two-pass stats file prefix (CPU encoders only). Set to a temp path so the
    /// log is never written into the app's working folder (read-only under
    /// Program Files) and two runs never share one.
    /// </summary>
    public string? PassLogFile { get; set; }

    /// <summary>
    /// Target Size uses two passes only on CPU encoders. NVENC/AMF/QSV ignore
    /// FFmpeg's -pass 1/2, so pass 1 would be a full wasted encode; they get a
    /// single capped-bitrate pass instead.
    /// </summary>
    public bool UsesTwoPass => QualityMode == QualityMode.TargetSize && !IsUsingGpu;

    private static readonly HashSet<string> TextSubtitleCodecs =
        new(StringComparer.OrdinalIgnoreCase)
        { "subrip", "srt", "ass", "ssa", "mov_text", "webvtt", "text" };

    // ── FFmpeg arg builder ───────────────────────────────────────────────────
    /// <param name="durationSecs">
    ///   Actual video duration in seconds (used for the Target Size bitrate).
    ///   Pass 0 to fall back to a 60-second estimate.
    /// </param>
    /// <param name="secondPass">
    ///   Only meaningful when <see cref="UsesTwoPass"/>: false builds pass 1.
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

        var isPass1 = UsesTwoPass && !secondPass;
        var enc     = GetCodecLib();

        // ── Stream selection ─────────────────────────────────────────────────
        // FFmpeg's default picks ONE video + ONE audio and drops the rest.
        // Keep the main video (V = not cover art), every audio track and the
        // subtitles the output container can carry.
        a.Append("-map 0:V:0? ");
        if (!isPass1 && AudioCodec != AudioCodecType.Strip)
            a.Append("-map 0:a? ");
        if (!isPass1)
            a.Append(BuildSubtitleArgs());

        // ── Video ────────────────────────────────────────────────────────────
        a.Append($"-c:v {enc} ");

        switch (QualityMode)
        {
            case QualityMode.CRF:
                a.Append(GetConstantQualityArgs(enc) + " ");
                break;
            case QualityMode.Bitrate:
                a.Append($"-b:v {BitrateKbps}k ");
                break;
            case QualityMode.TargetSize:
                var videoBit = GetTargetVideoKbps(durationSecs);
                if (UsesTwoPass)
                {
                    // Both passes use the same bitrate and filters, as x264/libvpx expect.
                    a.Append($"-b:v {videoBit}k -pass {(isPass1 ? 1 : 2)} ");
                    if (!string.IsNullOrEmpty(PassLogFile))
                        a.Append($"-passlogfile \"{PassLogFile}\" ");
                }
                else
                {
                    // GPU: one pass, capped so the file lands near the target.
                    a.Append($"-b:v {videoBit}k -maxrate {videoBit}k -bufsize {videoBit * 2}k ");
                }
                break;
        }

        // Encoding preset (speed/quality trade-off)
        // Hardware encoders use a different preset scale — skip for GPU paths
        if (!IsUsingGpu && Codec is VideoCodec.H264 or VideoCodec.H265)
            a.Append("-preset medium ");

        // ── Scale filter ─────────────────────────────────────────────────────
        var vf = BuildScaleFilter();
        if (vf.Length > 0)
            a.Append($"-vf {vf} ");

        if (isPass1)
        {
            // First pass — stats only, no audio, discard output
            a.Append("-an -sn -f null NUL");
            return a.ToString().Trim();
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

    /// <summary>
    /// Constant-quality args for the encoder in use. Only the CPU encoders
    /// understand -crf; hardware encoders silently ignore it and fall back to
    /// their ~2 Mbps default, so map the same 0–51 value to their own knob.
    /// </summary>
    private string GetConstantQualityArgs(string enc)
    {
        if (enc.Contains("nvenc"))
            return $"-rc vbr -cq {CRF} -b:v 0 -preset p5";
        if (enc.Contains("amf"))
            return $"-rc cqp -qp_i {CRF} -qp_p {CRF}";
        if (enc.Contains("qsv"))
            return $"-global_quality {CRF}";
        return $"-crf {CRF}";
    }

    private int GetTargetVideoKbps(double durationSecs)
    {
        var secs     = durationSecs > 0 ? durationSecs : 60.0;
        var totalKb  = TargetSizeMB * 8_192.0;          // MB → kbits
        var audioBit = AudioCodec != AudioCodecType.Strip ? AudioBitrateKbps : 0;
        return Math.Max(100, (int)((totalKb / secs) - audioBit));
    }

    /// <summary>
    /// Preset resolutions are a size class ("1080p"), applied to the SHORT side so
    /// portrait and non-16:9 videos keep their shape, and never upscale.
    /// </summary>
    private string BuildScaleFilter()
    {
        if (ResMode == ResolutionMode.Preset)
        {
            var parts = ResPreset.Split('x');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out var w) ||
                !int.TryParse(parts[1], out var h)) return "";

            var shortSide = Math.Min(w, h);
            // Landscape: limit height, width follows. Portrait: limit width, height follows.
            // trunc(...)*2 keeps the limited side even (yuv420 needs even dimensions).
            return $"scale='if(gte(iw,ih),-2,trunc(min({shortSide},iw)/2)*2)'" +
                   $":'if(gte(iw,ih),trunc(min({shortSide},ih)/2)*2,-2)'";
        }

        if (ResMode == ResolutionMode.Custom)
        {
            // Use -2 for the constrained dimension to keep aspect ratio
            return MaintainAspect
                ? $"scale={CustomWidth}:-2"
                : $"scale={CustomWidth}:{CustomHeight}";
        }

        return "";
    }

    /// <summary>Maps and codec for subtitle streams the output container can hold.</summary>
    private string BuildSubtitleArgs()
    {
        if (SourceSubtitleCodecs.Count == 0) return "";

        var ext = GetContainerExt();
        var sb  = new StringBuilder();

        if (ext == ".mkv")
        {
            // Matroska holds nearly every subtitle type as-is; the exception is
            // MP4's mov_text, which has to be converted to SRT.
            for (var i = 0; i < SourceSubtitleCodecs.Count; i++)
            {
                var codec = SourceSubtitleCodecs[i].Equals("mov_text", StringComparison.OrdinalIgnoreCase)
                    ? "srt" : "copy";
                sb.Append($"-map 0:s:{i} -c:s:{i} {codec} ");
            }
            return sb.ToString();
        }

        // MP4 / WebM only hold text subtitles (image subs like PGS/VobSub can't be converted).
        var kept = 0;
        for (var i = 0; i < SourceSubtitleCodecs.Count; i++)
        {
            if (!TextSubtitleCodecs.Contains(SourceSubtitleCodecs[i])) continue;
            sb.Append($"-map 0:s:{i} ");
            kept++;
        }
        if (kept > 0)
            sb.Append(ext == ".webm" ? "-c:s webvtt " : "-c:s mov_text ");

        return sb.ToString();
    }

    // Preview shown in the UI (no real paths)
    public string GetPreviewCommand()
    {
        var ext     = GetContainerExt();
        var outName = $"output{ext}";
        return "ffmpeg " + BuildFFmpegArgs("input.mp4", outName, secondPass: true);
    }
}
