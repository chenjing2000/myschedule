@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] dotnet not found. Install .NET 8 SDK first.
  pause
  exit /b 1
)
dotnet restore
if errorlevel 1 goto :fail
dotnet build -c Release
if errorlevel 1 goto :fail
echo.
echo Build succeeded.
echo Output: bin\Release\net8.0-windows\
pause
exit /b 0
:fail
echo.
echo Build failed.
pause
exit /b 1
