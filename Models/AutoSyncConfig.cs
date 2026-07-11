using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace BadmintonClub.Models;

/// <summary>
/// Une synchronisation automatique : un Google Sheet importé périodiquement vers un libellé cible.
/// Chaque libellé ne peut être ciblé que par une seule synchro (unicité gérée par AppServices).
/// </summary>
public class AutoSyncConfig : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string SheetUrl { get; set; } = string.Empty;

    private string _labelResourceName = string.Empty;
    public string LabelResourceName
    {
        get => _labelResourceName;
        set { _labelResourceName = value; OnPropertyChanged(); }
    }

    /// <summary>Nom lisible du libellé cible (mémorisé pour l'affichage hors ligne).</summary>
    private string _labelName = string.Empty;
    public string LabelName
    {
        get => _labelName;
        set { _labelName = value; OnPropertyChanged(); }
    }

    public int StartRow { get; set; } = 1;
    public int EndRow { get; set; }
    public string ColNom { get; set; } = string.Empty;
    public string ColPrenom { get; set; } = string.Empty;
    public string ColTel { get; set; } = string.Empty;
    public string ColEmail { get; set; } = string.Empty;

    /// <summary>
    /// Associe automatiquement au libellé les personnes sans e-mail exploitable mais dont les infos
    /// correspondent de façon certaine (« Connue ») à un UNIQUE contact existant. Ces personnes ne
    /// sont alors plus affichées dans les inscriptions non finalisées. Aucune écriture dans le Sheet.
    /// </summary>
    public bool AutoAssociateKnown { get; set; }

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); Touch(); }
    }

    /// <summary>
    /// Une synchro est « complète » (donc lançable) quand le nom, le lien du Sheet, le libellé
    /// cible et la colonne e-mail sont renseignés. Sinon elle est enregistrée en brouillon
    /// (ligne orange) mais ne peut pas être démarrée.
    /// </summary>
    [JsonIgnore]
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(SheetUrl)
        && !string.IsNullOrWhiteSpace(LabelResourceName)
        && !string.IsNullOrWhiteSpace(ColEmail);

    // ---- État d'exécution (non sérialisé) --------------------------------

    [JsonIgnore] public DateTime? NextRun { get; set; }

    private bool _isImporting;
    [JsonIgnore]
    public bool IsImporting
    {
        get => _isImporting;
        set { _isImporting = value; OnPropertyChanged(); Touch(); }
    }

    private int _progress;
    /// <summary>Progression de la synchro en cours (0-100).</summary>
    [JsonIgnore]
    public int Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }

    /// <summary>Progression formatée (« 42 % »).</summary>
    [JsonIgnore] public string ProgressText => $"{_progress} %";

    /// <summary>Vrai si la synchro est active (« en marche »).</summary>
    [JsonIgnore] public bool IsRunning => Enabled;

    /// <summary>Minuteur avant la prochaine exécution (ou « en cours… », « — »).</summary>
    [JsonIgnore]
    public string CountdownText
    {
        get
        {
            if (!Enabled) return "—";
            if (IsImporting) return "en cours…";
            if (NextRun is DateTime n)
            {
                var r = n - DateTime.Now;
                if (r < TimeSpan.Zero) r = TimeSpan.Zero;
                return $"{r:mm\\:ss}";
            }
            return "…";
        }
    }

    // ---- Avertissements (infos manquantes / doublons d'e-mail — non sérialisé) ------

    /// <summary>Nombre de personnes aux infos incomplètes rattachées à cette synchro.</summary>
    [JsonIgnore] public int IncompleteCount { get; private set; }

    /// <summary>Vrai s'il y a au moins une personne aux infos manquantes pour cette synchro.</summary>
    [JsonIgnore] public bool HasIncomplete => IncompleteCount > 0;

    /// <summary>Personnes ayant renseigné le même e-mail plusieurs fois (associées quand même).</summary>
    [JsonIgnore] public List<PendingPerson> Duplicates { get; } = new();

    /// <summary>Vrai s'il y a des infos manquantes OU des doublons d'e-mail → bouton d'alerte visible.</summary>
    [JsonIgnore] public bool HasWarning => IncompleteCount > 0 || Duplicates.Count > 0;

    /// <summary>Infobulle du bouton d'alerte.</summary>
    [JsonIgnore] public string WarningTooltip { get; private set; } = string.Empty;

    // ---- Trace (personnes associées lors de la dernière exécution — non sérialisé) ----

    /// <summary>Personnes associées au libellé lors de la dernière synchro (dont celles via l'option « connues »).</summary>
    [JsonIgnore] public List<SyncTraceEntry> Trace { get; } = new();

    /// <summary>Vrai si la dernière synchro a associé au moins une personne → bouton « Trace » visible.</summary>
    [JsonIgnore] public bool HasTrace => Trace.Count > 0;

    /// <summary>Remplace la trace par celle de la dernière exécution.</summary>
    public void SetTrace(IEnumerable<SyncTraceEntry> entries)
    {
        Trace.Clear();
        Trace.AddRange(entries);
        OnPropertyChanged(nameof(HasTrace));
    }

    /// <summary>Met à jour les compteurs d'alerte (appelé par le moteur après chaque synchro).</summary>
    public void SetWarnings(int incompleteCount, IEnumerable<PendingPerson> duplicates)
    {
        IncompleteCount = incompleteCount;
        Duplicates.Clear();
        Duplicates.AddRange(duplicates);

        var parts = new List<string>();
        if (IncompleteCount > 0) parts.Add($"{IncompleteCount} information(s) manquante(s)");
        if (Duplicates.Count > 0) parts.Add($"{Duplicates.Count} doublon(s) d'e-mail");
        WarningTooltip = parts.Count > 0 ? "⚠ " + string.Join(" · ", parts) : string.Empty;

        OnPropertyChanged(nameof(IncompleteCount));
        OnPropertyChanged(nameof(HasIncomplete));
        OnPropertyChanged(nameof(HasWarning));
        OnPropertyChanged(nameof(WarningTooltip));
    }

    /// <summary>Rafraîchit les propriétés dérivées de l'exécution (état, minuteur). Appelé ~1×/s.</summary>
    public void Touch()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsImporting));
        OnPropertyChanged(nameof(CountdownText));
        OnPropertyChanged(nameof(ProgressText));
    }

    public AutoSyncConfig Clone() => new()
    {
        Id = Id,
        Name = Name,
        SheetUrl = SheetUrl,
        LabelResourceName = LabelResourceName,
        LabelName = LabelName,
        StartRow = StartRow,
        EndRow = EndRow,
        ColNom = ColNom,
        ColPrenom = ColPrenom,
        ColTel = ColTel,
        ColEmail = ColEmail,
        AutoAssociateKnown = AutoAssociateKnown,
        Enabled = Enabled
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
