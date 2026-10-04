using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Verrdoss.Core;
using Forms = System.Windows.Forms;

namespace Verrdoss.App;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private VaultService? _service;
    private byte[]? _masterPassword;
    private Forms.NotifyIcon? _tray;
    private MainWindow? _window;
    private DispatcherTimer? _idleTimer;
    private DispatcherTimer? _explorerTimer;
    private object? _shellApp;
    private bool _balloonShown;
    private bool _idleNoticeShown;
    private ShellCommand? _pending;

    public OperationGate Gate { get; } = new();
    public bool IsQuitting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, Brand.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        if (HasFlag(e.Args, "--unregister"))
        {
            ShellRegistration.Unregister();
            StartupRegistration.Disable();
            Shutdown();
            return;
        }

        if (HasFlag(e.Args, "--register-shell"))
        {
            ShellRegistration.Register();
            Shutdown();
            return;
        }

        var packAt = Array.FindIndex(e.Args, arg => string.Equals(arg, "--pack-shell", StringComparison.OrdinalIgnoreCase));
        if (packAt >= 0 && packAt + 1 < e.Args.Length)
        {
            ShellRegistration.PrepareLayout(e.Args[packAt + 1]);
            Environment.Exit(0);
            return;
        }

        var startup = HasFlag(e.Args, "--startup");
        var shell = ParseShellCommand(e.Args);
        if (!AcquireMutex())
        {
            if (shell != null)
                SingleInstance.Send(shell.Value.Verb, shell.Value.Path);
            Shutdown();
            return;
        }

        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Verrdoss");
        try
        {
            _service = new VaultService(data, AppContext.BaseDirectory);
            var iconPath = Path.Combine(data, "lock.ico");
            LockIcon.Write(iconPath);
            _service.LockIconPath = iconPath;
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
                _service.ShellExecutable = Environment.ProcessPath;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Brand.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        IReadOnlyList<string> recovery;
        try
        {
            recovery = RecoveryService.Recover(_service.Store);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Brand.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        try
        {
            Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        }
        catch (InvalidOperationException)
        {
        }

        ShellRegistration.Register();
        SingleInstance.Listen(line => Dispatcher.InvokeAsync(() =>
        {
            var request = ParsePipe(line);
            if (request == null)
                return;
            if (_window == null)
                _pending = request;
            else
                Dispatch(request.Value);
        }));

        if (!_service.Store.HasMaster)
        {
            var setup = new SetupWindow();
            if (setup.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            var created = PasswordBytes.From(setup.Password);
            try
            {
                _service.Store.SetMaster(created, _service.Kdf);
                RememberMaster(created);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Brand.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
            finally
            {
                PasswordBytes.Clear(created);
            }
        }

        _window = new MainWindow(_service, this);
        if (!startup)
            _window.Show();
        CreateTray();
        StartIdle();
        StartExplorerWatch();
        _ = CheckUpdatesQuietly();
        if (recovery.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, recovery.Distinct()), Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        if (shell != null)
            Dispatch(shell.Value);
        else if (_pending != null)
            Dispatch(_pending.Value);
    }

    private void Dispatch(ShellCommand request)
    {
        if (_window == null)
            return;
        if (request.Verb == "unlock")
            _window.UnlockFromShell(request.Path);
        else
            _window.LockFromShell(request.Path);
    }

    public void RememberMaster(byte[] password)
    {
        PasswordBytes.Clear(_masterPassword);
        _masterPassword = (byte[])password.Clone();
    }

    public byte[]? CopyMaster() => _masterPassword == null ? null : (byte[])_masterPassword.Clone();

    public void NotifyHidden()
    {
        if (_balloonShown || _tray == null)
            return;
        _balloonShown = true;
        _tray.ShowBalloonTip(4000, Brand.Name, "VerrDoss reste actif. Utilisez Quitter pour verrouiller les dossiers ouverts et fermer.", Forms.ToolTipIcon.Info);
    }

    public async Task QuitAsync()
    {
        if (_service == null)
        {
            ShutdownApplication();
            return;
        }

        if (!Gate.TryBegin())
        {
            MessageBox.Show("Une opération est déjà en cours.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var errors = await Task.Run(() => _service.LockAllInSession());
            _window?.Reload();
            if (errors.Count > 0)
            {
                MessageBox.Show("Fermeture annulée." + Environment.NewLine + string.Join(Environment.NewLine, errors), Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_window != null && !await _window.RelockMissingAsync())
            {
                MessageBox.Show("Fermeture annulée : un dossier est encore déverrouillé.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ShutdownApplication();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (!IsQuitting)
                Gate.End();
        }
    }

    private void ShutdownApplication()
    {
        IsQuitting = true;
        _idleTimer?.Stop();
        _explorerTimer?.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        PasswordBytes.Clear(_masterPassword);
        _masterPassword = null;
        Shutdown();
    }

    private void StartExplorerWatch()
    {
        _explorerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _explorerTimer.Tick += (_, _) => WatchExplorer();
        _explorerTimer.Start();
    }

    private void WatchExplorer()
    {
        if (_service == null || _window == null || _window.UnlockPromptActive || IsQuitting)
            return;
        try
        {
            _shellApp ??= Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            dynamic shell = _shellApp!;
            foreach (dynamic window in shell.Windows())
            {
                string url;
                try { url = (string)window.LocationURL; }
                catch { continue; }
                var path = ToFilePath(url);
                if (path == null)
                    continue;
                var record = _service.Store.FindByOriginalPath(path);
                if (record == null || !_service.Describe(record).StartsWith("Verrouillé", StringComparison.Ordinal))
                    continue;
                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                {
                    try { window.Navigate(new Uri(parent).AbsoluteUri); }
                    catch { }
                }
                _window.UnlockFromShell(path);
                return;
            }
        }
        catch
        {
        }
    }

    private static string? ToFilePath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            return new Uri(url).LocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private void StartIdle()
    {
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _idleTimer.Tick += async (_, _) => await OnIdleAsync();
        _idleTimer.Start();
    }

    private async Task OnIdleAsync()
    {
        if (_service == null || IsQuitting)
            return;
        if (!IdlePolicy.ShouldLock(IdleTime.Get(), _service.Store.Data.IdleMinutes, _service.OpenCount))
        {
            _idleNoticeShown = false;
            return;
        }
        if (!Gate.TryBegin())
            return;

        try
        {
            var errors = await Task.Run(() => _service.LockAllInSession());
            _window?.Reload();
            if (_tray == null || _idleNoticeShown)
                return;
            _idleNoticeShown = true;
            if (errors.Count > 0)
                _tray.ShowBalloonTip(5000, Brand.Name, "Certains dossiers n'ont pas pu être verrouillés. Un fichier est peut-être encore ouvert.", Forms.ToolTipIcon.Warning);
            else
                _tray.ShowBalloonTip(3000, Brand.Name, "Dossiers verrouillés après inactivité.", Forms.ToolTipIcon.Info);
        }
        finally
        {
            Gate.End();
        }
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir", null, (_, _) => Dispatcher.Invoke(ShowMain));
        menu.Items.Add("Quitter", null, (_, _) => Dispatcher.Invoke(async () => await QuitAsync()));
        _tray = new Forms.NotifyIcon
        {
            Icon = CreateIcon(),
            Visible = true,
            Text = Brand.Name,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
    }

    private void ShowMain()
    {
        if (_window == null)
            return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private bool AcquireMutex()
    {
        try
        {
            _mutex = new Mutex(true, @"Local\Verrdoss.SingleInstance", out var created);
            return created;
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private async Task CheckUpdatesQuietly()
    {
        try
        {
            var result = await UpdateChecker.CheckAsync();
            if (result.Offer == null || _tray == null)
                return;
            _tray.ShowBalloonTip(5000, Brand.Name, result.Message + " Ouvrez les paramètres pour l'installer.", Forms.ToolTipIcon.Info);
        }
        catch (Exception)
        {
        }
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));

    private static ShellCommand? ParseShellCommand(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--lock" || args[i] == "--unlock") && i + 1 < args.Length)
                return new ShellCommand(args[i] == "--unlock" ? "unlock" : "lock", args[i + 1]);
        }
        return null;
    }

    private static ShellCommand? ParsePipe(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        var tab = line.IndexOf('\t');
        if (tab <= 0 || tab >= line.Length - 1)
            return new ShellCommand("lock", line.Trim());
        return new ShellCommand(line[..tab], line[(tab + 1)..].Trim());
    }

    private readonly record struct ShellCommand(string Verb, string Path);

    private static Icon CreateIcon()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var extracted = Icon.ExtractAssociatedIcon(path);
            if (extracted != null)
                return extracted;
        }

        return SystemIcons.Application;
    }
}
