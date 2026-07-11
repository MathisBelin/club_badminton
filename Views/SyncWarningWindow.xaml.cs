using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

/// <summary>
/// Liste (lecture seule) des personnes à problème d'une synchro : informations manquantes et
/// doublons d'e-mail. Permet de filtrer par type et d'ouvrir la page « Inscriptions non finalisées »
/// pré-filtrée sur le libellé de la synchro.
/// </summary>
public partial class SyncWarningWindow : Window
{
    private readonly ObservableCollection<WarningRow> _rows = new();
    private readonly ICollectionView _view;

    /// <summary>Vrai si l'utilisateur a demandé l'ouverture de la page des inscriptions non finalisées.</summary>
    public bool GoToPending { get; private set; }

    public SyncWarningWindow(AppServices services, AutoSyncConfig config)
    {
        InitializeComponent();

        HeaderText.Text = $"Synchronisation « {config.Name} »   —   libellé « {config.LabelName} »";

        // Informations manquantes : les personnes en attente rattachées à ce libellé.
        foreach (var p in services.Pending)
            if (string.Equals(p.LabelResourceName, config.LabelResourceName, StringComparison.Ordinal))
                _rows.Add(new WarningRow(WarningKind.Incomplete, p));

        // Doublons d'e-mail (associés quand même, mais signalés).
        foreach (var d in config.Duplicates)
            _rows.Add(new WarningRow(WarningKind.Duplicate, d));

        var incomplete = _rows.Count(r => r.Kind == WarningKind.Incomplete);
        var duplicate = _rows.Count(r => r.Kind == WarningKind.Duplicate);
        HintText.Text =
            $"{incomplete} information(s) manquante(s) · {duplicate} doublon(s) d'e-mail. " +
            "Les doublons sont bien associés au libellé ; les informations manquantes doivent être " +
            "complétées dans le Sheet.";

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        Grid.ItemsSource = _view;
    }

    private bool FilterRow(object obj)
    {
        if (obj is not WarningRow r)
            return false;
        return (KindCombo?.SelectedIndex ?? 0) switch
        {
            1 => r.Kind == WarningKind.Incomplete,
            2 => r.Kind == WarningKind.Duplicate,
            _ => true
        };
    }

    private void KindFilter_Changed(object sender, SelectionChangedEventArgs e) => _view?.Refresh();

    private void OpenPending_Click(object sender, RoutedEventArgs e)
    {
        GoToPending = true;
        DialogResult = true;
    }

    private void Fermer_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

internal enum WarningKind { Incomplete, Duplicate }

/// <summary>Ligne de la modale d'alerte (info manquante ou doublon d'e-mail).</summary>
internal sealed class WarningRow
{
    private readonly PendingPerson _person;
    public WarningKind Kind { get; }

    public WarningRow(WarningKind kind, PendingPerson person)
    {
        Kind = kind;
        _person = person;
    }

    public string Nom => _person.Nom;
    public string Prenom => _person.Prenom;
    public string Telephone => _person.Telephone;

    public string TypeText => Kind == WarningKind.Incomplete ? "Info manquante" : "Doublon";

    public string EmailDisplay => Kind == WarningKind.Incomplete && string.IsNullOrWhiteSpace(_person.Email)
        ? "mail non renseigné"
        : _person.Email;

    public Brush TypeBrush => Kind == WarningKind.Incomplete ? ErrBrush : WarnBrush;

    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B));
    private static readonly Brush ErrBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
}
