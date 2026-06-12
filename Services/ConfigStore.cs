using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HideIt.Models;

namespace HideIt.Services;

/// <summary>Loads/saves <see cref="AppConfig"/> as indented JSON in %AppData%\HideIt.</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HideIt");

    public string FilePath => Path.Combine(Dir, "config.json");

    /// <summary>Returns the saved config, or a fresh default if missing/corrupt.</summary>
    public AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg != null) return cfg;
            }
        }
        catch
        {
            // Corrupt file — fall through to a clean default rather than crashing.
        }
        return new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(FilePath, json);
    }
}
