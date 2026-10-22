using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PCSuite.Application;
using PCSuite.Application.Developers;
using PCSuite.Application.Jobs;
using PCSuite.Application.Operations;
using PCSuite.Application.Tools;
using PCSuite.Contracts;
using PCSuite.Core.Events;
using PCSuite.Desktop.Bridge;
using PCSuite.Infrastructure.Notifications;
using PCSuite.Blazor;
using PCSuite.Blazor.Components;
using PCSuite.Infrastructure;
using PCSuite.Infrastructure.ClamAV;
using PCSuite.Infrastructure.Cleaner;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Elevation;
using PCSuite.Infrastructure.Logging;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.PGP;
using PCSuite.Infrastructure.Processes;
using PCSuite.Infrastructure.SSH;
using PCSuite.Infrastructure.Security;
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

    // Piloto Blazor Hybrid (Task 015)
    private readonly ServiceProvider _blazorServices;
    private readonly ClamAvState _clamAvState;
    private readonly BlazorWebView _blazorWebView;
    private readonly WindowsToastNotifier _toastNotifier;

    public MainForm()
    {
        InitializeComponent();

        // WinForms (.NET 10) no usa el icono del exe para la ventana: se
        // asigna explícitamente el mismo PCSuite.ico (recurso embebido).
        var appIcon = LoadAppIcon();
        if (appIcon is not null)
        {
            Icon = appIcon;
        }

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

        // Publisher de eventos DIFERIDO: los servicios lo reciben en el acto
        // y el WebViewBridge se conecta al final del arranque (el router
        // necesita los servicios y el bridge necesita el router). Antes este
        // orden pasaba _bridge null a ClamAV: los eventos de progreso de los
        // escaneos jamás llegaban a la UI.
        var events = new DeferredEventPublisher();

        // Jobs (larga duración): ciclo de vida, cancelación y eventos hacia
        // el mismo publisher. Los escaneos clamav.* lo usan desde las ops.
        var jobManager = new JobManager(new BridgeJobEventPublisher(events));

        // ClamAV (Task 008): orquestación de clamscan/freshclam; el mismo
        // elevationManager cubre freshclam vía la allow-list de Task 005 y la
        // instalación delega en wingetService (paquete fijo Cisco.ClamAV).
        var clamAvService = new ClamAvService(
            locator,
            processRunner,
            elevationManager,
            wingetService,
            new ClamAvHistoryStore(),
            _loggerFactory.CreateLogger<ClamAvService>(),
            installDirectories: null,
            events: events);

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

        // Certificados (Task 019): X.509 autofirmado, inspección universal,
        // conversión PEM↔DER y firma PGP de archivos — todo BouncyCastle en
        // proceso, sin elevación. Reutiliza el keyring de PgpService para
        // firmar. Sin logger de contenido: solo rutas y metadatos públicos.
        var certificateService = new PCSuite.Infrastructure.Certificates.CertificateService(
            pgpService,
            _loggerFactory.CreateLogger<PCSuite.Infrastructure.Certificates.CertificateService>());

        // Limpiador (Task 011): escaneo y borrado SOLO en raíces fijas del
        // sistema (§16 "Temporary file cleanup"); sin procesos ni elevación —
        // lo que la ACL no permite se omite y se contabiliza.
        var cleanerService = new CleanerService();

        // Seguridad (Task 012): Security Center de solo lectura (§14) —
        // Defender, firewall, antivirus, puertos y diagnóstico; sin elevación
        // y sin ninguna acción de modificación.
        var securityService = new SecurityService();

        // Local Share (Task 016): servicio de transferencia de archivos en LAN.
        // elevationManager solo se usa para la regla de firewall (UAC puntual);
        // las transferencias corren con privilegios normales de usuario.
        // events publica a la UI (deferred) los eventos de dispositivos,
        // solicitudes entrantes, progreso y emparejamiento.
        var localShareService = new PCSuite.Infrastructure.LocalShare.LocalShareService(
            PCSuitePaths.DataDirectory,
            _loggerFactory.CreateLogger<PCSuite.Infrastructure.LocalShare.LocalShareService>(),
            elevation: elevationManager,
            events: events);

        // Actualizaciones (Task 017.3/17.4): manifiesto público + SHA-256 en
        // streaming. update.install cierra la app tras lanzar el Setup.
        var updateService = new PCSuite.Infrastructure.Updates.PcsuiteUpdateService();

        // ------------------------------------------------------------------
        // Task 021: notificaciones de Windows (Centro de Notificaciones).
        // Regla de oro (ToastDecider): si la app tiene el foco, NADA de
        // toasts — los banners internos ya cubren ese caso. El toast existe
        // para lo que pasa cuando estás en otra cosa.
        // ------------------------------------------------------------------
        _toastNotifier = new WindowsToastNotifier(
            isAppFocused: () => Focused || ContainsFocus || Form.ActiveForm == this,
            bringToFront: () =>
            {
                BeginInvoke(() =>
                {
                    if (WindowState == FormWindowState.Minimized)
                    {
                        WindowState = FormWindowState.Normal;
                    }

                    Activate();
                    BringToFront();
                });
            });

        // Local Share: lo que llega cuando nadie está mirando la app.
        localShareService.IncomingRequest += (transfer, items) =>
        {
            var isMessage = items.Any(item => item.IsMessage);
            _toastNotifier.Show(new ToastInput(
                Kind: isMessage ? ToastKind.IncomingMessage : ToastKind.IncomingFiles,
                AppFocused: false, // lo decide Show() al momento del evento
                PeerName: transfer.PeerDisplayName,
                ItemCount: transfer.ItemCount,
                TotalBytes: transfer.TotalBytes));
        };

        localShareService.PairRequested += request =>
        {
            _toastNotifier.Show(new ToastInput(
                Kind: ToastKind.PairRequested,
                AppFocused: false,
                PeerName: request.PeerDisplayName));
        };

        localShareService.TransferFinished += transfer =>
        {
            // Solo el lado receptor: el emisor está por definición en la app.
            if (transfer.Direction != PCSuite.Core.LocalShare.TransferDirection.Receiving
                || transfer.Status != PCSuite.Core.LocalShare.TransferStatus.Completed)
            {
                return;
            }

            // Mensaje: el texto quedó en la transferencia (se leyó al publicar).
            if (transfer.MessageText is not null)
            {
                _toastNotifier.Show(new ToastInput(
                    Kind: ToastKind.MessageReceived,
                    AppFocused: false,
                    PeerName: transfer.PeerDisplayName,
                    MessageText: transfer.MessageText));
                return;
            }

            _toastNotifier.Show(new ToastInput(
                Kind: ToastKind.FilesReceived,
                AppFocused: false,
                PeerName: transfer.PeerDisplayName,
                ItemCount: transfer.ItemCount));
        };

        // ClamAV: amenazas halladas al terminar un escaneo.
        clamAvService.ScanCompleted += (outcome, kind) =>
        {
            if (outcome.InfectedFiles > 0)
            {
                _toastNotifier.Show(new ToastInput(
                    Kind: ToastKind.ThreatsFound,
                    AppFocused: false,
                    ThreatCount: outcome.InfectedFiles));
            }
        };

        // Update disponible al arrancar: check diferido y silencioso (el
        // frontend hace el suyo; este es el que puede toalear). Fire-and-forget.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                var check = await updateService.CheckAsync().ConfigureAwait(false);
                if (check.Available)
                {
                    _toastNotifier.Show(new ToastInput(
                        Kind: ToastKind.UpdateAvailable,
                        AppFocused: false,
                        Version: check.LatestVersion));
                }
            }
            catch
            {
                // sin red / firma ausente: sin drama — el badge interno
                // reportará lo suyo cuando el usuario abra la app.
            }
        });

        _router = new CommandRouter(
            [
                new GetSystemInfoOperation(),
                new SystemPickFilesOperation(this),
                new SystemPickFolderOperation(this),
                new ClipboardWriteTextOperation(),
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
                new ClamAvQuickScanOperation(clamAvService, jobManager),
                new ClamAvQuickTargetsOperation(),
                new ClamAvFullScanOperation(clamAvService, jobManager),
                new ClamAvCustomScanOperation(clamAvService, jobManager),
                new ClamAvHistoryOperation(clamAvService),
                new ClamAvActiveScanOperation(jobManager),
                new JobGetOperation(jobManager),
                new JobCancelOperation(jobManager),
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
                new CleanerScanOperation(cleanerService),
                new CleanerCleanOperation(cleanerService),
                new SecurityGetStatusOperation(securityService),
                new ExplorerOpenPathOperation(),
                new ExplorerOpenUrlOperation(),
                new BlazorToggleOperation(ToggleBlazor),
                new LocalShareStatusOperation(localShareService),
                new LocalShareStartOperation(localShareService),
                new LocalShareStopOperation(localShareService),
                new LocalShareDevicesOperation(localShareService),
                new LocalSharePairOperation(localShareService),
                new LocalSharePairAcceptOperation(localShareService),
                new LocalSharePairRejectOperation(localShareService),
                new LocalSharePairCancelOperation(localShareService),
                new LocalShareSendOperation(localShareService),
                new LocalShareSendMessageOperation(localShareService),
                new LocalShareAcceptOperation(localShareService),
                new LocalShareRejectOperation(localShareService),
                new LocalShareTransfersOperation(localShareService),
                new LocalShareCancelTransferOperation(localShareService),
                new LocalShareConfigOperation(localShareService),
                new LocalShareClearHistoryOperation(localShareService),
                new LocalShareRevokeOperation(localShareService),
                new LocalShareFirewallStatusOperation(localShareService),
                new LocalShareFirewallRuleOperation(localShareService),
                new LocalSharePauseTransferOperation(localShareService),
                new LocalShareResumeTransferOperation(localShareService),
                new UpdateCheckOperation(updateService),
                new UpdateDownloadOperation(updateService, jobManager),
                new UpdateInstallOperation(updateService, onInstalled: () =>
                {
                    // El Setup ya quedó lanzado: cerrar la app para dejarle el
                    // paso (él pide UAC si la instalación lo requiere).
                    BeginInvoke(Close);
                }),
                new CertX509GenerateOperation(certificateService),
                new CertInspectOperation(certificateService),
                new CertConvertOperation(certificateService),
                new CertPgpSignFileOperation(certificateService),
                new CertCsrGenerateOperation(certificateService),
                new CertPgpVerifyFileOperation(certificateService),
                new UninstallListOperation(),
                new UninstallDiscoverOperation(),
                new UninstallRunOfficialOperation(elevationManager),
                new UninstallCleanupOperation(elevationManager),
            ],
            _loggerFactory.CreateLogger<CommandRouter>());
        _bridge = new WebViewBridge(_router, _loggerFactory.CreateLogger<WebViewBridge>());
        events.Attach(_bridge);

        // Local Share auto-arranque (16.7+): si el usuario lo activó, el
        // servicio levanta con la app — detección y recepción activas sin
        // tocar la UI. Fire-and-forget: el arranque nunca bloquea la ventana
        // y los errores quedan en el log (no en un diálogo en la cara).
        if (localShareService.Settings.AutoStartService)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await localShareService.StartAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _loggerFactory.CreateLogger<MainForm>()
                        .LogError(ex, "Auto-arranque de Local Share falló");
                }
            });
        }

        // Piloto Blazor Hybrid (Task 015): servicio de estado observable
        // con suscripción directa a ClamAvService — sin IPC.
        var services = new ServiceCollection();
        // CRÍTICO: registra la infraestructura de Blazor (JSRuntime,
        // NavigationManager, Dispatcher, etc.). Sin esto los componentes
        // Razor no renderizan → pantalla blanca.
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton<PCSuite.Core.Jobs.IJobManager>(jobManager);
        services.AddSingleton<PCSuite.Core.ClamAV.IClamAvService>(clamAvService);
        services.AddSingleton<ClamAvState>();
        _blazorServices = services.BuildServiceProvider();
        _clamAvState = _blazorServices.GetRequiredService<ClamAvState>();

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);

        // Piloto Blazor (Task 015): un segundo WebView que renderiza Razor
        // components en vez de React. Toggle con Ctrl+Shift+B.
        // Try-catch: un fallo del piloto NO debe tumbar la app — solo
        // deshabilita el toggle.
        try
        {
            var blazorHost = new BlazorHost();
            _blazorWebView = new BlazorWebView
            {
                Dock = DockStyle.Fill,
                HostPage = blazorHost.HostPagePath,
                Services = _blazorServices,
                Visible = false,
            };
            _blazorWebView.RootComponents.Add(
                new RootComponent("#app", typeof(PCSuite.Blazor.Components.ClamavPage), null));
            Controls.Add(_blazorWebView);

            // Ctrl+Shift+B alterna entre React y Blazor
            KeyPreview = true;
            KeyDown += OnPilotKeyDown;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Piloto Blazor: no se pudo inicializar. La app continúa sin él.");
            _blazorWebView = null!;
        }

        _logger.LogInformation("PCSuite {Version} starting.", AppInfo.Version);
    }

    /// <summary>Toggle del piloto Blazor: intercambia los dos WebViews.</summary>
    private void OnPilotKeyDown(object? sender, KeyEventArgs e)
    {
        if (_blazorWebView is null) return;

        if (e.Control && e.Shift && e.KeyCode == Keys.B)
        {
            e.SuppressKeyPress = true;
            ToggleBlazor();
        }
    }

    /// <summary>Muestra/oculta el BlazorWebView. También lo llama el botón.</summary>
    public void ToggleBlazor()
    {
        // Piloto bloqueado (Task 015.2): el componente Razor no renderiza.
        // Se registra el estado en el log para no perder trazabilidad.
        _logger.LogWarning(
            "Piloto Blazor: bloqueado (Task 015.2) — el RootComponent no monta. " +
            "Pendiente: depurar con DevTools del BlazorWebView, verificar versión " +
            "del paquete para net10.0. La app funciona normalmente sin el piloto.");
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

    /// <summary>
    /// Icono de la ventana (title bar + taskbar): WinForms en .NET 10 no
    /// hereda el ApplicationIcon del exe para la ventana — se carga el
    /// mismo PCSuite.ico desde el recurso embebido, pidiendo el tamaño
    /// mayor disponible para que Windows escoja la variante justa por
    /// contexto (16 title bar, 24/32 taskbar, Alt-Tab…).
    /// </summary>
    private static System.Drawing.Icon? LoadAppIcon()
    {
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("PCSuite.ico");
            return stream is null ? null : new System.Drawing.Icon(stream, new System.Drawing.Size(256, 256));
        }
        catch
        {
            return null; // sin icono la ventana sigue siendo totalmente usable
        }
    }

    private async Task InitializeWebViewAsync()
    {

        // Perfil de WebView2 por usuario en %LocalAppData%: una instalación
        // en Program Files no puede escribir junto al .exe (la app corre
        // sin elevar, §12) y una carpeta portable no debe contaminarse
        // con caches/cookies. Sin esto, WebView2 falla al inicializar en
        // una instalación estándar.
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCSuite",
            "WebView2");
        Directory.CreateDirectory(userDataFolder);

        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await _webView.EnsureCoreWebView2Async(environment);

        var core = _webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.NewWindowRequested += (_, args) => args.Handled = true;

        // Task 026.3: drop NATIVO de archivos del SO — el host toma el
        // destino OLE del contenido de WebView2 y publica las RUTAS reales
        // por evento system.filesDropped (la página no puede verlas:
        // limitación de Chromium). No-op silencioso si el árbol de ventanas
        // cambia en un runtime futuro: queda el aviso honesto de la página.
        NativeFileDropTarget.Attach(
            _webView.Handle,
            paths => _bridge.SendEvent(
                EventIds.SystemFilesDropped,
                new SystemFilesDroppedPayload { Paths = paths }));

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
