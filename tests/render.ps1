$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$outputDirectory = Join-Path $root 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$renderer = Join-Path $outputDirectory 'RenderQa.exe'
$preview = Join-Path $root 'preview.png'

& $compiler /nologo /utf8output /target:exe /optimize+ `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    "/out:$renderer" `
    (Join-Path $root 'UsageModels.cs') `
    (Join-Path $root 'DisplayFormatting.cs') `
    (Join-Path $root 'UiControls.cs') `
    (Join-Path $root 'UsageCardControl.cs') `
    (Join-Path $root 'UsageDetailsControl.cs') `
    (Join-Path $root 'MainForm.cs') `
    (Join-Path $PSScriptRoot 'RenderQa.cs')

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $renderer $preview
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Output $preview
