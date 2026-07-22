namespace BadmintonClub.Models;

/// <summary>
/// Modèle de Google Form : structure réutilisable (titre + questions) enregistrée localement
/// en JSON. Permet de recréer un nouveau formulaire via l'API, depuis la liste locale ou un fichier importé.
/// </summary>
public class FormTemplate
{
    /// <summary>Nom du modèle (sert de nom de fichier et de titre par défaut).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Questions du formulaire, dans l'ordre.</summary>
    public List<FormTemplateItem> Items { get; set; } = new();
}

/// <summary>Une question d'un modèle de formulaire.</summary>
public class FormTemplateItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Type : TEXT, PARAGRAPH, RADIO, CHECKBOX, DROP_DOWN, DATE.</summary>
    public string Type { get; set; } = "TEXT";

    public bool Required { get; set; }

    /// <summary>Options (pour RADIO / CHECKBOX / DROP_DOWN).</summary>
    public List<string> Options { get; set; } = new();
}
