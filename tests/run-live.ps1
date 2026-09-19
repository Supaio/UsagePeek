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
$output = Join-Path $outputDirectory 'LiveProviderQa.exe'

& $compiler /nologo /utf8output /target:exe /optimize+ `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Web.Extensions.dll `
    "/out:$output" `
    (Join-Path $root 'UsageModels.cs') `
    (Join-Path $root 'IUsageProvider.cs') `
    (Join-Path $root 'CodexResponseParser.cs') `
    (Join-Path $root 'CodexExecutableLocator.cs') `
    (Join-Path $root 'LocalUsageScanner.cs') `
    (Join-Path $root 'CodexUsageProvider.cs') `
    (Join-Path $PSScriptRoot 'LiveProviderQa.cs')

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $output
exit $LASTEXITCODE
