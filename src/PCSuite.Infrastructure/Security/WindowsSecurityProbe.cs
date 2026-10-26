using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PCSuite.Core.Security;

namespace PCSuite.Infrastructure.Security;

// Task 012 / parte 12.2: implementación nativa de ISecurityProbe — TODO de
// SOLO LECTURA y sin elevación: WscGetSecurityProviderHealth (wscapi.dll),
// WMI (Defender y SecurityCenter2), registro (firewall/UAC/RDP) e
// IPGlobalProperties + GetExtendedTcpTable/UdpTable (iphlpapi) para puertos y
// su PID. Cada helper degrada a null/vacío ante CUALQUIER fallo (contrato del
// probe): la página de Seguridad siempre se renderiza, jamás lanza.
// [SupportedOSPlatform]: toda la API usada aquí es Windows-only por diseño
// (WSC/WMI/registro/iphlpapi); así CA1416 no avisa sitio a sitio.
[SupportedOSPlatform("windows")]
public sealed class WindowsSecurityProbe : ISecurityProbe
{
    // WSC_SECURITY_PROVIDER: firewall=0x1, antivirus=0x4 (la salud se pide por proveedor).
    private const int WscFirewall = 0x1;
    private const int WscAntivirus = 0x4;

    private const string FirewallPolicyPrefix =
        @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\";

    public RawSecurityReading ReadRaw() => new(
        FirewallHealth: ReadProviderHealth(WscFirewall),
        AntivirusHealth: ReadProviderHealth(WscAntivirus),
        Defender: ReadDefender(),
        AntivirusProducts: ReadAntivirusProducts(),
        Firewall: ReadFirewallProfiles(),
        System: ReadSystemFlags(),
        Ports: ReadPorts(),
        Connections: ReadConnections(),
        UdpEndpoints: ReadUdpEndpoints(),
        Audit: ReadAudit(),
        Discovery: ReadDiscovery());

    // --- Centro de seguridad (WSC) ---------------------------------------

    /// <summary>Salud del proveedor en el vocabulario fijo SecurityStatuses; null si WSC falla.</summary>
    private static string? ReadProviderHealth(int provider)
    {
        try
        {
            if (WscGetSecurityProviderHealth(provider, out var health) != 0)
            {
                return null;
            }

            return health switch
            {
                0 => SecurityStatuses.Good,
                1 => SecurityStatuses.NotMonitored,
                2 => SecurityStatuses.Poor,
                3 => SecurityStatuses.Snoozed,
                _ => null,
            };
        }
        catch (Exception)
        {
            // wscapi ausente o error HRESULT: "sin datos" (unknown en la UI).
            return null;
        }
    }

    [DllImport("wscapi.dll")]
    private static extern int WscGetSecurityProviderHealth(int providers, out int health);

    // --- WMI ---------------------------------------------------------------

    private static DefenderRaw? ReadDefender()
    {
        // Tier 1: WMI clásico, instantáneo — donde el namespace existe.
        var viaWmi = ReadDefenderViaWmi();
        if (viaWmi is not null)
        {
            return viaWmi;
        }

        // Tier 2 (Task 028.2): Windows 11 actuales ELIMINARON
        // root\Microsoft\Defender ("espacio de nombres no válido"). El
        // Centro de seguridad (root\SecurityCenter2, vivo desde Vista)
        // publica el estado del producto codificado en productState —
        // instantáneo. Get-MpComputerStatus daría el timestamp exacto de
        // firmas, pero arrancar PowerShell + el módulo de Defender cuesta
        // ~6 s por visita a la página: mala tara por un dato secundario.
        return ReadDefenderFromSecurityCenter();
    }

    private static DefenderRaw? ReadDefenderViaWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Defender",
                "SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureLastUpdated FROM MSFT_MpComputerStatus");
            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (item["AntivirusEnabled"] is not bool antivirus ||
                        item["RealTimeProtectionEnabled"] is not bool realtime)
                    {
                        return null; // propiedades inesperadas: se degrada a "sin datos"
                    }

                    DateTimeOffset? updated = item["AntivirusSignatureLastUpdated"] switch
                    {
                        DateTime stamp when stamp > DateTime.MinValue => new DateTimeOffset(
                            stamp.Kind == DateTimeKind.Unspecified
                                ? DateTime.SpecifyKind(stamp, DateTimeKind.Local)
                                : stamp),
                        _ => null,
                    };

                    return new DefenderRaw(antivirus, realtime, updated);
                }
            }

            return null; // sin filas: Defender WMI no disponible
        }
        catch (Exception)
        {
            // WMI puede lanzar ManagementException/COMException/UnauthorizedAccess:
            // en Windows 11 actuales es "espacio de nombres no válido" — el
            // tier 2 toma el relevo.
            return null;
        }
    }

    /// <summary>
    /// Tier 2: el estado de Defender registrado en el Centro de seguridad.
    /// El namespace existe desde Vista y sigue vivo: es la fuente que la
    /// página ya usa para listar los antivirus.
    /// </summary>
    private static DefenderRaw? ReadDefenderFromSecurityCenter()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\SecurityCenter2",
                "SELECT displayName, productState FROM AntiVirusProduct");
            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (item["displayName"] is not string name ||
                        !name.Contains("Defender", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // otros productos: la lista general los reporta
                    }

                    return item["productState"] is uint state ? DecodeDefenderState(state) : null;
                }
            }

            return null; // Defender no registrado en el Centro de seguridad
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Decodifica el productState del Centro de seguridad (esquema de facto,
    /// estable desde Vista, sin documentación oficial). Los 6 dígitos hex:
    /// byte 2 = escáner (10/11 activo; 01 en espera — otro AV tomó el
    /// control —; 00 desactivado); byte 3 = firmas (00 al día; 10
    /// desactualizadas). Antivirus y tiempo real comparten el byte: el
    /// Centro reporta UN solo estado por producto. Esquema desconocido →
    /// null, antes que mentir.
    /// </summary>
    internal static DefenderRaw? DecodeDefenderState(uint productState)
    {
        var hex = productState.ToString("X6");
        if (hex.Length != 6)
        {
            return null;
        }

        var scanner = hex.Substring(2, 2);
        if (scanner is not ("10" or "11" or "01" or "00"))
        {
            return null;
        }

        bool? upToDate = hex.Substring(4, 2) switch
        {
            "00" => true,
            "10" => false,
            _ => null,
        };

        var enabled = scanner is "10" or "11";
        return new DefenderRaw(enabled, enabled, null, upToDate);
    }

    private static IReadOnlyList<string>? ReadAntivirusProducts()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\SecurityCenter2",
                "SELECT displayName FROM AntiVirusProduct");
            using var results = searcher.Get();

            var names = new List<string>();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (item["displayName"] is string name && !string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
            }

            names.Sort(StringComparer.Ordinal); // orden fijo, determinista para la UI
            return names;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // --- Registro ----------------------------------------------------------

    private static FirewallRaw ReadFirewallProfiles() => new(
        DomainEnabled: ReadEnabled(FirewallPolicyPrefix + "DomainProfile", "EnableFirewall"),
        // StandardProfile es el perfil privado en la API de firewall de Windows.
        PrivateEnabled: ReadEnabled(FirewallPolicyPrefix + "StandardProfile", "EnableFirewall"),
        PublicEnabled: ReadEnabled(FirewallPolicyPrefix + "PublicProfile", "EnableFirewall"));

    private static SystemRaw ReadSystemFlags() => new(
        UacEnabled: ReadEnabled(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA"),
        RdpConnectionsDenied: ReadEnabled(@"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections"));

    /// <summary>DWORD del registro; null si la clave/valor falta o HKLM no se pudo leer.</summary>
    private static bool? ReadEnabled(string subKey, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(valueName) is int dword ? dword != 0 : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // --- Puertos -----------------------------------------------------------

    private static IReadOnlyList<ListeningPort> ReadPorts()
    {
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var pids = BuildListenerPidMap();
            var details = BuildProcessDetails([.. pids.Values.Distinct()]);

            var ports = new List<ListeningPort>();
            foreach (var endpoint in properties.GetActiveTcpListeners())
            {
                ports.Add(ToPort("TCP", endpoint, pids, details));
            }

            foreach (var endpoint in properties.GetActiveUdpListeners())
            {
                ports.Add(ToPort("UDP", endpoint, pids, details));
            }

            return ports;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static ListeningPort ToPort(
        string protocol,
        IPEndPoint endpoint,
        IReadOnlyDictionary<string, int> pids,
        IReadOnlyDictionary<int, PortProcessInfo> details)
    {
        pids.TryGetValue($"{endpoint.Address}:{endpoint.Port}", out var pid);

        string? process = null;
        if (pid > 0)
        {
            try
            {
                using var owner = Process.GetProcessById(pid);
                process = owner.ProcessName;
            }
            catch (Exception)
            {
                // Proceso terminado entre lecturas o sin permiso (p. ej. System): PID sin nombre.
            }
        }

        var detail = pid > 0 && details.TryGetValue(pid, out var found) ? found : null;
        return new ListeningPort(
            protocol, endpoint.Address.ToString(), endpoint.Port,
            pid > 0 ? pid : null, process, detail);
    }

    // --- Conexiones TCP establecidas (Task 029) ----------------------------

    /// <summary>
    /// Conexiones TCP en estado ESTABLISHED (la tabla OWNER_PID_CONNECTIONS
    /// de iphlpapi — lo que netstat -ano lee por debajo), cada una con su
    /// extremo remoto y el proceso dueño enriquecido (ruta + firma). Es la
    /// vista de "¿quién habla con quién?" para detectar conexiones no
    /// autorizadas.
    /// </summary>
    private static IReadOnlyList<ActiveConnection> ReadConnections()
    {
        try
        {
            var raw = new List<(string LocalAddress, int LocalPort, string RemoteAddress, int RemotePort, uint Pid)>();
            foreach (var family in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 })
            {
                ReadConnectionTable(family, raw);
            }

            if (raw.Count == 0)
            {
                return [];
            }

            var details = BuildProcessDetails([.. raw.Select(c => (int)c.Pid).Distinct()]);
            return [.. raw
                .Select(c => new ActiveConnection(
                    c.LocalAddress, c.LocalPort, c.RemoteAddress, c.RemotePort,
                    (int)c.Pid,
                    details.TryGetValue((int)c.Pid, out var found) ? found : null))
                .OrderBy(c => c.RemoteAddress, StringComparer.Ordinal)
                .ThenBy(c => c.RemotePort)
                .ThenBy(c => c.LocalPort)];
        }
        catch (Exception)
        {
            return []; // iphlpapi/WMI caen: la página se renderiza igual
        }
    }

    private static void ReadConnectionTable(
        AddressFamily family,
        List<(string LocalAddress, int LocalPort, string RemoteAddress, int RemotePort, uint Pid)> into)
    {
        var size = 0;
        if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, (int)family, TcpTableOwnerPidConnections, 0)
            is not (0 or 122) || size <= 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, (int)family, TcpTableOwnerPidConnections, 0) != 0)
            {
                return;
            }

            var isV4 = family == AddressFamily.InterNetwork;
            var rowSize = isV4 ? Marshal.SizeOf<MibTcpRowV4>() : Marshal.SizeOf<MibTcpRowV6>();
            var count = Marshal.ReadInt32(buffer);
            var row = buffer + sizeof(uint);
            for (var i = 0; i < count; i++, row += rowSize)
            {
                if (isV4)
                {
                    var r = Marshal.PtrToStructure<MibTcpRowV4>(row);
                    if (r.State != TcpStateEstablished || r.Pid == 0)                    {
                        continue;
                    }

                    into.Add((
                        new IPAddress(BitConverter.GetBytes(r.LocalAddr)).ToString(),
                        NetworkToHost(r.LocalPort),
                        new IPAddress(BitConverter.GetBytes(r.RemoteAddr)).ToString(),
                        NetworkToHost(r.RemotePort),
                        r.Pid));
                }
                else
                {
                    var r6 = Marshal.PtrToStructure<MibTcpRowV6>(row);
                    if (r6.State != TcpStateEstablished || r6.Pid == 0)
                    {
                        continue;
                    }

                    into.Add((
                        new IPAddress(r6.LocalAddr).ToString(),
                        NetworkToHost(r6.LocalPort),
                        new IPAddress(r6.RemoteAddr).ToString(),
                        NetworkToHost(r6.RemotePort),
                        r6.Pid));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Task 029: endpoints UDP VINCULADOS con proceso dueño enriquecido.
    /// UDP es connectionless — no hay "establecida", pero un puerto
    /// vinculado por un proceso sin firma corriendo de TEMP es el mismo
    /// patrón de sospecha que una TCP. La fuente es la misma
    /// GetExtendedUdpTable de iphlpapi (lo que ya usa BuildListenerPidMap),
    /// ahora enriquecida con ruta + firma.
    /// </summary>
    private static IReadOnlyList<UdpEndpoint> ReadUdpEndpoints()
    {
        try
        {
            var raw = new List<(string Address, int Port, uint Pid)>();
            foreach (var family in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 })
            {
                ReadUdpEndpointTable(family, raw);
            }

            if (raw.Count == 0)
            {
                return [];
            }

            var details = BuildProcessDetails([.. raw.Select(e => (int)e.Pid).Distinct()]);
            return [.. raw
                .Select(e => new UdpEndpoint(
                    e.Address, e.Port, (int)e.Pid,
                    details.TryGetValue((int)e.Pid, out var found) ? found : null))
                .OrderBy(e => e.LocalPort)
                .ThenBy(e => e.LocalAddress, StringComparer.Ordinal)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static void ReadUdpEndpointTable(AddressFamily family, List<(string Address, int Port, uint Pid)> into)
    {
        var size = 0;
        if (GetExtendedUdpTable(IntPtr.Zero, ref size, false, (int)family, UdpTableOwnerPid, 0)
            is not (0 or 122) || size <= 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(buffer, ref size, false, (int)family, UdpTableOwnerPid, 0) != 0)
            {
                return;
            }

            var isV4 = family == AddressFamily.InterNetwork;
            var rowSize = isV4 ? Marshal.SizeOf<MibUdpRowV4>() : Marshal.SizeOf<MibUdpRowV6>();
            var count = Marshal.ReadInt32(buffer);
            var row = buffer + sizeof(uint);
            for (var i = 0; i < count; i++, row += rowSize)
            {
                if (isV4)
                {
                    var r = Marshal.PtrToStructure<MibUdpRowV4>(row);
                    if (r.Pid > 0)
                    {
                        into.Add((new IPAddress(BitConverter.GetBytes(r.LocalAddr)).ToString(), NetworkToHost(r.LocalPort), r.Pid));
                    }
                }
                else
                {
                    var r6 = Marshal.PtrToStructure<MibUdpRowV6>(row);
                    if (r6.Pid > 0)
                    {
                        into.Add((new IPAddress(r6.LocalAddr).ToString(), NetworkToHost(r6.LocalPort), r6.Pid));
                    }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // --- Auditoría de persistencia y procesos (Task 030) -------------------

    /// <summary>
    /// Task 030: auditoría de persistencia (inicio automático) y procesos
    /// en ejecución que requieren revisión. Todo de SOLO LECTURA sin elevación.
    /// </summary>
    private static SecurityAudit ReadAudit()
    {
        var autostart = ReadAutostartEntries();
        var (total, flagged) = ReadProcessAudit();
        return new SecurityAudit(autostart, flagged, total);
    }

    /// <summary>
    /// Entradas de inicio automático: registro Run/RunOnce, carpeta Startup
    /// (usuario y común) y servicios con arranque automático. El vector nº1
    /// de persistencia de malware.
    /// </summary>
    private static IReadOnlyList<AutostartEntry> ReadAutostartEntries()
    {
        var entries = new List<AutostartEntry>();

        // --- Registro: Run/RunOnce (HKCU + HKLM + Wow6432Node) ---
        foreach (var (source, keyPath) in AutostartRegistryKeys())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(keyPath)
                    ?? Registry.LocalMachine.OpenSubKey(keyPath);
                if (key is null)
                {
                    continue;
                }

                foreach (var valueName in key.GetValueNames())
                {
                    if (key.GetValue(valueName) is string command && !string.IsNullOrWhiteSpace(command))
                    {
                        entries.Add(BuildAutostartEntry(source, valueName, command));
                    }
                }
            }
            catch (Exception)
            {
                // Clave protegida o inexistente: sigue con la siguiente.
            }
        }

        // --- Carpetas Startup (usuario + común) ---
        foreach (var (source, folder) in StartupFolders())
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    entries.Add(BuildAutostartEntry(source, Path.GetFileName(file), file));
                }
            }
            catch (Exception)
            {
                // Permisos: carpeta ilegible, seguimos.
            }
        }

        // --- Servicios con arranque automático (WMI) ---
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT Name, DisplayName, PathName FROM Win32_Service WHERE StartMode = 'Auto'");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (item["Name"] is string name
                        && item["PathName"] is string path
                        && !string.IsNullOrWhiteSpace(path))
                    {
                        entries.Add(new AutostartEntry(
                            "Servicio automático",
                            item["DisplayName"] as string ?? name,
                            path,
                            null, null, null));
                    }
                }
            }
        }
        catch (Exception)
        {
            // WMI caído: sin servicios — lo que ya está listado sirve.
        }

        return [.. entries.OrderBy(e => e.Source, StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.Ordinal)];
    }

    private static (string Source, string KeyPath)[] AutostartRegistryKeys() =>
    [
        ("Registro (HKCU Run)", @"Software\Microsoft\Windows\CurrentVersion\Run"),
        ("Registro (HKCU RunOnce)", @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
        ("Registro (HKLM Run)", @"Software\Microsoft\Windows\CurrentVersion\Run"),
        ("Registro (HKLM RunOnce)", @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
        ("Registro (HKLM Run 32-bit)", @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run"),
    ];

    private static (string Source, string Folder)[] StartupFolders() =>
    [
        ("Carpeta Startup (usuario)", Environment.GetFolderPath(Environment.SpecialFolder.Startup)),
        ("Carpeta Startup (común)", Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)),
    ];

    /// <summary>Construye la entrada con la ruta extraída y su firma.</summary>
    private static AutostartEntry BuildAutostartEntry(string source, string name, string command)
    {
        var path = ExtractExecutablePath(command);
        string? signer = null;
        if (path is not null && File.Exists(path))
        {
            signer = ReadSigner(path);
        }

        return new AutostartEntry(source, name, command, path, signer);
    }

    /// <summary>
    /// Extrae la ruta del ejecutable de un comando de registro/Startup:
    /// "C:\Program Files\app\app.exe" /arg → C:\Program Files\app\app.exe
    /// C:\app\app.exe /arg → C:\app\app.exe
    /// </summary>
    internal static string? ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();

        // Caso 1: entre comillas dobles — "C:\...\app.exe" args
        if (trimmed.StartsWith('"'))
        {
            var close = trimmed.IndexOf('"', 1);
            if (close > 1)
            {
                return trimmed[1..close];
            }
            return null;
        }

        // Caso 2: sin comillas — C:\...\app.exe args (la ruta termina en .exe)
        // Buscar la primera ocurrencia de .exe, .dll, .bat, .cmd, .scr, .vbs, .ps1
        var extensions = new[] { ".exe", ".dll", ".bat", ".cmd", ".scr", ".vbs", ".ps1", ".msi" };
        foreach (var ext in extensions)
        {
            var idx = trimmed.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                return trimmed[..(idx + ext.Length)];
            }
        }

        // Caso 3: solo la ruta (sin argumentos ni extensión conocida) — tomar hasta el primer espacio
        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    /// <summary>
    /// Auditoría de procesos: total corriendo + los que tienen marcadores
    /// de revisión (sin firma o en TEMP). Los procesos del sistema que
    /// protegen su ruta NO acusan — solo los legibles.
    /// </summary>
    private static (int Total, IReadOnlyList<FlaggedProcess> Flagged) ReadProcessAudit()
    {
        try
        {
            var processes = Process.GetProcesses();
            var pids = processes.Select(p => p.Id).ToList();
            foreach (var p in processes)
            {
                p.Dispose();
            }

            var details = BuildProcessDetails(pids);
            var tempRoot = Path.GetTempPath();
            var flagged = new List<FlaggedProcess>();

            foreach (var pid in pids)
            {
                if (!details.TryGetValue(pid, out var info) || info.ExecutablePath is null)
                {
                    continue;
                }

                var note = SecurityService.BuildReviewNote(info, tempRoot);
                if (note is not null)
                {
                    flagged.Add(new FlaggedProcess(pid, info.Name, info.ExecutablePath, info.Signer, note));
                }
            }

            return (pids.Count,
                [.. flagged.OrderBy(p => p.Name, StringComparer.Ordinal)]);
        }
        catch (Exception)
        {
            return (0, []);
        }
    }
    // --- Descubrimiento de actividad (Task 031) ------------------------------

    private static SecurityDiscovery ReadDiscovery() => new(
        ReadDnsCache(), ReadScheduledTasks(), ReadNetworkShares(), ReadHostsFile());

    private static IReadOnlyList<DnsCacheEntry> ReadDnsCache()
    {
        try
        {
            var entries = new List<(string Domain, ushort Type)>();
            if (DnsGetCacheDataTable(out var head) == 0)
            {
                var current = head;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (current != IntPtr.Zero)
                {
                    var entry = Marshal.PtrToStructure<DnsCacheEntryStruct>(current);
                    if (entry.Name is not null && seen.Add(entry.Name))
                    {
                        entries.Add((entry.Name, entry.Type));
                    }
                    current = entry.Next;
                }
            }

            return [.. entries
                .Select(e => new DnsCacheEntry(e.Domain, DnsTypeLabel(e.Type)))
                .OrderBy(e => e.Domain, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string DnsTypeLabel(ushort type) => type switch
    {
        1 => "A", 5 => "CNAME", 6 => "SOA", 12 => "PTR", 15 => "MX", 16 => "TXT",
        28 => "AAAA", 33 => "SRV", 65 => "HTTPS", _ => $"T{type}",
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsCacheEntryStruct
    {
        public IntPtr Next;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Name;
        public ushort Type;
        public ushort DataLength;
        public uint Flags;
    }

    [DllImport("dnsapi.dll", SetLastError = true)]
    private static extern int DnsGetCacheDataTable(out IntPtr ppEntry);

    private static IReadOnlyList<ScheduledTaskEntry> ReadScheduledTasks()
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/query /fo csv /v /nh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(start);
            if (process is null) return [];
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return ParseScheduledTasks(output);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static IReadOnlyList<ScheduledTaskEntry> ParseScheduledTasks(string csv)
    {
        var result = new List<ScheduledTaskEntry>();
        foreach (var rawLine in csv.Split('\n'))
        {
            var line = rawLine.Trim('\r');
            if (line.Length == 0 || line.StartsWith("\"HostName", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fields = SplitCsvLine(line);
            if (fields.Length < 9) continue;

            result.Add(new ScheduledTaskEntry(
                fields[1].Trim('\\', '"'), fields[3], fields[8], fields[2]));
        }
        return result;
    }

    internal static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return [.. fields];
    }

    private static IReadOnlyList<NetworkShareEntry> ReadNetworkShares()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2", "SELECT Name, Path, Description, Type FROM Win32_Share");
            using var results = searcher.Get();

            var shares = new List<NetworkShareEntry>();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (item["Name"] is string name)
                    {
                        shares.Add(new NetworkShareEntry(
                            name, item["Path"] as string, item["Description"] as string,
                            ShareTypeLabel(item["Type"])));
                    }
                }
            }
            return [.. shares.OrderBy(s => s.Name, StringComparer.Ordinal)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string ShareTypeLabel(object? typeValue)
    {
        if (typeValue is not uint type) return "Desconocido";
        return (type & 0x80000000) != 0
            ? $"Admin ({BaseShareType(type & 0x7FFFFFFF)})"
            : BaseShareType(type);
    }

    private static string BaseShareType(uint type) => type switch
    {
        0 => "Disco", 1 => "Impresora", 2 => "Dispositivo", 3 => "IPC", _ => $"Tipo {type}",
    };

    private static IReadOnlyList<HostsFileEntry> ReadHostsFile()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            if (!File.Exists(path)) return [];

            var entries = new List<HostsFileEntry>();
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    entries.Add(new HostsFileEntry(parts[0], string.Join(" ", parts[1..])));
                }
            }
            return entries;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>ruta del ejecutable y firma del certificado (si firma). Procesos del
    /// sistema/ protegidos devuelven lo que dejen leer — nunca lanza.
    /// </summary>
    private static Dictionary<int, PortProcessInfo> BuildProcessDetails(IReadOnlyList<int> pids)
    {
        var map = new Dictionary<int, PortProcessInfo>();
        if (pids.Count == 0)
        {
            return map;
        }

        // WMI Win32_Process: una consulta, TODAS las rutas de golpe.
        var paths = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT ProcessId, ExecutablePath FROM Win32_Process");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (item["ProcessId"] is string idText
                        && int.TryParse(idText, out var id)
                        && item["ExecutablePath"] is string path)
                    {
                        paths[id] = path;
                    }
                }
            }
        }
        catch (Exception)
        {
            // WMI caído: sin rutas — solo nombres y PIDs.
        }

        foreach (var pid in pids)
        {
            string? name = null;
            try
            {
                using var owner = Process.GetProcessById(pid);
                name = owner.ProcessName;
            }
            catch (Exception)
            {
                // Terminado o protegido: el PID queda, sin nombre.
            }

            string? path = null;
            string? signer = null;
            if (paths.TryGetValue(pid, out var executable) && File.Exists(executable))
            {
                path = executable;
                signer = ReadSigner(executable);
            }

            map[pid] = new PortProcessInfo(name, path, signer);
        }

        return map;
    }

    /// <summary>Nombre legible (CN) del certificado Authenticode del archivo;
    /// null si no firma o ilegible — "sin firma" es un HECHO útil para
    /// detectar sospechosos, no un error.</summary>
    private static string? ReadSigner(string path)
    {
        try
        {
            using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader
                .LoadCertificateFromFile(path);

            // Subject = "CN=Microsoft Windows, O=Microsoft Corporation, …":
            // solo el CN para la UI (el DN completo no cabe en la tabla).
            var cn = certificate.Subject
                .Split(',')
                .Select(part => part.Trim())
                .FirstOrDefault(part => part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
            return cn is null ? certificate.Subject : cn["CN=".Length..];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Mapa "dirección:puerto" → PID de los puertos en escucha (iphlpapi). Si
    /// ipphlpapi falla se devuelve el mapa vacío: los puertos se listan igual,
    /// sin PID/proceso (tolerante, nunca rompe la lectura).
    /// </summary>
    private static Dictionary<string, int> BuildListenerPidMap()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);

        try
        {
            foreach (var family in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 })
            {
                ReadTable(family, isTcp: true, map);
                ReadTable(family, isTcp: false, map);
            }
        }
        catch (Exception)
        {
            // iphlpapi no disponible o buffer inconsistente: mapa (parcial) sin PID.
        }

        return map;
    }

    private static void ReadTable(AddressFamily family, bool isTcp, Dictionary<string, int> map)
    {
        // 1ª llamada para medir el buffer (ERROR_INSUFFICIENT_BUFFER = 122).
        var size = 0;
        var measure = isTcp
            ? GetExtendedTcpTable(IntPtr.Zero, ref size, false, (int)family, TcpTableOwnerPidListener, 0)
            : GetExtendedUdpTable(IntPtr.Zero, ref size, false, (int)family, UdpTableOwnerPid, 0);

        if ((measure != 0 && measure != 122) || size <= 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var result = isTcp
                ? GetExtendedTcpTable(buffer, ref size, false, (int)family, TcpTableOwnerPidListener, 0)
                : GetExtendedUdpTable(buffer, ref size, false, (int)family, UdpTableOwnerPid, 0);

            if (result != 0)
            {
                return;
            }

            var isV4 = family == AddressFamily.InterNetwork;
            var rowSize = isTcp
                ? (isV4 ? Marshal.SizeOf<MibTcpRowV4>() : Marshal.SizeOf<MibTcpRowV6>())
                : (isV4 ? Marshal.SizeOf<MibUdpRowV4>() : Marshal.SizeOf<MibUdpRowV6>());

            var count = Marshal.ReadInt32(buffer);
            var row = buffer + sizeof(uint);
            for (var i = 0; i < count; i++, row += rowSize)
            {
                (var address, var port, var pid) = isTcp
                    ? ReadTcpRow(row, isV4)
                    : ReadUdpRow(row, isV4);

                if (port > 0 && pid > 0)
                {
                    map.TryAdd($"{address}:{port}", (int)pid); // el primero gana
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (string Address, int Port, uint Pid) ReadTcpRow(IntPtr row, bool isV4)
    {
        if (isV4)
        {
            var r = Marshal.PtrToStructure<MibTcpRowV4>(row);
            return (new IPAddress(BitConverter.GetBytes(r.LocalAddr)).ToString(), NetworkToHost(r.LocalPort), r.Pid);
        }

        var r6 = Marshal.PtrToStructure<MibTcpRowV6>(row);
        return (new IPAddress(r6.LocalAddr).ToString(), NetworkToHost(r6.LocalPort), r6.Pid);
    }

    private static (string Address, int Port, uint Pid) ReadUdpRow(IntPtr row, bool isV4)
    {
        if (isV4)
        {
            var r = Marshal.PtrToStructure<MibUdpRowV4>(row);
            return (new IPAddress(BitConverter.GetBytes(r.LocalAddr)).ToString(), NetworkToHost(r.LocalPort), r.Pid);
        }

        var r6 = Marshal.PtrToStructure<MibUdpRowV6>(row);
        return (new IPAddress(r6.LocalAddr).ToString(), NetworkToHost(r6.LocalPort), r6.Pid);
    }

    /// <summary>Puerto en orden de red (big-endian en la mitad baja del DWORD) a host.</summary>
    private static int NetworkToHost(uint networkPort) =>
        (int)(((networkPort & 0xFF) << 8) | ((networkPort >> 8) & 0xFF));

    private const int TcpTableOwnerPidListener = 3;    // MIB_TCP_TABLE_CLASS
    private const int TcpTableOwnerPidConnections = 4; // OWNER_PID_CONNECTIONS: no-listeners (ESTABLISHED, FIN_WAIT…)
    private const int UdpTableOwnerPid = 1;            // MIB_UDP_TABLE_CLASS
    private const uint TcpStateEstablished = 5;       // MIB_TCP_STATE

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable, ref int size, bool order, int family, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr udpTable, ref int size, bool order, int family, int tableClass, uint reserved);

    // Layouts MIB_*ROW_OWNER_PID (documentados en iphlpapi): todos campos de
    // 4 bytes o arrays; tamaños 24/56 (TCP) y 12/28 (UDP).
    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowV4
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint Pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowV6
    {
        public MibTcpRowV6()
        {
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr = [];
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr = [];
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint Pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowV4
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint Pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowV6
    {
        public MibUdpRowV6()
        {
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr = [];
        public uint LocalScopeId;
        public uint LocalPort;
        public uint Pid;
    }
}
