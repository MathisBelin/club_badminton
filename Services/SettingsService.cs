using System.IO;
using System.Text.Json;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>
/// Charge et enregistre les paramètres dans settings.json.
/// </summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        AppPaths.EnsureDataFolder();

        if (!File.Exists(AppPaths.SettingsFile))
        {
            // Premier lancement : on peut pré-remplir depuis un config.json placé à côté de l'exe.
            var initial = LoadSeedConfig() ?? new AppSettings();
            Save(initial);
            return initial;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Paramètres corrompus : on repart sur des valeurs par défaut plutôt que de planter.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureDataFolder();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsFile, json);
    }

    /// <summary>
    /// Charge le config.json optionnel (à côté de l'exe) servant de graine au 1er lancement.
    /// Renvoie null s'il est absent ou illisible.
    /// </summary>
    private static AppSettings? LoadSeedConfig()
    {
        if (!File.Exists(AppPaths.SeedConfigFile))
            return null;

        try
        {
            var json = File.ReadAllText(AppPaths.SeedConfigFile);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Renvoie le chemin effectif du JSON des adhérents (param ou défaut).</summary>
    public static string ResolveAdherentsPath(AppSettings settings)
        => string.IsNullOrWhiteSpace(settings.AdherentsJsonPath)
            ? AppPaths.DefaultAdherentsFile
            : settings.AdherentsJsonPath;
}
