# PCSuite — Brief para propuesta de logo

> Documento de partida para diseñar el logo (diseñador o generador de IA).
> Fuente de verdad: el propio producto (`Directory.Build.props`, tokens de
> `frontend/src/index.css`).

## 1. Qué es PCSuite

PCSuite es una **suite de utilidades de escritorio para Windows** — «la navaja
suiza del desarrollador y del power user». Reúne en una sola app nativa:

- Herramientas de sistema y mantenimiento + Limpiador
- Antivirus (ClamAV) y panel de Seguridad
- Gestión de paquetes (winget) y WSL
- Portapapeles inteligente (detecta JSON, JWT, claves SSH/PEM)
- Herramientas dev: SSH, PGP/GPG, terminal, plugins

Rápida, offline, sin telemetría; la elevación de privilegios es puntual y
transparente (UAC por operación).

| | |
|---|---|
| **Nombre** | PCSuite (una palabra) |
| **Tagline** | «The Swiss Army Knife for Windows developers and power users» |
| **Desarrollador** | AES Studio · https://aes-studio.dev |
| **Plataforma** | Windows 10/11 · app nativa (.NET 10 + WebView2) · tema oscuro principal |

## 2. Audiencia

Desarrolladores y power users de Windows. Valoran: precisión, velocidad,
privacidad, herramientas serias sin adornos. El logo debe parecer una
herramienta de confianza, no un juguete ni un SaaS genérico.

## 3. Personalidad de marca

- **Es:** preciso, profesional, estética dev/terminal, potente, fiable.
- **No es:** infantil, corporativo-gris, "app de utilidades" genérica,
  ni estética IA con gradientes y brillos por todas partes.

**Keywords (generadores):** `developer tool · precision · toolkit ·
swiss army knife · terminal · dark UI · minimal · professional · trustworthy`

## 4. Paleta de la UI (ancla de marca: el azul acento)

| Rol | Hex |
|---|---|
| Acento (ancla) | `#3b82f6` · modo claro: `#2563eb` |
| Fondo (tema oscuro) | `#0b1220` |
| Superficies | `#121a2b` · `#182338` |
| Texto | `#e6edf7` |
| Éxito / Peligro | `#34d399` / `#f87171` |

Se exige **variante monocroma**: azul plano sobre blanco y blanco sobre azul.

## 5. Direcciones conceptuales sugeridas

1. **Monograma «PC»** — evolución del badge actual (cuadrado redondeado
   azul con «PC» en negrita): continuidad con la UI, máxima legibilidad.
2. **Navaja suiza abstracta** — la metáfora del tagline: un contenedor del
   que se despliegan herramientas.
3. **Terminal + escudo** — prompt `>_` con protección: dev + seguridad.
4. **Celdas/módulos** — grid de módulos (la "suite") formando una P o un cuadrado.
5. Libre, siempre que transmita *toolkit dev para Windows*.

## 6. Requisitos técnicos

- Legible a **16 px** (taskbar/favicon) y a **256 px** (exe/instalador).
  Test de silueta: reconocible con blur de 2 px.
- **Una sola tinta**: debe funcionar sin gradientes.
- Entregables: máster **SVG** + `.ico` multi-tamaño (16–256) para
  `PCSuite.exe` y el instalador (Inno `SetupIconFile`) + PNG.
- **Evitar:** texto largo, detalle fino, engranaje suelto (cliché de
  "ajustes"), estética IA genérica, más de 2 colores.

## 7. Dónde vivirá el logo

- Icono de la app (`PCSuite.exe`) y del instalador Setup.
- Badge de 32 px en la sidebar junto al nombre.
- Sección «Acerca de» (Ajustes) y futura presencia en aes-studio.dev.

## 8. Prompt listo para generador de IA (inglés)

```text
Minimal flat app icon for "PCSuite", a Windows developer utilities suite.
Rounded square, deep navy background (#0b1220), bold geometric "PC"
monogram in vivid blue (#3b82f6), subtle hint of a swiss-army-knife or
modular toolkit, clean vector style, crisp edges, legible at 16px,
no text besides "PC", professional developer-tool aesthetic, high contrast.
```
