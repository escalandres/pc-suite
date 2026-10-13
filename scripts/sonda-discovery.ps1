# ============================================================================
# Sonda de diagnostico Local Share (independiente de la app) — v4 a prueba
# de balones: NUNCA se cierra sola. Imprime cualquier error completo y
# espera "Enter" antes de cerrar (aunque todo vaya bien).
#
# QUE HACE:
#   1. Se une al grupo multicast 239.255.67.85:5354 en TODAS las
#      interfaces IPv4 reales (igual que la app corregida).
#   2. Fija la interfaz de SALIDA a la que tiene pasarela (red real).
#   3. Escucha durante N segundos e imprime CADA anuncio recibido.
#   4. Envia cada 10s un anuncio "SONDA-DIAGNOSTICO": si el OTRO equipo
#      tiene PCSuite con Local Share activo, en su pagina de Dispositivos
#      deberia aparecer "SONDA-DIAGNOSTICO".
#
# USO (PowerShell, ambos equipos):
#   powershell -ExecutionPolicy Bypass -File .\sonda-discovery.ps1 -Segundos 45
# ============================================================================

param([int]$Segundos = 45)

$script:recibidos = 0
$script:deOtros = 0

function Pausa-Fin([string]$Titulo) {
    Write-Output ""
    Write-Output ("=== {0} — Pulsa Enter para cerrar ===" -f $Titulo)
    [void](Read-Host)
}

try {
    # ------------------------------------------------------------------
    # Configuracion de red local (se imprime ANTES de todo, para tenerla
    # incluso si algo explota luego)
    # ------------------------------------------------------------------
    Write-Output "=== Interfaces IPv4 de esta maquina ==="
    $interfaces = [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()
    foreach ($nic in $interfaces) {
        foreach ($addr in $nic.GetIPProperties().UnicastAddresses) {
            if ($addr.Address.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork) {
                $gw = $nic.GetIPProperties().GatewayAddresses.Count
                Write-Output ("{0} | {1} | estado={2} | gateway={3}" -f $nic.Name, $addr.Address, $nic.OperationalStatus, $gw)
            }
        }
    }
    Write-Output ""

    $group = [System.Net.IPAddress]::Parse("239.255.67.85")
    $port = 5354

    # ------------------------------------------------------------------
    # Socket
    # ------------------------------------------------------------------
    $udp = New-Object System.Net.Sockets.UdpClient
    try {
        # SIO_UDP_CONNRESET off (mejor esfuerzo)
        $udp.Client.IOControl(-1744830452, [BitConverter]::GetBytes([int]0), [byte[]]$null)
    } catch {
        Write-Output ("AVISO: IOControl(SIO_UDP_CONNRESET) no disponible: {0}" -f $_.Exception.Message)
    }
    $udp.Client.SetSocketOption(
        [System.Net.Sockets.SocketOptionLevel]::Socket,
        [System.Net.Sockets.SocketOptionName]::ReuseAddress, $true)
    $udp.Client.Bind((New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, $port)))
    $udp.Client.ReceiveTimeout = 500
    Write-Output ("Socket UDP ligado a 0.0.0.0:{0} (ReuseAddress activo)" -f $port)

    # ------------------------------------------------------------------
    # Interfaz de salida: la que tiene pasarela (red real)
    # ------------------------------------------------------------------
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
        try {
            $opt = [BitConverter]::ToInt32($egress.GetAddressBytes(), 0)
            $udp.Client.SetSocketOption(
                [System.Net.Sockets.SocketOptionLevel]::IP,
                [System.Net.Sockets.SocketOptionName]::MulticastInterface, $opt)
            Write-Output ("Anuncios saldran por: {0}" -f $egress)
        } catch {
            Write-Output ("AVISO: no se pudo fijar la interfaz de salida: {0}" -f $_.Exception.Message)
        }
    } else {
        Write-Output "AVISO: ninguna interfaz con pasarela; el SO elegira la salida."
    }

    # ------------------------------------------------------------------
    # Join multicast en todas las interfaces IPv4 reales
    # ------------------------------------------------------------------
    $joins = 0
    foreach ($nic in $interfaces) {
        if ($nic.OperationalStatus -ne "Up") { continue }
        if ($nic.NetworkInterfaceType -eq "Loopback") { continue }
        if (-not $nic.SupportsMulticast) { continue }
        foreach ($addr in $nic.GetIPProperties().UnicastAddresses) {
            if ($addr.Address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) { continue }
            if ($addr.Address.ToString().StartsWith("169.254.")) { continue }
            try {
                $mopt = New-Object System.Net.Sockets.MulticastOption($group, $addr.Address)
                $udp.Client.SetSocketOption(
                    [System.Net.Sockets.SocketOptionLevel]::IP,
                    [System.Net.Sockets.SocketOptionName]::AddMembership, $mopt)
                $joins++
                Write-Output ("Join en {0} ({1})" -f $nic.Name, $addr.Address)
            } catch {
                Write-Output ("Join rechazado en {0} ({1}): {2}" -f $nic.Name, $addr.Address, $_.Exception.Message)
            }
        }
    }

    # ------------------------------------------------------------------
    # Bucle: enviar SONDA cada 10s + escuchar
    # ------------------------------------------------------------------
    $sonda = @{
        deviceId    = "sonda-diagnostico"
        displayName = "SONDA-DIAGNOSTICO"
        port        = 0
        thumbprint  = "diagnostico"
    } | ConvertTo-Json -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($sonda)
    $targetEP = New-Object System.Net.IPEndPoint($group, $port)

    Write-Output ""
    Write-Output ("=== Sonda activa {0} s — grupo $group`:$port, $joins join(s), SONDA cada 10 s ===" -f $Segundos)
    Write-Output ""

    $deadline = (Get-Date).AddSeconds($Segundos)
    $nextSend = Get-Date
    while ((Get-Date) -lt $deadline) {
        if ((Get-Date) -ge $nextSend) {
            try {
                [void]$udp.Send($bytes, $bytes.Length, $targetEP)
            } catch {
                Write-Output ("AVISO: Send fallo: {0}" -f $_.Exception.Message)
            }
            $nextSend = (Get-Date).AddSeconds(10)
        }
        try {
            $remoteEP = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
            $rx = $udp.Receive([ref]$remoteEP)
            $text = [System.Text.Encoding]::UTF8.GetString($rx)
            $script:recibidos++
            if ($text -like "*SONDA-DIAGNOSTICO*") {
                Write-Host ("[{0:mm:ss}] {1} -> (mi propio anuncio: loopback OK)" -f (Get-Date), $remoteEP.Address) -ForegroundColor DarkYellow
            } else {
                $script:deOtros++
                Write-Host ("[{0:mm:ss}] {1} -> {2}" -f (Get-Date), $remoteEP.Address, $text) -ForegroundColor Green
            }
        } catch [System.Net.Sockets.SocketException] {
            # timeout de 500 ms: seguir
        }
    }

    $udp.Close()
    Write-Output ""
    Write-Output ("=== RESULTADO: {0} datagramas ({1} del OTRO equipo, {2} propios) ===" -f $script:recibidos, $script:deOtros, ($script:recibidos - $script:deOtros))
    if ($script:deOtros -gt 0) {
        Write-Host "El multicast FUNCIONA con el otro equipo: la red no es el problema." -ForegroundColor Green
    } else {
        Write-Host "Ningun anuncio del otro equipo: revisa que su app/sonda este activa, o hay bloqueo de red." -ForegroundColor Yellow
    }
    Pausa-Fin "Sonda completada"
} catch {
    Write-Output ""
    Write-Host ("ERROR FATAL: {0}" -f $_.Exception.Message) -ForegroundColor Red
    Write-Host ("Tipo: {0}" -f $_.Exception.GetType().FullName) -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host ("Interno: {0}" -f $_.Exception.InnerException.Message) -ForegroundColor Red
    }
    Write-Host "Traza:" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    Pausa-Fin "Error — copia este texto"
}
