@echo off
setlocal
cd /d "%~dp0"
if exist "artifacts\Ftracker-actualizado\Ftracker.exe" (
  start "" "artifacts\Ftracker-actualizado\Ftracker.exe"
  exit /b
)
if exist "artifacts\Ftracker\Ftracker.exe" (
  start "" "artifacts\Ftracker\Ftracker.exe"
  exit /b
)
if exist "src\Ftracker.Desktop\bin\Debug\net10.0-windows\Ftracker.exe" (
  start "" "src\Ftracker.Desktop\bin\Debug\net10.0-windows\Ftracker.exe"
  exit /b
)
echo Primero compila Ftracker.Desktop o genera el ejecutable publicado.
pause
