# Recorrido real del menu Limpiador: escaneo -> confirm -> resultado.
# Robusto ante multi-monitor: escala DINAMICA (GetDpiForWindow) y capturas con
# PrintWindow. El confirm de WebView2 NO es una ventana Win32: se DIBUJA dentro
# de nuestra ventana, asi que se detecta por PIXELES (filas grises neutras) y se
# acepta clicando "Aceptar" (sin foco ni SendKeys). Clicks solo si nada tapa.
param(
  [string]$OutDir = 'C:\Users\andre\Documentos\Git_projects\pc-suite\.screens'
)
$ErrorActionPreference = 'Stop'
$env:LIB = $null   # LIB apunta a una ruta MSVC inexistente y rompe el csc de Add-Type
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Win32 {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static string Info(IntPtr h) {
    var c = new StringBuilder(256); GetClassName(h, c, 256);
    var t = new StringBuilder(512); GetWindowText(h, t, 512);
    uint pid; GetWindowThreadProcessId(h, out pid);
    RECT r; GetWindowRect(h, out r);
    string proc = "?";
    try { proc = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch {}
    return string.Format("hwnd={0} class={1} proc={2} title='{3}' rect=({4},{5})-({6},{7})",
      h, c, proc, t, r.Left, r.Top, r.Right, r.Bottom);
  }
}
'@
[Win32]::SetProcessDPIAware() | Out-Null
$TitleBarDip = 44 / 1.5   # 29.33 CSS: calibrado al 1.5 de ayer; nativo, escala con DPI

function Get-State {
  $script:P.Refresh()
  if ($script:P.MainWindowHandle -eq 0) { return $null }
  $r = New-Object Win32+RECT
  if (-not [Win32]::GetWindowRect([IntPtr]$script:P.MainWindowHandle, [ref]$r)) { return $null }
  $h = [Win32]::GetDpiForWindow([IntPtr]$script:P.MainWindowHandle)
  if ($h -le 0) { $h = 96 }
  return @{ Hwnd = [IntPtr]$script:P.MainWindowHandle; R = $r; S = ($h / 96.0) }
}
function Wait-State {
  $deadline = (Get-Date).AddSeconds(60)   # arranque en frio ya supero 30s alguna vez
  while ((Get-Date) -lt $deadline) {
    $st = Get-State
    if ($st -and $st.R.Right - $st.R.Left -gt 100 -and $st.R.Bottom - $st.R.Top -gt 100) { return $st }
    Start-Sleep -Milliseconds 400
  }
  throw 'ventana de PCSuite no disponible'
}
function To-Screen($st, $dx, $dy) {
  # dx/dy en CSS (DIP) desde la esquina del contenido (bajo la title bar)
  $x = [int]($st.R.Left + $dx * $st.S)
  $y = [int]($st.R.Top + ($TitleBarDip + $dy) * $st.S)
  return @{ X = $x; Y = $y }
}
function Ensure-App($st, $x, $y) {
  [Win32]::ShowWindow($st.Hwnd, 9) | Out-Null            # SW_RESTORE
  [Win32]::SetForegroundWindow($st.Hwnd) | Out-Null
  Start-Sleep -Milliseconds 350
  if ([Win32]::GetForegroundWindow() -ne $st.Hwnd) {     # truco ALT (foreground lock)
    [Win32]::keybd_event(0x12, 0, 0, [IntPtr]::Zero)
    [Win32]::SetForegroundWindow($st.Hwnd) | Out-Null
    [Win32]::keybd_event(0x12, 0, 2, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 350
  }
  if ([Win32]::GetForegroundWindow() -ne $st.Hwnd) { throw 'PCSuite no consigue el foreground' }
  $pt = New-Object Win32+POINT; $pt.X = $x; $pt.Y = $y
  $root = [Win32]::GetAncestor([Win32]::WindowFromPoint($pt), 2)   # GA_ROOT
  if ($root -ne $st.Hwnd) { throw ("otra ventana tapa a PCSuite en ($x,$y): " + [Win32]::Info($root)) }
}
function Click-Css($st, $dx, $dy) {
  $p = To-Screen $st $dx $dy
  Ensure-App $st $p.X $p.Y
  Invoke-Click $st.Hwnd $p.X $p.Y
}
function Invoke-Click($hwnd, $x, $y) {
  # click endurecido: reafirma el cursor justo ANTES del DOWN (el raton del
  # usuario puede desviarlo durante cualquier espera intermedia)
  [Win32]::SetCursorPos($x, $y) | Out-Null
  Start-Sleep -Milliseconds 60
  $c = New-Object Win32+POINT
  [Win32]::GetCursorPos([ref]$c) | Out-Null
  if ([Math]::Abs($c.X - $x) -gt 2 -or [Math]::Abs($c.Y - $y) -gt 2) {
    Write-Host "cursor desviado a ($($c.X),$($c.Y)) - reafirmando"
    [Win32]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 30
  }
  [Win32]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  [Win32]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 150
}
function Grab-Window($hwnd, $path) {
  # $path = $null: solo memoria (sondeo de deteccion); con path: guarda el PNG
  $r = New-Object Win32+RECT
  if (-not [Win32]::GetWindowRect($hwnd, [ref]$r)) { throw 'Grab: GetWindowRect fallo' }
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  if ($w -le 0 -or $h -le 0) { throw 'Grab: rect invalido' }
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  $ok = [Win32]::PrintWindow($hwnd, $hdc, 2)   # PW_RENDERFULLCONTENT
  $g.ReleaseHdc($hdc); $g.Dispose()
  if (-not $ok) { $bmp.Dispose(); throw 'Grab: PrintWindow fallo' }
  if ($path) { $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png) }
  return $bmp
}
function Save-Main($name) {
  $st = Wait-State
  $bmp = Grab-Window $st.Hwnd (Join-Path $OutDir $name)
  $bmp.Dispose()
  Write-Host "grab $name"
  return $st
}
function Test-RowBlue($bmp, $st, $dx, $dy) {
  # muestrea 5x5 en el centro-fondo de una fila del nav; azul activo => True
  $cx = [int]($dx * $st.S); $cy = [int](($TitleBarDip + $dy) * $st.S)
  $hits = 0; $n = 0
  for ($i = -2; $i -le 2; $i++) { for ($j = -2; $j -le 2; $j++) {
    $c = $bmp.GetPixel([Math]::Min($bmp.Width - 1, $cx + $i), [Math]::Min($bmp.Height - 1, $cy + $j))
    $n++
    if ($c.B -gt 170 -and $c.R -lt 110 -and $c.G -gt 60 -and $c.G -lt 170) { $hits++ }
  } }
  return ($hits -ge 20)
}
function Find-RedCenter($bmp, $x0, $x1, $y0, $y1) {
  # localiza el boton danger por su texto/icono rojo vivo (layout-independiente)
  $minX = [int]::MaxValue; $maxX = -1; $minY = [int]::MaxValue; $maxY = -1
  for ($y = $y0; $y -lt [Math]::Min($y1, $bmp.Height); $y += 2) {
    for ($x = $x0; $x -lt [Math]::Min($x1, $bmp.Width); $x += 2) {
      $c = $bmp.GetPixel($x, $y)
      if ($c.R -gt 170 -and $c.G -lt 130 -and $c.B -lt 130 -and ($c.R - $c.G) -gt 70) {
        if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
        if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
      }
    }
  }
  if ($maxX -lt 0) { return $null }
  return @{ X = [int](($minX + $maxX) / 2); Y = [int](($minY + $maxY) / 2) }
}
function Get-PanelBox($bmp) {
  # overlay del confirm de WebView2 (se dibuja DENTRO de nuestra ventana; no
  # existe ventana Win32 que enumerar): filas con >=250 px grises neutros
  # exactos. Validado: con dialogo = 249 filas, sin dialogo = 0.
  $W = $bmp.Width; $H = $bmp.Height
  $x0 = 150; $x1 = [Math]::Min($W - 40, 1150); $y0 = 46; $y1 = [Math]::Min([int]($H * 0.55), 500)
  $rows = @()
  for ($y = $y0; $y -lt $y1; $y++) {
    $cnt = 0
    for ($x = $x0; $x -lt $x1; $x++) {
      $c = $bmp.GetPixel($x, $y)
      if ([Math]::Abs($c.R - $c.G) -le 1 -and [Math]::Abs($c.G - $c.B) -le 1 -and $c.R -ge 35 -and $c.R -le 60) { $cnt++ }
    }
    if ($cnt -ge 250) { $rows += $y }
  }
  if ($rows.Count -lt 50) { return $null }   # ruido suelto jamas llega a 50 filas
  $minY = $rows[0]; $maxY = $rows[$rows.Count - 1]
  $minX = [int]::MaxValue; $maxX = -1
  for ($y = $minY; $y -le $maxY; $y++) {
    for ($x = $x0; $x -lt $x1; $x++) {
      $c = $bmp.GetPixel($x, $y)
      if ([Math]::Abs($c.R - $c.G) -le 1 -and [Math]::Abs($c.G - $c.B) -le 1 -and $c.R -ge 35 -and $c.R -le 60) {
        if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
      }
    }
  }
  return @{ X0 = $minX; X1 = $maxX; Y0 = $minY; Y1 = $maxY }
}
function Get-AcceptCenter($bmp, $box) {
  # boton "Aceptar": fila con RUN continuo >=60px de azul en la mitad derecha
  # del panel (los fringes subpixel del texto son runs de 2-6px y quedan fuera)
  $bx0 = [int]($box.X0 + ($box.X1 - $box.X0) * 0.45)
  $minX = [int]::MaxValue; $maxX = -1; $minY = [int]::MaxValue; $maxY = -1; $n = 0
  for ($y = $box.Y0; $y -le $box.Y1; $y++) {
    $run = 0; $runStart = -1
    for ($x = $bx0; $x -le ($box.X1 + 1); $x++) {
      $azul = $false
      if ($x -le $box.X1) {
        $c = $bmp.GetPixel($x, $y)
        $azul = ($c.B -ge 180 -and ($c.B - $c.R) -ge 100 -and $c.G -ge 80 -and $c.G -le 170)
      }
      if ($azul) { if ($run -eq 0) { $runStart = $x }; $run++ }
      else {
        if ($run -ge 60) {
          $n += $run
          if ($runStart -lt $minX) { $minX = $runStart }
          if (($runStart + $run - 1) -gt $maxX) { $maxX = $runStart + $run - 1 }
          if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
        $run = 0
      }
    }
  }
  if ($n -eq 0) { return $null }
  return @{ X = [int](($minX + $maxX) / 2); Y = [int](($minY + $maxY) / 2) }
}
function Wait-Confirm($st, $timeoutMs) {
  # sondea la ventana hasta ver el overlay del confirm; devuelve el punto de
  # SCREEN del boton "Aceptar" (02-confirm.png guarda la evidencia)
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
    $bmp = $null
    try { $bmp = Grab-Window $st.Hwnd $null } catch { Start-Sleep -Milliseconds 300; continue }
    try {
      $box = Get-PanelBox $bmp
      if ($box) {
        $c = Get-AcceptCenter $bmp $box
        if ($c) {
          $bmp.Save((Join-Path $OutDir '02-confirm.png'), [System.Drawing.Imaging.ImageFormat]::Png)
          Write-Host "confirm detectado; grab 02-confirm.png; Aceptar en ($($c.X),$($c.Y))"
          return @{ X = $st.R.Left + $c.X; Y = $st.R.Top + $c.Y }
        }
        Write-Host 'panel del confirm visible pero sin boton Aceptar; reintento'
      }
    } finally { $bmp.Dispose() }
    Start-Sleep -Milliseconds 300
  }
  return $null
}
function Test-ConfirmOpen($st) {
  try { $bmp = Grab-Window $st.Hwnd $null } catch { return $false }
  try { return ($null -ne (Get-PanelBox $bmp)) } finally { $bmp.Dispose() }
}

Get-Process PCSuite.Desktop -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$p = Start-Process 'C:\Users\andre\Documentos\Git_projects\pc-suite\src\PCSuite.Desktop\bin\Debug\net10.0-windows\PCSuite.Desktop.exe' -PassThru
$script:P = $p
try {
  Start-Sleep -Seconds 5   # arranque + WebView2 + render Home
  $st = Save-Main '00-launch.png'

  # "Empezar" del overlay first-run (defensivo: si no esta, el click cae en Home)
  Click-Css $st 623 384
  # nav "Limpiador" (7a posicion, sidebar fijo)
  $st = Wait-State
  Click-Css $st 100 322
  Start-Sleep -Seconds 5   # navegacion + auto-scan de las 3 raices

  $bmp = Grab-Window $st.Hwnd (Join-Path $OutDir '01-scan.png')
  $onHome = Test-RowBlue $bmp $st 180 82     # Inicio debe estar INACTIVO
  $onClean = Test-RowBlue $bmp $st 180 322   # Limpiador debe estar ACTIVO (azul)
  $bmp.Dispose()
  Write-Host "grab 01-scan.png (nav verificado: limpiador=$onClean inicioActivo=$onHome)"
  if (-not $onClean) { throw 'el nav no navego a Limpiador (fila activa no azul); ver 01-scan.png' }

  # boton "Limpiar seleccionadas": rojo vivo dentro de la tarjeta (layout-independiente)
  $sx = [int]($st.R.Left + 250 * $st.S); $ex = [int]($st.R.Left + 460 * $st.S)
  $sy = [int]($st.R.Top + ($TitleBarDip + 330) * $st.S); $ey = [int]($st.R.Top + ($TitleBarDip + 440) * $st.S)
  $bmp = Grab-Window $st.Hwnd (Join-Path $OutDir '01b-preclick.png')
  $btn = Find-RedCenter $bmp ([Math]::Max(0, $sx - $st.R.Left)) ([Math]::Min($bmp.Width, $ex - $st.R.Left)) ([Math]::Max(0, $sy - $st.R.Top)) ([Math]::Min($bmp.Height, $ey - $st.R.Top))
  $bmp.Dispose()
  if (-not $btn) { throw 'no se localizo el boton rojo "Limpiar"; ver 01b-preclick.png' }
  $bx = $st.R.Left + $btn.X; $by = $st.R.Top + $btn.Y
  Write-Host "boton rojo en ($bx,$by)"
  Ensure-App $st $bx $by
  Invoke-Click $st.Hwnd $bx $by

  $cf = Wait-Confirm $st 8000
  if (-not $cf) {
    Save-Main '02-nodialog.png' | Out-Null
    throw 'no aparecio el window.confirm'
  }
  Ensure-App $st $cf.X $cf.Y
  Invoke-Click $st.Hwnd $cf.X $cf.Y
  $cerrado = $false
  foreach ($i in 1..20) {
    Start-Sleep -Milliseconds 250
    if (-not (Test-ConfirmOpen $st)) { $cerrado = $true; break }
  }
  if (-not $cerrado) { Save-Main '02-stuck.png' | Out-Null; throw 'el confirm sigue abierto tras Aceptar' }
  Write-Host 'confirm aceptado'
  Start-Sleep -Seconds 8   # borrado + rescan automatico + toast
  Save-Main '03-result.png' | Out-Null
  Start-Sleep -Seconds 4
  Save-Main '04-final.png' | Out-Null
} finally {
  $p.Refresh()
  if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2 }
  Get-Process PCSuite.Desktop -ErrorAction SilentlyContinue | Stop-Process -Force
}
Write-Host 'DONE'
