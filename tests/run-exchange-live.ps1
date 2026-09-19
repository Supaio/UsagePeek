$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$outputDirectory = Join-Path $root 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$output = Join-Path $outputDirectory 'ExchangeRateQa.exe'

& $compiler /nologo /utf8output /target:exe /optimize+ `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Web.Extensions.dll `
    "/out:$output" `
    (Join-Path $root 'UsageModels.cs') `
    (Join-Path $root 'DisplayFormatting.cs') `
    (Join-Path $root 'ExchangeRateService.cs') `
    (Join-Path $PSScriptRoot 'ExchangeRateQa.cs')

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $output
exit $LASTEXITCODE
