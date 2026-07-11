using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>Lecture/écriture de l'historique des activités dans un fichier JSON local.</summary>
public class ActivityRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public ActivityRepository(string path) => _path = path;

    public List<ActivityEntry> Load()
    {
        if (!File.Exists(_path))
            return new List<ActivityEntry>();
        try
        {
            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
                return new List<ActivityEntry>();
            return JsonSerializer.Deserialize<List<ActivityEntry>>(json, JsonOptions)
                   ?? new List<ActivityEntry>();
        }
        catch (JsonException)
        {
            return new List<ActivityEntry>(); // historique corrompu : on repart à vide (non critique)
        }
    }

    public void Save(IEnumerable<ActivityEntry> entries)
    {
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(entries, JsonOptions));
    }
}
