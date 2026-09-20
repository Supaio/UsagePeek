@echo off
setlocal
cd /d "%~dp0"

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
  echo [ERROR] Windows .NET Framework compiler was not found.
  echo Enable .NET Framework 4.x in Windows Features and try again.
  exit /b 1
)

if not exist "bin" mkdir "bin"

if not exist "assets\usagepeek.ico" (
  echo [ERROR] Application icon was not found: assets\usagepeek.ico
  exit /b 1
)

set "OUTPUT=bin\UsagePeek.exe"
if not "%~1"=="" set "OUTPUT=%~1"

"%CSC%" /nologo /utf8output /target:winexe /optimize+ /debug- /platform:anycpu ^
  /win32icon:"assets\usagepeek.ico" ^
  /win32manifest:app.manifest ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Web.Extensions.dll ^
  /out:"%OUTPUT%" ^
  Program.cs UsageModels.cs IUsageProvider.cs CodexResponseParser.cs ^
  CodexExecutableLocator.cs CodexUsageProvider.cs LocalUsageScanner.cs UsageCache.cs DisplayFormatting.cs ^
  ExchangeRateService.cs StartupManager.cs UpdateService.cs CodexBootstrapService.cs ^
  ResetCreditNotificationTracker.cs ^
  UiControls.cs UsageCardControl.cs UsageDetailsControl.cs ^
  MainForm.cs IconFactory.cs TrayApplicationContext.cs

if errorlevel 1 (
  echo.
  echo Build failed.
  exit /b 1
)

echo.
echo Build succeeded: %OUTPUT%
exit /b 0
