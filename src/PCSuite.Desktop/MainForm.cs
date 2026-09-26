using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PCSuite.Application;
using PCSuite.Application.Developers;
using PCSuite.Application.Tools;
using PCSuite.Contracts;
using PCSuite.Desktop.Bridge;
using PCSuite.Infrastructure;
using PCSuite.Infrastructure.ClamAV;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Elevation;
using PCSuite.Infrastructure.Logging;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.PGP;
using PCSuite.Infrastructure.Processes;
using PCSuite.Infrastructure.SSH;
using PCSuite.Infrastructure.Tools;
using PCSuite.Infrastructure.WinGet;
using PCSuite.Infrastructure.Wsl;

namespace PCSuite.Desktop;

/// <summary>
/// Main window. Hosts WebView2, wires the message bridge to the command
/// router and loads the local React frontend. Runs unprivileged.
/// </summary>
public partial class MainForm : Form
{
    private const string VirtualHost = "app.pcsuite.local";

    private const string MissingFrontendHtml = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <title>PCSuite</title>
          <style>
            body { font-family: "Segoe UI", system-ui, sans-serif; background: #0d1117; color: #e6edf3;
                   display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }
            .box { text-align: center; max-width: 32rem; padding: 2rem; }
            code { background: #161b22; color: #4c8dff; padding: .15rem .4rem; border-radius: 4px; }
            h1 { font-weight: 600; }
            p { color: #8b949e; line-height: 1.6; }
          </style>
        </head>
        <body>
          <div class="box">
            <h1>Frontend not built</h1>
            <p>Build the React frontend and restart PCSuite:</p>
            <p><code>npm --prefix frontend install</code><br /><code>npm --prefix frontend run build</code></p>
          </div>
        </body>
        </html>
        """;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MainForm> _logger;
    private readonly CommandRouter _router;
    private readonly WebViewBridge _bridge;
    private readonly WebView2 _webView;
    private readonly List<string> _allowedOrigins = [];

    public MainForm()
    {
        InitializeComponent();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(PCSuitePaths.LogDirectory));
        });

        _logger = _loggerFactory.CreateLogger<MainForm>();

        // Tool Framework (Task 002): registro + búsqueda; la UI descubre
        // las tools por metadatos y nunca las tiene hardcodeadas.
        var toolRegistry = new ToolRegistry();
        toolRegistry.RegisterModule(new ToolsModule());
        var toolSearch = new ToolSearchService(toolRegistry);

        // Developer Providers (Task 004): un único runner sin shell sirve a
        // los 5 proveedores; los ids registrados aquí son los únicos que la
        // UI puede invocar vía developer.*.
        var processRunner = new ProcessRunner();
        var locator = new ExecutableLocator();
        var developerRegistry = new DeveloperToolRegistry();
        developerRegistry.Register(new NodeProvider(processRunner, locator));
        developerRegistry.Register(new NpmProvider(processRunner, locator));
        developerRegistry.Register(new PnpmProvider(processRunner, locator));
        developerRegistry.Register(new BunProvider(processRunner, locator));
        developerRegistry.Register(new ClaudeCodeProvider(processRunner, locator));

        // WinGet (Task 006): la app corre SIEMPRE sin privilegios; ElevationManager
        // solo se usa para elevación POR OPERACIÓN (allow-list de Task 005) cuando
        // una mutación winget falla por permisos (el prompt UAC lo genera Windows).
        var elevationManager = new ElevationManager(
            processRunner,
            Path.Combine(AppContext.BaseDirectory, "PCSuite.Elevated.exe"),
            logger: _loggerFactory.CreateLogger<ElevationManager>());
        var wingetService = new WinGetService(
            locator,
            processRunner,
            elevationManager,
            _loggerFactory.CreateLogger<WinGetService>());

        // WSL (Task 007): payloads cero y argv fijos (§12/§53); el mismo
        // elevationManager cubre wsl --update vía la allow-list de Task 005.
        var wslService = new WslService(
            locator,
            processRunner,
            elevationManager,
            _loggerFactory.CreateLogger<WslService>());

        // ClamAV (Task 008): orquestación de clamscan/freshclam; el mismo
        // elevationManager cubre freshclam vía la allow-list de Task 005 y la
        // instalación delega en wingetService (paquete fijo Cisco.ClamAV).
        var clamAvService = new ClamAvService(
            locator,
            processRunner,
            elevationManager,
            wingetService,
            new ClamAvHistoryStore(),
            _loggerFactory.CreateLogger<ClamAvService>());

        // SSH (Task 009): solo la generación invoca ssh-keygen con argv fijo
        // (sin privilegios); listado, inspección, fingerprint e importación son
        // parseo .NET puro sobre ~/.ssh (nunca se ejecuta nada para leer claves).
        var sshKeyService = new SshKeyService(
            locator,
            processRunner,
            _loggerFactory.CreateLogger<SshKeyService>());

        // PGP (Task 009): 100% BouncyCastle en proceso (nunca gpg ni procesos
        // externos); el keyring vive en %APPDATA%\PCSuite\pgp. Sin logger a
        // propósito: contraseña, texto plano y clave privada no se registran.
        var pgpService = new PgpService(new PgpKeyringStore());

        _router = new CommandRouter(
            [
                new GetSystemInfoOperation(),
                new ToolsListOperation(toolRegistry),
                new ToolsSearchOperation(toolSearch),
                new ToolExecuteOperation(toolRegistry),
                new DeveloperListOperation(developerRegistry),
                new DeveloperCheckUpdateOperation(developerRegistry),
                new DeveloperUpdateOperation(developerRegistry),
                new DeveloperLaunchOperation(developerRegistry),
                new WinGetDetectOperation(wingetService),
                new WinGetSearchOperation(wingetService),
                new WinGetListOperation(wingetService),
                new WinGetUpdatesOperation(wingetService),
                new WinGetInstallOperation(wingetService),
                new WinGetUpgradeOperation(wingetService),
                new WinGetUpgradeAllOperation(wingetService),
                new WinGetUninstallOperation(wingetService),
                new WslStatusOperation(wslService),
                new WslListOperation(wslService),
                new WslUpdateOperation(wslService),
                new WslShutdownOperation(wslService),
                new WslStartOperation(wslService),
                new ClamAvStatusOperation(clamAvService),
                new ClamAvInstallOperation(clamAvService),
                new ClamAvUpdateDefinitionsOperation(clamAvService),
                new ClamAvQuickScanOperation(clamAvService),
                new ClamAvFullScanOperation(clamAvService),
                new ClamAvCustomScanOperation(clamAvService),
                new ClamAvHistoryOperation(clamAvService),
                new SshDetectOperation(sshKeyService),
                new SshListOperation(sshKeyService),
                new SshGenerateOperation(sshKeyService),
                new SshInspectOperation(sshKeyService),
                new SshImportPublicKeyOperation(sshKeyService),
                new SshOpenDirectoryOperation(sshKeyService),
                new PgpGenerateKeyOperation(pgpService),
                new PgpListKeysOperation(pgpService),
                new PgpImportKeyOperation(pgpService),
                new PgpExportKeyOperation(pgpService),
                new PgpEncryptOperation(pgpService),
                new PgpDecryptOperation(pgpService),
                new PgpSignOperation(pgpService),
                new PgpVerifyOperation(pgpService),
            ],
            _loggerFactory.CreateLogger<CommandRouter>());
        _bridge = new WebViewBridge(_router, _loggerFactory.CreateLogger<WebViewBridge>());

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);

        _logger.LogInformation("PCSuite {Version} starting.", AppInfo.Version);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            await InitializeWebViewAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _logger.LogCritical("WebView2 runtime is not installed.");
            MessageBox.Show(
                this,
                "PCSuite requires the Microsoft Edge WebView2 Runtime.\n\n"
                    + "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and try again.",
                "PCSuite",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "WebView2 initialization failed.");
            MessageBox.Show(
                this,
                "PCSuite could not initialize the interface. See logs for details.",
                "PCSuite",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _logger.LogInformation("PCSuite stopping.");
        _loggerFactory.Dispose();
        base.OnFormClosed(e);
    }

    private async Task InitializeWebViewAsync()
    {
        await _webView.EnsureCoreWebView2Async();

        var core = _webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.NewWindowRequested += (_, args) => args.Handled = true;

        core.NavigationStarting += (_, args) =>
        {
            if (!IsAllowedNavigation(args.Uri))
            {
                _logger.LogWarning("Blocked navigation to {Uri}", args.Uri);
                args.Cancel = true;
            }
        };

        core.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
            {
                _bridge.SendEvent(
                    EventIds.AppReady,
                    new AppReadyEvent
                    {
                        AppVersion = AppInfo.Version,
                        Timestamp = DateTimeOffset.Now,
                    });
            }
        };

        var devUrl = Environment.GetEnvironmentVariable("PCSUITE_DEV_URL")?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(devUrl))
        {
            _allowedOrigins.Add(devUrl);
            _bridge.Attach(core, _allowedOrigins);
            _logger.LogInformation("Loading frontend from dev server {Url}", devUrl);
            core.Navigate(devUrl);
            return;
        }

        var distDirectory = FrontendLocator.FindDistDirectory();
        if (distDirectory is null)
        {
            _logger.LogError("Frontend build not found. Run 'npm --prefix frontend run build'.");
            core.NavigateToString(MissingFrontendHtml);
            return;
        }

        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            distDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

        var origin = $"https://{VirtualHost}";
        _allowedOrigins.Add(origin);
        _bridge.Attach(core, _allowedOrigins);
        _logger.LogInformation("Loading frontend from {Directory}", distDirectory);
        core.Navigate($"{origin}/index.html");
    }

    private bool IsAllowedNavigation(string uri)
    {
        // Placeholder page (no origins configured yet) allows anything it shows.
        if (_allowedOrigins.Count == 0)
        {
            return true;
        }

        if (uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _allowedOrigins.Any(origin => BridgeOriginPolicy.Matches(uri, origin));
    }
}
