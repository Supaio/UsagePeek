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
  /resource:"assets\pet-whale-maid-curled.png",UsagePeek.Assets.PetWhaleMaidCurled.png ^
  /resource:"assets\pet-phoebe-chibi.png",UsagePeek.Assets.PetPhoebeChibi.png ^
  /resource:"assets\pet-chatgpt-girl.png",UsagePeek.Assets.PetChatGptGirl.png ^
  /resource:"assets\pet-claude-girl.png",UsagePeek.Assets.PetClaudeGirl.png ^
  /resource:"assets\pet-milk-frog.png",UsagePeek.Assets.PetMilkFrog.png ^
  /resource:"assets\clawd\clawd-idle-living.gif",UsagePeek.Assets.Clawd.clawd-idle-living.gif ^
  /resource:"assets\clawd\clawd-crab-walking.gif",UsagePeek.Assets.Clawd.clawd-crab-walking.gif ^
  /resource:"assets\clawd\clawd-happy.gif",UsagePeek.Assets.Clawd.clawd-happy.gif ^
  /resource:"assets\clawd\clawd-working-confused.gif",UsagePeek.Assets.Clawd.clawd-working-confused.gif ^
  /resource:"assets\clawd\clawd-working-typing.gif",UsagePeek.Assets.Clawd.clawd-working-typing.gif ^
  /resource:"assets\clawd\LICENSE.txt",UsagePeek.Assets.Clawd.LICENSE.txt ^
  /resource:"assets\petpet-hand-v2.png",UsagePeek.Assets.PetpetHandV2.png ^
  /resource:"assets\provider-codex.png",UsagePeek.Assets.ProviderCodex.png ^
  /resource:"assets\provider-antigravity.png",UsagePeek.Assets.ProviderAntigravity.png ^
  /resource:"assets\provider-claude.png",UsagePeek.Assets.ProviderClaude.png ^
  /resource:"assets\provider-ustc.jpg",UsagePeek.Assets.ProviderUstc.jpg ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Web.Extensions.dll ^
  /out:"%OUTPUT%" ^
  Program.cs UsageModels.cs IUsageProvider.cs CodexResponseParser.cs ^
  CodexExecutableLocator.cs CodexUsageProvider.cs CodexTpsMonitor.cs LocalUsageScanner.cs UsageCache.cs DisplayFormatting.cs ^
  AdditionalUsageReader.cs AntigravityUsageDatabase.cs SourceQuotaStatusLine.cs AdditionalUsagePanel.cs UsageSourceButton.cs ^
  ExchangeRateService.cs StartupManager.cs ExecutableIntegrity.cs UpdateService.cs CodexBootstrapService.cs ^
  DisplayModeSettings.cs DiagnosticsSnapshot.cs SettingsForm.cs ^
  PetRigAnimation.cs PetGifAnimation.cs AnimationFrameClock.cs LayeredPetSurface.cs ^
  PetForm.cs PetSizeDialog.cs TaskbarCreatedListener.cs ^
  ResetCreditNotificationTracker.cs QuotaHistory.cs ^
  DpiAwareForm.cs UiTheme.cs UiControls.cs SelectionControls.cs UsageCardControl.cs UsageDetailsControl.cs ^
  MainForm.cs UsageActivityControl.cs ModelUsageForm.cs UsageHistoryForm.cs UsageBreakdownForm.cs AboutForm.cs IconFactory.cs TrayApplicationContext.cs

if errorlevel 1 (
  echo.
  echo Build failed.
  exit /b 1
)

echo.
echo Build succeeded: %OUTPUT%
exit /b 0
