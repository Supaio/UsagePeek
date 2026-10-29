$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
$output = Join-Path $root 'bin\PetRigAnimationQa.exe'
& $compiler /nologo /utf8output /target:exe /optimize+ /define:RIG_STANDALONE `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll "/out:$output" `
    (Join-Path $root 'DisplayModeSettings.cs') (Join-Path $root 'PetRigAnimation.cs') `
    (Join-Path $PSScriptRoot 'PetRigAnimationQa.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $output $root
exit $LASTEXITCODE
