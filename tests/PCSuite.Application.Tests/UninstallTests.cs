using System.Text.Json;
using Microsoft.Win32;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Operations;
using PCSuite.Core.Uninstall;
using PCSuite.Infrastructure.Uninstall;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests del desinstalador forzado (Task 020): catÃ¡logo del registro de
/// desinstalaciÃ³n, validador de rutas/identificadores, split del comando
/// oficial y las operaciones por el puente. Con entrada REAL de HKCU
/// (creada y limpia por el propio test) y elevaciÃ³n falsa (el UAC jamÃ¡s
/// se simula).
/// </summary>
public sealed class UninstallTests : IDisposable
{
    private const string TestKeyName = "PCSuiteUninstallTestEntry";

    /// <summary>RaÃ­z HKCU de desinstalaciÃ³n â€” escribible sin elevaciÃ³n.</summary>
    private static readonly string HkcuUninstall =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly UninstallCatalog _catalog = new();

    public void Dispose()
    {
        DeleteTestEntry();
    }

    // ------------------------------------------------------------------
    // CatÃ¡logo
    // ------------------------------------------------------------------

    [Fact]
    public void ListApps_RealMachine_ReturnsEntriesWithValidKeyPaths()
    {
        var apps = _catalog.ListApps();

        Assert.NotEmpty(apps); // cualquier Windows real tiene apps instaladas
        Assert.All(apps, app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.DisplayName));
            Assert.True(UninstallCatalog.IsAllowedKeyPath(app.RegistryKeyPath),
                $"Ruta fuera de las raÃ­ces permitidas: {app.RegistryKeyPath}");
        });
        // Orden alfabÃ©tico estable.
        var sorted = apps.Select(a => a.DisplayName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(sorted, apps.Select(a => a.DisplayName));
    }

    [Fact]
    public void GetApp_HkcuEntry_CreatedByTest_ReadsAllFields()
    {
        CreateTestEntry(msiGuid: false);
        var keyPath = $@"{HkcuUninstall}\{TestKeyName}";

        var app = _catalog.GetApp(keyPath);

        Assert.NotNull(app);
        Assert.Equal("App de Prueba PCSuite", app!.DisplayName);
        Assert.Equal("AES Studio Test", app.Publisher);
        Assert.Equal("9.9.9", app.DisplayVersion);
        Assert.Null(app.MsiGuid); // no MSI
        Assert.Contains(keyPath, _catalog.ListApps().Select(a => a.RegistryKeyPath));
    }

    [Fact]
    public void GetApp_MsiGuidEntry_ExtractsGuid()
    {
        CreateTestEntry(msiGuid: true);
        var keyPath = $@"{HkcuUninstall}\{{A1B2C3D4-E5F6-4789-ABCD-1234567890AB}}";

        var app = _catalog.GetApp(keyPath);

        Assert.NotNull(app);
        Assert.Equal("{a1b2c3d4-e5f6-4789-abcd-1234567890ab}", app!.MsiGuid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\X\..\Y")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\BadService")]
    [InlineData(@"HKEY_USERS\Something\Uninstall\App")]
    public void GetApp_InvalidKeyPath_ReturnsNull(string keyPath)
    {
        Assert.Null(_catalog.GetApp(keyPath));
        Assert.False(UninstallCatalog.IsAllowedKeyPath(keyPath));
    }

    // ------------------------------------------------------------------
    // Validador
    // ------------------------------------------------------------------

    [Fact]
    public void IsSafeFolder_RejectsSystemRoots()
    {
        Assert.False(UninstallValidator.IsSafeFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.False(UninstallValidator.IsSafeFolder(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        Assert.False(UninstallValidator.IsSafeFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)));
        Assert.False(UninstallValidator.IsSafeFolder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.False(UninstallValidator.IsSafeFolder("C:\\"));
        Assert.False(UninstallValidator.IsSafeFolder("App\\Relativa"));
        Assert.False(UninstallValidator.IsSafeFolder(""));
    }

    [Fact]
    public void IsSafeFolder_AcceptsSubfolders()
    {
        Assert.True(UninstallValidator.IsSafeFolder(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "App")));
        Assert.True(UninstallValidator.IsSafeFolder(Path.Combine(Path.GetTempPath(), "App")));
    }

    [Fact]
    public void IsSafeRegistryKey_Rules()
    {
        Assert.True(UninstallValidator.IsSafeRegistryKey($@"{HkcuUninstall}\App"));
        Assert.True(UninstallValidator.IsSafeRegistryKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\App"));
        Assert.False(UninstallValidator.IsSafeRegistryKey(@"HKEY_LOCAL_MACHINE\SOFTWARE")); // la raÃ­z JAMÃS
        Assert.False(UninstallValidator.IsSafeRegistryKey(@"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\X"));
        Assert.False(UninstallValidator.IsSafeRegistryKey(@"HKEY_CURRENT_USER\Software\X\..\Y"));
        Assert.False(UninstallValidator.IsSafeRegistryKey(""));
    }

    [Fact]
    public void IsSafeIdentifier_Rules()
    {
        Assert.True(UninstallValidator.IsSafeIdentifier("DriverHub Service"));
        Assert.True(UninstallValidator.IsSafeIdentifier("svc.exe.x64"));
        Assert.False(UninstallValidator.IsSafeIdentifier("a/b"));
        Assert.False(UninstallValidator.IsSafeIdentifier("..\\.."));
        Assert.False(UninstallValidator.IsSafeIdentifier(""));
    }

    [Fact]
    public void IsSafeTaskPath_Rules()
    {
        Assert.True(UninstallValidator.IsSafeTaskPath("\\DriverHub\\Update"));
        Assert.False(UninstallValidator.IsSafeTaskPath("Update"));
        Assert.False(UninstallValidator.IsSafeTaskPath("\\..\\Update"));
        Assert.False(UninstallValidator.IsSafeTaskPath(""));
    }

    [Fact]
    public void IsPathInside_SegmentWiseNotPrefixWise()
    {
        Assert.True(UninstallValidator.IsPathInside(
            @"C:\Program Files\App\bin\svc.exe", @"C:\Program Files\App"));
        Assert.False(UninstallValidator.IsPathInside(
            @"C:\Program Files\AppOtro\svc.exe", @"C:\Program Files\App"));
        Assert.True(UninstallValidator.IsPathInside(
            @"C:\Program Files\App", @"C:\Program Files\App"));
        Assert.False(UninstallValidator.IsPathInside("", @"C:\Program Files\App"));
    }

    // ------------------------------------------------------------------
    // SplitCommand (desinstalador oficial)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\unins.exe\" /S /silent", "C:\\Program Files\\App\\unins.exe", "/S /silent")]
    [InlineData("C:\\App\\unins.exe", "C:\\App\\unins.exe", "")]
    [InlineData("MsiExec.exe /I{GUID}", "MsiExec.exe", "/I{GUID}")]
    [InlineData("", "", "")]
    public void SplitCommand_QuotesAndArguments(string command, string expectedExe, string expectedArgs)
    {
        var (executable, arguments) = UninstallRunOfficialElevatedOperation.SplitCommand(command);
        Assert.Equal(expectedExe, executable);
        Assert.Equal(expectedArgs, arguments);
    }

    [Fact]
    public async Task RunOfficialElevated_MissingEntry_FailsControlled()
    {
        var operation = new UninstallRunOfficialElevatedOperation();
        var context = new OperationContext { Payload = JsonSerializer.SerializeToElement(
            new UninstallKeyPayload
            {
                RegistryKeyPath = $@"{HkcuUninstall}\NoExiste",
            }) };

        var result = await operation.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.UninstallFailed, result.ErrorCode);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task RunOfficialElevated_BrokenUninstallString_FailsControlled()
    {
        // App cuyo UninstallString apunta a un exe que NO existe: el helper
        // se niega a lanzar algo roto.
        CreateTestEntry(msiGuid: false, uninstallString: "C:\\Ruta\\Inexistente\\unins.exe /S");
        var context = new OperationContext { Payload = JsonSerializer.SerializeToElement(
            new UninstallKeyPayload
            {
                RegistryKeyPath = $@"{HkcuUninstall}\{TestKeyName}",
            }) };

        var operation = new UninstallRunOfficialElevatedOperation();
        var result = await operation.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.UninstallFailed, result.ErrorCode);
    }

    // ------------------------------------------------------------------
    // Limpieza elevada (directa: HKCU + %TEMP% no requieren admin)
    // ------------------------------------------------------------------

    [Fact]
    public async Task CleanupElevated_DeletesApprovedFolderAndHkcuKey()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pcsuite-uninstall-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "residuo.txt"), "x");

        var keyPath = @"HKEY_CURRENT_USER\Software\PCSuiteUninstallTestResidue";
        using (Registry.CurrentUser.CreateSubKey("Software\\PCSuiteUninstallTestResidue"))
        {
            // crear y cerrar
        }

        var operation = new UninstallCleanupElevatedOperation();
        var payload = JsonSerializer.SerializeToElement(new CleanupPayload
        {
            Items =
            [
                new CleanupItemPayload { Kind = "folder", Value = folder },
                new CleanupItemPayload { Kind = "registrykey", Value = keyPath },
            ],
        });
        var result = await operation.ExecuteAsync(
            new OperationContext { Payload = payload }, CancellationToken.None);

        Assert.True(result.Success);
        var dto = JsonSerializer.Deserialize<RemovalOutcomeDto>(result.Data!.Value.GetRawText(), BridgeJson.Options);
        Assert.NotNull(dto);
        Assert.All(dto!.Results, item => Assert.True(item.Succeeded, $"{item.Identifier}: {item.Detail}"));
        Assert.False(Directory.Exists(folder));
        Assert.Null(Registry.CurrentUser.OpenSubKey("Software\\PCSuiteUninstallTestResidue"));
    }

    [Fact]
    public async Task CleanupElevated_RefusesProtectedPaths()
    {
        var operation = new UninstallCleanupElevatedOperation();
        var payload = JsonSerializer.SerializeToElement(new CleanupPayload
        {
            Items =
            [
                new CleanupItemPayload { Kind = "folder", Value = Environment.GetFolderPath(Environment.SpecialFolder.Windows) },
                new CleanupItemPayload { Kind = "folder", Value = "C:\\" },
                new CleanupItemPayload { Kind = "registrykey", Value = @"HKEY_LOCAL_MACHINE\SOFTWARE" },
                new CleanupItemPayload { Kind = "service", Value = "..\\bad" },
                new CleanupItemPayload { Kind = "scheduledtask", Value = "SinRuta" },
            ],
        });

        var result = await operation.ExecuteAsync(
            new OperationContext { Payload = payload }, CancellationToken.None);

        Assert.True(result.Success); // la operaciÃ³n completa; los Ã­tems fallan
        var dto = JsonSerializer.Deserialize<RemovalOutcomeDto>(result.Data!.Value.GetRawText(), BridgeJson.Options);
        Assert.NotNull(dto);
        Assert.All(dto!.Results, item => Assert.False(item.Succeeded, $"{item.Identifier} NO debÃ­a tocarse"));
        Assert.All(dto.Results, item => Assert.False(string.IsNullOrWhiteSpace(item.Detail)));
    }

    // ------------------------------------------------------------------
    // Operaciones por el puente (router + elevaciÃ³n falsa)
    // ------------------------------------------------------------------

    [Fact]
    public async Task UninstallList_ThroughBridge_CamelCase()
    {
        CreateTestEntry(msiGuid: false);
        var router = CreateRouter();

        var response = await Send(router, "l1", OperationIds.UninstallList);

        Assert.True(response.Success);
        var apps = response.Result!.Value.GetProperty("apps").EnumerateArray().ToList();
        Assert.Contains(apps, app =>
            app.GetProperty("displayName").GetString() == "App de Prueba PCSuite");
        Assert.All(apps, app =>
            Assert.False(string.IsNullOrWhiteSpace(app.GetProperty("registryKeyPath").GetString())));
    }

    [Fact]
    public async Task UninstallDiscover_ThroughBridge_PlanWithDerivedEntry()
    {
        CreateTestEntry(msiGuid: false);
        var router = CreateRouter();

        var response = await Send(
            router,
            "d1",
            OperationIds.UninstallDiscover,
            JsonSerializer.Serialize(new
            {
                registryKeyPath = $@"{HkcuUninstall}\{TestKeyName}",
            }));

        Assert.True(response.Success);
        var plan = response.Result!.Value;
        Assert.True(plan.GetProperty("hasOfficialUninstaller").GetBoolean());
        Assert.Equal("App de Prueba PCSuite", plan.GetProperty("app").GetProperty("displayName").GetString());
        var items = plan.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item =>
            item.GetProperty("tier").GetString() == "derived"
            && item.GetProperty("kind").GetString() == "registrykey");
    }

    [Fact]
    public async Task UninstallDiscover_UnknownKey_FailsWithoutElevation()
    {
        var router = CreateRouter(elevation: new FakeElevationManager());

        var response = await Send(
            router,
            "d2",
            OperationIds.UninstallDiscover,
            JsonSerializer.Serialize(new { registryKeyPath = @"HKEY_CURRENT_USER\Software\NoExiste" }));

        Assert.False(response.Success);
        Assert.NotEqual(BridgeErrorCodes.InternalError, response.Error!.Code);
    }

    [Fact]
    public async Task RunOfficial_MissingEntry_NoUacRequested()
    {
        // La clave no existe: NUNCA debe abrirse el prompt (validaciÃ³n
        // previa a la elevaciÃ³n).
        var router = CreateRouter(elevation: new FakeElevationManager());

        var response = await Send(
            router,
            "o1",
            OperationIds.UninstallRunOfficial,
            JsonSerializer.Serialize(new { registryKeyPath = $@"{HkcuUninstall}\NoExiste" }));

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.UninstallFailed, response.Error!.Code);
    }

    [Fact]
    public async Task RunOfficial_ExistingEntry_ElevationCarriesOnlyKeyPath()
    {
        CreateTestEntry(msiGuid: false);
        var elevation = new FakeElevationManager();
        var router = CreateRouter(elevation: elevation);

        var response = await Send(
            router,
            "o2",
            OperationIds.UninstallRunOfficial,
            JsonSerializer.Serialize(new { registryKeyPath = $@"{HkcuUninstall}\{TestKeyName}" }));

        Assert.True(response.Success); // la elevaciÃ³n falsa acepta
        var request = Assert.Single(elevation.Requests);
        Assert.Equal(ElevatedOperationIds.UninstallRunOfficial, request.OperationId);
        // El payload solo porta la clave â€” el comando NUNCA viaja.
        var payload = JsonSerializer.Deserialize<UninstallKeyPayload>(
            request.Payload!.Value.GetRawText(), BridgeJson.Options);
        Assert.Equal($@"{HkcuUninstall}\{TestKeyName}", payload!.RegistryKeyPath);
    }

    [Fact]
    public async Task Cleanup_AllItemsInvalid_NoUacRequested()
    {
        var elevation = new FakeElevationManager();
        var router = CreateRouter(elevation: elevation);

        var response = await Send(
            router,
            "c1",
            OperationIds.UninstallCleanup,
            JsonSerializer.Serialize(new
            {
                items = new[]
                {
                    new { kind = "folder", value = @"C:\Windows" },
                },
            }));

        // Todo invÃ¡lido â†’ error controlado SIN pedir UAC.
        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.UninstallFailed, response.Error!.Code);
        Assert.Empty(elevation.Requests);
    }

    [Fact]
    public async Task Cleanup_ValidHkcuKey_ElevationCarriesFilteredItems()
    {
        var elevation = new FakeElevationManager();
        var router = CreateRouter(elevation: elevation);

        var response = await Send(
            router,
            "c2",
            OperationIds.UninstallCleanup,
            JsonSerializer.Serialize(new
            {
                items = new object[]
                {
                    new { kind = "registrykey", value = @"HKEY_CURRENT_USER\Software\PCSuite\Test" },
                    new { kind = "folder", value = @"C:\Windows" }, // filtrado antes del UAC
                },
            }));

        Assert.True(response.Success);
        var request = Assert.Single(elevation.Requests);
        Assert.Equal(ElevatedOperationIds.UninstallCleanup, request.OperationId);
        var payload = JsonSerializer.Deserialize<CleanupPayload>(
            request.Payload!.Value.GetRawText(), BridgeJson.Options);
        var item = Assert.Single(payload!.Items); // solo el vÃ¡lido cruzÃ³
        Assert.Equal("registrykey", item.Kind);
    }

    [Theory]
    [InlineData(OperationIds.UninstallDiscover, "\"no soy objeto\"")]
    [InlineData(OperationIds.UninstallRunOfficial, "123")]
    [InlineData(OperationIds.UninstallCleanup, "\"texto\"")]
    public async Task NonObjectPayload_FailsWithoutInternalError(string operation, string payloadJson)
    {
        var router = CreateRouter();

        var response = await Send(router, "x1", operation, payloadJson);

        Assert.False(response.Success);
        Assert.NotEqual(BridgeErrorCodes.InternalError, response.Error!.Code);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    [Fact]
    public async Task UninstallOperations_NeverRequireBridgeElevation()
    {
        var router = CreateRouter();

        // Las 4 ops declaran RequiresElevation=false: la elevaciÃ³n es UAC
        // puntual dentro del servicio, no del puente.
        var operations = new IOperation[]
        {
            new UninstallListOperation(),
            new UninstallDiscoverOperation(),
            new UninstallRunOfficialOperation(new FakeElevationManager()),
            new UninstallCleanupOperation(new FakeElevationManager()),
        };

        Assert.All(operations, operation => Assert.False(operation.RequiresElevation));
        Assert.Equal(OperationIds.UninstallList, operations[0].Id);
        Assert.Equal(OperationIds.UninstallDiscover, operations[1].Id);
        Assert.Equal(OperationIds.UninstallRunOfficial, operations[2].Id);
        Assert.Equal(OperationIds.UninstallCleanup, operations[3].Id);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static CommandRouter CreateRouter(IElevationManager? elevation = null)
    {
        var manager = elevation ?? new FakeElevationManager();
        return new CommandRouter(
            new IOperation[]
            {
                new UninstallListOperation(),
                new UninstallDiscoverOperation(),
                new UninstallRunOfficialOperation(manager),
                new UninstallCleanupOperation(manager),
            },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandRouter>.Instance);
    }

    private static Task<BridgeResponse> Send(
        CommandRouter router, string id, string operation, string? payloadJson = null)
    {
        var payload = payloadJson is null ? string.Empty : $",\"payload\":{payloadJson}";
        return router.RouteAsync($"{{\"id\":\"{id}\",\"operation\":\"{operation}\"{payload}}}");
    }

    private static void CreateTestEntry(
        bool msiGuid,
        string uninstallString = "\"C:\\Program Files\\App\\unins000.exe\" /silent")
    {
        DeleteTestEntry();
        var subKey = msiGuid
            ? "{A1B2C3D4-E5F6-4789-ABCD-1234567890AB}"
            : TestKeyName;
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + subKey);
        key.SetValue("DisplayName", "App de Prueba PCSuite");
        key.SetValue("Publisher", "AES Studio Test");
        key.SetValue("DisplayVersion", "9.9.9");
        key.SetValue("UninstallString", uninstallString);
        key.SetValue("EstimatedSize", 12345);
    }

    private static void DeleteTestEntry()
    {
        TryDelete($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{TestKeyName}");
        TryDelete(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1B2C3D4-E5F6-4789-ABCD-1234567890AB}");
        TryDelete(@"Software\PCSuiteUninstallTestResidue");
    }

    private static void TryDelete(string subKey)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // best-effort de limpieza
        }
    }
}
