$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$outputDirectory = Join-Path $root 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$output = Join-Path $outputDirectory 'DpiLayoutQa.exe'

& $compiler /nologo /utf8output /target:exe /optimize+ `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    "/resource:$(Join-Path $root 'assets\pet-whale-maid-curled.png'),UsagePeek.Assets.PetWhaleMaidCurled.png" `
    "/resource:$(Join-Path $root 'assets\pet-phoebe-chibi.png'),UsagePeek.Assets.PetPhoebeChibi.png" `
    "/resource:$(Join-Path $root 'assets\pet-chatgpt-girl.png'),UsagePeek.Assets.PetChatGptGirl.png" `
    "/resource:$(Join-Path $root 'assets\pet-claude-girl.png'),UsagePeek.Assets.PetClaudeGirl.png" `
    "/resource:$(Join-Path $root 'assets\pet-milk-frog.png'),UsagePeek.Assets.PetMilkFrog.png" `
    "/resource:$(Join-Path $root 'assets\clawd\clawd-idle-living.gif'),UsagePeek.Assets.Clawd.clawd-idle-living.gif" `
    "/resource:$(Join-Path $root 'assets\clawd\clawd-crab-walking.gif'),UsagePeek.Assets.Clawd.clawd-crab-walking.gif" `
    "/resource:$(Join-Path $root 'assets\clawd\clawd-happy.gif'),UsagePeek.Assets.Clawd.clawd-happy.gif" `
    "/resource:$(Join-Path $root 'assets\clawd\clawd-working-confused.gif'),UsagePeek.Assets.Clawd.clawd-working-confused.gif" `
    "/resource:$(Join-Path $root 'assets\clawd\clawd-working-typing.gif'),UsagePeek.Assets.Clawd.clawd-working-typing.gif" `
    "/resource:$(Join-Path $root 'assets\clawd\LICENSE.txt'),UsagePeek.Assets.Clawd.LICENSE.txt" `
    "/resource:$(Join-Path $root 'assets\petpet-hand-v2.png'),UsagePeek.Assets.PetpetHandV2.png" `
    "/resource:$(Join-Path $root 'assets\provider-codex.png'),UsagePeek.Assets.ProviderCodex.png" `
    "/resource:$(Join-Path $root 'assets\provider-antigravity.png'),UsagePeek.Assets.ProviderAntigravity.png" `
    "/resource:$(Join-Path $root 'assets\provider-claude.png'),UsagePeek.Assets.ProviderClaude.png" `
    "/resource:$(Join-Path $root 'assets\provider-ustc.jpg'),UsagePeek.Assets.ProviderUstc.jpg" `
    "/out:$output" `
    (Join-Path $root 'UsageModels.cs') `
    (Join-Path $root 'DisplayModeSettings.cs') `
    (Join-Path $root 'DiagnosticsSnapshot.cs') `
    (Join-Path $root 'ExecutableIntegrity.cs') `
    (Join-Path $root 'DisplayFormatting.cs') `
    (Join-Path $root 'DpiAwareForm.cs') `
    (Join-Path $root 'UiTheme.cs') `
    (Join-Path $root 'UiControls.cs') `
    (Join-Path $root 'SelectionControls.cs') `
    (Join-Path $root 'UsageCardControl.cs') `
    (Join-Path $root 'UsageDetailsControl.cs') `
    (Join-Path $root 'MainForm.cs') `
    (Join-Path $root 'UsageActivityControl.cs') `
    (Join-Path $root 'ModelUsageForm.cs') `
    (Join-Path $root 'UsageHistoryForm.cs') `
    (Join-Path $root 'UsageBreakdownForm.cs') `
    (Join-Path $root 'AdditionalUsagePanel.cs') `
    (Join-Path $root 'UsageSourceButton.cs') `
    (Join-Path $root 'AdditionalUsageReader.cs') `
    (Join-Path $root 'AntigravityUsageDatabase.cs') `
    (Join-Path $root 'SourceQuotaStatusLine.cs') `
    (Join-Path $root 'AntigravityDesktopStatus.cs') `
    (Join-Path $root 'QuotaHistory.cs') `
    (Join-Path $root 'LocalUsageScanner.cs') `
    (Join-Path $root 'OpenAiModelPrices.cs') `
    (Join-Path $root 'AntigravityModelPrices.cs') `
    (Join-Path $root 'UsageCache.cs') `
    (Join-Path $root 'AboutForm.cs') `
    (Join-Path $root 'SettingsForm.cs') `
    (Join-Path $root 'PetSizeDialog.cs') `
    (Join-Path $root 'PetForm.cs') `
    (Join-Path $root 'PetRigAnimation.cs') `
    (Join-Path $root 'PetGifAnimation.cs') `
    (Join-Path $root 'AnimationFrameClock.cs') `
    (Join-Path $root 'LayeredPetSurface.cs') `
    (Join-Path $PSScriptRoot 'DpiLayoutQa.cs')

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $output
exit $LASTEXITCODE
