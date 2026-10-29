param([string]$ApplicationPath,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(-not $ApplicationPath){$ApplicationPath=Join-Path $root 'bin\UsagePeek.UI-preview.exe'}
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root 'bin\eye-review'}
$compiler="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if(-not(Test-Path -LiteralPath $compiler)){$compiler="$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"}
$executable=Join-Path $root 'bin\EyeAlignmentQa.exe'
& $compiler /nologo /utf8output /target:exe /optimize+ /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll "/out:$executable" (Join-Path $PSScriptRoot 'EyeAlignmentQa.cs')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& $executable $ApplicationPath $OutputDirectory
exit $LASTEXITCODE
