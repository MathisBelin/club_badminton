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

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); Touch(); }
    }

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
        Enabled = Enabled
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
