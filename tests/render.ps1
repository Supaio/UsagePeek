$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$outputDirectory = Join-Path $root 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$renderer = Join-Path $outputDirectory 'RenderQa.exe'
$preview = if ($args.Count -gt 0) {
    $args[0]
} else {
    Join-Path $root 'preview.png'
}
$mode = if ($args.Count -gt 1) { $args[1] } else { '' }

& $compiler /nologo /utf8output /target:exe /optimize+ `
    "/win32manifest:$(Join-Path $root 'app.manifest')" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    "/resource:$(Join-Path $root 'assets\pet-whale-maid-curled.png'),UsagePeek.Assets.PetWhaleMaidCurled.png" `
    "/resource:$(Join-Path $root 'assets\pet-phoebe-chibi.png'),UsagePeek.Assets.PetPhoebeChibi.png" `
    "/resource:$(Join-Path $root 'assets\petpet-hand-v2.png'),UsagePeek.Assets.PetpetHandV2.png" `
    "/out:$renderer" `
    (Join-Path $root 'UsageModels.cs') `
    (Join-Path $root 'DisplayModeSettings.cs') `
    (Join-Path $root 'DiagnosticsSnapshot.cs') `
    (Join-Path $root 'DisplayFormatting.cs') `
    (Join-Path $root 'DpiAwareForm.cs') `
    (Join-Path $root 'UiControls.cs') `
    (Join-Path $root 'UsageCardControl.cs') `
    (Join-Path $root 'UsageDetailsControl.cs') `
    (Join-Path $root 'MainForm.cs') `
    (Join-Path $root 'ModelUsageForm.cs') `
    (Join-Path $root 'AboutForm.cs') `
    (Join-Path $root 'SettingsForm.cs') `
    (Join-Path $root 'PetSizeDialog.cs') `
    (Join-Path $root 'PetForm.cs') `
    (Join-Path $PSScriptRoot 'RenderQa.cs')

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $renderer $preview $mode
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Output $preview
