$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$outputDirectory = Join-Path $root 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$executable = Join-Path $outputDirectory 'WinExeProviderQa.exe'
$result = Join-Path $outputDirectory 'WinExeProviderQa.result.json'

& $compiler /nologo /utf8output /target:winexe /optimize+ `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Web.Extensions.dll `
    "/out:$executable" `
    (Join-Path $root 'UsageModels.cs') `
    (Join-Path $root 'IUsageProvider.cs') `
    (Join-Path $root 'CodexResponseParser.cs') `
    (Join-Path $root 'CodexExecutableLocator.cs') `
    (Join-Path $root 'LocalUsageScanner.cs') `
    (Join-Path $root 'CodexUsageProvider.cs') `
    (Join-Path $PSScriptRoot 'WinExeProviderQa.cs')

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (Test-Path -LiteralPath $result) {
    Remove-Item -LiteralPath $result -Force
}

$process = Start-Process -FilePath $executable -ArgumentList ('"' + $result + '"') -PassThru
if (-not $process.WaitForExit(30000)) {
    Stop-Process -Id $process.Id
    throw 'WinExe provider QA timed out.'
}

if (-not (Test-Path -LiteralPath $result)) {
    throw 'WinExe provider QA did not produce a result.'
}

Get-Content -LiteralPath $result -Raw
