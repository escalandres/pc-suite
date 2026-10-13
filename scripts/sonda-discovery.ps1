# ============================================================================
# Sonda de diagnostico Local Share (independiente de la app)
#
# QUE HACE:
#   1. Se une al grupo multicast 239.255.67.85:5354 en TODAS las interfaces
#      IPv4 reales (igual que la app corregida).
#   2. Escucha durante N segundos e imprime CADA anuncio recibido
#      (IP de origen + JSON) — de la app del OTRO equipo o de otra sonda.
#   3. Envia cada 10s un anuncio "SONDA-DIAGNOSTICO": si el OTRO equipo
#      tiene PCSuite con Local Share activo, en su pagina de Dispositivos
#      deberia aparecer "SONDA-DIAGNOSTICO" (prueba de que SU receive va).
#   4. Recibe sus propios anuncios (loopback multicast) = sanity local.
#
# USO (PowerShell, ambos equipos):
#   powershell -ExecutionPolicy Bypass -File .\sonda-discovery.ps1 -Segundos 45
#
# LECTURA DE RESULTADOS:
#   - Veo los anuncios del otro equipo ......... red OK, el fallo era de la app
#   - Solo veo mis propios SONDA* ............. mi receive va; el otro no
#                                               anuncia (app cerrada ahi,
#                                               o su envio/la red fallan)
#   - No veo NADA (ni los mios) ............... multicast bloqueado local
#                                               (probable en Wi-Fi con
#                                               aislamiento de clientes)
# ============================================================================
param([int]$Segundos = 45)

$group  = [System.Net.IPAddress]::Parse("239.255.67.85")
$port   = 5354
$local  = [System.Net.IPAddress]::Any

$udp = New-Object System.Net.Sockets.UdpClient
# SIO_UDP_CONNRESET off
$udp.Client.IOControl(-1744830452, [BitConverter]::GetBytes([int]0), $null)
$udp.Client.SetSocketOption(
    [System.Net.Sockets.SocketOptionLevel]::Socket,
    [System.Net.Sockets.SocketOptionName]::ReuseAddress, $true)
$udp.Client.Bind((New-Object System.Net.IPEndPoint($local, $port)))
$udp.Client.ReceiveTimeout = 500

# Interfaces: capturar ANTES de usar (join y egress).
$interfaces = [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()

# Interfaz de SALIDA: la que tiene pasarela (red real). Los adaptadores
# virtuales (VirtualBox/WSL) ganan por metrica y se tragan los anuncios.
$egress = $null
foreach ($nic in $interfaces) {
    if ($nic.OperationalStatus -ne "Up") { continue }
    if ($nic.NetworkInterfaceType -eq "Loopback") { continue }
    if ($nic.GetIPProperties().GatewayAddresses.Count -eq 0) { continue }
    foreach ($addr in $nic.GetIPProperties().UnicastAddresses) {
        if ($addr.Address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) { continue }
        if ($addr.Address.ToString().StartsWith("169.254.")) { continue }
        $egress = $addr.Address
        break
    }
    if ($egress) { break }
}
if ($egress) {
    $opt = [BitConverter]::ToInt32($egress.GetAddressBytes(), 0)
    $udp.Client.SetSocketOption(
        [System.Net.Sockets.SocketOptionLevel]::IP,
        [System.Net.Sockets.SocketOptionName]::MulticastInterface, $opt)
    Write-Output ("Anuncios saldran por: {0}" -f $egress)
} else {
    Write-Output "AVISO: ninguna interfaz con pasarela; el SO elegira la salida (posible adaptador virtual)."
}
Write-Output ""

# Join en todas las interfaces IPv4 reales
$joins = 0
foreach ($nic in $interfaces) {
    if ($nic.OperationalStatus -ne "Up") { continue }
    if ($nic.NetworkInterfaceType -eq "Loopback") { continue }
    if (-not $nic.SupportsMulticast) { continue }
    foreach ($addr in $nic.GetIPProperties().UnicastAddresses) {
        if ($addr.Address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) { continue }
        if ($addr.Address.ToString().StartsWith("169.254.")) { continue }
        try {
            $opt = New-Object System.Net.Sockets.MulticastOption($group, $addr.Address)
            $udp.Client.SetSocketOption(
                [System.Net.Sockets.SocketOptionLevel]::IP,
                [System.Net.Sockets.SocketOptionName]::AddMembership, $opt)
            $joins++
            Write-Output ("Join en {0} ({1})" -f $nic.Name, $addr.Address)
        } catch { }
    }
}
Write-Output ""
Write-Output ("Sonda activa {0} s — grupo $group`:$port, $joins interface(s), enviando SONDA cada 10s" -f $Segundos)
Write-Output ""

$sonda = @{
    deviceId    = "sonda-diagnostico"
    displayName = "SONDA-DIAGNOSTICO"
    port        = 0
    thumbprint  = "diagnostico"
    soy         = "SONDA"
} | ConvertTo-Json -Compress
$bytes = [System.Text.Encoding]::UTF8.GetBytes($sonda)
$targetEP = New-Object System.Net.IPEndPoint($group, $port)

$deadline = (Get-Date).AddSeconds($Segundos)
$nextSend = Get-Date
$recibidos = 0
while ((Get-Date) -lt $deadline) {
    if ((Get-Date) -ge $nextSend) {
        [void]$udp.Send($bytes, $bytes.Length, $targetEP)
        $nextSend = (Get-Date).AddSeconds(10)
    }
    try {
        $remoteEP = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
        $rx = $udp.Receive([ref]$remoteEP)
        $text = [System.Text.Encoding]::UTF8.GetString($rx)
        $recibidos++
        $marca = if ($text -like "*SONDA*") { " (mi propio anuncio: loopback OK)" } else { "" }
        Write-Host ("[{0:mm:ss}] {1} -> {2}{3}" -f (Get-Date), $remoteEP.Address, $text, $marca) -ForegroundColor Green
    } catch [System.Net.Sockets.SocketException] {
        # timeout de 500ms: seguir
    }
}

Write-Output ""
Write-Output ("Fin: {0} datagrama(s) recibidos." -f $recibidos)
if ($recibidos -eq 0) {
    Write-Host "CERO recibidos (ni los propios): el multicast NO funciona en esta maquina/red." -ForegroundColor Red
    Write-Host "Sospechos habituales: Wi-Fi con aislamiento de clientes (AP isolation), IGMP snooping agresivo en el router, o VPN tragandose el multicast." -ForegroundColor Yellow
}
$udp.Close()
