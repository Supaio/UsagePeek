$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Windows .NET Framework compiler was not found.'
}

$outputDirectory = Join-Path $root 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$output = Join-Path $outputDirectory 'PetModeQa.exe'

& $compiler /nologo /utf8output /target:exe /optimize+ `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    "/resource:$(Join-Path $root 'assets\pet-whale-maid-curled.png'),UsagePeek.Assets.PetWhaleMaidCurled.png" `
    "/out:$output" `
    (Join-Path $root 'DpiAwareForm.cs') `
    (Join-Path $root 'DisplayModeSettings.cs') `
    (Join-Path $root 'UsageModels.cs') `
    (Join-Path $root 'PetForm.cs') `
    (Join-Path $PSScriptRoot 'PetModeQa.cs')

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $output
exit $LASTEXITCODE
