using System.IO;
using System.Text.Json;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>Registre local (JSON) des Google Forms connus de l'application (dont les modèles).</summary>
public class FormRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public FormRepository(string path) => _path = path;

    public string Path => _path;

    public List<FormRecord> Load()
    {
        if (!File.Exists(_path))
            return new List<FormRecord>();
        try
        {
            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
                return new List<FormRecord>();
            return JsonSerializer.Deserialize<List<FormRecord>>(json, JsonOptions)
                   ?? new List<FormRecord>();
        }
        catch (JsonException)
        {
            return new List<FormRecord>();
        }
    }

    public void Save(IEnumerable<FormRecord> forms)
    {
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(forms, JsonOptions));
    }

    public void Add(FormRecord record)
    {
        var list = Load();
        list.Add(record);
        Save(list);
    }
}
