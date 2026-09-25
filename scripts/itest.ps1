# Runs the integration tests against a local Graylog in docker.
# Starts Graylog, OpenSearch and MongoDB, seeds synthetic messages through a GELF HTTP input,
# creates an access token, runs tests/Glq.Cli.IntegrationTests, and tears everything down.
# Usage:  pwsh -File ./scripts/itest.ps1 [-KeepRunning]

[CmdletBinding()]
param(
    # Leave the containers up afterwards, for poking at them by hand.
    [switch]$KeepRunning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$compose = Join-Path $repoRoot 'tests/Glq.Cli.IntegrationTests/docker-compose.yml'
$graylog = 'http://127.0.0.1:9000'
$admin = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('admin:admin'))
$headers = @{ Authorization = "Basic $admin"; 'X-Requested-By' = 'glq-itest'; Accept = 'application/json' }

function Wait-Until([string]$What, [scriptblock]$Condition, [int]$TimeoutSeconds = 300) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try { if (& $Condition) { return } } catch { }
        Start-Sleep -Seconds 3
    }
    throw "Timed out waiting for $What."
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker is required for the integration tests.'
}

docker compose -f $compose up -d
if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed.' }

try {
    Wait-Until 'Graylog to start' {
        (Invoke-RestMethod "$graylog/api/system/lbstatus") -eq 'ALIVE'
    }

    # A GELF HTTP input to receive the synthetic messages.
    $inputs = Invoke-RestMethod "$graylog/api/system/inputs" -Headers $headers
    if (-not ($inputs.inputs | Where-Object title -eq 'glq-itest')) {
        $body = @{
            title = 'glq-itest'
            type = 'org.graylog2.inputs.gelf.http.GELFHttpInput'
            global = $true
            configuration = @{ bind_address = '0.0.0.0'; port = 12201; recv_buffer_size = 1048576; decompress_size_limit = 8388608 }
        } | ConvertTo-Json -Depth 5
        Invoke-RestMethod "$graylog/api/system/inputs" -Method Post -Headers $headers -ContentType 'application/json' -Body $body | Out-Null
    }

    Wait-Until 'the GELF input' {
        Invoke-RestMethod 'http://127.0.0.1:12201/gelf' -Method Post -ContentType 'application/json' `
            -Body (@{ version = '1.1'; host = 'itest-probe'; short_message = 'probe' } | ConvertTo-Json) | Out-Null
        $true
    } 120

    # Synthetic messages only - the tests look for these exact values.
    $messages = @(
        @{ host = 'itest-api'; short_message = 'GET /orders 500 for jane.doe@example.com'; level = 3; _http_status = 500; _customer_id = 'C-1001' }
        @{ host = 'itest-api'; short_message = 'GET /orders 200'; level = 6; _http_status = 200; _customer_id = 'C-1002' }
        @{ host = 'itest-payments'; short_message = 'charged card 4111111111111111'; level = 6; _http_status = 200; _customer_id = 'C-1001' }
        @{ host = 'itest-payments'; short_message = 'payment declined'; level = 3; _http_status = 402; _customer_id = 'C-1003' }
    )
    foreach ($message in $messages) {
        $message.version = '1.1'
        Invoke-RestMethod 'http://127.0.0.1:12201/gelf' -Method Post -ContentType 'application/json' -Body ($message | ConvertTo-Json) | Out-Null
    }

    Wait-Until 'the messages to be searchable' {
        $found = Invoke-RestMethod "$graylog/api/search/universal/relative?query=source:itest*&range=3600&limit=10" -Headers $headers
        $found.total_results -ge $messages.Count
    } 120

    # An access token for the admin user - what glq authenticates with.
    $user = Invoke-RestMethod "$graylog/api/users/admin" -Headers $headers
    $token = Invoke-RestMethod "$graylog/api/users/$($user.id)/tokens/glq-itest-$([Guid]::NewGuid().ToString('N'))" `
        -Method Post -Headers $headers -ContentType 'application/json' -Body '{}'

    $env:GLQ_ITEST_URL = $graylog
    $env:GLQ_ITEST_TOKEN = $token.token
    try {
        dotnet test (Join-Path $repoRoot 'tests/Glq.Cli.IntegrationTests')
        if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
    }
    finally {
        Remove-Item Env:GLQ_ITEST_URL, Env:GLQ_ITEST_TOKEN -ErrorAction SilentlyContinue
    }
}
finally {
    if (-not $KeepRunning) {
        docker compose -f $compose down -v
    }
}
