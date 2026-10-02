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
    "/resource:$(Join-Path $root 'assets\pet-whale-maid-curled.png'),UsagePeek.Assets.PetWhaleMaidCurled.png" `
    "/out:$output" `
    (Join-Path $root 'UsageModels.cs') `
    (Join-Path $root 'DisplayFormatting.cs') `
    (Join-Path $root 'DpiAwareForm.cs') `
    (Join-Path $root 'UiControls.cs') `
    (Join-Path $root 'UsageCardControl.cs') `
    (Join-Path $root 'UsageDetailsControl.cs') `
    (Join-Path $root 'MainForm.cs') `
    (Join-Path $root 'ModelUsageForm.cs') `
    (Join-Path $root 'AboutForm.cs') `
    (Join-Path $root 'PetForm.cs') `
    (Join-Path $PSScriptRoot 'DpiLayoutQa.cs')

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $output
exit $LASTEXITCODE
