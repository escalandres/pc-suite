@echo off
setlocal
chcp 65001 >nul
title AES Studio - publicar PCSuite (sitio + manifiesto + SHA-256)
cd /d "%~dp0"

REM ============================================================
REM  publicar.bat (Task 17.1) — wrapper fino de scripts\publicar.ps1
REM
REM  Genera el sitio de AES Studio para la version actual:
REM    site\pcsuite\latest.json          (manifiesto del updater)
REM    site\pcsuite\download\*.exe|.zip  (artfactos)
REM    site\archive\<ver>\manifest.json  (historial inmutable)
REM
REM  Prerequisito: empaquetar.bat + instalador.iss ya corrieron.
REM  El SHA-256 se computa del archivo exacto que se publica.
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
