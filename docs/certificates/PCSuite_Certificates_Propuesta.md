# Propuesta — Task 019: Certificados

> Unificar el CertificatesGenerator dentro de PCSuite como módulo "Certificados",
> añadiendo capacidades estilo OpenSSH: generación, inspección, conversión y firma.
>
> Fuente de inspiración: `CertificatesGenerator` (repo local del autor) +
> `ssh-keygen` de OpenSSH + infraestructura PGP/SSH ya existente en PCSuite.

---

## Qué existe HOY en PCSuite (no duplicar)

| Ya existe | Dónde | Qué hace |
|---|---|---|
| **PGP claves** | `PgpService` (Task 009) | Genera (RSA/Ed25519), lista, importa, exporta, cifra, firma, verifica. Keyring en `%APPDATA%\PCSuite\pgp`. |
| **SSH claves** | `SshKeyService` (Task 009) | Genera (ssh-keygen), lista, inspecciona (fingerprint), importa. |
| **Certificado TLS autofirmado** | `CertificateStore` (Task 016) | Genera RSA 2048 para Local Share (interno, sin UI). |

## Qué añade el CertificatesGenerator

| Del CertificatesGenerator | Qué aporta a PCSuite |
|---|---|
| X.509 autofirmado RSA 4096 con **SHA256withRSAandMGF1 (PSS)** | Certificados TLS reales, no solo internos — con UI |
| Exportación PEM (cert + clave privada) | Formato estándar que el usuario puede llevar a nginx/IIS |
| Clave PGP de firma dedicada (identidad + passphrase + expiración) | Claves de release (17.6) — hoy solo hay la genérica de PgpService |
| RSA 4096 (vs el 2048 interno de Local Share) | Certificados más fuertes cuando el usuario lo necesita |

## Qué añade "estilo OpenSSH"

`ssh-keygen` no solo genera claves: **convierte formatos, firma, inspecciona y genera CSRs**. PCSuite debería cubrir el mismo espectro:

| Capacidad ssh-keygen | PCSuite hoy | Propuesta |
|---|---|---|
| `-t rsa/ed25519/ecdsa` — generar pares de claves | SSH ✓ / PGP ✓ / X.509 ✗ | **Añadir X.509** |
| `-L` — inspeccionar certificado/clave | SSH fingerprint ✓ / PGP ✓ / X.509 ✗ | **Añadir inspección X.509** |
| `-e` — exportar a formatos (PEM, RFC4716, PKCS#8) | ✗ | **Añadir conversión** |
| `-s` — firmar clave con CA (certificado SSH) | ✗ | Fuera de scope (SSH CA es niche) |
| `-lf` — fingerprint de archivo | ✓ parcial | Unificar en un solo inspector |

---

## Task 019 — Módulo "Certificados"

### Arquitectura

```
PCSuite.Core/Certificates/
  ICertificateService.cs          ← interfaz de dominio
  CertificateModels.cs             ← X509CertInfo, PgpKeyInfo, SshKeyInfo,
                                     FormatType, CertificateRequest

PCSuite.Infrastructure/Certificates/
  CertificateService.cs            ← implementación BouncyCastle (sin procesos)
  X509Generator.cs                 ← generación X.509 autofirmada
  CertificateInspector.cs          ← parsear PEM/DER/PFX/OpenSSH y mostrar
  FormatConverter.cs               ← PEM↔DER, PEM↔PFX, OpenSSH→PEM
  CsrGenerator.cs                  ← Certificate Signing Request (PKCS#10)

PCSuite.Contracts/
  CertificateContracts.cs         ← DTOs del bridge (camelCase)
  OperationIds.cs                 ← cert.* (nuevos IDs)
  EventIds.cs                      ← (sin eventos necesarios: todo síncrono)

frontend/src/pages/Certificates.tsx ← página UI
frontend/src/types/bridge.ts        ← tipos
```

### Fases

#### 19.1 — X.509: generación de certificados autofirmados

Generar certificados TLS/code-signing con UI. **Todo BouncyCastle en proceso** (sin OpenSSL ni makecert):

| Parámetro | Opciones |
|---|---|
| Algoritmo | RSA 2048, RSA 4096, ECDSA P-256, ECDSA P-384, Ed25519 |
| Hash de firma | SHA-256, SHA-384, SHA-512 (+ RSA-PSS) |
| Common Name | texto libre (ej. `localhost`, `*.aes-studio.dev`) |
| Organization / OU / Country | opcional |
| SANs (Subject Alternative Names) | DNS, IP, email |
| Validez | días (default 365) |
| Exportar | PEM (cert + key), PFX/PKCS#12 (con password) |

**Operaciones del bridge:**
```
cert.x509.generate     { algorithm, commonName, organization, sans, validityDays, exportFormat, pfxPassword }
```

**Output:** `{ certificatePem, privateKeyPem, pfxPath?, fingerprint, serialNumber, validFrom, validTo }`

#### 19.2 — PGP: clave de firma dedicada

Separado del keyring personal de PgpService: una **clave de propósito específico** (releases, documentos, códigos) con:

| Parámetro | Opciones |
|---|---|
| Identidad | texto libre (ej. `AES Studio <releases@aes-studio.dev>`) |
| Algoritmo | RSA 4096 (firma), Ed25519 (firma) |
| Passphrase | obligatoria, confirmada (FixedTimeEquals) |
| Expiración | años (default 2) |
| Uso | CanSign + CanCertify |

**Operaciones:**
```
cert.pgp.generateSigning   { identity, algorithm, passphrase, expiryYears }
cert.pgp.signFile          { privateKeyPath, passphrase, inputPath } → { signaturePath, fingerprint }
```

La segunda operación **cierra el flujo de 17.6**: `publicar.ps1` llama a `cert.pgp.signFile` para firmar `latest.json`.

#### 19.3 — Inspección universal (estilo `ssh-keygen -L`)

Un solo inspector que reconozca el formato automáticamente y muestre la información:

| Formato de entrada | Muestra |
|---|---|
| X.509 PEM/DER (.pem, .crt, .der) | Subject, Issuer, Serial, Validez, Algoritmo, SANs, Fingerprint SHA-256 |
| Clave privada PEM (RSA/EC/Ed25519) | Algoritmo, tamaño de clave, tipo (PKCS#8/PKCS#1) |
| PFX/PKCS#12 (.pfx, .p12) | Certificado + cadena (con password si aplica) |
| CSR (.pem, .req) | Subject, Algoritmo, SANs solicitados |
| PGP pública (.asc) | Fingerprint, Algoritmo, Identidad, Expiración |
| SSH pública (.pub, id_ed25519, id_rsa) | Tipo, Fingerprint, Comentario |

**Operación:**
```
cert.inspect   { path, password? } → { format, algorithm, detailsJson, fingerprint }
```

#### 19.4 — Conversión de formatos

```
cert.convert   { inputPath, inputFormat, outputFormat, password?, outputPassword? } → { outputPath }
```

| Desde | Hacia | Caso de uso |
|---|---|---|
| PEM | DER | Binario para Java keystore |
| DER | PEM | Leer en OpenSSL |
| PEM | PFX/PKCS#12 | Importar a Windows cert store |
| PFX/PKCS#12 | PEM | Extraer cert+key de un bundle |
| OpenSSH | PEM | Usar clave SSH en OpenSSL |

#### 19.5 — CSR (Certificate Signing Request)

Generar un PKCS#10 CSR para enviar a una CA real (Let's Encrypt, DigiCert, etc.):

```
cert.csr.generate   { commonName, organization, sans, algorithm, keySize }
→ { csrPem, privateKeyPem }
```

El usuario lleva el CSR a la CA, la CA firma, y el usuario puede importar el certificado firmado de vuelta.

#### 19.6 — Integración con 17.6 (firma del manifiesto)

Flujo cerrado dentro de PCSuite:
1. `cert.pgp.generateSigning` → clave de release de AES Studio
2. `cert.pgp.signFile` → firma `latest.json`
3. El updater valida con la clave pública embebida

`publicar.ps1` llama a PCSuite (vía CLI o bridge) para firmar — no necesita gpg instalado.

---

## UI: página "Certificados"

| Sección | Contenido |
|---|---|
| **Generar** | Tabs: X.509 / PGP / CSR — formulario con parámetros, preview del resultado |
| **Inspeccionar** | Drop-zone o botón "Seleccionar archivo" → resultado estructurado |
| **Convertir** | Selector origen/destino + archivo → descarga |
| **Mis claves** | Lista de PGP keys generadas (reusa PgpService.ListKeysAsync) |
| **SSH keys** | Reusa SshKeyService.ListAsync (ya existe) |

---

## Seguirdad (coherente con el diseño de PCSuite)

- **Todo BouncyCastle en proceso** — sin gpg, OpenSSL ni procesos externos (excepto ssh-keygen que ya se usa)
- **Claves privadas NUNCA en el bridge** — el servicio las escribe a archivo y devuelve solo la ruta + fingerprint
- **Passphrase con `FixedTimeEquals`** — mismo patrón que el CertificatesGenerator original
- **PFX con password obligatoria** — no exportar bundles sin cifrar
- **Sin secretos en logs** — fingerprints sí, passphrases jamás

---

## Qué NO se implementa (fuera de scope)

| Excluido | Razón |
|---|---|
| SSH Certificate Authority (`ssh-keygen -s`) | Niche, requiere infraestructura CA completa |
| Let's Encrypt ACME client | Requiere servidor web para challenges |
| Code signing (Authenticode) | Requiere certificado de pago de una CA real |
| Certificate chain validation | Solo generamos; validar contra CAs es un lector de(bundle) aparte |
