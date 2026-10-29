param(
    [string]$ApplicationPath,
    [string]$OutputPath,
    [string]$FfmpegPath = 'D:\oopz\ffmpeg.exe',
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not $ApplicationPath) { $ApplicationPath = Join-Path $root 'bin\UsagePeek.UI-preview.exe' }
if (-not $OutputPath) { $OutputPath = Join-Path $root 'bin\continuous-animation-preview.mp4' }
$executable = Join-Path $root 'bin\ContinuousAnimationQa.exe'
& $compiler /nologo /utf8output /target:exe /optimize+ `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll "/out:$executable" `
    (Join-Path $PSScriptRoot 'ContinuousAnimationQa.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$qaArguments = @($ApplicationPath, $OutputPath, $FfmpegPath)
if ($ValidateOnly) { $qaArguments += '--validate-only' }
& $executable @qaArguments
exit $LASTEXITCODE
