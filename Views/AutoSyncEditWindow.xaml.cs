using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BadmintonClub.Models;
using BadmintonClub.Services;

namespace BadmintonClub.Views;

public partial class AutoSyncEditWindow : Window
{
    private readonly AppServices _services;
    private readonly AutoSyncConfig _config; // instance éditée (nouvelle ou existante)
    private readonly bool _isNew;

    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush ErrBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B));
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

    /// <param name="existing">null pour créer, sinon la synchro à modifier.</param>
    public AutoSyncEditWindow(AppServices services, AutoSyncConfig? existing = null)
    {
        InitializeComponent();
        _services = services;
        _isNew = existing == null;
        _config = existing?.Clone() ?? new AutoSyncConfig();

        Title = _isNew ? "Nouvelle synchronisation" : "Modifier la synchronisation";

        NameBox.Text = _config.Name;
        UrlBox.Text = _config.SheetUrl;
        StartRowBox.Text = _config.StartRow > 0 ? _config.StartRow.ToString() : "1";
        EndRowBox.Text = _config.EndRow > 0 ? _config.EndRow.ToString() : string.Empty;
        ColNomBox.Text = _config.ColNom;
        ColPrenomBox.Text = _config.ColPrenom;
        ColTelBox.Text = _config.ColTel;
        ColEmailBox.Text = _config.ColEmail;
        AutoKnownCheck.IsChecked = _config.AutoAssociateKnown;
        EnabledCheck.IsChecked = _isNew || _config.Enabled;

        LabelSelect.Placeholder = "Choisir un libellé";
        LabelSelect.SetEmptyText("Aucun libellé disponible.");

        Loaded += async (_, _) => await LoadLabelsAsync();
    }

    private async Task LoadLabelsAsync()
    {
        try
        {
            var labels = await _services.GetLabelsAsync();
            LabelSelect.SetOptions(labels.Select(l => new CheckOption
            {
                Text = l.Nom,
                Tag = l.ResourceName,
                IsSelected = string.Equals(l.ResourceName, _config.LabelResourceName, StringComparison.Ordinal)
            }));
        }
        catch (GoogleSyncException)
        {
            LabelSelect.SetEmptyText("Libellés indisponibles (hors ligne ?).");
        }
    }

    private (int start, int end, string nom, string prenom, string tel, string mail) ReadInputs()
    {
        var start = int.TryParse(StartRowBox.Text.Trim(), out var s) && s > 0 ? s : 1;
        var end = int.TryParse(EndRowBox.Text.Trim(), out var en) && en > 0 ? en : 0;
        return (start, end,
            ColNomBox.Text.Trim().ToUpperInvariant(),
            ColPrenomBox.Text.Trim().ToUpperInvariant(),
            ColTelBox.Text.Trim().ToUpperInvariant(),
            ColEmailBox.Text.Trim().ToUpperInvariant());
    }

    private void ShowResult(string text, Brush brush)
    {
        TestBorder.Visibility = Visibility.Visible;
        TestResult.Text = text;
        TestResult.Foreground = brush;
    }

    private async void AutoFill_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowResult("✘ Renseignez d'abord le lien du Sheet.", ErrBrush);
            return;
        }

        var (_, end, _, _, _, _) = ReadInputs();
        AutoFillBtn.IsEnabled = false;
        ShowResult("Détection des colonnes en cours…", NeutralBrush);
        try
        {
            var d = await _services.DetectColumnsAsync(url, end);
            if (d.Nom != null) ColNomBox.Text = d.Nom;
            if (d.Prenom != null) ColPrenomBox.Text = d.Prenom;
            if (d.Tel != null) ColTelBox.Text = d.Tel;
            if (d.Email != null) ColEmailBox.Text = d.Email;

            var found = new List<string>();
            var missing = new List<string>();
            (d.Nom != null ? found : missing).Add("Nom");
            (d.Prenom != null ? found : missing).Add("Prénom");
            (d.Tel != null ? found : missing).Add("Téléphone");
            (d.Email != null ? found : missing).Add("E-mail");

            if (found.Count == 4)
                ShowResult("✔ Les 4 colonnes ont été détectées et renseignées.", OkBrush);
            else if (found.Count == 0)
                ShowResult("✘ Aucune colonne détectée (en-tête introuvable).", ErrBrush);
            else
                ShowResult($"⚠ Détectées : {string.Join(", ", found)}.\nÀ renseigner à la main : {string.Join(", ", missing)}.", WarnBrush);
        }
        catch (Exception ex)
        {
            ShowResult("✘ " + ex.Message, ErrBrush);
        }
        finally
        {
            AutoFillBtn.IsEnabled = true;
        }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowResult("✘ Renseignez d'abord le lien du Sheet.", ErrBrush);
            return;
        }

        var (start, end, nom, prenom, tel, mail) = ReadInputs();
        TestBtn.IsEnabled = false;
        ShowResult("Lecture en cours…", NeutralBrush);
        try
        {
            var r = await _services.CheckColumnsAsync(url, start, end, nom, prenom, tel, mail);
            ShowResult(r.Message, r.Ok ? OkBrush : ErrBrush);
        }
        catch (Exception ex)
        {
            ShowResult("✘ " + ex.Message, ErrBrush);
        }
        finally
        {
            TestBtn.IsEnabled = true;
        }
    }

    // ---- Vérification du lien du Sheet -----------------------------------

    private async void VerifyUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            SetUrlBorder(null);
            ShowResult("✘ Renseignez d'abord le lien du Sheet.", ErrBrush);
            return;
        }
        if (!IsOnline())
        {
            SetUrlBorder(null);
            ShowResult("Hors ligne : impossible de vérifier le lien pour le moment.", NeutralBrush);
            return;
        }

        VerifyUrlBtn.IsEnabled = false;
        ShowResult("Vérification du lien en cours…", NeutralBrush);
        try
        {
            var ok = await _services.SheetExistsAsync(url);
            SetUrlBorder(ok);
            ShowResult(ok
                ? "✔ Le lien du Sheet est valide et accessible."
                : "✘ Ce lien de Google Sheet est introuvable ou inaccessible.", ok ? OkBrush : ErrBrush);
        }
        catch (System.Exception ex)
        {
            SetUrlBorder(false);
            ShowResult("✘ " + ex.Message, ErrBrush);
        }
        finally
        {
            VerifyUrlBtn.IsEnabled = true;
        }
    }

    private void UrlBox_TextChanged(object sender, TextChangedEventArgs e) => SetUrlBorder(null);

    /// <summary>Colore le contour du champ lien : vert = existe, rouge = introuvable, null = neutre.</summary>
    private void SetUrlBorder(bool? ok)
    {
        if (ok == null)
        {
            UrlBox.ClearValue(BorderBrushProperty);
            UrlBox.ClearValue(BorderThicknessProperty);
        }
        else
        {
            UrlBox.BorderBrush = ok.Value ? OkBrush : ErrBrush;
            UrlBox.BorderThickness = new Thickness(2);
        }
    }

    private static bool IsOnline()
        => System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();

    private async void Enregistrer_Click(object sender, RoutedEventArgs e)
    {
        // Enregistrement en brouillon autorisé : on n'exige plus tous les champs. Seule contrainte
        // bloquante conservée : si un libellé est choisi, il ne doit pas être déjà utilisé ailleurs.
        var label = LabelSelect.SelectedOption;
        var labelResource = label?.Tag as string ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(labelResource) && _services.IsLabelInUse(labelResource, _config.Id))
        {
            Warn("Ce libellé est déjà ciblé par une autre synchro. Choisissez-en un autre.");
            return;
        }

        // Un lien renseigné doit exister (vérifiable uniquement en ligne).
        var linkUrl = UrlBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(linkUrl) && IsOnline())
        {
            IsEnabled = false;
            bool exists;
            try { exists = await _services.SheetExistsAsync(linkUrl); }
            finally { IsEnabled = true; }

            if (!exists)
            {
                SetUrlBorder(false);
                ShowResult("✘ Ce lien de Google Sheet est introuvable ou inaccessible.", ErrBrush);
                Warn("Le lien du Google Sheet est introuvable ou inaccessible. Corrigez-le ou laissez-le vide.");
                return;
            }
            SetUrlBorder(true);
        }

        var (start, end, nom, prenom, tel, mail) = ReadInputs();

        _config.Name = NameBox.Text.Trim();
        _config.SheetUrl = UrlBox.Text.Trim();
        _config.LabelResourceName = labelResource;
        _config.LabelName = label?.Text ?? string.Empty;
        _config.StartRow = start;
        _config.EndRow = end;
        _config.ColNom = nom;
        _config.ColPrenom = prenom;
        _config.ColTel = tel;
        _config.ColEmail = mail;
        _config.AutoAssociateKnown = AutoKnownCheck.IsChecked == true;

        // Une synchro incomplète (brouillon) ne peut pas être activée, même si la case est cochée.
        _config.Enabled = EnabledCheck.IsChecked == true && _config.IsComplete;

        _services.AddOrUpdateSync(_config);
        if (_config.Enabled)
            _services.StartSync(_config); // (re)programme une exécution immédiate

        DialogResult = true;
    }

    private void Warn(string message)
        => MessageBox.Show(this, message, "Synchronisation", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
