@echo off
setlocal
chcp 65001 >nul
title AES Studio - publicar PCSuite (sitio + manifiesto + SHA-256)
cd /d "%~dp0"

REM ============================================================
REM  publicar.bat (Task 17.1) — wrapper fino de scripts\publicar.ps1
REM
REM  Genera la carpeta autocontenida del release (Task 022):
REM    releases\<ver>\latest.json        (manifiesto live del updater)
REM    releases\<ver>\manifest.json      (copia del historial inmutable)
REM    releases\<ver>\latest.json.sig    (firma PGP)
REM    releases\<ver>\latest.json.pub    (clave pública)
REM    releases\<ver>\<ver>.html         (notas de versión)
REM
REM  Prerequisito: empaquetar.bat + instalador.iss ya corrieron.
REM  El SHA-256 se computa del archivo exacto que se publica.
REM  Mapa de copia al repo Astro: releases\COPIAR-A-ASTRO.md
REM
REM  Uso: publicar.bat [producto]   (solo 'pcsuite' hoy)
REM ============================================================

powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publicar.ps1 -Product pcsuite %*
if errorlevel 1 (
    echo.
    pause
    exit /b 1
)
exit /b 0
