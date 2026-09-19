param([string]$HostName = '127.0.0.1', [int]$Port = 5000)
$ErrorActionPreference = 'Stop'

function New-Envelope([int]$type, $payload, [int]$version = 1) {
    $id = [guid]::NewGuid()
    $inner = $payload | ConvertTo-Json -Compress -Depth 20
    $env = [ordered]@{ ProtocolVersion=$version; MessageId=$id; Type=$type; Payload=$inner }
    [pscustomobject]@{ Id=$id; Json=($env | ConvertTo-Json -Compress -Depth 20) }
}
function Read-Exact($stream, [int]$count) {
    $buffer = [byte[]]::new($count); $offset = 0
    while ($offset -lt $count) { $n=$stream.Read($buffer,$offset,$count-$offset); if($n -eq 0){throw 'EOF'}; $offset += $n }
    $buffer
}
function Read-Message($client) {
    $s=$client.GetStream(); $h=Read-Exact $s 4
    $len=([int]$h[0]-shl 24)-bor([int]$h[1]-shl 16)-bor([int]$h[2]-shl 8)-bor[int]$h[3]
    $json=[Text.Encoding]::UTF8.GetString((Read-Exact $s $len))
    $m=$json|ConvertFrom-Json
    $p=$null; if($m.Payload){$p=$m.Payload|ConvertFrom-Json}
    [pscustomobject]@{ Envelope=$m; Payload=$p; Raw=$json }
}
function Send-Request($client,[int]$type,$payload,[int]$version=1) {
    $e=New-Envelope $type $payload $version; $b=[Text.Encoding]::UTF8.GetBytes($e.Json)
    $h=[byte[]]@([byte](($b.Length -shr 24) -band 255),[byte](($b.Length -shr 16) -band 255),[byte](($b.Length -shr 8) -band 255),[byte]($b.Length -band 255))
    $s=$client.GetStream(); $s.Write($h,0,4); $s.Write($b,0,$b.Length); $s.Flush()
    $events=@()
    while($true){$m=Read-Message $client; if($m.Envelope.CorrelationId -eq $e.Id){return [pscustomobject]@{Response=$m;Events=$events}}; $events += $m}
}
function New-Client { $c=[Net.Sockets.TcpClient]::new(); $c.ReceiveTimeout=10000; $c.SendTimeout=10000; $c.Connect($HostName,$Port); $c }
function Out-Case($id,$label,$value){ "[$id] $label=$($value|ConvertTo-Json -Compress -Depth 12)" }

$tag=[guid]::NewGuid().ToString('N').Substring(0,8)
$a=New-Client; $b=New-Client; $c=New-Client
try {
    $ha=Send-Request $a 0 @{PlayerId="exec-a-$tag";PlayerName='Execution A'}; Out-Case 'TC-006' 'HANDSHAKE_A' @{PlayerId=$ha.Response.Payload.PlayerId;TokenIssued=![string]::IsNullOrWhiteSpace($ha.Response.Payload.SessionToken)}
    $hb=Send-Request $b 0 @{PlayerId="exec-b-$tag";PlayerName='Execution B'}; Out-Case 'TC-006' 'HANDSHAKE_B' @{PlayerId=$hb.Response.Payload.PlayerId;TokenIssued=![string]::IsNullOrWhiteSpace($hb.Response.Payload.SessionToken)}
    $hc=Send-Request $c 0 @{PlayerId="exec-c-$tag";PlayerName='Execution C'}; Out-Case 'TC-100' 'HANDSHAKE_C' @{PlayerId=$hc.Response.Payload.PlayerId;TokenIssued=![string]::IsNullOrWhiteSpace($hc.Response.Payload.SessionToken)}
    $list=Send-Request $a 8 @{}; Out-Case 'TC-099' 'LIST_INITIAL_TYPE' $list.Response.Envelope.Type
    $create=Send-Request $a 9 @{RoomName="Execution Room $tag";Difficulty=1}; $room=$create.Response.Payload; Out-Case 'TC-016' 'CREATE_ROOM' $room
    $join=Send-Request $b 10 @{RoomId=$room.RoomId}; Out-Case 'TC-016' 'JOIN_ROOM' $join.Response.Payload
    $joinC=Send-Request $c 10 @{RoomId=$room.RoomId}; Out-Case 'TC-017' 'THIRD_JOIN' @{Type=$joinC.Response.Envelope.Type;Error=$joinC.Response.Envelope.Error}
    $start=Send-Request $a 13 @{RoomId=[string]$room.RoomId;Difficulty=1;Duration=5}; $sa=$start.Response.Payload; Out-Case 'TC-018' 'START_A' $sa
    $preparedB=Read-Message $b; Out-Case 'TC-018' 'PREPARED_B' $preparedB.Payload
    $readyA=Send-Request $a 15 @{MatchId=$sa.MatchId}; Out-Case 'TC-018' 'READY_A' $readyA.Response.Payload
    $readyB=Send-Request $b 15 @{MatchId=$sa.MatchId}; Out-Case 'TC-018' 'READY_B' $readyB.Response.Payload
    $statusA=(Send-Request $a 17 @{MatchId=$sa.MatchId}).Response.Payload
    $statusB=(Send-Request $b 17 @{MatchId=$sa.MatchId}).Response.Payload
    $same=(($statusA.Puzzle|ConvertTo-Json -Compress) -eq ($statusB.Puzzle|ConvertTo-Json -Compress)); Out-Case 'TC-023' 'SAME_PUZZLE' @{Same=$same;PuzzleA=$statusA.Puzzle;PuzzleB=$statusB.Puzzle}
    $sameInitial=(($statusA.OwnBoard|ConvertTo-Json -Compress) -eq ($statusB.OwnBoard|ConvertTo-Json -Compress)); Out-Case 'TC-024' 'SAME_INITIAL_BOARD' @{Same=$sameInitial}
    $badRow=Send-Request $a 19 @{MatchId=$sa.MatchId;MoveId=[guid]::NewGuid().ToString('N');Row=-1;Column=0;Value=1}; Out-Case 'TC-043' 'ROW_MINUS_1' $badRow.Response.Payload
    $badCol=Send-Request $a 19 @{MatchId=$sa.MatchId;MoveId=[guid]::NewGuid().ToString('N');Row=0;Column=9;Value=1}; Out-Case 'TC-043' 'COLUMN_9' $badCol.Response.Payload
    $badVal=Send-Request $a 19 @{MatchId=$sa.MatchId;MoveId=[guid]::NewGuid().ToString('N');Row=0;Column=0;Value=10}; Out-Case 'TC-044' 'VALUE_10' $badVal.Response.Payload
    $outside=Send-Request $c 19 @{MatchId=$sa.MatchId;MoveId=[guid]::NewGuid().ToString('N');Row=0;Column=0;Value=1}; Out-Case 'TC-046' 'OUTSIDER_MOVE' $outside.Response.Payload
    $given=0; while($given -lt 81 -and $statusA.Puzzle[$given] -eq 0){$given++}; $gr=[math]::Floor($given/9); $gc=$given%9
    $givenMove=Send-Request $a 19 @{MatchId=$sa.MatchId;MoveId=[guid]::NewGuid().ToString('N');Row=$gr;Column=$gc;Value=0}; Out-Case 'TC-045' 'GIVEN_MOVE' $givenMove.Response.Payload
    $listActive=(Send-Request $c 8 @{}).Response.Payload; $active=($listActive.Rooms|Where-Object RoomId -eq $room.RoomId).HasActiveMatch; Out-Case 'TC-070' 'HAS_ACTIVE_MATCH' $active
    $token=$ha.Response.Payload.SessionToken; $a.Close(); Start-Sleep -Milliseconds 500
    $disconnectEvent=Read-Message $b; Out-Case 'TC-090' 'B_EVENT_AFTER_A_CLOSE' @{Type=$disconnectEvent.Envelope.Type;Payload=$disconnectEvent.Payload}
    $a2=New-Client; $reconnect=Send-Request $a2 5 @{SessionToken=$token}; Out-Case 'TC-095' 'RECONNECT_A' @{PlayerId=$reconnect.Response.Payload.PlayerId;TokenReused=($reconnect.Response.Payload.SessionToken -eq $token)}
    $restored=(Send-Request $a2 17 @{MatchId=$sa.MatchId}).Response.Payload; Out-Case 'TC-096' 'RESTORED_STATUS' $restored
    $a2.Close()
} finally { try{$a.Close()}catch{}; try{$b.Close()}catch{}; try{$c.Close()}catch{} }

$raw=New-Client
try { $unsupported=Send-Request $raw 8 @{} 2; Out-Case 'TC-086' 'UNSUPPORTED_PROTOCOL' @{Type=$unsupported.Response.Envelope.Type;Error=$unsupported.Response.Envelope.Error} } finally {$raw.Close()}
