@echo off
setlocal
chcp 65001 >nul
title PCSuite - empaquetar dist instalable
cd /d "%~dp0"

echo ============================================
echo  PCSuite - construir el dist instalable
echo  (autocontenido win-x64 + zip)
echo ============================================

REM Version: unica fuente = Directory.Build.props
set "VERSION=0.0.0"
for /f "tokens=3 delims=<>" %%A in ('findstr /c:"<Version>" Directory.Build.props') do set "VERSION=%%A"
echo  Version: %VERSION%

REM Prerrequisitos
where npm >nul 2>&1 || goto :error_prereq
where dotnet >nul 2>&1 || goto :error_prereq

echo.
echo [1/5] Compilando el frontend (frontend/dist)...
if not exist frontend\node_modules (
    call npm --prefix frontend install
    if errorlevel 1 goto :error
)
call npm --prefix frontend run build
if errorlevel 1 goto :error

echo.
echo [2/5] Limpiando dist anterior...
if exist dist rmdir /s /q dist
mkdir dist\PCSuite-win-x64

echo.
echo [3/5] Publicando PCSuite (Release, win-x64, autocontenida)...
dotnet publish src\PCSuite.Desktop\PCSuite.Desktop.csproj -c Release -r win-x64 --self-contained -o dist\PCSuite-win-x64
if errorlevel 1 goto :error

echo.
echo [4/5] Publicando el helper elevado (PCSuite.Elevated)...
dotnet publish src\PCSuite.Elevated\PCSuite.Elevated.csproj -c Release -r win-x64 --self-contained -o dist\PCSuite-win-x64
if errorlevel 1 goto :error

echo.
echo [5/5] Copiando la UI junto al ejecutable...
robocopy frontend\dist dist\PCSuite-win-x64\frontend\dist /E /NFL /NDL /NJH >nul
REM robocopy: 0-7 es exito (1 = archivos copiados), 8+ es error
if errorlevel 8 goto :error

REM Verificacion del paquete: lo que exigen FrontendLocator y Task 005
if not exist dist\PCSuite-win-x64\PCSuite.exe goto :error_paquete
if not exist dist\PCSuite-win-x64\PCSuite.Elevated.exe goto :error_paquete
if not exist dist\PCSuite-win-x64\frontend\dist\index.html goto :error_paquete

echo.
echo Comprimiendo el paquete...
powershell -NoProfile -Command "Compress-Archive -Path 'dist\PCSuite-win-x64' -DestinationPath ('dist\PCSuite-%VERSION%-win-x64.zip') -Force"
if errorlevel 1 goto :error
for %%Z in ("dist\PCSuite-%VERSION%-win-x64.zip") do set /a ZIP_MB=%%~zZ/1048576

echo.
echo ============================================
echo  Paquete PCSuite v%VERSION% (win-x64) listo
echo ============================================
echo   Carpeta : dist\PCSuite-win-x64
echo   Zip     : PCSuite-%VERSION%-win-x64.zip (~%ZIP_MB% MB)
echo.
echo  Instalacion portable:
echo    1. Copia la carpeta PCSuite-win-x64 donde quieras
echo       (p. ej. C:\Program Files\PCSuite).
echo    2. Ejecuta PCSuite.exe.
echo.
echo  Notas:
echo   - No requiere .NET instalado en la maquina destino.
echo   - Requiere WebView2 Runtime, preinstalado en Windows 11 / Edge.
echo   - Nunca ejecutes PCSuite.exe como Administrador: la elevacion
echo     es puntual por operacion y con UAC (PCSuite.Elevated.exe).
echo ============================================
exit /b 0

:error_prereq
echo.
echo  ERROR: faltan 'npm' o 'dotnet' en el PATH.
goto :error_end

:error_paquete
echo.
echo  ERROR: paquete incompleto. Falta PCSuite.exe, PCSuite.Elevated.exe
echo  o frontend\dist\index.html. Revisa los pasos anteriores.
goto :error_end

:error
echo.
echo ============================================
echo  ERROR: fallo al empaquetar.
echo  Revisa los mensajes de arriba.
echo ============================================
:error_end
pause
exit /b 1
