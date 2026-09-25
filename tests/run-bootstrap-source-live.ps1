$ErrorActionPreference = 'Stop'

$client = [System.Net.WebClient]::new()
$client.Headers['User-Agent'] = 'UsagePeek/0.3.5'
try {
    $content = $client.DownloadString('https://chatgpt.com/codex/install.ps1')
} finally {
    $client.Dispose()
}
if ($content.Length -lt 1000) {
    throw 'Official installer response was unexpectedly short.'
}
if ($content -notmatch 'releases\.openai\.com/codex') {
    $hosts = [regex]::Matches($content, 'https://[A-Za-z0-9.-]+') |
        ForEach-Object Value | Sort-Object -Unique
    throw ('Official installer response did not contain the expected release source. Hosts: ' +
        ($hosts -join ', '))
}

Write-Output ('Official bootstrap source QA passed: ' +
    $content.Length + ' characters.')
