using System.IO;
using System.Text.Json;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>Un fichier de modèle de formulaire présent dans le dépôt local.</summary>
public sealed record FormModelFile(string Name, string Path);

/// <summary>
/// Dépôt local (fichiers JSON) des modèles de Google Forms. Chaque modèle est un fichier
/// <c>&lt;nom&gt;.json</c> dans <see cref="AppPaths.FormModelsFolder"/>.
/// </summary>
public class FormTemplateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _folder;

    public FormTemplateRepository(string? folder = null)
        => _folder = folder ?? AppPaths.FormModelsFolder;

    /// <summary>Liste les modèles disponibles (triés par nom).</summary>
    public List<FormModelFile> List()
    {
        if (!Directory.Exists(_folder))
            return new List<FormModelFile>();

        return Directory.EnumerateFiles(_folder, "*.json")
            .Select(p => new FormModelFile(System.IO.Path.GetFileNameWithoutExtension(p), p))
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Charge un modèle depuis un fichier (renvoie null si illisible / invalide).</summary>
    public static FormTemplate? Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var tpl = JsonSerializer.Deserialize<FormTemplate>(json, JsonOptions);
            return tpl != null && tpl.Items != null ? tpl : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Enregistre un modèle dans le dépôt local (nom de fichier assaini). Renvoie le chemin écrit.</summary>
    public string Save(FormTemplate template)
    {
        Directory.CreateDirectory(_folder);
        var file = System.IO.Path.Combine(_folder, Sanitize(template.Name) + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(template, JsonOptions));
        return file;
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = "modele";
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
