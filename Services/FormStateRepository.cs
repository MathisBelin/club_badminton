using System.IO;
using System.Text.Json;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>
/// Décisions locales prises sur les réponses des formulaires (form_states.json du compte) :
/// inscriptions validées, statuts forcés, différences ignorées. Un dictionnaire
/// identifiant de formulaire → <see cref="FormState"/>.
/// </summary>
public class FormStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private Dictionary<string, FormState>? _cache;

    public FormStateRepository(string path) => _path = path;

    private Dictionary<string, FormState> All
    {
        get
        {
            if (_cache != null)
                return _cache;
            if (!File.Exists(_path))
                return _cache = new Dictionary<string, FormState>(StringComparer.Ordinal);
            try
            {
                var json = File.ReadAllText(_path);
                _cache = string.IsNullOrWhiteSpace(json)
                    ? new Dictionary<string, FormState>(StringComparer.Ordinal)
                    : JsonSerializer.Deserialize<Dictionary<string, FormState>>(json, JsonOptions)
                      ?? new Dictionary<string, FormState>(StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                _cache = new Dictionary<string, FormState>(StringComparer.Ordinal);
            }
            return _cache;
        }
    }

    /// <summary>État d'un formulaire (créé à la volée s'il n'existe pas encore).</summary>
    public FormState Get(string formId)
    {
        if (!All.TryGetValue(formId, out var state))
            All[formId] = state = new FormState();

        // Les dictionnaires relus du JSON perdent leur comparateur : on le rétablit.
        if (!ReferenceEquals(state.StatusOverrides.Comparer, StringComparer.OrdinalIgnoreCase))
            state.StatusOverrides = new Dictionary<string, string>(state.StatusOverrides, StringComparer.OrdinalIgnoreCase);
        if (!ReferenceEquals(state.ContactLinks.Comparer, StringComparer.OrdinalIgnoreCase))
            state.ContactLinks = new Dictionary<string, string>(state.ContactLinks, StringComparer.OrdinalIgnoreCase);

        return state;
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(All, JsonOptions));
    }
}
