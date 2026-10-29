$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
$output = Join-Path $root 'bin\AnimationFrameClockQa.exe'
& $compiler /nologo /utf8output /target:exe /optimize+ `
    /reference:System.dll /reference:System.Core.dll `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    "/out:$output" (Join-Path $root 'AnimationFrameClock.cs') `
    (Join-Path $PSScriptRoot 'AnimationFrameClockQa.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $output
exit $LASTEXITCODE
