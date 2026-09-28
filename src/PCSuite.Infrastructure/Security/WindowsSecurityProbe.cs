using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PCSuite.Core.Security;

namespace PCSuite.Infrastructure.Security;

// Task 012 / parte 12.2: implementación nativa de ISecurityProbe — TODO de
// SOLO LECTURA y sin elevación: WscGetSecurityProviderHealth (wscapi.dll),
// WMI (Defender y SecurityCenter2), registro (firewall/UAC/RDP) e
// IPGlobalProperties + GetExtendedTcpTable/UdpTable (iphlpapi) para puertos y
// su PID. Cada helper degrada a null/vacío ante CUALQUIER fallo (contrato del
// probe): la página de Seguridad siempre se renderiza, jamás lanza.
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
        Ports: ReadPorts());

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
            // WMI puede lanzar ManagementException/COMException/UnauthorizedAccess: degradación.
            return null;
        }
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

            var ports = new List<ListeningPort>();
            foreach (var endpoint in properties.GetActiveTcpListeners())
            {
                ports.Add(ToPort("TCP", endpoint, pids));
            }

            foreach (var endpoint in properties.GetActiveUdpListeners())
            {
                ports.Add(ToPort("UDP", endpoint, pids));
            }

            return ports;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static ListeningPort ToPort(string protocol, IPEndPoint endpoint, IReadOnlyDictionary<string, int> pids)
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

        return new ListeningPort(protocol, endpoint.Address.ToString(), endpoint.Port,
            pid > 0 ? pid : null, process);
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

    private const int TcpTableOwnerPidListener = 3; // MIB_TCP_TABLE_CLASS
    private const int UdpTableOwnerPid = 1;          // MIB_UDP_TABLE_CLASS

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
