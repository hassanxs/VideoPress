using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace VideoPress.Services;

/// <summary>
/// Wraps MediaInfo CLI (MediaInfo.exe) bundled as an embedded resource.
/// Runs MediaInfo --Output=XML and parses the result.
/// Run fetch-mediainfo.bat once to download MediaInfo.exe into Assets\mediainfo\
/// before building.
/// </summary>
public class MediaInfoService
{
    private static readonly string AppDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "VideoPress");

    public static string MediaInfoPath => Path.Combine(AppDir, "MediaInfo.exe");

    private const string ResMediaInfo = "VideoPress.Assets.MediaInfo.exe";

    public bool IsAvailable => File.Exists(MediaInfoPath);
    public bool IsBundled   => HasEmbeddedResource(ResMediaInfo);

    // ── Extraction ────────────────────────────────────────────────────────────

    public async Task ExtractAsync(CancellationToken ct = default)
    {
        if (!IsBundled) return;

        Directory.CreateDirectory(AppDir);

        var stampPath    = Path.Combine(AppDir, "mediainfo.stamp");
        var embeddedHash = GetEmbeddedHash(ResMediaInfo);

        if (File.Exists(MediaInfoPath) &&
            File.Exists(stampPath) &&
            File.ReadAllText(stampPath).Trim() == embeddedHash)
            return;

        await ExtractResourceAsync(ResMediaInfo, MediaInfoPath, ct);
        await File.WriteAllTextAsync(stampPath, embeddedHash, ct);
    }

    // ── Core parse ────────────────────────────────────────────────────────────

    public async Task<MediaInfoResult> GetInfoAsync(string filePath)
    {
        if (!IsAvailable || !File.Exists(filePath))
            return new MediaInfoResult();

        var psi = new ProcessStartInfo(MediaInfoPath, $"--Output=XML \"{filePath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute  = false,
            CreateNoWindow   = true,
        };

        using var proc = Process.Start(psi)!;
        var xml = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        return ParseXml(xml);
    }

    // ── XML parser ────────────────────────────────────────────────────────────

    private static MediaInfoResult ParseXml(string xml)
    {
        var result = new MediaInfoResult();
        if (string.IsNullOrWhiteSpace(xml)) return result;

        try
        {
            var doc   = XDocument.Parse(xml);
            var ns    = doc.Root?.Name.Namespace ?? XNamespace.None;
            var tracks = doc.Descendants(ns + "track").ToList();

            // General
            var gen = tracks.FirstOrDefault(t => (string?)t.Attribute("type") == "General");
            if (gen != null)
            {
                result.Container          = E(gen, ns, "Format");
                result.DurationMs         = ParseLong(E(gen, ns, "Duration"));
                result.OverallBitrateKbps = ParseLong(E(gen, ns, "OverallBitRate")) / 1000;
                result.WritingApp         = E(gen, ns, "Encoded_Application");
            }

            // Video
            var vid = tracks.FirstOrDefault(t => (string?)t.Attribute("type") == "Video");
            if (vid != null)
            {
                result.VideoCodec        = E(vid, ns, "Format");
                result.VideoProfile      = E(vid, ns, "Format_Profile");
                result.Width             = (int)ParseLong(E(vid, ns, "Width"));
                result.Height            = (int)ParseLong(E(vid, ns, "Height"));
                result.FrameRate         = E(vid, ns, "FrameRate");
                result.FrameRateMode     = E(vid, ns, "FrameRate_Mode");
                result.VideoBitrateKbps  = ParseLong(E(vid, ns, "BitRate")) / 1000;
                result.BitDepth          = E(vid, ns, "BitDepth");
                result.ChromaSubsampling = E(vid, ns, "ChromaSubsampling");
                result.ColorRange        = E(vid, ns, "colour_range");
                result.ColorPrimaries    = E(vid, ns, "colour_primaries");
                result.TransferChar      = E(vid, ns, "transfer_characteristics");
                result.ScanType          = E(vid, ns, "ScanType");
                result.AspectRatio       = E(vid, ns, "DisplayAspectRatio_String");
                result.HdrFormat         = E(vid, ns, "HDR_Format");

                if (string.IsNullOrEmpty(result.HdrFormat))
                {
                    var t = result.TransferChar.ToLowerInvariant();
                    if (t.Contains("pq"))       result.HdrFormat = "HDR10";
                    else if (t.Contains("hlg")) result.HdrFormat = "HLG";
                }
            }

            // Audio tracks
            foreach (var aud in tracks.Where(t => (string?)t.Attribute("type") == "Audio"))
            {
                result.AudioTracks.Add(new AudioTrackInfo
                {
                    Index       = result.AudioTracks.Count + 1,
                    Codec       = E(aud, ns, "Format"),
                    Channels    = E(aud, ns, "Channels"),
                    SampleRate  = E(aud, ns, "SamplingRate"),
                    BitrateKbps = ParseLong(E(aud, ns, "BitRate")) / 1000,
                    Language    = E(aud, ns, "Language") is { Length: > 0 } lang ? lang : "und",
                    Title       = E(aud, ns, "Title"),
                    Default     = E(aud, ns, "Default") == "Yes",
                });
            }

            // Subtitle tracks
            foreach (var sub in tracks.Where(t => (string?)t.Attribute("type") == "Text"))
            {
                result.SubtitleTracks.Add(new SubtitleTrackInfo
                {
                    Index    = result.SubtitleTracks.Count + 1,
                    Format   = E(sub, ns, "Format"),
                    Language = E(sub, ns, "Language") is { Length: > 0 } lang ? lang : "und",
                    Title    = E(sub, ns, "Title"),
                    Default  = E(sub, ns, "Default") == "Yes",
                    Forced   = E(sub, ns, "Forced") == "Yes",
                });
            }
        }
        catch { }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string E(XElement el, XNamespace ns, string name)
        => el.Element(ns + name)?.Value?.Trim() ?? "";

    private static long ParseLong(string s)
        => long.TryParse((s ?? "").Split('.')[0].Split(' ')[0], out var v) ? v : 0;

    private static async Task ExtractResourceAsync(string resourceName, string destPath,
                                                   CancellationToken ct)
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        await using var src  = asm.GetManifestResourceStream(resourceName)
                               ?? throw new InvalidOperationException(
                                   $"Embedded resource not found: {resourceName}");
        await using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write,
                                              FileShare.None, 81920, useAsync: true);
        await src.CopyToAsync(dest, ct);
    }

    private static string GetEmbeddedHash(string resourceName)
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return "not-embedded";
        using var md5 = System.Security.Cryptography.MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(stream));
    }

    private static bool HasEmbeddedResource(string name)
        => System.Reflection.Assembly.GetExecutingAssembly()
               .GetManifestResourceNames().Contains(name);
}

// ── Data models ───────────────────────────────────────────────────────────────

public class MediaInfoResult
{
    public string Container          { get; set; } = "";
    public long   DurationMs         { get; set; }
    public long   OverallBitrateKbps { get; set; }
    public string WritingApp         { get; set; } = "";

    public string VideoCodec         { get; set; } = "";
    public string VideoProfile       { get; set; } = "";
    public int    Width              { get; set; }
    public int    Height             { get; set; }
    public string FrameRate          { get; set; } = "";
    public string FrameRateMode      { get; set; } = "";
    public long   VideoBitrateKbps   { get; set; }
    public string BitDepth           { get; set; } = "";
    public string ChromaSubsampling  { get; set; } = "";
    public string ColorRange         { get; set; } = "";
    public string ColorPrimaries     { get; set; } = "";
    public string TransferChar       { get; set; } = "";
    public string ScanType           { get; set; } = "";
    public string AspectRatio        { get; set; } = "";
    public string HdrFormat          { get; set; } = "";

    public List<AudioTrackInfo>    AudioTracks    { get; } = new();
    public List<SubtitleTrackInfo> SubtitleTracks { get; } = new();

    public string ResolutionStr => Width > 0 && Height > 0 ? $"{Width}×{Height}" : "";
    public string DurationStr   => DurationMs > 0
        ? TimeSpan.FromMilliseconds(DurationMs).ToString(@"h\:mm\:ss") : "";
    public string BitrateStr    => VideoBitrateKbps > 0
        ? (VideoBitrateKbps >= 1000 ? $"{VideoBitrateKbps/1000.0:F2} Mbps" : $"{VideoBitrateKbps} kbps")
        : "";
    public string OverallBitrateStr => OverallBitrateKbps > 0
        ? (OverallBitrateKbps >= 1000 ? $"{OverallBitrateKbps/1000.0:F2} Mbps" : $"{OverallBitrateKbps} kbps")
        : "";
    public string VideoProfileStr => !string.IsNullOrEmpty(VideoProfile) ? VideoProfile : VideoCodec;
    public string BitDepthStr     => !string.IsNullOrEmpty(BitDepth) && BitDepth != "0"
                                         ? $"{BitDepth}-bit" : "";
    public string ChromaStr       => ChromaSubsampling;
    public string HdrBadge        => HdrFormat;
    public bool   IsHdr           => HdrFormat.Length > 0;

    public string FrameRateStr
    {
        get
        {
            if (string.IsNullOrEmpty(FrameRate)) return "";
            if (!double.TryParse(FrameRate,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var fps))
                return FrameRate;
            var s = $"{fps:F3}".TrimEnd('0').TrimEnd('.');
            return $"{s} fps";
        }
    }
}

public class AudioTrackInfo
{
    public int    Index       { get; set; }
    public string Codec       { get; set; } = "";
    public string Channels    { get; set; } = "";
    public string SampleRate  { get; set; } = "";
    public long   BitrateKbps { get; set; }
    public string Language    { get; set; } = "";
    public string Title       { get; set; } = "";
    public bool   Default     { get; set; }

    public string ChannelsStr  => Channels switch
    {
        "1" => "Mono", "2" => "Stereo", "6" => "5.1", "8" => "7.1",
        _   => string.IsNullOrEmpty(Channels) ? "" : $"{Channels}ch"
    };
    public string SampleRateStr => long.TryParse(SampleRate, out var hz)
        ? $"{hz/1000.0:F1} kHz" : SampleRate;
    public string BitrateStr    => BitrateKbps > 0 ? $"{BitrateKbps} kbps" : "";
    public string Summary       => string.Join(" · ",
        new[] { Codec, ChannelsStr, SampleRateStr, BitrateStr, Language }
        .Where(s => !string.IsNullOrEmpty(s)));
}

public class SubtitleTrackInfo
{
    public int    Index    { get; set; }
    public string Format   { get; set; } = "";
    public string Language { get; set; } = "";
    public string Title    { get; set; } = "";
    public bool   Default  { get; set; }
    public bool   Forced   { get; set; }
}
