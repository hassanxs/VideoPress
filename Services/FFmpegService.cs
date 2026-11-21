using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VideoPress.Services;

public class FFmpegProgressEventArgs : EventArgs
{
    public double Percent  { get; init; }   // 0–99
    public string RawLine  { get; init; } = "";
    public string Speed    { get; init; } = "";   // e.g. "2.8×"
    public string Bitrate  { get; init; } = "";   // e.g. "1.4 Mbps"
    public string Eta      { get; init; } = "";   // e.g. "0:42"
    public bool   HasStats { get; init; }
}

public class FFmpegService
{
    // ── Paths ────────────────────────────────────────────────────────────────
    private static readonly string AppDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "VideoPress");

    public static string FFmpegPath  => Path.Combine(AppDir, "ffmpeg.exe");
    public static string FFprobePath => Path.Combine(AppDir, "ffprobe.exe");

    // Embedded resource logical names (set in csproj LogicalName)
    private const string ResFFmpeg  = "VideoPress.Assets.ffmpeg.exe";
    private const string ResFFprobe = "VideoPress.Assets.ffprobe.exe";

    // Stamp file — stores the hash of the embedded binary.
    // If it matches what's on disk, we skip extraction (fast path).
    private static string StampPath => Path.Combine(AppDir, "ffmpeg.stamp");

    public bool IsAvailable      => File.Exists(FFmpegPath);
    public bool IsBundled        => HasEmbeddedResource(ResFFmpeg);

    public event EventHandler<FFmpegProgressEventArgs>? Progress;
    public event EventHandler<string>?                  LogLine;

    // ── Extraction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the embedded ffmpeg.exe/ffprobe.exe to %LOCALAPPDATA%\VideoPress\.
    /// Safe to call on every startup — skips extraction if the binary is already
    /// up-to-date (compared by embedded resource hash).
    /// </summary>
    public async Task EnsureExtractedAsync(IProgress<(double pct, string msg)>? progress = null,
                                           CancellationToken ct = default)
    {
        Directory.CreateDirectory(AppDir);

        // Compute hash of the embedded resource to detect version changes
        var embeddedHash = GetEmbeddedHash(ResFFmpeg);

        // Fast path: already extracted and up-to-date
        if (File.Exists(FFmpegPath) && File.Exists(FFprobePath) &&
            File.Exists(StampPath)  && File.ReadAllText(StampPath).Trim() == embeddedHash)
        {
            progress?.Report((100, "FFmpeg ready"));
            return;
        }

        // Extract ffmpeg.exe
        progress?.Report((5, "Extracting ffmpeg.exe…"));
        await ExtractResourceAsync(ResFFmpeg, FFmpegPath, ct);

        // Extract ffprobe.exe
        progress?.Report((60, "Extracting ffprobe.exe…"));
        await ExtractResourceAsync(ResFFprobe, FFprobePath, ct);

        // Write stamp so next launch skips extraction
        await File.WriteAllTextAsync(StampPath, embeddedHash, ct);

        progress?.Report((100, "FFmpeg ready"));
    }

    private static async Task ExtractResourceAsync(string resourceName, string destPath,
                                                    CancellationToken ct)
    {
        var asm = Assembly.GetExecutingAssembly();
        await using var src  = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. " +
                $"Run fetch-ffmpeg.bat and rebuild.");
        await using var dest = new FileStream(destPath, FileMode.Create,
                                              FileAccess.Write, FileShare.None,
                                              bufferSize: 81_920, useAsync: true);
        await src.CopyToAsync(dest, ct);
    }

    private static string GetEmbeddedHash(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return "not-embedded";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash)[..16]; // first 16 chars is enough
    }

    private static bool HasEmbeddedResource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetManifestResourceNames().Contains(name);
    }

    // ── Duration probe ───────────────────────────────────────────────────────

    public async Task<TimeSpan> GetDurationAsync(string filePath)
    {
        if (!File.Exists(FFprobePath)) return TimeSpan.Zero;

        var psi = new ProcessStartInfo(FFprobePath,
            $"-v error -show_entries format=duration " +
            $"-of default=noprint_wrappers=1:nokey=1 \"{filePath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute  = false,
            CreateNoWindow   = true,
        };

        using var proc = Process.Start(psi)!;
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        return double.TryParse(output.Trim(),
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var secs)
            ? TimeSpan.FromSeconds(secs)
            : TimeSpan.Zero;
    }

    // ── Subtitle probe ───────────────────────────────────────────────────────

    /// <summary>Codec names of the file's subtitle streams, in stream order.</summary>
    public async Task<IReadOnlyList<string>> GetSubtitleCodecsAsync(string filePath)
    {
        if (!File.Exists(FFprobePath)) return Array.Empty<string>();

        var psi = new ProcessStartInfo(FFprobePath,
            "-v error -select_streams s -show_entries stream=codec_name " +
            "-of csv=p=0 \"" + filePath + "\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow  = true,
        };

        try
        {
            using var proc = Process.Start(psi)!;
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var output     = await proc.StandardOutput.ReadToEndAsync();
            await stderrTask;
            await proc.WaitForExitAsync();

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                         .Select(l => l.Trim().TrimEnd(','))
                         .Where(l => l.Length > 0)
                         .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    // ── Broken edit-list detection ───────────────────────────────────────────

    /// <summary>
    /// Detects MP4/MOV files whose edit list hides most of the video track.
    ///
    /// Some writers store the edit-list duration in a 32-bit field; long recordings
    /// overflow it (e.g. 12 h at a 1 MHz timescale wraps to ~850 s). FFmpeg honours
    /// the edit list, so it reads only the first few minutes of video, then encodes
    /// audio alone for the rest of the file — the GPU goes idle almost immediately.
    ///
    /// Signature (deliberately strict so legitimate trim edit lists are left alone):
    ///   • the sample table holds far more frames than the edited video duration
    ///     (frames / fps &gt; 2× the video stream duration), AND
    ///   • the file/audio duration matches the full frame count (within 2 %),
    ///     i.e. the audio runs the whole length the frames cover.
    /// A real trim cuts audio as well, so it fails the second test.
    /// </summary>
    /// <returns>Null when the file looks fine, otherwise a human-readable reason.</returns>
    public async Task<string?> DetectBrokenEditListAsync(string filePath)
    {
        if (!File.Exists(FFprobePath)) return null;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is not (".mp4" or ".m4v" or ".mov")) return null;

        var psi = new ProcessStartInfo(FFprobePath,
            "-v quiet -print_format json -show_format " +
            "-show_entries stream=codec_type,duration,nb_frames,avg_frame_rate,r_frame_rate " +
            "\"" + filePath + "\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow  = true,
        };

        try
        {
            using var proc = Process.Start(psi)!;
            var json = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var inv  = System.Globalization.CultureInfo.InvariantCulture;

            static double ParseNum(System.Text.Json.JsonElement el, string name)
            {
                if (!el.TryGetProperty(name, out var p)) return 0;
                var s = p.ValueKind == System.Text.Json.JsonValueKind.String ? p.GetString() : p.GetRawText();
                return double.TryParse(s, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            }

            static double ParseRate(System.Text.Json.JsonElement el, string name)
            {
                if (!el.TryGetProperty(name, out var p)) return 0;
                var parts = (p.GetString() ?? "").Split('/');
                if (parts.Length != 2) return 0;
                return double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var n) &&
                       double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var d) && d > 0
                    ? n / d : 0;
            }

            double videoDur = 0, frames = 0, fps = 0, audioDur = 0;
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var s in streams.EnumerateArray())
                {
                    var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : "";
                    if (type == "video" && frames == 0)
                    {
                        videoDur = ParseNum(s, "duration");
                        frames   = ParseNum(s, "nb_frames");
                        fps      = ParseRate(s, "avg_frame_rate");
                        if (fps <= 0) fps = ParseRate(s, "r_frame_rate");
                    }
                    else if (type == "audio" && audioDur == 0)
                    {
                        audioDur = ParseNum(s, "duration");
                    }
                }
            }

            var formatDur = root.TryGetProperty("format", out var fmt) ? ParseNum(fmt, "duration") : 0;

            if (videoDur <= 0 || frames <= 0 || fps <= 0) return null;

            var frameSpan = frames / fps;                       // what the frames really cover
            if (frameSpan <= videoDur * 2) return null;         // edit list not hiding much

            var fullDur = audioDur > 0 ? audioDur : formatDur;
            if (fullDur <= 0 || Math.Abs(fullDur - frameSpan) / frameSpan > 0.02) return null;

            return string.Format(inv,
                "video edit list says {0:F0}s but the track holds {1:F0} frames ({2:F0}s at {3:F2} fps), " +
                "matching the {4:F0}s file length",
                videoDur, frames, frameSpan, fps, fullDur);
        }
        catch { return null; }
    }

    // ── Run ──────────────────────────────────────────────────────────────────

    public async Task<bool> RunAsync(string args, TimeSpan totalDuration,
                                     CancellationToken ct)
    {
        if (!File.Exists(FFmpegPath))
            throw new FileNotFoundException("FFmpeg not found.", FFmpegPath);

        var psi = new ProcessStartInfo(FFmpegPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute  = false,
            CreateNoWindow   = true,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        // Regexes compiled once per RunAsync call
        var rxTime    = new Regex(@"time=(\d+):(\d+):(\d+\.?\d*)",          RegexOptions.Compiled);
        var rxSpeed   = new Regex(@"speed=\s*([\d.]+)x",                    RegexOptions.Compiled);
        var rxBitrate = new Regex(@"bitrate=\s*([\d.]+)(\w+bits/s)",        RegexOptions.Compiled);

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            LogLine?.Invoke(this, e.Data);

            var mTime = rxTime.Match(e.Data);
            if (!mTime.Success) return;

            // ── Elapsed / progress ──────────────────────────────────────────
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var elapsedSecs =
                int.Parse(mTime.Groups[1].Value) * 3600 +
                int.Parse(mTime.Groups[2].Value) * 60  +
                double.Parse(mTime.Groups[3].Value, inv);

            var pct = totalDuration == TimeSpan.Zero
                ? 0.0
                : Math.Min(99.0, elapsedSecs / totalDuration.TotalSeconds * 100.0);

            // ── Speed ───────────────────────────────────────────────────────
            var mSpeed    = rxSpeed.Match(e.Data);
            var speedVal  = mSpeed.Success
                ? double.Parse(mSpeed.Groups[1].Value, inv) : 0;
            var speedStr  = speedVal > 0 ? $"{speedVal:F1}×" : "";

            // ── Bitrate ─────────────────────────────────────────────────────
            var mBit      = rxBitrate.Match(e.Data);
            var bitrateStr = "";
            if (mBit.Success)
            {
                var val  = double.Parse(mBit.Groups[1].Value, inv);
                var unit = mBit.Groups[2].Value; // e.g. "kbits/s"
                bitrateStr = unit.StartsWith("k")
                    ? $"{val / 1000.0:F1} Mbps"
                    : $"{val:F0} kbps";
            }

            // ── ETA ─────────────────────────────────────────────────────────
            var etaStr = "";
            if (speedVal > 0 && totalDuration != TimeSpan.Zero)
            {
                var remainingSecs = (totalDuration.TotalSeconds - elapsedSecs) / speedVal;
                if (remainingSecs > 0)
                {
                    var ts = TimeSpan.FromSeconds(remainingSecs);
                    etaStr = ts.TotalHours >= 1
                        ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                        : $"{ts.Minutes}:{ts.Seconds:D2}";
                }
            }

            Progress?.Invoke(this, new FFmpegProgressEventArgs
            {
                Percent  = pct,
                RawLine  = e.Data,
                Speed    = speedStr,
                Bitrate  = bitrateStr,
                Eta      = etaStr,
                HasStats = true,
            });
        };

        proc.Start();
        proc.BeginErrorReadLine();

        try   { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            return false;
        }

        return proc.ExitCode == 0;
    }


    // ── Metadata probe ───────────────────────────────────────────────────────

    public record VideoMetadata(
        string Duration,
        string Resolution,
        string VideoCodec,
        string Fps,
        string Bitrate);

    public async Task<VideoMetadata> ProbeAsync(string filePath)
    {
        if (!File.Exists(FFprobePath)) return new VideoMetadata("","","","","");

        var probeArgs = "-v quiet -print_format json -show_streams -show_format " +
                       "\"" + filePath + "\"";
        var psi = new ProcessStartInfo(FFprobePath, probeArgs)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow  = true,
        };

        using var proc = Process.Start(psi)!;
        var json = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Find video stream
            string duration = "", resolution = "", codec = "", fps = "", bitrate = "";

            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var s in streams.EnumerateArray())
                {
                    if (!s.TryGetProperty("codec_type", out var ct) ||
                        ct.GetString() != "video") continue;

                    if (s.TryGetProperty("codec_name", out var cn))
                        codec = cn.GetString() ?? "";

                    int w = 0, h = 0;
                    if (s.TryGetProperty("width",  out var wv)) w = wv.GetInt32();
                    if (s.TryGetProperty("height", out var hv)) h = hv.GetInt32();
                    if (w > 0 && h > 0) resolution = $"{w}×{h}";

                    if (s.TryGetProperty("r_frame_rate", out var fr))
                    {
                        var parts = fr.GetString()?.Split('/');
                        if (parts?.Length == 2 &&
                            double.TryParse(parts[0], out var num) &&
                            double.TryParse(parts[1], out var den) && den > 0)
                            fps = $"{num / den:F0} fps";
                    }
                    break;
                }
            }

            if (root.TryGetProperty("format", out var fmt))
            {
                if (fmt.TryGetProperty("duration", out var dur) &&
                    double.TryParse(dur.GetString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var secs))
                {
                    var ts = TimeSpan.FromSeconds(secs);
                    duration = ts.TotalHours >= 1
                        ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                        : $"{ts.Minutes}:{ts.Seconds:D2}";
                }

                if (fmt.TryGetProperty("bit_rate", out var br) &&
                    long.TryParse(br.GetString(), out var bps))
                    bitrate = bps >= 1_000_000
                        ? $"{bps / 1_000_000.0:F1} Mbps"
                        : $"{bps / 1_000:F0} kbps";
            }

            return new VideoMetadata(duration, resolution, codec.ToUpperInvariant(), fps, bitrate);
        }
        catch { return new VideoMetadata("","","","",""); }
    }

    // ── Thumbnail extraction ──────────────────────────────────────────────────

    public async Task<string?> ExtractThumbnailAsync(string filePath)
    {
        if (!File.Exists(FFmpegPath)) return null;

        var tmp = Path.Combine(Path.GetTempPath(),
            $"vp_thumb_{Path.GetFileNameWithoutExtension(filePath)}_{Guid.NewGuid():N}.jpg");

        // Seek to 5% of duration for a representative frame (skip black intros)
        var args = "-ss 00:00:03 -i " + "\"" + filePath + "\" " +
                   "-vframes 1 -vf scale=240:-1 -q:v 4 " + "\"" + tmp + "\" -y -v error";

        var psi = new ProcessStartInfo(FFmpegPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow  = true,
        };

        using var proc = Process.Start(psi)!;
        await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        return File.Exists(tmp) ? tmp : null;
    }

    // ── Encoder probe (used by GpuDetectionService) ──────────────────────────

    /// <summary>Returns the raw output of `ffmpeg -encoders`.</summary>
    public async Task<string> GetEncodersAsync()
    {
        if (!File.Exists(FFmpegPath)) return "";

        var psi = new ProcessStartInfo(FFmpegPath, "-encoders -v quiet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow  = true,
        };

        using var proc = Process.Start(psi)!;
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return output;
    }

    /// <summary>
    /// Tries to encode a single black frame with the given encoder.
    /// Returns true only if FFmpeg exits 0 — confirms the encoder is functional.
    ///
    /// Hardware encoders (nvenc, amf, qsv) have strict requirements:
    ///   - Minimum resolution (320x240 is safe)
    ///   - Must receive yuv420p pixel format explicitly
    ///   - AMF additionally needs -usage transcoding hint
    /// </summary>
    public async Task<bool> TestEncoderAsync(string encoder)
    {
        if (!File.Exists(FFmpegPath)) return false;

        // Build encoder-specific args
        // yuv420p is the universal hardware-compatible pixel format
        // 320x240 meets minimum resolution for all known HW encoders
        var extraArgs = encoder switch
        {
            "h264_amf" or "hevc_amf"   => "-usage transcoding",
            "h264_qsv" or "hevc_qsv"   => "-load_plugins hevc_hw",
            _                           => ""
        };

        // Strip invalid extra args for h264 qsv (load_plugins is hevc-only)
        if (encoder == "h264_qsv") extraArgs = "";

        var args = $"-f lavfi -i color=black:s=320x240:r=1 -vframes 1 " +
                   $"-vf format=yuv420p " +
                   $"-c:v {encoder} {extraArgs} -f null NUL -v error";

        var psi = new ProcessStartInfo(FFmpegPath, args.Trim())
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow  = true,
        };

        using var proc = Process.Start(psi)!;
        // Capture stderr for diagnostic logging
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        // Store last test output for caller to log if needed
        LastTestOutput = $"[{encoder}] exit={proc.ExitCode}" +
                         (string.IsNullOrWhiteSpace(stderr) ? "" : $" | {stderr.Trim()[..Math.Min(200, stderr.Trim().Length)]}");

        return proc.ExitCode == 0;
    }

    /// <summary>Diagnostic output from the last TestEncoderAsync call.</summary>
    public string LastTestOutput { get; private set; } = "";
}
