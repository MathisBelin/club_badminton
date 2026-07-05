using System.IO;
using System.Text.Json;

namespace BadmintonClub.Services;

/// <summary>Un modèle de mail (nom, objet, corps).</summary>
public sealed class MailTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

/// <summary>Stockage des modèles de mails (un fichier JSON par modèle).</summary>
public static class MailTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<MailTemplate> LoadAll()
    {
        AppPaths.EnsureMailTemplatesFolder();
        var list = new List<MailTemplate>();
        foreach (var file in Directory.GetFiles(AppPaths.MailTemplatesFolder, "*.json"))
        {
            try
            {
                var t = JsonSerializer.Deserialize<MailTemplate>(File.ReadAllText(file), JsonOptions);
                if (t != null && !string.IsNullOrWhiteSpace(t.Name))
                    list.Add(t);
            }
            catch (JsonException) { /* ignore fichier illisible */ }
        }
        return list.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static void Save(MailTemplate template)
    {
        AppPaths.EnsureMailTemplatesFolder();
        var path = Path.Combine(AppPaths.MailTemplatesFolder, Sanitize(template.Name) + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(template, JsonOptions));
    }

    public static void Delete(string name)
    {
        var path = Path.Combine(AppPaths.MailTemplatesFolder, Sanitize(name) + ".json");
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "modele" : name;
    }
}
