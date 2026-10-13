# Local Share — Solución de problemas

Guía práctica para diagnosticar por qué dos equipos con PCSuite no se
 ven entre sí o no pueden transferir archivos. Ordenada de lo más común
 a lo menos común.

> **Regla de oro:** Local Share nunca sale a Internet. Si algo falla, la
> causa está en la red local, el firewall o la configuración — no en un
> "servidor" remoto.

---

## 1. El otro equipo no aparece en la lista

**Causa más común: el firewall de Windows bloquea el puerto entrante.**

El descubrimiento (UDP multicast 239.255.67.85:5354) y la recepción de
archivos (TCP, puerto 45180 por defecto) necesitan conexiones entrantes
que el firewall bloquea por defecto.

**Solución:**

1. En la página **Local Share**, con el servicio iniciado, aparece un
   aviso amarillo: *"El firewall de Windows podría bloquear conexiones
   entrantes de otros equipos."*
2. Pulsa **Permitir en el firewall**. Windows muestra un UAC puntual
   (solo para esta acción; la app nunca se ejecuta como administrador).
3. Se crean **dos** reglas mínimas (ambas solo para `PCSuite.exe`):
   - **TCP** entrante, solo el puerto del servicio (transferencias)
   - **UDP** entrante, solo el puerto 5354 (detección automática por
     multicast — sin esta, los equipos no se ven entre sí)

> **¿Y al cerrar/detener la app?** Las reglas de firewall son permanentes
> (como las de cualquier juego multiplayer) pero **inertes sin PCSuite**:
> solo aplican a `PCSuite.exe` y al detener el servicio no queda ningún
> puerto escuchando. La desinstalación sí las elimina automáticamente.

También puedes crearlas manualmente (PowerShell como administrador):

```powershell
netsh advfirewall firewall add rule name="PCSuite Local Share" `
  dir=in action=allow program="C:\...\RutaA\PCSuite.exe" `
  protocol=TCP localport=45180 enable=yes
netsh advfirewall firewall add rule name="PCSuite Local Share" `
  dir=in action=allow program="C:\...\RutaA\PCSuite.exe" `
  protocol=UDP localport=5354 enable=yes
```

> Si cambias el puerto en Configuración, la regla se actualiza al
> pulsar de nuevo "Permitir en el firewall" (la operación es
> idempotente: borra y vuelve a crear la regla).

---

## 2. Se ven pero la transferencia falla al conectarse

| Síntoma | Causa | Solución |
|---|---|---|
| "Received an unexpected EOF" en el emisor | El receptor no está escuchando (servicio detenido) | Inicia Local Share en el receptor |
| "TLS authentication fallida" en el receptor | Los equipos no están emparejados o la huella cambió | Empareja de nuevo (ver §4) |
| Conecta y luego se corta a mitad de archivo | El receptor se suspendió o cambió de red | Reanuda y reenvía; los archivos parciales se limpian solos |

---

## 3. Están en la misma red pero el router los aísla

Muchos routers Wi-Fi activan **aislamiento de clientes (AP isolation)**
por defecto: los dispositivos conectados al Wi-Fi no pueden verse
entre sí aunque compartan subred.

**Diagnóstico rápido** (PowerShell en ambos equipos):

```powershell
ping <IP-del-otro-equipo>
```

- Si el ping falla entre ambos pero cada uno tiene Internet → el router
  aísla los clientes.
- **Solución:** desactiva "Aislamiento de clientes" / "Client
  isolation" / "AP isolation" en el panel del router, o conecta al
  menos un equipo por Ethernet.

---

## 4. "Dispositivo no emparejado" tras reinstalar o cambiar de equipo

El emparejamiento se basa en la **huella SHA-256 del certificado TLS**
de cada instalación (guardada con DPAPI del usuario). Si:

- reinstalas Windows,
- borras `%LOCALAPPDATA%\PCSuite\LocalShare\localshare-cert.pfx`, o
- cambias de usuario de Windows,

…la huella cambia y el otro equipo rechazará la conexión por seguridad
(TOFU: Trust On First Use, no "confía para siempre").

**Solución:** en el equipo que recibe, abre **Local Share →
Dispositivos**, revoca la confianza del equipo antiguo y empareja de
nuevo: pulsa «Emparejar» en el equipo iniciador y, cuando aparezca el
**código de comparación de 6 dígitos**, comprueba que coincide en ambas
pantallas antes de aceptar (igual que al emparejar dos móviles por
Bluetooth).

---

## 5. El servicio no arranca: "puerto en uso"

**Síntoma:** `StartAsync` falla con *"Solo se permite un uso de cada
dirección de socket"*.

El puerto configurado está ocupado por otra aplicación o por una
conexión saliente del sistema en TIME_WAIT. Nota técnica: Windows usa
el rango **49152-65535** como puertos efímeros para conexiones
salientes; un puerto dentro de ese rango puede "robarlo" el sistema en
cualquier momento.

**Solución:** en **Configuración**, cambia el puerto a uno fuera del
rango efímero (recomendado: **10000-45000**, p. ej. 45180) o usa `0`
para auto-asignar.

---

## 6. ¿Dónde acabaron mis archivos?

- Los archivos recibidos van a la **carpeta de recepción** configurada
  (por defecto `Descargas\PCSuite`).
- Durante la transferencia se escriben como `*.part` en una subcarpeta
  interna; **solo al completarse** (tamaño + SHA-256 verificados) se
  publican con su nombre final en la carpeta de recepción.
- Si ya existía un archivo con el mismo nombre, el nuevo se guarda como
  `nombre (1).ext`, `nombre (2).ext`… — **nunca se sobreescribe en
  silencio**.

### Transferencias interrumpidas: ahora se reanudan

- Si una transferencia se corta (caída de red, cancelación, suspensión
  del equipo), el prefijo ya recibido **se conserva como estado de
  reanudación** (`.part` + manifiesto interno, nunca visible en tu
  carpeta).
- Al reenviar el mismo archivo desde el mismo equipo, la transferencia
  **continúa desde donde se cortó** en vez de empezar de cero. El
  SHA-256 se verifica sobre el archivo completo, así que si el archivo
  cambió entre intentos, el receptor lo detecta y empieza limpio.
- Los estados de reanudación caducan a los **7 días** y se limpian
  automáticamente al arrancar el servicio.
- Un envío también se puede **pausar** (botón Pausar) y reanudar luego
  sin perder el progreso.

---

## 7. La velocidad es muy baja

- Local Share usa TLS 1.3 (o 1.2) sobre TCP directo; en una LAN cableada
  debería saturar el disco o la red (100+ MB/s en gigabit).
- Si la velocidad es de KiB/s, revisa: Wi-Fi con señal débil, el router
  está en modo "ahorro de energía", o un antivirus de terceros está
  escaneando cada escritura en el receptor. En ese caso, añade la
  carpeta de recepción a las exclusiones de tu antivirus (ClamAV de
  PCSuite no interfiere: solo escanea bajo demanda).

---

## 8. Privacidad y seguridad: qué revisar si algo suena raro

- **Ninguna conexión sale de la LAN.** Si tu firewall de terceros
  reporta tráfico externo de PCSuite.exe, no es Local Share.
- **Los dispositivos solo aceptan conexiones de huellas emparejadas.**
  Un equipo desconocido en la red no puede enviar nada: el handshake
  TLS se rechaza antes de leer el primer frame del protocolo.
- **El receptor siempre confirma.** Con "Aceptar automáticamente de
  dispositivos emparejados" desactivado (por defecto), cada
  transferencia requiere decisión explícita del usuario.
- La regla de firewall (§1) es la única operación de Local Share que
  pide UAC, y es opcional: las transferencias corren siempre con
  privilegios normales de usuario.

---

## 9. Recolección de datos para un reporte de error

Al reportar un problema con Local Share, incluye:

1. Estado del servicio en ambos equipos (página Local Share, badge de
   estado) y puerto efectivo.
2. Versión de PCSuite de ambos equipos.
3. Si el aviso de firewall aparecía y qué botón se pulsó.
4. El mensaje de error exacto de la transferencia (la UI nunca muestra
   stack traces; el detalle técnico queda en el log).

Los logs técnicos van al directorio de datos de PCSuite
(`%LOCALAPPDATA%\PCSuite\logs`). No contienen secretos: los nonces de
elevación y las claves privadas jamás se registran.
