using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BadmintonClub.Controls;

/// <summary>
/// Champ de recherche arrondi avec bouton « ✕ ». L'événement <see cref="TextChanged"/> est
/// déclenché à chaque frappe, mais en priorité <see cref="DispatcherPriority.Background"/> :
/// il s'exécute juste après l'affichage de la lettre, donc il ne bloque jamais la saisie, et
/// plusieurs frappes très rapprochées sont fusionnées en un seul filtrage (avec le texte le
/// plus récent). Résultat : filtrage quasi instantané à chaque frappe, sans lag.
/// </summary>
public partial class SearchBox : UserControl
{
    public event TextChangedEventHandler? TextChanged;

    private bool _refreshQueued;

    public SearchBox()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => Box.Text;
        set => Box.Text = value ?? string.Empty;
    }

    public string PlaceholderText
    {
        get => Watermark.Text;
        set => Watermark.Text = value;
    }

    public void Clear() => Box.Clear();

    private void Box_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Retour visuel immédiat (filigrane / croix).
        var empty = string.IsNullOrEmpty(Box.Text);
        Watermark.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearBtn.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        QueueRefresh();
    }

    /// <summary>Planifie un filtrage après la frappe (coalescé), sans bloquer la saisie.</summary>
    private void QueueRefresh()
    {
        if (_refreshQueued)
            return; // un filtrage est déjà prévu : il lira le texte le plus récent
        _refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _refreshQueued = false;
            TextChanged?.Invoke(this, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
        }));
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Box.Clear();
        Box.Focus();
    }
}
