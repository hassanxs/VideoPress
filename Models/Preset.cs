using System.Text.Json.Serialization;

namespace VideoPress.Models;

/// <summary>
/// A named snapshot of all compression settings.
/// Built-in presets are marked IsBuiltIn = true and cannot be deleted.
/// </summary>
public class Preset
{
    public string  Name       { get; set; } = "";
    public bool    IsBuiltIn  { get; set; }

    // Codec
    public VideoCodec    Codec        { get; set; } = VideoCodec.H264;

    // Quality
    public QualityMode   QualityMode  { get; set; } = QualityMode.CRF;
    public int           CRF          { get; set; } = 23;
    public int           BitrateKbps  { get; set; } = 2000;
    public int           TargetSizeMB { get; set; } = 50;

    // Resolution
    public ResolutionMode ResMode      { get; set; } = ResolutionMode.Original;
    public int            ResPresetIdx { get; set; } = 1;   // index into ResPresets array
    public int            CustomWidth  { get; set; } = 1920;
    public int            CustomHeight { get; set; } = 1080;
    public bool           MaintainAspect { get; set; } = true;

    // Audio
    public int  AudioCodecIndex  { get; set; } = 0;   // 0=AAC
    public int  AudioBitrateKbps { get; set; } = 128;

    // Custom flags
    public string CustomFlags { get; set; } = "";

    // Display helpers
    [JsonIgnore]
    public string Badge => IsBuiltIn ? "built-in" : "custom";

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var codec = Codec switch
            {
                VideoCodec.H264 => "H.264",
                VideoCodec.H265 => "H.265",
                VideoCodec.VP9  => "VP9",
                VideoCodec.AV1  => "AV1",
                _               => "H.264",
            };
            var quality = QualityMode switch
            {
                QualityMode.CRF        => $"CRF {CRF}",
                QualityMode.Bitrate    => $"{BitrateKbps} kbps",
                QualityMode.TargetSize => $"{TargetSizeMB} MB",
                _                      => "",
            };
            var res = ResMode switch
            {
                ResolutionMode.Original => "original res",
                ResolutionMode.Preset   => new[]{"4K","1080p","720p","480p","360p"}
                                               [Math.Clamp(ResPresetIdx,0,4)],
                ResolutionMode.Custom   => $"{CustomWidth}×{CustomHeight}",
                _                       => "",
            };
            return $"{codec} · {quality} · {res}";
        }
    }
}
