# PCSuite Local Share

## Propuesta técnica y plan de implementación para un agente de IA

**Producto:** PCSuite\
**Módulo:** Local Share\
**Tipo:** Aplicación de transferencia de archivos en red local (LAN)\
**Plataforma inicial:** Windows 10/11\
**Stack de PCSuite:** .NET 10, WebView2, React, TypeScript y Vite\
**Objetivo del documento:** servir como especificación de producto,
arquitectura y plan de trabajo para un agente de programación como
OpenCode.

------------------------------------------------------------------------

## 1. Instrucciones para el agente

Implementa **Local Share** como un módulo integrado en PCSuite, no como
una aplicación independiente salvo que la estructura actual del
repositorio haga imposible una integración limpia.

Antes de modificar código:

1.  Inspecciona el repositorio y documenta la arquitectura existente,
    convenciones, sistema de navegación, comunicación WebView2/.NET,
    manejo de configuración, logging, pruebas y empaquetado.
2.  Reutiliza componentes, servicios y estilos existentes. No crees una
    segunda infraestructura para resolver algo que PCSuite ya tenga.
3.  Presenta un breve plan de implementación basado en el repositorio
    real.
4.  Implementa en etapas pequeñas, con pruebas y compilación después de
    cada etapa.
5.  No reemplaces ni refactorices módulos no relacionados con Local
    Share.
6.  No afirmes que una etapa está terminada si no compila y sus pruebas
    no pasan.
7.  Si una decisión importante depende de una característica desconocida
    del repositorio, inspecciónala primero; no inventes archivos,
    interfaces o APIs existentes.

**Prioridades:** seguridad, funcionamiento sin Internet, simplicidad de
uso, integridad de archivos y mantenibilidad.

## 2. Visión del producto

Local Share permite enviar archivos y carpetas entre computadoras que
ejecutan PCSuite y están conectadas a la misma red local, mediante Wi-Fi
o Ethernet. La transferencia debe realizarse directamente entre el
emisor y el receptor, sin nube, relay externo ni almacenamiento de
terceros.

La experiencia deseada:

1.  El usuario abre Local Share en dos equipos.
2.  Los equipos se detectan automáticamente cuando la red lo permite.
3.  El usuario selecciona el dispositivo receptor.
4.  Selecciona o arrastra archivos y carpetas.
5.  El receptor revisa los detalles y acepta o rechaza la solicitud.
6.  Los archivos se transfieren directamente por la LAN.
7.  El receptor verifica su integridad y finaliza la operación.

La conexión a Internet no debe ser un requisito. El descubrimiento
automático puede verse afectado por el aislamiento de clientes Wi-Fi,
VLAN, firewall o configuración del router; por ello debe existir una
opción de conexión manual por IP y puerto.

### 2.1 Objetivos

-   Enviar archivos y carpetas sin servicios externos.
-   Descubrir instancias de PCSuite en la LAN.
-   Mostrar progreso, velocidad, tamaño transferido y errores.
-   Solicitar autorización del receptor antes de aceptar archivos.
-   Verificar la integridad del contenido recibido.
-   Evitar sobrescrituras y escritura fuera del directorio autorizado.
-   Ofrecer una arquitectura extensible para reanudación y clientes
    multiplataforma futuros.

### 2.2 Fuera del alcance inicial

No implementar en el MVP:

-   Transferencia por Internet o NAT traversal.
-   Relay o almacenamiento en la nube.
-   Sincronización automática de carpetas.
-   Control remoto de equipos.
-   Compartición anónima permanente.
-   Ejecución automática de archivos recibidos.
-   Elevación de privilegios de administrador para realizar
    transferencias.
-   Compatibilidad con Linux, macOS o móviles como requisito de la
    primera versión.

## 3. Usuarios y casos de uso

### Caso A: enviar un archivo

Un usuario selecciona un equipo detectado, arrastra un archivo, el
receptor acepta y el archivo se guarda en su carpeta configurada.

### Caso B: enviar varios archivos o una carpeta

La aplicación mantiene los nombres y la estructura relativa de
directorios, presenta un resumen de cantidad de archivos y tamaño total
y evita rutas maliciosas.

### Caso C: conectar manualmente

Si mDNS no funciona, el usuario introduce la dirección IP y el puerto
del equipo receptor. La conexión debe seguir exigiendo autenticación y
validación del dispositivo.

### Caso D: transferencia interrumpida

En el MVP, una transferencia interrumpida debe marcarse claramente como
incompleta y no publicar el archivo parcial con su nombre final. La
reanudación real por bloques es una mejora prioritaria de la siguiente
fase.

### Caso E: dispositivo no autorizado

Un equipo desconocido no puede enviar archivos sin que el receptor
apruebe la solicitud. Los dispositivos emparejados tampoco deben poder
escribir fuera del directorio de recepción.

## 4. Alcance funcional

### 4.1 MVP obligatorio

-   [ ] Módulo Local Share accesible desde la navegación de PCSuite.
-   [ ] Estado del servicio local: activo, detenido o con error.
-   [ ] Descubrimiento de dispositivos mediante mDNS/DNS-SD.
-   [ ] Conexión manual por IP y puerto.
-   [ ] Nombre del equipo, estado y datos mínimos del dispositivo.
-   [ ] Emparejamiento explícito entre dispositivos.
-   [ ] Envío de archivos individuales y múltiples.
-   [ ] Envío de carpetas con estructura relativa.
-   [ ] Solicitud entrante con emisor, nombres, cantidad y tamaño total.
-   [ ] Aceptar o rechazar transferencia.
-   [ ] Carpeta de recepción configurable.
-   [ ] Progreso en bytes y porcentaje.
-   [ ] Velocidad aproximada y tiempo restante estimado cuando haya
    datos suficientes.
-   [ ] Cancelación de una transferencia.
-   [ ] Manejo de colisiones de nombres sin sobrescritura silenciosa.
-   [ ] Transferencia por conexión cifrada y autenticada.
-   [ ] Verificación de integridad mediante SHA-256.
-   [ ] Escritura temporal y publicación del nombre final solo al
    completar la transferencia.
-   [ ] Registro local de operaciones y errores sin almacenar el
    contenido de los archivos.
-   [ ] Estados vacíos, carga, error, desconexión y confirmación.
-   [ ] Pruebas automatizadas de protocolo, rutas y transferencia.
-   [ ] Documentación de configuración, puertos y firewall.

### 4.2 Mejoras posteriores

-   Reanudación de transferencias mediante bloques.
-   Cola con transferencias simultáneas configurables.
-   Pausar y reanudar.
-   Límite de ancho de banda.
-   Límites de tamaño y espacio disponible.
-   Lista de dispositivos de confianza con opción de revocar confianza.
-   Notificaciones nativas de Windows.
-   Envío a varios dispositivos.
-   Cliente temporal basado en navegador y código QR, después de una
    revisión específica de seguridad.
-   Soporte para Linux y macOS si se decide ampliar el alcance.

## 5. Arquitectura propuesta

La arquitectura debe respetar el diseño existente de PCSuite. Los
siguientes componentes son responsabilidades lógicas; conviértelos en
clases, proyectos o servicios según las convenciones reales del
repositorio.

### 5.1 Componentes

**Interfaz React + TypeScript** - Vista de dispositivos. - Selector de
archivos y carpetas. - Solicitudes entrantes. - Cola e historial de
transferencias. - Configuración del módulo. - Suscripción a eventos de
progreso desde el host .NET.

**Host y servicios .NET 10** - Ciclo de vida del módulo. - Inicio y
detención del listener local. - Descubrimiento y registro de
dispositivos. - Emparejamiento y gestión de identidad. - Coordinación de
transferencias. - Validación de solicitudes y límites. - Escritura
segura en disco. - Registro de eventos y errores.

**Capa de red** - Descubrimiento: mDNS/DNS-SD. - Transporte: TCP. -
Cifrado: TLS. - Protocolo de aplicación: HTTP sobre TLS o un protocolo
de streaming equivalente, siempre documentado y probado. - La
transferencia debe transmitir el contenido como flujo; no cargar
archivos completos en memoria.

**Sistema de archivos** - Directorio de recepción configurado por el
usuario. - Archivos parciales en una carpeta temporal controlada. -
Validación de rutas y nombres. - Cálculo de SHA-256 en streaming. -
Movimiento al destino final después de completar la validación.

### 5.2 Diagrama lógico

``` text
┌────────────────────────────────────────────┐
│ PCSuite UI (React + TypeScript + WebView2) │
│ Devices | Send | Incoming | Queue | Config │
└──────────────────────┬─────────────────────┘
                       │ Bridge existente
┌──────────────────────▼─────────────────────┐
│ Host .NET 10 / Local Share Module          │
│ Lifecycle | Pairing | Transfer Coordinator │
│ Policy | Events | Logging                  │
└───────────────┬────────────────┬───────────┘
                │                │
      ┌─────────▼────────┐ ┌─────▼────────────┐
      │ Discovery        │ │ Transfer Server  │
      │ mDNS / DNS-SD    │ │ TCP + TLS        │
      └──────────────────┘ └─────┬────────────┘
                                 │ LAN directa
                       ┌─────────▼────────────┐
                       │ PCSuite receptor     │
                       │ Auth | Validation    │
                       │ Temp files | SHA-256 │
                       └──────────────────────┘
```

### 5.3 Límites de responsabilidad

-   El frontend nunca debe decidir si una transferencia está autorizada;
    el host .NET es la autoridad.
-   El frontend no debe construir rutas de destino confiables ni
    escribir directamente al sistema de archivos.
-   La capa de red no debe escribir datos entrantes en rutas
    proporcionadas sin validación.
-   La UI debe recibir eventos de progreso mediante el mecanismo de
    comunicación existente en PCSuite.
-   Evitar duplicar el estado de una transferencia en múltiples
    servicios sin una fuente de verdad definida.

## 6. Protocolo y flujo de transferencia

Diseñar un contrato versionado, documentado y probado. Los endpoints
siguientes son una propuesta de referencia, no una obligación si el
repositorio ya tiene un patrón mejor.

  -----------------------------------------------------------------------
  Operación                           Propósito
  ----------------------------------- -----------------------------------
  `GET /health`                       Verificar disponibilidad del
                                      servicio local sin exponer
                                      información sensible

  `POST /pair`                        Iniciar el emparejamiento

  `POST /transfers`                   Solicitar una transferencia y
                                      transmitir metadatos

  `GET /transfers/{id}`               Consultar el estado de una
                                      transferencia

  `PUT /transfers/{id}/content`       Enviar contenido en streaming para
                                      la versión inicial

  `DELETE /transfers/{id}`            Cancelar una transferencia
  -----------------------------------------------------------------------

Si se usa HTTP, la API debe escuchar únicamente en interfaces de red
apropiadas y usar TLS. No exponer endpoints de control sin
autenticación.

### 6.1 Flujo recomendado

1.  Descubrimiento o conexión manual.
2.  Negociación de versión del protocolo.
3.  Validación de identidad y estado de emparejamiento.
4.  Solicitud de transferencia con metadatos.
5.  El receptor presenta la solicitud al usuario.
6.  El receptor acepta o rechaza.
7.  Si acepta, se crea un destino temporal seguro.
8.  El contenido se copia en streaming y se informa del progreso.
9.  Se valida tamaño y SHA-256.
10. Se mueve el archivo a su ubicación final sin sobrescritura
    implícita.
11. Ambos lados reciben un resultado final consistente.

### 6.2 Contrato mínimo de metadatos

Cada elemento de transferencia debería incluir:

-   Identificador de transferencia.
-   Identificador del elemento.
-   Nombre base del archivo.
-   Ruta relativa normalizada para conservar carpetas.
-   Tamaño esperado en bytes.
-   SHA-256 esperado cuando esté disponible.
-   Versión del protocolo.
-   Fecha de creación de la solicitud.
-   Identidad autenticada del dispositivo emisor.

No confiar en la ruta relativa ni en el nombre recibidos. Validarlos en
el receptor. Rechazar rutas absolutas, segmentos `..`, rutas UNC,
prefijos de unidad, caracteres de control y cualquier ruta que escape
del directorio autorizado.

## 7. Seguridad

La seguridad es requisito de arquitectura, no una mejora opcional.

### 7.1 Identidad y emparejamiento

-   Asignar una identidad estable a cada instalación.
-   El primer emparejamiento debe requerir confirmación explícita en
    ambos equipos o un código de verificación mostrado en ambos.
-   No considerar confiable un dispositivo solo porque aparece por mDNS.
-   Guardar únicamente el material de identidad necesario y proteger las
    claves privadas usando mecanismos del sistema operativo, por ejemplo
    Windows DPAPI o Windows Certificate Store, si corresponde a la
    arquitectura.
-   Permitir revocar un dispositivo emparejado.
-   No aceptar transferencias silenciosas de dispositivos no
    autorizados.

### 7.2 TLS

-   Usar TLS con validación de identidad del par.
-   No deshabilitar la validación de certificados como solución a
    errores de conexión.
-   Si se usan certificados autofirmados, diseñar un mecanismo explícito
    de confianza durante el emparejamiento y verificar la identidad del
    certificado o su huella.
-   No enviar claves privadas, tokens ni secretos en logs.

### 7.3 Sistema de archivos

-   Canonicalizar y validar rutas.
-   Resolver la ruta final y verificar que permanezca dentro de la
    carpeta de recepción.
-   Considerar enlaces simbólicos, junctions y puntos de reanálisis de
    Windows.
-   Crear archivos temporales con nombres aleatorios no controlados por
    el emisor.
-   No sobrescribir archivos existentes sin una política explícita.
-   Eliminar o aislar archivos parciales después de una cancelación o
    error.
-   No ejecutar, abrir ni previsualizar automáticamente los archivos
    recibidos.

### 7.4 Disponibilidad y abuso

-   Limitar tamaño máximo configurable, número de transferencias
    simultáneas, solicitudes entrantes y conexiones.
-   Aplicar timeouts y cancelación cooperativa.
-   Verificar espacio disponible antes de iniciar cuando sea posible.
-   No reservar cantidades ilimitadas de memoria.
-   No confiar en el tamaño declarado: contar los bytes reales
    recibidos.
-   Evitar que el descubrimiento o las solicitudes entrantes provoquen
    crecimiento ilimitado de memoria o logs.

### 7.5 Red y firewall

-   No desactivar Windows Defender Firewall.
-   Documentar el puerto y protocolo utilizados.
-   Si hace falta una regla de firewall, solicitarla de forma
    específica, con explicación y consentimiento del usuario, siguiendo
    el mecanismo ya definido en PCSuite.
-   No exigir permisos de administrador para la operación normal de
    transferencia.
-   Respetar redes públicas y perfiles de firewall; no abrir el servicio
    indiscriminadamente a todas las interfaces.
-   Mostrar un diagnóstico claro cuando el firewall o el aislamiento de
    clientes impida conectar.

### 7.6 Privacidad

-   Sin telemetría ni llamadas a servicios externos.
-   No indexar ni escanear todo el sistema de archivos.
-   Registrar metadatos operativos mínimos; permitir limpiar el
    historial.
-   No registrar contenido de archivos ni secretos.
-   La app debe seguir funcionando sin Internet.

## 8. Interfaz de usuario y branding

### 8.1 Estilo visual

Seguir el branding existente de PCSuite:

-   Fondo oscuro principal: `#0b1220`.
-   Acento azul: `#3b82f6`.
-   Tipografía legible y jerarquía visual clara.
-   Apariencia profesional, técnica y orientada a desarrolladores y
    power users.
-   Evitar gradientes decorativos, efectos excesivos o patrones visuales
    genéricos de SaaS.
-   Mantener consistencia con los componentes de PCSuite.

### 8.2 Navegación

Añadir Local Share a la navegación de herramientas. Usar el sistema de
rutas y registro de módulos existente, sin crear un sistema paralelo.

### 8.3 Vistas

**Vista 1: Dispositivos** - Lista de dispositivos detectados y
emparejados. - Nombre, estado, dirección local cuando sea útil y tipo de
conexión. - Indicador de dispositivo actual. - Acciones: conectar,
enviar, emparejar, revocar confianza y actualizar.

**Vista 2: Enviar** - Zona de arrastrar y soltar. - Botones para
seleccionar archivos y carpetas. - Lista de elementos seleccionados. -
Tamaño total y cantidad de archivos. - Selector de destino. - Acción de
envío claramente visible.

**Vista 3: Solicitud entrante** - Identidad/nombre del emisor. - Nombres
o resumen de archivos. - Cantidad y tamaño total. - Carpeta de
destino. - Acciones Aceptar y Rechazar. - Advertencias de colisión o
falta de espacio cuando se detecten.

**Vista 4: Transferencias** - Cola de transferencias activas y
recientes. - Progreso por transferencia y, si aplica, por archivo. -
Velocidad aproximada y tiempo restante estimado. - Estados: esperando
aprobación, preparando, transfiriendo, verificando, completada,
cancelada y fallida. - Mensaje de error accionable y opción de
reintentar cuando corresponda.

**Vista 5: Configuración** - Nombre visible del equipo. - Carpeta de
recepción. - Puerto y estado del servicio. - Descubrimiento automático
activado/desactivado. - Dispositivos emparejados. - Límites de
transferencia. - Gestión de historial y archivos parciales. -
Diagnóstico de conectividad.

### 8.4 Accesibilidad y experiencia

-   Soportar teclado y foco visible.
-   No comunicar el estado solo mediante color.
-   Mantener etiquetas claras y mensajes de error concretos.
-   Confirmar acciones destructivas.
-   Evitar que una transferencia larga bloquee la interfaz.
-   Mostrar un estado vacío útil cuando no haya dispositivos.

## 9. Modelo de datos sugerido

Ajustar el almacenamiento a la infraestructura existente de PCSuite. No
añadir SQLite si ya existe una solución local adecuada, salvo
justificación técnica.

### Device

-   `deviceId`: identificador estable de instalación.
-   `displayName`: nombre visible.
-   `lastKnownAddresses`: direcciones vistas recientemente, si es
    necesario.
-   `trustState`: pendiente, emparejado o revocado.
-   `lastSeenAt`: fecha de última detección.
-   `protocolVersion`: versión compatible.

No persistir información de red que no sea necesaria.

### Transfer

-   `transferId`.
-   `direction`: envío o recepción.
-   `peerDeviceId`.
-   `status`.
-   `createdAt`, `startedAt`, `completedAt`.
-   `totalBytes`, `transferredBytes`.
-   `itemCount`.
-   `destinationSummary`.
-   `errorCode` y mensaje apto para usuario, cuando aplique.

### TransferItem

-   `itemId`.
-   `transferId`.
-   `relativePath`.
-   `expectedSize`.
-   `transferredBytes`.
-   `expectedHash`.
-   `status`.

El historial debe poder limpiarse y no debe conservar contenido de
archivos. No almacenar rutas completas sensibles salvo que sea
imprescindible.

## 10. Gestión de errores

Definir errores tipados y traducirlos a mensajes útiles en la interfaz.
Casos mínimos:

-   Dispositivo fuera de línea.
-   Descubrimiento no disponible.
-   Conexión rechazada o timeout.
-   Dispositivo no emparejado.
-   Certificado o identidad no válida.
-   Transferencia rechazada.
-   Falta de espacio.
-   Archivo bloqueado o sin permisos.
-   Nombre duplicado.
-   Ruta no permitida.
-   Integridad incorrecta.
-   Cancelación por usuario.
-   Conexión perdida.
-   Versión de protocolo incompatible.

Los logs técnicos pueden incluir códigos y detalles de diagnóstico, pero
no secretos ni contenido de archivos. La UI no debe mostrar stack
traces.

## 11. Pruebas y validación

### 11.1 Pruebas unitarias

-   Normalización y validación de rutas.
-   Generación de nombres temporales.
-   Política de colisiones.
-   Cálculo de progreso.
-   Transiciones de estado.
-   Verificación SHA-256.
-   Validación de límites y metadatos.
-   Autorización de dispositivos.

### 11.2 Pruebas de integración

-   Envío de un archivo pequeño.
-   Envío de archivos grandes usando streaming.
-   Envío de múltiples archivos y carpetas anidadas.
-   Cancelación durante una transferencia.
-   Desconexión del emisor y receptor.
-   Archivo de destino ya existente.
-   Hash incorrecto.
-   Nombre o ruta maliciosa.
-   Dispositivo no emparejado.
-   Certificado o identidad inválida.
-   Sin espacio disponible.
-   Solicitudes concurrentes.
-   Reinicio del servicio durante una transferencia.

### 11.3 Pruebas manuales con dos equipos

-   Ambos conectados al mismo Wi-Fi.
-   Ambos conectados por Ethernet.
-   Emisor por Wi-Fi y receptor por Ethernet.
-   Sin conexión a Internet.
-   Firewall habilitado.
-   Perfil de red pública y privada.
-   Router con aislamiento de clientes, comprobando que el diagnóstico
    sea claro.
-   Cambio de IP del receptor.
-   Suspensión y reanudación del equipo.

### 11.4 Criterios de aceptación del MVP

-   Dos instalaciones de PCSuite en Windows pueden conectarse y
    transferir archivos de manera segura.
-   El descubrimiento automático funciona en una LAN compatible y existe
    conexión manual alternativa.
-   El receptor debe aprobar las transferencias entrantes.
-   Los archivos grandes se transfieren en streaming, sin cargarse
    completos en memoria.
-   Los archivos finales solo aparecen como completados después de
    validar tamaño e integridad.
-   Los errores y cancelaciones no dejan archivos parciales con aspecto
    de archivos completos.
-   La aplicación no sobrescribe archivos silenciosamente.
-   Las rutas maliciosas no pueden escapar de la carpeta de recepción.
-   No hay llamadas de red a servicios externos.
-   La interfaz permanece receptiva durante transferencias.
-   La solución compila, las pruebas automatizadas pasan y las
    instrucciones de ejecución están documentadas.

## 12. Plan de implementación por fases

Las estimaciones son orientativas para un desarrollador con apoyo de un
agente de IA y una base PCSuite ya funcional.

### Fase 0 --- Reconocimiento del repositorio (0.5--1 día)

-   Inspeccionar estructura y arquitectura.
-   Identificar cómo se registran módulos y cómo se comunican React y
    .NET.
-   Identificar componentes de UI, logging, configuración, pruebas y
    empaquetado reutilizables.
-   Entregar un resumen técnico y plan ajustado.
-   No modificar módulos no relacionados.

**Salida:** `docs/local-share/repository-assessment.md`.

### Fase 1 --- Diseño y contratos (1--2 días)

-   Definir estados y transiciones.
-   Definir contrato del protocolo y versión.
-   Definir identidad y emparejamiento.
-   Definir política de archivos temporales y destino.
-   Documentar amenazas y decisiones técnicas.

**Salida:** `docs/local-share/technical-design.md` y
`docs/local-share/protocol.md`.

### Fase 2 --- Transporte seguro mínimo (2--4 días)

-   Crear el servicio receptor.
-   Implementar conexión TLS y validación de identidad.
-   Implementar transferencia en streaming.
-   Añadir cancelación y límites.
-   Crear pruebas de integración con dos instancias locales.

**Salida:** transferencia funcional entre dos instancias, inicialmente
con conexión manual.

### Fase 3 --- Descubrimiento y emparejamiento (2--4 días)

-   Integrar mDNS/DNS-SD.
-   Presentar dispositivos descubiertos.
-   Implementar emparejamiento explícito y revocación.
-   Añadir conexión manual por IP y puerto como alternativa.
-   Manejar cambios de dirección y dispositivos desconectados.

**Salida:** equipos detectables y autenticados en una LAN compatible.

### Fase 4 --- Integridad y escritura segura (2--4 días)

-   Añadir hashes SHA-256.
-   Implementar archivos temporales y publicación final.
-   Validar rutas y nombres.
-   Gestionar colisiones y errores de disco.
-   Probar entradas maliciosas y desconexiones.

**Salida:** recepción segura con validación de integridad.

### Fase 5 --- Interfaz (3--5 días)

-   Implementar vistas de dispositivos, envío, solicitudes entrantes,
    transferencias y configuración.
-   Conectar eventos de progreso al mecanismo existente.
-   Implementar estados vacíos, errores, cancelación y confirmaciones.
-   Verificar accesibilidad y consistencia visual.

**Salida:** experiencia de usuario integrada en PCSuite.

### Fase 6 --- Robustez y empaquetado (2--4 días)

-   Ejecutar pruebas automatizadas y manuales.
-   Revisar firewall y ciclo de vida del servicio.
-   Validar que no haya llamadas externas.
-   Probar el instalador y arranque en frío.
-   Documentar limitaciones y solución de problemas.

**Salida:** MVP listo para uso personal controlado.

### Fase 7 --- Transferencias reanudables (posterior al MVP)

-   Diseñar protocolo por bloques con identificadores estables.
-   Persistir manifiesto y bloques completados.
-   Verificar cada bloque y el hash final.
-   Reanudar solo tras volver a autenticar el dispositivo.
-   Gestionar caducidad y limpieza de transferencias parciales.

**Salida:** transferencias robustas ante desconexiones.

**Estimación inicial total:** aproximadamente 13--24 días laborables,
con variación según la arquitectura real de PCSuite y la biblioteca
mDNS/TLS elegida. No incluir la fase de reanudación en la promesa del
MVP.

## 13. Organización del código sugerida

Adaptar estos nombres a las convenciones existentes; no crear proyectos
separados sin necesidad.

``` text
PCSuite/
├── src/
│   ├── [host .NET existente]/
│   │   └── LocalShare/
│   │       ├── Discovery/
│   │       ├── Devices/
│   │       ├── Pairing/
│   │       ├── Protocol/
│   │       ├── Transfers/
│   │       ├── Storage/
│   │       ├── Security/
│   │       └── Configuration/
│   └── [frontend existente]/
│       └── features/
│           └── local-share/
│               ├── components/
│               ├── pages/
│               ├── hooks/
│               ├── services/
│               ├── types/
│               └── styles/
├── tests/
│   ├── [unit tests existentes]/
│   └── [integration tests existentes]/
└── docs/
    └── local-share/
        ├── repository-assessment.md
        ├── technical-design.md
        ├── protocol.md
        └── troubleshooting.md
```

No crear todos estos directorios vacíos de antemano. Crear únicamente
los necesarios para la implementación.

## 14. Observabilidad y rendimiento

-   Usar logs estructurados con identificador de transferencia y código
    de error.
-   No registrar contenido ni secretos.
-   Evitar logs por cada pequeño bloque si generan volumen excesivo.
-   Actualizar la UI con una frecuencia razonable, no en cada escritura
    de bytes.
-   Usar streaming y buffers acotados.
-   Medir la velocidad con ventanas de tiempo, suavizando variaciones.
-   El tiempo restante es estimado y debe ocultarse cuando no haya datos
    suficientes.
-   Aplicar backpressure y límites de concurrencia.
-   No bloquear el hilo de UI ni el hilo principal de WebView2.

## 15. Reglas de implementación para el agente

1.  Primero inspeccionar; después diseñar; luego implementar.
2.  Mantener la arquitectura existente de PCSuite.
3.  Preferir bibliotecas maduras y mantenidas para mDNS y TLS; comprobar
    compatibilidad con .NET 10, licencia, mantenimiento y
    vulnerabilidades antes de añadir una dependencia.
4.  No implementar criptografía propia.
5.  No desactivar validaciones TLS para facilitar pruebas.
6.  No solicitar privilegios de administrador para transferencias
    normales.
7.  No desactivar el firewall ni crear reglas amplias automáticamente.
8.  No leer ni enumerar directorios ajenos a la selección del usuario y
    la carpeta de recepción.
9.  No cargar archivos completos en memoria.
10. No confiar en metadatos, rutas o tamaños suministrados por el
    emisor.
11. No sobrescribir archivos silenciosamente.
12. Mantener los estados de transferencia explícitos y probados.
13. Añadir pruebas junto con cada capacidad.
14. Ejecutar formato, análisis estático, compilación y pruebas
    disponibles antes de declarar terminada una fase.
15. Documentar decisiones que se aparten de esta propuesta.

## 16. Definition of Done

Una tarea se considera terminada cuando:

-   Cumple los criterios funcionales descritos.
-   Está integrada con los patrones existentes de PCSuite.
-   Tiene pruebas apropiadas.
-   Maneja cancelación y errores de manera segura.
-   No introduce llamadas externas ni telemetría.
-   No contiene secretos ni datos sensibles en logs.
-   Compila y supera las pruebas relevantes.
-   Incluye documentación actualizada.
-   Se ha revisado el impacto en los demás módulos de PCSuite.

## 17. Primer encargo recomendado para OpenCode

Comienza únicamente con la **Fase 0**.

1.  Inspecciona el repositorio de PCSuite.
2.  Resume los proyectos, el host .NET, la interfaz, la comunicación
    WebView2, el sistema de módulos, configuración, logging, pruebas y
    empaquetado.
3.  Identifica qué piezas pueden reutilizarse.
4.  Propón la ubicación concreta del módulo Local Share.
5.  Evalúa bibliotecas candidatas para mDNS/DNS-SD y el enfoque TLS,
    considerando compatibilidad con .NET 10, licencia, mantenimiento y
    seguridad.
6.  Identifica riesgos y preguntas que bloqueen decisiones.
7.  Crea `docs/local-share/repository-assessment.md` con hallazgos
    verificables y un plan de implementación ajustado al repositorio.
8.  No empieces a implementar la transferencia hasta terminar el
    análisis y presentar el plan.

El objetivo final es una funcionalidad integrada, privada y fiable para
transferir archivos directamente entre computadoras de la red local, sin
depender de servicios externos.
