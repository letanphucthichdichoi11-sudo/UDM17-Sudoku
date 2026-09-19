param([string]$HostName = '127.0.0.1', [int]$Port = 5000, [switch]$OnlyWrongFieldType)
$ErrorActionPreference = 'Stop'

function New-Client {
    $client = [Net.Sockets.TcpClient]::new()
    $client.ReceiveTimeout = 2000
    $client.SendTimeout = 2000
    $client.Connect($HostName, $Port)
    $client
}
function New-Envelope([int]$type, $payload = @{}, [int]$version = 1) {
    $id = [guid]::NewGuid()
    $inner = $payload | ConvertTo-Json -Compress -Depth 20
    $json = [ordered]@{ ProtocolVersion = $version; MessageId = $id; Type = $type; Payload = $inner } | ConvertTo-Json -Compress -Depth 20
    [pscustomobject]@{ Id = $id; Bytes = [Text.Encoding]::UTF8.GetBytes($json) }
}
function Send-Frame($client, [byte[]]$body, [int]$declaredLength = -1) {
    if ($declaredLength -lt 0) { $declaredLength = $body.Length }
    $header = [byte[]]@([byte](($declaredLength -shr 24) -band 255), [byte](($declaredLength -shr 16) -band 255), [byte](($declaredLength -shr 8) -band 255), [byte]($declaredLength -band 255))
    $stream = $client.GetStream()
    $stream.Write($header, 0, 4)
    if ($body.Length -gt 0) { $stream.Write($body, 0, $body.Length) }
    $stream.Flush()
}
function Read-Exact($stream, [int]$count) {
    $buffer = [byte[]]::new($count)
    $offset = 0
    while ($offset -lt $count) {
        $read = $stream.Read($buffer, $offset, $count - $offset)
        if ($read -eq 0) { throw 'EOF' }
        $offset += $read
    }
    $buffer
}
function Read-Message($client) {
    $stream = $client.GetStream()
    $h = Read-Exact $stream 4
    $length = ([int]$h[0] -shl 24) -bor ([int]$h[1] -shl 16) -bor ([int]$h[2] -shl 8) -bor [int]$h[3]
    $message = [Text.Encoding]::UTF8.GetString((Read-Exact $stream $length)) | ConvertFrom-Json
    [pscustomobject]@{ Type = $message.Type; Error = $message.Error; CorrelationId = $message.CorrelationId; Payload = if ($message.Payload) { $message.Payload | ConvertFrom-Json } else { $null } }
}
function Send-Request($client, [int]$type, $payload = @{}, [int]$version = 1) {
    $envelope = New-Envelope $type $payload $version
    Send-Frame $client $envelope.Bytes
    $messages = @()
    while ($true) {
        $message = Read-Message $client
        $messages += $message
        if ($message.CorrelationId -eq $envelope.Id) { return [pscustomobject]@{ Response = $message; Messages = $messages } }
    }
}
function Show($caseId, $step, $value) {
    [pscustomobject]@{ TestCase = $caseId; Step = $step; Result = $value } | ConvertTo-Json -Compress -Depth 15
}
function Try-Read($client) {
    try {
        $message = Read-Message $client
        return [pscustomobject]@{ Outcome = 'Message'; Type = $message.Type; Error = $message.Error }
    }
    catch {
        return [pscustomobject]@{ Outcome = $_.Exception.GetType().Name; Message = $_.Exception.Message }
    }
}
function New-Authenticated($tag) {
    $client = New-Client
    $handshake = Send-Request $client 0 @{ PlayerId = "frame-$tag"; PlayerName = 'Frame Tester' }
    if ($handshake.Response.Type -ne 1) { throw "Handshake failed for $tag" }
    $client
}

if ($OnlyWrongFieldType) {
    $client = New-Client
    try {
        $body = [Text.Encoding]::UTF8.GetBytes('{"ProtocolVersion":1,"MessageId":"00000000-0000-0000-0000-000000000001","Type":"not-an-integer","Payload":"{}"}')
        Send-Frame $client $body
        Show 'TC-082' 'wrong-field-type' (Try-Read $client)
    }
    finally { $client.Close() }
    $health = New-Authenticated ([guid]::NewGuid().ToString('N').Substring(0, 8))
    try { Show 'TC-082' 'new-client-after-wrong-field-type' @{ Connected = $health.Connected } }
    finally { $health.Close() }
    return
}

$tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
"RUN tag=$tag at=$(Get-Date -Format o)"

$before = New-Client
try {
    $notAuthorized = Send-Request $before 8 @{}
    Show 'TC-098' 'list-before-handshake' @{ Type = $notAuthorized.Response.Type; Error = $notAuthorized.Response.Error }
    $hello = Send-Request $before 0 @{ PlayerId = "preauth-$tag"; PlayerName = 'Preauth Tester' }
    Show 'TC-098' 'handshake-after-unauthorized' @{ Type = $hello.Response.Type; PlayerId = $hello.Response.Payload.PlayerId; TokenIssued = ![string]::IsNullOrWhiteSpace($hello.Response.Payload.SessionToken) }
}
finally { $before.Close() }

$split = New-Client
try {
    $envelope = New-Envelope 0 @{ PlayerId = "split-$tag"; PlayerName = 'Split Tester' }
    $body = $envelope.Bytes
    $header = [byte[]]@([byte](($body.Length -shr 24) -band 255), [byte](($body.Length -shr 16) -band 255), [byte](($body.Length -shr 8) -band 255), [byte]($body.Length -band 255))
    $stream = $split.GetStream()
    foreach ($part in @($header[0..1], $header[2..3], $body[0..4], $body[5..($body.Length - 1)])) {
        $stream.Write([byte[]]$part, 0, $part.Count)
        $stream.Flush()
        Start-Sleep -Milliseconds 30
    }
    $response = Read-Message $split
    Show 'TC-097' 'split-header-and-body' @{ Type = $response.Type; CorrelationMatches = ($response.CorrelationId -eq $envelope.Id); PlayerId = $response.Payload.PlayerId }
    $heartbeat = New-Envelope 2 @{}
    $list = New-Envelope 8 @{}
    $all = [byte[]]::new(4 + $heartbeat.Bytes.Length + 4 + $list.Bytes.Length)
    $offset = 0
    foreach ($item in @($heartbeat, $list)) {
        $length = $item.Bytes.Length
        $frameHeader = [byte[]]@([byte](($length -shr 24) -band 255), [byte](($length -shr 16) -band 255), [byte](($length -shr 8) -band 255), [byte]($length -band 255))
        [Array]::Copy($frameHeader, 0, $all, $offset, 4); $offset += 4
        [Array]::Copy($item.Bytes, 0, $all, $offset, $length); $offset += $length
    }
    $stream.Write($all, 0, $all.Length)
    $stream.Flush()
    $first = Read-Message $split
    $second = Read-Message $split
    Show 'TC-097' 'coalesced-heartbeat-list' @{ FirstType = $first.Type; FirstCorrelationMatches = ($first.CorrelationId -eq $heartbeat.Id); SecondType = $second.Type; SecondCorrelationMatches = ($second.CorrelationId -eq $list.Id) }
}
finally { $split.Close() }

foreach ($scenario in @(
    @{ Name = 'length-zero'; Length = 0; Body = [byte[]]@() },
    @{ Name = 'length-over-1MiB'; Length = 1048577; Body = [byte[]]@() },
    @{ Name = 'malformed-json'; Length = -1; Body = [Text.Encoding]::UTF8.GetBytes('{bad json') },
    @{ Name = 'wrong-field-type'; Length = -1; Body = [Text.Encoding]::UTF8.GetBytes('{"ProtocolVersion":1,"MessageId":"00000000-0000-0000-0000-000000000001","Type":"not-an-integer","Payload":"{}"}') },
    @{ Name = 'invalid-utf8'; Length = -1; Body = [byte[]]@(0xC3, 0x28) }
)) {
    $client = New-Client
    try {
        Send-Frame $client $scenario.Body $scenario.Length
        $caseId = if ($scenario.Name -like 'length-*') { 'TC-083' } else { 'TC-082' }
        Show $caseId $scenario.Name (Try-Read $client)
    }
    finally { $client.Close() }
    $health = New-Authenticated "$($scenario.Name)-$tag"
    try { Show 'TC-082/083' "new-client-after-$($scenario.Name)" @{ Connected = $health.Connected } }
    finally { $health.Close() }
}

$partial = New-Client
try {
    $stream = $partial.GetStream()
    $stream.Write([byte[]]@(0, 0, 0, 20), 0, 4)
    $stream.Write([byte[]]@(123, 34, 120), 0, 3)
    $stream.Flush()
}
finally { $partial.Close() }
Start-Sleep -Milliseconds 200
$health = New-Authenticated "partial-$tag"
try { Show 'TC-083' 'new-client-after-partial-frame' @{ Connected = $health.Connected } }
finally { $health.Close() }

"END at=$(Get-Date -Format o)"
