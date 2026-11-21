using System.Linq;

namespace VideoPress.Services;

public enum GpuVendor { Detecting, Nvidia, Amd, Intel, Cpu }

public class GpuInfo
{
    public GpuVendor Vendor        { get; init; } = GpuVendor.Cpu;
    public string    DisplayName   { get; init; } = "CPU";
    public string    BadgeColor    { get; init; } = "#555560";
    public bool      TestPassed    { get; init; } = true;
    public string?   H264Encoder   { get; init; }
    public string?   H265Encoder   { get; init; }

    public static readonly GpuInfo Detecting = new()
    {
        Vendor = GpuVendor.Detecting, DisplayName = "Detecting\u2026", BadgeColor = "#F39C12"
    };
    public static readonly GpuInfo CpuFallback = new()
    {
        Vendor = GpuVendor.Cpu, DisplayName = "CPU", BadgeColor = "#555560"
    };
}

public class GpuDetectionService
{
    private readonly FFmpegService _ffmpeg;
    public string DetectionLog { get; private set; } = "";

    public GpuDetectionService(FFmpegService ffmpeg) => _ffmpeg = ffmpeg;

    /// <summary>
    /// Probes hardware encoders in priority order: NVIDIA -> AMD -> Intel QSV -> CPU.
    /// Each candidate runs a real 1-frame test-encode before being selected.
    /// Intel QSV (iGPU) is last because it runs on CPU integrated graphics.
    /// </summary>
    public async Task<GpuInfo> DetectAsync(CancellationToken ct = default)
    {
        var encoders = await _ffmpeg.GetEncodersAsync();
        if (string.IsNullOrEmpty(encoders))
        {
            DetectionLog = "Could not read encoder list from FFmpeg.";
            return GpuInfo.CpuFallback;
        }

        var sb = new System.Text.StringBuilder();

        var hwList = new[] { "h264_nvenc","hevc_nvenc","h264_amf","hevc_amf","h264_qsv","hevc_qsv" };
        var found  = hwList.Where(e => encoders.Contains(e)).ToList();
        sb.AppendLine(found.Count > 0
            ? $"Hardware encoders listed: {string.Join(", ", found)}"
            : "No hardware encoders listed.");

        var candidates = new[]
        {
            (enc: "h264_nvenc", vendor: GpuVendor.Nvidia, name: "NVIDIA GPU", color: "#76b900", h265: "hevc_nvenc"),
            (enc: "h264_amf",   vendor: GpuVendor.Amd,    name: "AMD GPU",    color: "#ED1C24", h265: "hevc_amf"),
            (enc: "h264_qsv",   vendor: GpuVendor.Intel,  name: "iGPU (QSV)", color: "#0071C5", h265: "hevc_qsv"),
        };

        foreach (var (enc, vendor, name, color, h265Key) in candidates)
        {
            if (!encoders.Contains(enc)) continue;

            var ok = await _ffmpeg.TestEncoderAsync(enc);

            if (ok)
            {
                sb.AppendLine($"  [{enc}] OK");
                DetectionLog = sb.ToString();
                return new GpuInfo
                {
                    Vendor      = vendor,
                    DisplayName = name,
                    BadgeColor  = color,
                    TestPassed  = true,
                    H264Encoder = enc,
                    H265Encoder = encoders.Contains(h265Key) ? h265Key : null,
                };
            }
            else
            {
                var raw   = _ffmpeg.LastTestOutput;
                var parts = raw.Split('|');
                var brief = (parts.Length > 1 ? parts[1] : raw).Trim();
                var nl    = brief.IndexOf('\n'); if (nl > 0) brief = brief[..nl];
                brief = brief.Trim();

                // AMF error 10 = hardware too old for AMF (pre-GCN2). Not fixable by driver install.
                if (enc.Contains("amf") && brief.Contains("error 10"))
                    brief = "GPU too old for AMF hardware encoding (pre-GCN 2nd gen)";

                // nvcuda missing = NVIDIA driver not fully installed
                if (enc.Contains("nvenc") && brief.Contains("nvcuda"))
                    brief = "NVIDIA CUDA runtime not found — install full Game Ready Driver";

                sb.AppendLine($"  [{enc}] FAILED: {brief}");
            }
        }

        DetectionLog = sb.ToString();
        return GpuInfo.CpuFallback;
    }
}
