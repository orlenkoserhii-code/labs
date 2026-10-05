param(
    [ValidateSet('before', 'after')][string]$Phase = 'after',
    [string]$Api = 'http://127.0.0.1:5082'
)
$ErrorActionPreference = 'Stop'
$cases = [ordered]@{
    A = 'USB'
    B = 'zz-no-match'
    C = "zz-no-match' OR TRUE -- "
    D = "O'Brien"
}
$evidence = foreach ($entry in $cases.GetEnumerator()) {
    $url = "$Api/api/incidents/search?q=$([uri]::EscapeDataString($entry.Value))"
    $response = & curl.exe --silent --show-error --include $url
    if ($LASTEXITCODE -ne 0) { throw "HTTP check $($entry.Key) failed" }
    "CASE $($entry.Key) ($Phase)"
    $response
    ''
}
$target = Join-Path $PSScriptRoot "../docs/evidence/search-$Phase.txt"
$evidence | Set-Content -Encoding utf8 $target
$evidence
