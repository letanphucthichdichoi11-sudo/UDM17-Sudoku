param([int]$DurationSeconds=420, [int]$Clients=4, [int]$SampleEverySeconds=10, [string]$MetricsPath='D:\prj_sudoku\testing\performance\TC-122_soak_metrics.csv')
$ErrorActionPreference='Stop'
$hostName='127.0.0.1'; $port=5000
function New-Client {
    $c=[Net.Sockets.TcpClient]::new(); $c.ReceiveTimeout=10000; $c.SendTimeout=10000; $c.Connect($hostName,$port); $c
}
function Read-Exact($stream,[int]$count) {
    $bytes=[byte[]]::new($count); $offset=0
    while($offset -lt $count){$read=$stream.Read($bytes,$offset,$count-$offset);if($read -eq 0){throw 'EOF'};$offset+=$read}
    $bytes
}
function Read-Message($client) {
    $stream=$client.GetStream(); $header=Read-Exact $stream 4
    $n=([int]$header[0]-shl24)-bor([int]$header[1]-shl16)-bor([int]$header[2]-shl8)-bor[int]$header[3]
    $m=([Text.Encoding]::UTF8.GetString((Read-Exact $stream $n))|ConvertFrom-Json)
    $payload=if($m.Payload){$m.Payload|ConvertFrom-Json}else{$null}
    [pscustomobject]@{Envelope=$m;Payload=$payload}
}
function Request($client,[int]$type,$payload) {
    $id=[guid]::NewGuid(); $inner=$payload|ConvertTo-Json -Compress -Depth 10
    $json=[ordered]@{ProtocolVersion=1;MessageId=$id;Type=$type;Payload=$inner}|ConvertTo-Json -Compress -Depth 10
    $bytes=[Text.Encoding]::UTF8.GetBytes($json)
    $header=[byte[]]@([byte](($bytes.Length-shr24)-band255),[byte](($bytes.Length-shr16)-band255),[byte](($bytes.Length-shr8)-band255),[byte]($bytes.Length-band255))
    $stream=$client.GetStream();$stream.Write($header,0,4);$stream.Write($bytes,0,$bytes.Length);$stream.Flush()
    while($true){$msg=Read-Message $client;if($msg.Envelope.CorrelationId -eq $id){return $msg}}
}
$listener=Get-NetTCPConnection -LocalPort $port -State Listen | Select-Object -First 1
if(-not $listener){throw 'Game Server is not listening'}
$serverPid=[int]$listener.OwningProcess
$process=Get-Process -Id $serverPid
$tag=[guid]::NewGuid().ToString('N').Substring(0,8)
$connections=@();$rooms=@();$matches=@();$rows=[Collections.Generic.List[object]]::new()
$sent=0;$errors=0;$latencyTotal=0.0;$latencyMax=0.0;$serverCrashed=$false
"PLAN TC-122 duration=${DurationSeconds}s clients=$Clients matches=$([int]($Clients/2)) sample=${SampleEverySeconds}s heartbeat=1/client/second serverPid=$serverPid tag=$tag"
try {
    for($i=0;$i -lt $Clients;$i++){
        $c=New-Client; $hello=Request $c 0 @{PlayerId="soak-$tag-$i";PlayerName="Soak $i"}
        if($hello.Envelope.Type -ne 1){throw "Handshake failed for client $i"}
        $connections+=,$c
    }
    for($i=0;$i+1 -lt $Clients;$i+=2){
        $room=(Request $connections[$i] 9 @{RoomName="Soak $tag $i";Difficulty=0}).Payload
        if(-not $room.RoomId){throw "CreateRoom failed for pair $i"}
        $rooms+=,[string]$room.RoomId
        $null=Request $connections[$i+1] 10 @{RoomId=[string]$room.RoomId}
        $start=Request $connections[$i] 13 @{RoomId=[string]$room.RoomId;Difficulty=0;Duration=5}
        if($start.Envelope.Type -ne 14){throw "StartMatch failed for pair $i"}
        $matchId=[string]$start.Payload.MatchId;$matches+=,$matchId
        $null=Request $connections[$i] 15 @{MatchId=$matchId}
        $null=Request $connections[$i+1] 15 @{MatchId=$matchId}
    }
    "SETUP complete rooms=$($rooms.Count) matches=$($matches.Count) at=$(Get-Date -Format o)"
    $clock=[Diagnostics.Stopwatch]::StartNew();$nextSample=0;$lastCpu=$process.TotalProcessorTime.TotalMilliseconds;$lastSampleElapsed=0
    while($clock.Elapsed.TotalSeconds -lt $DurationSeconds){
        foreach($c in $connections){
            try{
                $watch=[Diagnostics.Stopwatch]::StartNew();$reply=Request $c 2 @{};$watch.Stop()
                if($reply.Envelope.Type -ne 3){throw "Unexpected heartbeat response Type=$($reply.Envelope.Type)"}
                $sent++;$latencyTotal+=$watch.Elapsed.TotalMilliseconds
                if($watch.Elapsed.TotalMilliseconds -gt $latencyMax){$latencyMax=$watch.Elapsed.TotalMilliseconds}
            }catch{$errors++;"ERROR elapsed=$([math]::Round($clock.Elapsed.TotalSeconds,1)) detail=$($_.Exception.Message)"}
        }
        $elapsed=$clock.Elapsed.TotalSeconds
        if($elapsed -ge $nextSample){
            $live=Get-Process -Id $serverPid -ErrorAction SilentlyContinue
            if(-not $live){$serverCrashed=$true;"ERROR server-exited elapsed=$([math]::Round($elapsed,1))";break}
            $cpuNow=$live.TotalProcessorTime.TotalMilliseconds
            $deltaSeconds=[math]::Max(0.001,$elapsed-$lastSampleElapsed)
            $cpuPercent=($cpuNow-$lastCpu)/($deltaSeconds*1000*[Environment]::ProcessorCount)*100
            $row=[pscustomobject]@{Timestamp=(Get-Date -Format o);ElapsedSeconds=[math]::Round($elapsed,2);Clients=$connections.Count;Matches=$matches.Count;Messages=$sent;Errors=$errors;AvgLatencyMs=if($sent){[math]::Round($latencyTotal/$sent,3)}else{0};MaxLatencyMs=[math]::Round($latencyMax,3);CpuPercentApprox=[math]::Round($cpuPercent,3);RamMB=[math]::Round($live.WorkingSet64/1MB,2);Handles=$live.HandleCount;ServerAlive=$true}
            $rows.Add($row);$row|ConvertTo-Json -Compress
            $lastCpu=$cpuNow;$lastSampleElapsed=$elapsed;$nextSample+=$SampleEverySeconds
        }
        Start-Sleep -Milliseconds 800
    }
    $clock.Stop()
    $rows | Export-Csv -LiteralPath $MetricsPath -NoTypeInformation -Encoding UTF8
    [pscustomobject]@{TestCase='TC-122';PlannedDurationSeconds=$DurationSeconds;ActualDurationSeconds=[math]::Round($clock.Elapsed.TotalSeconds,2);Clients=$connections.Count;Matches=$matches.Count;Messages=$sent;Errors=$errors;ThroughputMsgPerSec=if($clock.Elapsed.TotalSeconds){[math]::Round($sent/$clock.Elapsed.TotalSeconds,3)}else{0};AvgLatencyMs=if($sent){[math]::Round($latencyTotal/$sent,3)}else{0};MaxLatencyMs=[math]::Round($latencyMax,3);ServerCrashed=$serverCrashed;Samples=$rows.Count;MetricsPath=$MetricsPath}|ConvertTo-Json -Compress
}
finally{foreach($c in $connections){try{$c.Close()}catch{}}}
