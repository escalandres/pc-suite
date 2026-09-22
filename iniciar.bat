@echo off
setlocal
chcp 65001 >nul
title PCSuite - compilar y lanzar
cd /d "%~dp0"

echo ============================================
echo  PCSuite - compilar y lanzar
echo ============================================

echo.
echo [1/3] Compilando la UI (frontend)...
if not exist frontend\node_modules (
    call npm --prefix frontend install
    if errorlevel 1 goto :error
)
call npm --prefix frontend run build
if errorlevel 1 goto :error

echo.
echo [2/3] Compilando la solucion .NET...
dotnet build PCSuite.sln
if errorlevel 1 goto :error

echo.
echo [3/3] Lanzando PCSuite...
dotnet run --project src/PCSuite.Desktop --no-build
if errorlevel 1 goto :error

echo.
echo App cerrada correctamente.
exit /b 0

:error
echo.
echo ============================================
echo  ERROR: fallo al compilar o lanzar la app.
echo  Revisa los mensajes de arriba.
echo ============================================
pause
exit /b 1
