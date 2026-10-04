using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Verrdoss.Core;
using Forms = System.Windows.Forms;

namespace Verrdoss.App;

public partial class MainWindow : Window
{
    private readonly VaultService _service;
    private readonly App _app;
    private bool _unlockPrompt;
    public bool UnlockPromptActive => _unlockPrompt;

    public MainWindow(VaultService service, App app)
    {
        InitializeComponent();
        _service = service;
        _app = app;
        LimitsText.Text =
            "Fermer la fenêtre laisse VerrDoss actif dans la zone de notification, afin de reverrouiller après inactivité. " +
            "Quitter chiffre d'abord les dossiers ouverts. Si le programme est tué, un dossier déjà déverrouillé reste lisible jusqu'au prochain verrouillage. " +
            "Sur un SSD, supprimer les fichiers en clair ne garantit pas l'effacement physique des anciennes données. " +
            "Un dossier verrouillé reste visible, avec une icône cadenas : un double-clic demande le mot de passe, et on ne peut pas y déposer de fichiers. " +
            "Le fichier chiffré reste masqué. C'est le chiffrement qui protège le contenu. " +
            "Le clic droit « Verrouiller avec VerrDoss » apparaît dans le menu de Windows 11.";
        Reload();
    }

    public void Reload()
    {
        var repaired = _service.EnsurePlaceholders();
        foreach (var created in repaired)
            ShellNotify.Refresh(created);
        if (repaired.Count > 0)
            ShellNotify.RefreshIcons();
        var selected = (VaultList.SelectedItem as VaultRow)?.Id;
        var rows = _service.Store.Data.Vaults
            .Select(vault => new VaultRow
            {
                Id = vault.Id,
                Name = Path.GetFileName(vault.OriginalPath),
                Location = vault.OriginalPath,
                State = _service.Describe(vault),
                OwnPassword = vault.HasFolderPassword ? "Oui" : "Non"
            })
            .ToList();
        VaultList.ItemsSource = rows;
        if (selected != null)
            VaultList.SelectedItem = rows.FirstOrDefault(row => row.Id == selected);
        foreach (var row in rows)
        {
            if (row.State.StartsWith("Verrouillé", StringComparison.Ordinal))
                ShellNotify.RefreshLocked(row.Location);
            else
                ShellNotify.Refresh(row.Location);
        }
    }

    public async void UnlockFromShell(string path)
    {
        if (_unlockPrompt)
            return;
        _unlockPrompt = true;
        try
        {
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                MessageBox.Show("Emplacement invalide.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var record = _service.Store.FindByOriginalPath(full);
            if (record == null)
            {
                MessageBox.Show("Ce dossier n'est pas protégé par VerrDoss.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_service.Describe(record).StartsWith("Déverrouillé", StringComparison.Ordinal))
            {
                OpenFolder(full);
                return;
            }

            var prompt = new PasswordWindow("Déverrouiller", "Mot de passe du dossier, ou mot de passe maître.");
            prompt.WindowStartupLocation = WindowStartupLocation.Manual;
            prompt.Topmost = true;
            prompt.ShowInTaskbar = true;
            prompt.SourceInitialized += (_, _) => PlaceOnCursorScreen(prompt);
            prompt.ContentRendered += (_, _) => BringToFront(prompt);

            if (prompt.ShowDialog() != true)
                return;
            var password = PasswordBytes.From(prompt.Password);
            prompt.Clear();
            var remember = false;
            try
            {
                var ok = await RunBusy(() =>
                {
                    _service.Unlock(record.Id, password);
                    remember = _service.Store.CheckMaster(password);
                });
                if (!ok)
                    return;
                if (remember)
                    _app.RememberMaster(password);
                OpenFolder(full);
            }
            finally
            {
                PasswordBytes.Clear(password);
            }
        }
        finally
        {
            _unlockPrompt = false;
        }
    }

    private static void PlaceOnCursorScreen(Window window)
    {
        var area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        window.Left = area.Left + Math.Max(0, (area.Width - window.Width) / 2);
        window.Top = area.Top + Math.Max(0, (area.Height - window.Height) / 2);
    }

    private static void BringToFront(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        AttachThreadInput(currentThread, foregroundThread, true);
        ShowWindow(handle, 9);
        SetForegroundWindow(handle);
        AttachThreadInput(currentThread, foregroundThread, false);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachFlag);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
            return;
        ShellNotify.Refresh(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void LockFromShell(string path)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            MessageBox.Show("Emplacement invalide.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var existing = _service.Store.FindByOriginalPath(full);
        if (existing == null)
        {
            _ = AddFolderAsync(full);
            return;
        }

        VaultList.SelectedItem = VaultList.Items.OfType<VaultRow>().FirstOrDefault(row => row.Id == existing.Id);
        _ = LockSelectedAsync();
    }

    public async Task<bool> RelockMissingAsync()
    {
        foreach (var vault in _service.UnlockedWithoutKey())
        {
            var name = Path.GetFileName(vault.OriginalPath);
            var proceed = MessageBox.Show(
                $"{name} est encore en clair, mais sa clé n'est plus en mémoire. Saisissez le mot de passe maître pour le reverrouiller.",
                Brand.Name,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.OK)
                return false;

            var masterPrompt = new PasswordWindow("Mot de passe maître", $"Mot de passe maître pour reverrouiller {name}.");
            masterPrompt.Owner = this;
            if (masterPrompt.ShowDialog() != true)
                return false;
            var master = PasswordBytes.From(masterPrompt.Password);
            masterPrompt.Clear();
            byte[]? folder = null;
            try
            {
                if (MessageBox.Show(
                        "Conserver un mot de passe propre à ce dossier ? Le mot de passe maître pourra toujours l'ouvrir.",
                        Brand.Name,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    var folderPrompt = new PasswordWindow("Mot de passe du dossier", "Au moins 4 caractères.", confirm: true);
                    folderPrompt.Owner = this;
                    if (folderPrompt.ShowDialog() != true)
                        return false;
                    folder = PasswordBytes.From(folderPrompt.Password);
                    folderPrompt.Clear();
                }

                await Task.Run(() => _service.Relock(vault.Id, master, folder));
                _app.RememberMaster(master);
            }
            finally
            {
                PasswordBytes.Clear(master);
                PasswordBytes.Clear(folder);
            }
        }

        Reload();
        return _service.UnlockedWithoutKey().Count == 0 && _service.OpenCount == 0;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_app.IsQuitting)
        {
            e.Cancel = true;
            Hide();
            _app.NotifyHidden();
        }
        base.OnClosing(e);
    }

    private async void OnAdd(object sender, RoutedEventArgs e) => await AddFolderAsync(null);

    private async void OnLock(object sender, RoutedEventArgs e) => await LockSelectedAsync();

    private async void OnUnlock(object sender, RoutedEventArgs e) => await UnlockSelectedAsync();

    private async void OnOpenItem(object sender, MouseButtonEventArgs e)
    {
        var row = VaultList.SelectedItem as VaultRow;
        if (row == null)
            return;
        if (row.State.StartsWith("Verrouillé", StringComparison.Ordinal))
            await UnlockSelectedAsync();
        else if (row.State.StartsWith("Déverrouillé", StringComparison.Ordinal))
            await LockSelectedAsync();
    }

    private async void OnFolderPassword(object sender, RoutedEventArgs e)
    {
        var row = Selected();
        if (row == null)
            return;
        var record = _service.Store.FindById(row.Id);
        if (record == null)
            return;

        byte[]? current = null;
        if (!_service.IsOpen(record.Id))
        {
            var prompt = new PasswordWindow("Mot de passe actuel", "Mot de passe du dossier ou mot de passe maître.");
            prompt.Owner = this;
            if (prompt.ShowDialog() != true)
                return;
            current = PasswordBytes.From(prompt.Password);
            prompt.Clear();
        }

        var choice = MessageBox.Show(
            "Voulez-vous définir un mot de passe propre ? Non retire le mot de passe du dossier. Le maître pourra toujours l'ouvrir.",
            Brand.Name,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel)
        {
            PasswordBytes.Clear(current);
            return;
        }

        byte[]? next = null;
        if (choice == MessageBoxResult.Yes)
        {
            var prompt = new PasswordWindow("Mot de passe du dossier", "Au moins 4 caractères.", confirm: true);
            prompt.Owner = this;
            if (prompt.ShowDialog() != true)
            {
                PasswordBytes.Clear(current);
                return;
            }
            next = PasswordBytes.From(prompt.Password);
            prompt.Clear();
        }

        var id = record.Id;
        await RunBusy(() => _service.SetFolderPassword(id, current, next));
        PasswordBytes.Clear(current);
        PasswordBytes.Clear(next);
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        var row = Selected();
        if (row == null)
            return;
        var confirm = MessageBox.Show(
            "Retirer la protection laisse le dossier en clair. S'il est verrouillé, il sera d'abord déverrouillé.",
            Brand.Name,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK)
            return;

        if (row.State.StartsWith("Verrouillé", StringComparison.Ordinal))
        {
            if (!await UnlockSelectedAsync())
                return;
        }

        await RunBusy(() => _service.RemoveProtection(row.Id));
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_service, _app) { Owner = this };
        window.ShowDialog();
    }

    private async void OnQuit(object sender, RoutedEventArgs e) => await _app.QuitAsync();

    private async Task AddFolderAsync(string? preset)
    {
        if (!_app.Gate.TryBegin())
        {
            MessageBox.Show("Une opération est déjà en cours.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var path = preset;
            if (path == null)
            {
                using var dialog = new Forms.FolderBrowserDialog
                {
                    Description = "Choisir le dossier à verrouiller",
                    UseDescriptionForTitle = true
                };
                if (dialog.ShowDialog() != Forms.DialogResult.OK)
                    return;
                path = dialog.SelectedPath;
            }

            var existing = _service.Store.FindByOriginalPath(path);
            if (existing != null)
            {
                MessageBox.Show($"{Path.GetFileName(path)} est déjà dans la liste ({_service.Describe(existing)}).", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                VaultList.SelectedItem = VaultList.Items.OfType<VaultRow>().FirstOrDefault(row => row.Id == existing.Id);
                return;
            }

            var confirm = MessageBox.Show(
                "Le dossier sera chiffré puis masqué. Vous pourrez le restaurer avec le mot de passe.\n\n" + path,
                Brand.Name,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (confirm != MessageBoxResult.OK)
                return;

            var master = _app.CopyMaster();
            byte[]? folder = null;
            try
            {
                if (master == null)
                {
                    var prompt = new PasswordWindow("Mot de passe maître", "Saisissez le mot de passe maître pour chiffrer ce dossier.");
                    prompt.Owner = this;
                    if (prompt.ShowDialog() != true)
                        return;
                    master = PasswordBytes.From(prompt.Password);
                    prompt.Clear();
                }

                if (MessageBox.Show(
                        "Ajouter un mot de passe propre à ce dossier ? Le mot de passe maître pourra toujours l'ouvrir.",
                        Brand.Name,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    var prompt = new PasswordWindow("Mot de passe du dossier", "Au moins 4 caractères.", confirm: true);
                    prompt.Owner = this;
                    if (prompt.ShowDialog() != true)
                        return;
                    folder = PasswordBytes.From(prompt.Password);
                    prompt.Clear();
                }

                IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;
                var masterCopy = master;
                var folderCopy = folder;
                await Task.Run(() => _service.LockNew(path, masterCopy, folderCopy));
                _app.RememberMaster(masterCopy);
                Reload();
            }
            finally
            {
                PasswordBytes.Clear(master);
                PasswordBytes.Clear(folder);
                Mouse.OverrideCursor = null;
                IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            _app.Gate.End();
        }
    }

    private async Task LockSelectedAsync()
    {
        var row = Selected();
        if (row == null)
            return;
        if (row.State.StartsWith("Verrouillé", StringComparison.Ordinal))
        {
            MessageBox.Show("Ce dossier est déjà verrouillé.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_service.IsOpen(row.Id))
        {
            await RunBusy(() => _service.LockOpen(row.Id));
            return;
        }

        var masterPrompt = new PasswordWindow("Mot de passe maître", "La clé n'est plus en mémoire. Saisissez le mot de passe maître pour reverrouiller.");
        masterPrompt.Owner = this;
        if (masterPrompt.ShowDialog() != true)
            return;
        var master = PasswordBytes.From(masterPrompt.Password);
        masterPrompt.Clear();
        byte[]? folder = null;
        var record = _service.Store.FindById(row.Id);
        try
        {
            if (record?.HasFolderPassword == true &&
                MessageBox.Show("Saisir aussi le mot de passe propre pour le conserver ?", Brand.Name, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                var prompt = new PasswordWindow("Mot de passe du dossier", "Au moins 4 caractères.", confirm: true);
                prompt.Owner = this;
                if (prompt.ShowDialog() != true)
                    return;
                folder = PasswordBytes.From(prompt.Password);
                prompt.Clear();
            }

            if (await RunBusy(() => _service.Relock(row.Id, master, folder)))
                _app.RememberMaster(master);
        }
        finally
        {
            PasswordBytes.Clear(master);
            PasswordBytes.Clear(folder);
        }
    }

    private async Task<bool> UnlockSelectedAsync()
    {
        var row = Selected();
        if (row == null)
            return false;
        if (row.State.StartsWith("Déverrouillé", StringComparison.Ordinal))
        {
            MessageBox.Show("Ce dossier est déjà déverrouillé.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        var prompt = new PasswordWindow("Déverrouiller", "Mot de passe du dossier, ou mot de passe maître.");
        prompt.Owner = this;
        if (prompt.ShowDialog() != true)
            return false;
        var password = PasswordBytes.From(prompt.Password);
        prompt.Clear();
        var remember = false;
        try
        {
            var ok = await RunBusy(() =>
            {
                _service.Unlock(row.Id, password);
                remember = _service.Store.CheckMaster(password);
            });
            if (ok && remember)
                _app.RememberMaster(password);
            return ok;
        }
        finally
        {
            PasswordBytes.Clear(password);
        }
    }

    private async Task<bool> RunBusy(Action action)
    {
        if (!_app.Gate.TryBegin())
        {
            MessageBox.Show("Une opération est déjà en cours.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            await Task.Run(action);
            Reload();
            return true;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            Reload();
            return false;
        }
        finally
        {
            Mouse.OverrideCursor = null;
            IsEnabled = true;
            _app.Gate.End();
        }
    }

    private VaultRow? Selected()
    {
        if (VaultList.SelectedItem is VaultRow row)
            return row;
        MessageBox.Show("Sélectionnez un dossier.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private static void ShowError(Exception exception)
    {
        var message = exception is VaultException ? exception.Message : "Opération impossible.\n" + exception.Message;
        MessageBox.Show(message, Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

public sealed class VaultRow
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Location { get; init; }
    public required string State { get; init; }
    public required string OwnPassword { get; init; }
}
