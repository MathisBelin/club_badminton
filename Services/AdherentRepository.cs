using System.IO;
using System.Text.Json;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>
/// Lecture/écriture des adhérents dans un fichier JSON local (System.Text.Json).
/// </summary>
public class AdherentRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public AdherentRepository(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public List<Adherent> Load()
    {
        if (!File.Exists(_path))
            return new List<Adherent>();

        try
        {
            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
                return new List<Adherent>();

            return JsonSerializer.Deserialize<List<Adherent>>(json, JsonOptions)
                   ?? new List<Adherent>();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Le fichier des adhérents est illisible ou corrompu : {_path}", ex);
        }
    }

    public void Save(IEnumerable<Adherent> adherents)
    {
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(adherents, JsonOptions);
        File.WriteAllText(_path, json);
    }
}
