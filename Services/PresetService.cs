using System.IO;
using System.Text.Json;
using VideoPress.Models;

namespace VideoPress.Services;

public class PresetService
{
    private static readonly string PresetPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "VideoPress", "presets.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── Built-in presets (never persisted — always re-created) ───────────────
    public static readonly IReadOnlyList<Preset> BuiltIn = new List<Preset>
    {
        new()
        {
            Name = "Web 1080p", IsBuiltIn = true,
            Codec = VideoCodec.H264, QualityMode = QualityMode.CRF, CRF = 23,
            ResMode = ResolutionMode.Preset, ResPresetIdx = 1,   // 1080p
            AudioCodecIndex = 0, AudioBitrateKbps = 128,
        },
        new()
        {
            Name = "Web 720p", IsBuiltIn = true,
            Codec = VideoCodec.H264, QualityMode = QualityMode.CRF, CRF = 26,
            ResMode = ResolutionMode.Preset, ResPresetIdx = 2,   // 720p
            AudioCodecIndex = 0, AudioBitrateKbps = 128,
        },
        new()
        {
            Name = "Archive HQ", IsBuiltIn = true,
            Codec = VideoCodec.H265, QualityMode = QualityMode.CRF, CRF = 18,
            ResMode = ResolutionMode.Original,
            AudioCodecIndex = 0, AudioBitrateKbps = 192,
        },
        new()
        {
            Name = "Discord (8 MB)", IsBuiltIn = true,
            Codec = VideoCodec.H264, QualityMode = QualityMode.TargetSize, TargetSizeMB = 8,
            ResMode = ResolutionMode.Preset, ResPresetIdx = 3,   // 480p
            AudioCodecIndex = 0, AudioBitrateKbps = 96,
        },
        new()
        {
            Name = "Twitter / X (10 MB)", IsBuiltIn = true,
            Codec = VideoCodec.H264, QualityMode = QualityMode.TargetSize, TargetSizeMB = 10,
            ResMode = ResolutionMode.Preset, ResPresetIdx = 2,   // 720p
            AudioCodecIndex = 0, AudioBitrateKbps = 128,
        },
        new()
        {
            Name = "Mobile", IsBuiltIn = true,
            Codec = VideoCodec.H264, QualityMode = QualityMode.CRF, CRF = 28,
            ResMode = ResolutionMode.Preset, ResPresetIdx = 3,   // 480p
            AudioCodecIndex = 0, AudioBitrateKbps = 96,
        },
    };

    // ── User presets ─────────────────────────────────────────────────────────
    public List<Preset> LoadUserPresets()
    {
        if (!File.Exists(PresetPath)) return new();
        try
        {
            var json = File.ReadAllText(PresetPath);
            return JsonSerializer.Deserialize<List<Preset>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    public void SaveUserPresets(IEnumerable<Preset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PresetPath)!);
        var json = JsonSerializer.Serialize(presets.ToList(), JsonOpts);
        File.WriteAllText(PresetPath, json);
    }

    public void AddOrReplace(List<Preset> userPresets, Preset preset)
    {
        var idx = userPresets.FindIndex(p => p.Name == preset.Name);
        if (idx >= 0) userPresets[idx] = preset;
        else          userPresets.Add(preset);
        SaveUserPresets(userPresets);
    }

    public void Delete(List<Preset> userPresets, Preset preset)
    {
        userPresets.Remove(preset);
        SaveUserPresets(userPresets);
    }
}
