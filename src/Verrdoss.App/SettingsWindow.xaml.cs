using System.Diagnostics;
using System.Windows;
using Verrdoss.Core;

namespace Verrdoss.App;

public partial class SettingsWindow : Window
{
    private readonly VaultService _service;
    private readonly App _app;
    private bool _startupReady;
    private UpdateOffer? _offer;

    public SettingsWindow(VaultService service, App app)
    {
        InitializeComponent();
        _service = service;
        _app = app;
        IdleInput.Text = service.Store.Data.IdleMinutes.ToString();
        StartupBox.IsChecked = StartupRegistration.IsEnabled();
        _startupReady = true;
        VersionText.Text = $"Version installée : {UpdateChecker.Current.ToString(3)}";
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (!_startupReady)
            return;
        if (StartupBox.IsChecked == true)
            StartupRegistration.Enable();
        else
            StartupRegistration.Disable();
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        _offer = null;
        UpdateStatus.Text = "Vérification…";
        IsEnabled = false;
        try
        {
            var result = await UpdateChecker.CheckAsync();
            UpdateStatus.Text = result.Message;
            _offer = result.Offer;
            InstallUpdateButton.Visibility = result.Offer == null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = ex.Message;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_offer == null)
            return;
        InstallUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Téléchargement…";
        try
        {
            var path = await UpdateChecker.DownloadAsync(_offer.Installer);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            await _app.QuitAsync();
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = ex.Message;
            InstallUpdateButton.IsEnabled = true;
        }
    }

    private void OnSaveIdle(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdleInput.Text.Trim(), out var minutes))
        {
            MessageBox.Show("Indiquez un nombre de minutes.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _service.Store.SetIdleMinutes(minutes);
            MessageBox.Show("Délai enregistré.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnChangeMaster(object sender, RoutedEventArgs e)
    {
        if (NewInput.Password.Length < 8)
        {
            MessageBox.Show("Le mot de passe de secours doit contenir au moins 8 caractères.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (NewInput.Password != ConfirmInput.Password)
        {
            MessageBox.Show("Les deux saisies ne correspondent pas.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var current = PasswordBytes.From(CurrentInput.Password);
        var next = PasswordBytes.From(NewInput.Password);
        IsEnabled = false;
        try
        {
            await Task.Run(() => _service.ChangeMasterPassword(current, next));
            _app.RememberMaster(next);
            CurrentInput.Password = "";
            NewInput.Password = "";
            ConfirmInput.Password = "";
            MessageBox.Show("Mot de passe de secours modifié.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            PasswordBytes.Clear(current);
            PasswordBytes.Clear(next);
            IsEnabled = true;
        }
    }
}
