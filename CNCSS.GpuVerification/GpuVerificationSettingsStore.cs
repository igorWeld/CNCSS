using System.Text.Json;

namespace CNCSS.GpuVerification;

/// <summary>JSON в %LocalAppData%\CNCSS\gpu_verification.settings.json</summary>
public static class GpuVerificationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetDefaultFilePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CNCSS");
        return Path.Combine(dir, "gpu_verification.settings.json");
    }

    public static GpuVerificationUserSettings Load(string? path = null)
    {
        path ??= GetDefaultFilePath();
        try
        {
            if (!File.Exists(path))
            {
                return new GpuVerificationUserSettings();
            }

            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<GpuVerificationUserSettingsDto>(json, JsonOptions);
            if (loaded == null)
            {
                return new GpuVerificationUserSettings();
            }

            int rawMax = loaded.MaxVoxelsPerAxis <= 0 ? 192 : loaded.MaxVoxelsPerAxis;
            return new GpuVerificationUserSettings
            {
                Enabled = loaded.Enabled ?? true,
                PreferHighPerformanceGpu = loaded.PreferHighPerformanceGpu,
                MaxVoxelsPerAxis = Math.Clamp(rawMax, 32, 512)
            };
        }
        catch
        {
            return new GpuVerificationUserSettings();
        }
    }

    public static void Save(GpuVerificationUserSettings settings, string? path = null)
    {
        path ??= GetDefaultFilePath();
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var dto = new GpuVerificationUserSettingsDto
            {
                Enabled = settings.Enabled,
                PreferHighPerformanceGpu = settings.PreferHighPerformanceGpu,
                MaxVoxelsPerAxis = Math.Clamp(settings.MaxVoxelsPerAxis, 32, 512)
            };
            File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch
        {
            // ignore IO errors
        }
    }

    private sealed class GpuVerificationUserSettingsDto
    {
        public bool? Enabled { get; set; }
        public bool PreferHighPerformanceGpu { get; set; } = true;
        public int MaxVoxelsPerAxis { get; set; } = 192;
    }
}
