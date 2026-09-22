param([string]$HostName = '127.0.0.1', [int]$Port = 5000, [switch]$ConcurrentOnly, [switch]$TimeoutOnly, [switch]$IdempotencyOnly, [switch]$GraceOnly, [switch]$CompletionOnly, [switch]$RestoreOnly, [switch]$AuthorityOnly, [switch]$BothDisconnectOnly, [switch]$FinishRaceOnly, [switch]$IsolationTimerOnly, [switch]$TimeoutBeforeGraceOnly, [switch]$ActiveListOnly, [switch]$DeadlineMoveOnly, [switch]$BindingOnly, [switch]$ListUpdateOnly, [switch]$GraceTimeoutOrderOnly, [switch]$EventOrderOnly, [switch]$ResultMemoryOnly)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
public static class SudokuTestSolver {
    public static int[] Solve(int[] puzzle) {
        var board = (int[])puzzle.Clone();
        if (!Fill(board)) throw new InvalidOperationException("Puzzle has no solution.");
        return board;
    }
    private static bool Fill(int[] board) {
        int best = -1, bestCount = 10, bestMask = 0;
        for (int i = 0; i < 81; i++) {
            if (board[i] != 0) continue;
            int row = i / 9, col = i % 9, used = 0;
            for (int j = 0; j < 9; j++) {
                used |= 1 << board[row * 9 + j];
                used |= 1 << board[j * 9 + col];
                int boxRow = (row / 3) * 3 + j / 3;
                int boxCol = (col / 3) * 3 + j % 3;
                used |= 1 << board[boxRow * 9 + boxCol];
            }
            int mask = 0, count = 0;
            for (int n = 1; n <= 9; n++) if ((used & (1 << n)) == 0) { mask |= 1 << n; count++; }
            if (count == 0) return false;
            if (count < bestCount) { best = i; bestCount = count; bestMask = mask; }
        }
        if (best < 0) return true;
        for (int n = 1; n <= 9; n++) if ((bestMask & (1 << n)) != 0) {
            board[best] = n;
            if (Fill(board)) return true;
        }
        board[best] = 0;
        return false;
    }
}
'@

function New-Client {
    $client = [Net.Sockets.TcpClient]::new()
    $client.ReceiveTimeout = 5000
    $client.SendTimeout = 5000
    $client.Connect($HostName, $Port)
    $client
}
function Read-Exact($stream, [int]$count) {
    $buffer = [byte[]]::new($count); $offset = 0
    while ($offset -lt $count) {
        $read = $stream.Read($buffer, $offset, $count - $offset)
        if ($read -eq 0) { throw 'EOF' }
        $offset += $read
    }
    $buffer
}
function Read-Message($client) {
    $s = $client.GetStream(); $h = Read-Exact $s 4
    $len = ([int]$h[0] -shl 24) -bor ([int]$h[1] -shl 16) -bor ([int]$h[2] -shl 8) -bor [int]$h[3]
    $m = [Text.Encoding]::UTF8.GetString((Read-Exact $s $len)) | ConvertFrom-Json
    [pscustomobject]@{ Envelope = $m; Payload = if ($m.Payload) { $m.Payload | ConvertFrom-Json } else { $null } }
}
function Send-Request($client, [int]$type, $payload = @{}) {
    $id = [guid]::NewGuid()
    $inner = $payload | ConvertTo-Json -Compress -Depth 20
    $envelope = [ordered]@{ ProtocolVersion = 1; MessageId = $id; Type = $type; Payload = $inner }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($envelope | ConvertTo-Json -Compress -Depth 20))
    $header = [byte[]]@([byte](($bytes.Length -shr 24) -band 255), [byte](($bytes.Length -shr 16) -band 255), [byte](($bytes.Length -shr 8) -band 255), [byte]($bytes.Length -band 255))
    $stream = $client.GetStream(); $stream.Write($header, 0, 4); $stream.Write($bytes, 0, $bytes.Length); $stream.Flush()
    $events = @()
    while ($true) {
        $message = Read-Message $client
        if ($message.Envelope.CorrelationId -eq $id) { return [pscustomobject]@{ Response = $message; Events = $events } }
        $events += $message
    }
}
function Show($caseId, $step, $data) {
    [pscustomobject]@{ TestCase = $caseId; Step = $step; Data = $data } | ConvertTo-Json -Compress -Depth 15
}
function Status($client, $matchId) { (Send-Request $client 17 @{ MatchId = $matchId }).Response.Payload }
function Digest($status) {
    [pscustomobject]@{
        MatchId = $status.MatchId
        State = $status.State
        OwnBoard = @($status.OwnBoard)
        OpponentBoard = @($status.OpponentBoard)
        OwnCorrectCount = $status.OwnCorrectCount
        OpponentCorrectCount = $status.OpponentCorrectCount
        OwnErrorCount = $status.OwnErrorCount
        OpponentErrorCount = $status.OpponentErrorCount
        OwnHasUnresolvedMistake = $status.OwnHasUnresolvedMistake
        OpponentHasUnresolvedMistake = $status.OpponentHasUnresolvedMistake
        StartedAtUtc = $status.StartedAtUtc
        EndsAtUtc = $status.EndsAtUtc
        WinnerPlayerId = $status.WinnerPlayerId
    }
}
function Send-Move($client, $matchId, [int]$index, [int]$value, [string]$moveId = '') {
    if (-not $moveId) { $moveId = [guid]::NewGuid().ToString('N') }
    $result = Send-Request $client 19 @{ MatchId = $matchId; MoveId = $moveId; Row = [math]::Floor($index / 9); Column = ($index % 9); Value = $value }
    [pscustomobject]@{ Response = $result.Response.Payload; Events = @($result.Events | ForEach-Object { $_.Envelope.Type }); MoveId = $moveId }
}
function Send-RequestCustomId($client,[int]$type,$payload,[guid]$messageId){
    $inner=$payload|ConvertTo-Json -Compress -Depth 20
    $envelope=[ordered]@{ProtocolVersion=1;MessageId=$messageId;Type=$type;Payload=$inner}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($envelope|ConvertTo-Json -Compress -Depth 20))
    $header=[byte[]]@([byte](($bytes.Length-shr24)-band255),[byte](($bytes.Length-shr16)-band255),[byte](($bytes.Length-shr8)-band255),[byte]($bytes.Length-band255))
    $stream=$client.GetStream();$stream.Write($header,0,4);$stream.Write($bytes,0,$bytes.Length);$stream.Flush()
    $events=@()
    while($true){$m=Read-Message $client;if($m.Envelope.CorrelationId -eq $messageId){return [pscustomobject]@{Response=$m;Events=$events}};$events+=,$m}
}
function Send-Frame($client,[int]$type,$payload,[guid]$messageId){
    $inner=$payload|ConvertTo-Json -Compress -Depth 20
    $envelope=[ordered]@{ProtocolVersion=1;MessageId=$messageId;Type=$type;Payload=$inner}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($envelope|ConvertTo-Json -Compress -Depth 20))
    $header=[byte[]]@([byte](($bytes.Length-shr24)-band255),[byte](($bytes.Length-shr16)-band255),[byte](($bytes.Length-shr8)-band255),[byte]($bytes.Length-band255))
    $stream=$client.GetStream();$stream.Write($header,0,4);$stream.Write($bytes,0,$bytes.Length);$stream.Flush()
}
function Await-Frame($client,[guid]$messageId){
    $events=@();while($true){$m=Read-Message $client;if($m.Envelope.CorrelationId -eq $messageId){return [pscustomobject]@{Response=$m;Events=$events}};$events+=,$m}
}

if($FinishRaceOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-058 finish-race tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client
    try{
        $aId="finish-a-$tag";$bId="finish-b-$tag"
        $null=Send-Request $a 0 @{PlayerId=$aId;PlayerName='Finish A'}
        $null=Send-Request $b 0 @{PlayerId=$bId;PlayerName='Finish B'}
        $room=Send-Request $a 9 @{RoomName="Finish $tag";Difficulty=0}
        if($room.Response.Envelope.Type -ne 9){throw "CreateRoom setup failed: $($room.Response.Envelope.Error.Message)"}
        $roomId=[string]$room.Response.Payload.RoomId
        $null=Send-Request $b 10 @{RoomId=$roomId}
        $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=0;Duration=5}
        $matchId=[string]$start.Response.Payload.MatchId
        $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        $initialA=Status $a $matchId;$initialB=Status $b $matchId
        $solutionA=[SudokuTestSolver]::Solve([int[]]$initialA.Puzzle);$solutionB=[SudokuTestSolver]::Solve([int[]]$initialB.Puzzle)
        $blankA=@();$blankB=@();for($i=0;$i -lt 81;$i++){if($initialA.Puzzle[$i] -eq 0){$blankA+=,$i};if($initialB.Puzzle[$i] -eq 0){$blankB+=,$i}}
        for($i=0;$i -lt $blankA.Count-1;$i++){$r=Send-Move $a $matchId $blankA[$i] $solutionA[$blankA[$i]];if(!$r.Response.Accepted -or !$r.Response.IsCorrect){throw "A prefill failed at $i"}}
        for($i=0;$i -lt $blankB.Count-1;$i++){$r=Send-Move $b $matchId $blankB[$i] $solutionB[$blankB[$i]];if(!$r.Response.Accepted -or !$r.Response.IsCorrect){throw "B prefill failed at $i"}}
        $beforeA=Status $a $matchId;$beforeB=Status $b $matchId
        Show 'TC-058' 'before-final' @{MatchId=$matchId;ACorrect=$beforeA.OwnCorrectCount;BCorrect=$beforeB.OwnCorrectCount;ABlanks=$blankA.Count;BBlanks=$blankB.Count;State=$beforeA.State}
        $indexA=[int]$blankA[-1];$indexB=[int]$blankB[-1]
        $moveA=[guid]::NewGuid().ToString('N');$moveB=[guid]::NewGuid().ToString('N')
        $msgA=[guid]::NewGuid();$msgB=[guid]::NewGuid()
        $payloadA=@{MatchId=$matchId;MoveId=$moveA;Row=[math]::Floor($indexA/9);Column=$indexA%9;Value=$solutionA[$indexA]}
        $payloadB=@{MatchId=$matchId;MoveId=$moveB;Row=[math]::Floor($indexB/9);Column=$indexB%9;Value=$solutionB[$indexB]}
        $watch=[Diagnostics.Stopwatch]::StartNew();Send-Frame $a 19 $payloadA $msgA;Send-Frame $b 19 $payloadB $msgB;$sendMs=$watch.Elapsed.TotalMilliseconds
        $responseA=Await-Frame $a $msgA;$responseB=Await-Frame $b $msgB;$watch.Stop()
        $statusA=Status $a $matchId;$statusB=Status $b $matchId
        $retry=Send-RequestCustomId $a 19 $payloadA $msgA
        $afterRetry=Status $a $matchId
        Show 'TC-058' 'after-final-race' @{SendTwoFramesMs=[math]::Round($sendMs,3);ElapsedMs=[math]::Round($watch.Elapsed.TotalMilliseconds,3);AResponseType=$responseA.Response.Envelope.Type;AResponse=$responseA.Response.Payload;AError=$responseA.Response.Envelope.Error;BResponseType=$responseB.Response.Envelope.Type;BResponse=$responseB.Response.Payload;BError=$responseB.Response.Envelope.Error;AEvents=@($responseA.Events|ForEach-Object{$_.Envelope.Type});BEvents=@($responseB.Events|ForEach-Object{$_.Envelope.Type});StateA=$statusA.State;StateB=$statusB.State;WinnerA=$statusA.WinnerPlayerId;WinnerB=$statusB.WinnerPlayerId;ReasonA=$statusA.FinishReason;ReasonB=$statusB.FinishReason;ACorrect=$statusA.OwnCorrectCount;BCorrect=$statusB.OwnCorrectCount;FinishedAtA=$statusA.FinishedAtUtc;FinishedAtB=$statusB.FinishedAtUtc;RetryType=$retry.Response.Envelope.Type;RetryResponse=$retry.Response.Payload;RetryError=$retry.Response.Envelope.Error;WinnerAfterRetry=$afterRetry.WinnerPlayerId;StateAfterRetry=$afterRetry.State}
        "END TC-058 at=$(Get-Date -Format o)"
    }finally{$a.Close();$b.Close()}
    return
}

if($IsolationTimerOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-066 two-match-timer tag=$tag at=$(Get-Date -Format o)"
    $clients=@();$matchIds=@();$playerIds=@()
    try{
        for($i=0;$i -lt 4;$i++){
            $client=New-Client;$clients+=,$client
            $playerId="isolate-$i-$tag";$playerIds+=,$playerId
            $null=Send-Request $client 0 @{PlayerId=$playerId;PlayerName=$playerId}
        }
        for($pair=0;$pair -lt 2;$pair++){
            $a=$clients[$pair*2];$b=$clients[$pair*2+1]
            $room=Send-Request $a 9 @{RoomName="Isolate $tag $pair";Difficulty=0}
            if($room.Response.Envelope.Type -ne 9){throw "CreateRoom pair $pair failed: $($room.Response.Envelope.Error.Message)"}
            $roomId=[string]$room.Response.Payload.RoomId
            $null=Send-Request $b 10 @{RoomId=$roomId}
            $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=0;Duration=5}
            if($start.Response.Envelope.Type -ne 14){throw "Start pair $pair failed"}
            $matchId=[string]$start.Response.Payload.MatchId;$matchIds+=,$matchId
            $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        }
        $initial1=Status $clients[0] $matchIds[0];$initial2=Status $clients[2] $matchIds[1]
        $solution=[SudokuTestSolver]::Solve([int[]]$initial1.Puzzle);$moves=0
        for($i=0;$i -lt 81;$i++){if($initial1.Puzzle[$i] -eq 0){$result=Send-Move $clients[0] $matchIds[0] $i $solution[$i];if(!$result.Response.Accepted -or !$result.Response.IsCorrect){throw "Match1 completion move $i failed"};$moves++}}
        $finished1=Status $clients[0] $matchIds[0];$still2=Status $clients[2] $matchIds[1]
        Show 'TC-066' 'after-match1-completion' @{Match1Id=$matchIds[0];Match1State=$finished1.State;Match1Winner=$finished1.WinnerPlayerId;Match1Reason=$finished1.FinishReason;Match1Moves=$moves;Match2Id=$matchIds[1];Match2State=$still2.State;Match2TimeLeft=$still2.TimeLeft;Match2SameDeadline=($still2.EndsAtUtc -eq $initial2.EndsAtUtc)}
        $watch=[Diagnostics.Stopwatch]::StartNew();$done=$false;$last=0
        while($watch.Elapsed.TotalSeconds -lt 315 -and !$done){
            $status2=Status $clients[2] $matchIds[1]
            if($status2.State -ge 2){$done=$true;Show 'TC-066' 'match2-finished' @{Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,1);State=$status2.State;Reason=$status2.FinishReason;Winner=$status2.WinnerPlayerId;TimeLeft=$status2.TimeLeft}}
            if($watch.Elapsed.TotalSeconds -ge $last+60){"PROGRESS elapsed=$([math]::Round($watch.Elapsed.TotalSeconds,1)) match2state=$($status2.State) match2timeleft=$($status2.TimeLeft) at=$(Get-Date -Format o)";$last=$watch.Elapsed.TotalSeconds}
            if(!$done){Start-Sleep -Seconds 2}
        }
        $after1=Status $clients[0] $matchIds[0];$after2A=Status $clients[2] $matchIds[1];$after2B=Status $clients[3] $matchIds[1]
        Show 'TC-066' 'final' @{Match1State=$after1.State;Match1Winner=$after1.WinnerPlayerId;Match1Reason=$after1.FinishReason;Match2StateA=$after2A.State;Match2StateB=$after2B.State;Match2WinnerA=$after2A.WinnerPlayerId;Match2WinnerB=$after2B.WinnerPlayerId;Match2ReasonA=$after2A.FinishReason;Match2ReasonB=$after2B.FinishReason;MatchIdsDistinct=($matchIds[0] -ne $matchIds[1]);Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,1)}
        "END TC-066 at=$(Get-Date -Format o)"
    }finally{foreach($client in $clients){try{$client.Close()}catch{}}}
    return
}

if($TimeoutBeforeGraceOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-090 timeout-before-grace tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client
    try{
        $aId="order-a-$tag";$bId="order-b-$tag"
        $null=Send-Request $a 0 @{PlayerId=$aId;PlayerName='Order A'}
        $null=Send-Request $b 0 @{PlayerId=$bId;PlayerName='Order B'}
        $room=Send-Request $a 9 @{RoomName="Order $tag";Difficulty=0}
        if($room.Response.Envelope.Type -ne 9){throw "CreateRoom setup failed: $($room.Response.Envelope.Error.Message)"}
        $roomId=[string]$room.Response.Payload.RoomId
        $null=Send-Request $b 10 @{RoomId=$roomId}
        $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=0;Duration=5}
        $matchId=[string]$start.Response.Payload.MatchId
        $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        $initial=Status $b $matchId
        Show 'TC-090' 'initial' @{MatchId=$matchId;State=$initial.State;StartedAtUtc=$initial.StartedAtUtc;EndsAtUtc=$initial.EndsAtUtc;TimeLeft=$initial.TimeLeft}
        $watch=[Diagnostics.Stopwatch]::StartNew()
        while($watch.Elapsed.TotalSeconds -lt 190){
            $status=Status $b $matchId
            if([math]::Floor($watch.Elapsed.TotalSeconds)%30 -lt 2){"PROGRESS pre-close elapsed=$([math]::Round($watch.Elapsed.TotalSeconds,1)) timeleft=$($status.TimeLeft) at=$(Get-Date -Format o)"}
            Start-Sleep -Seconds 5
        }
        $beforeClose=Status $b $matchId;$a.Close();$disconnectAt=$watch.Elapsed.TotalSeconds
        Show 'TC-090' 'A-closed' @{Elapsed=[math]::Round($disconnectAt,1);TimeLeft=$beforeClose.TimeLeft;GraceWouldEndAtElapsed=[math]::Round($disconnectAt+180,1)}
        $done=$false
        while($watch.Elapsed.TotalSeconds -lt 320 -and !$done){
            $status=Status $b $matchId
            if($status.State -ge 2){$done=$true;Show 'TC-090' 'finished' @{Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,1);State=$status.State;FinishReason=$status.FinishReason;Winner=$status.WinnerPlayerId;TimeLeft=$status.TimeLeft;GraceWouldEndAtElapsed=[math]::Round($disconnectAt+180,1)}}
            if(!$done){Start-Sleep -Seconds 2}
        }
        "END TC-090 elapsed=$([math]::Round($watch.Elapsed.TotalSeconds,1)) at=$(Get-Date -Format o)"
    }finally{try{$a.Close()}catch{};try{$b.Close()}catch{}}
    return
}

if($ActiveListOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-068 active-list tag=$tag at=$(Get-Date -Format o)"
    $clients=@();$rooms=@();$matches=@()
    try{
        for($i=0;$i -lt 7;$i++){
            $client=New-Client;$clients+=,$client
            $null=Send-Request $client 0 @{PlayerId="list-$i-$tag";PlayerName="List $i"}
        }
        $pairs=@(@(0),@(1,2),@(3,4),@(5,6))
        for($kind=0;$kind -lt 4;$kind++){
            $owner=$clients[$pairs[$kind][0]]
            $room=Send-Request $owner 9 @{RoomName="List $tag $kind";Difficulty=$kind%3}
            if($room.Response.Envelope.Type -ne 9){throw "CreateRoom $kind failed: $($room.Response.Envelope.Error.Message)"}
            $roomId=[string]$room.Response.Payload.RoomId;$rooms+=,$roomId
            if($kind -eq 0){continue}
            $guest=$clients[$pairs[$kind][1]]
            $null=Send-Request $guest 10 @{RoomId=$roomId}
            $start=Send-Request $owner 13 @{RoomId=$roomId;Difficulty=$kind%3;Duration=5}
            if($start.Response.Envelope.Type -ne 14){throw "Start $kind failed"}
            $matchId=[string]$start.Response.Payload.MatchId;$matches+=,$matchId
            if($kind -ge 2){$null=Send-Request $owner 15 @{MatchId=$matchId};$null=Send-Request $guest 15 @{MatchId=$matchId}}
            if($kind -eq 3){
                $initial=Status $owner $matchId;$solution=[SudokuTestSolver]::Solve([int[]]$initial.Puzzle)
                for($j=0;$j -lt 81;$j++){if($initial.Puzzle[$j] -eq 0){$move=Send-Move $owner $matchId $j $solution[$j];if(!$move.Response.Accepted -or !$move.Response.IsCorrect){throw "Complete $j failed"}}}
            }
        }
        $list=Send-Request $clients[0] 8 @{}
        $seen=@()
        for($kind=0;$kind -lt 4;$kind++){
            $roomState=@($list.Response.Payload.Rooms|Where-Object RoomId -eq $rooms[$kind])[0]
            $matchState=$null
            if($kind -ge 1){$matchState=(Status $clients[$pairs[$kind][0]] $matches[$kind-1]).State}
            $seen+=,[pscustomobject]@{Kind=$kind;RoomId=$rooms[$kind];Found=($null -ne $roomState);RoomName=$roomState.RoomName;Difficulty=$roomState.Difficulty;PlayerCount=@($roomState.Players).Count;HasActiveMatch=$roomState.HasActiveMatch;MatchState=$matchState}
        }
        [pscustomobject]@{TestCase='TC-068';ListType=$list.Response.Envelope.Type;Scenarios=$seen}|ConvertTo-Json -Compress -Depth 8
        "END TC-068 at=$(Get-Date -Format o)"
    }finally{foreach($client in $clients){try{$client.Close()}catch{}}}
    return
}

if($DeadlineMoveOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-054 boundary-moves tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client
    try{
        $aId="deadline-a-$tag";$bId="deadline-b-$tag"
        $null=Send-Request $a 0 @{PlayerId=$aId;PlayerName='Deadline A'}
        $null=Send-Request $b 0 @{PlayerId=$bId;PlayerName='Deadline B'}
        $room=Send-Request $a 9 @{RoomName="Deadline $tag";Difficulty=0}
        if($room.Response.Envelope.Type -ne 9){throw "CreateRoom setup failed: $($room.Response.Envelope.Error.Message)"}
        $roomId=[string]$room.Response.Payload.RoomId
        $null=Send-Request $b 10 @{RoomId=$roomId}
        $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=0;Duration=5}
        $matchId=[string]$start.Response.Payload.MatchId
        $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        $initial=Status $a $matchId;$solution=[SudokuTestSolver]::Solve([int[]]$initial.Puzzle)
        $editable=@();for($i=0;$i -lt 81;$i++){if($initial.Puzzle[$i] -eq 0){$editable+=,$i}}
        Show 'TC-054' 'initial' @{MatchId=$matchId;EndsAtUtc=$initial.EndsAtUtc;TimeLeft=$initial.TimeLeft;State=$initial.State}
        $watch=[Diagnostics.Stopwatch]::StartNew();$near=$null
        while($watch.Elapsed.TotalSeconds -lt 299){
            $near=Status $a $matchId
            $remaining=[Xml.XmlConvert]::ToTimeSpan([string]$near.TimeLeft)
            if($remaining.TotalSeconds -le 2.5){break}
            if($watch.Elapsed.TotalSeconds -ge 30 -and [math]::Floor($watch.Elapsed.TotalSeconds)%30 -lt 1){"PROGRESS elapsed=$([math]::Round($watch.Elapsed.TotalSeconds,1)) remaining=$([math]::Round($remaining.TotalSeconds,1)) at=$(Get-Date -Format o)"}
            $pause=if($remaining.TotalSeconds -gt 10){1000}else{100}
            Start-Sleep -Milliseconds $pause
        }
        $nearMove=Send-Move $a $matchId $editable[0] $solution[$editable[0]]
        $nearStatus=Status $a $matchId
        Show 'TC-054' 'near-deadline' @{Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,3);BeforeTimeLeft=$near.TimeLeft;MoveAccepted=$nearMove.Response.Accepted;MoveCorrect=$nearMove.Response.IsCorrect;ErrorCode=$nearMove.Response.ErrorCode;State=$nearStatus.State;CorrectCount=$nearStatus.OwnCorrectCount;TimeLeft=$nearStatus.TimeLeft}
        $finishEventsA=0
        while($watch.Elapsed.TotalSeconds -lt 305){$statusResult=Send-Request $a 17 @{MatchId=$matchId};$finishEventsA+=@($statusResult.Events|Where-Object{$_.Envelope.Type -eq 22}).Count;$status=$statusResult.Response.Payload;if($status.State -ge 2){break};Start-Sleep -Milliseconds 200}
        $afterMove=Send-Move $a $matchId $editable[1] $solution[$editable[1]]
        $afterAResult=Send-Request $a 17 @{MatchId=$matchId};$afterBResult=Send-Request $b 17 @{MatchId=$matchId}
        $finishEventsA+=@($afterMove.Events|Where-Object{$_.Envelope.Type -eq 22}).Count+@($afterAResult.Events|Where-Object{$_.Envelope.Type -eq 22}).Count
        $finishEventsB=@($afterBResult.Events|Where-Object{$_.Envelope.Type -eq 22}).Count
        $afterA=$afterAResult.Response.Payload;$afterB=$afterBResult.Response.Payload
        $repeatAResult=Send-Request $a 17 @{MatchId=$matchId};$repeatBResult=Send-Request $b 17 @{MatchId=$matchId}
        $finishEventsA+=@($repeatAResult.Events|Where-Object{$_.Envelope.Type -eq 22}).Count;$finishEventsB+=@($repeatBResult.Events|Where-Object{$_.Envelope.Type -eq 22}).Count
        Show 'TC-054' 'after-deadline' @{Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,3);MoveAccepted=$afterMove.Response.Accepted;MoveError=$afterMove.Response.ErrorCode;StateA=$afterA.State;StateB=$afterB.State;CorrectCountA=$afterA.OwnCorrectCount;OpponentCorrectB=$afterB.OpponentCorrectCount;WinnerA=$afterA.WinnerPlayerId;WinnerB=$afterB.WinnerPlayerId;ReasonA=$afterA.FinishReason;ReasonB=$afterB.FinishReason;TimeLeft=$afterA.TimeLeft;FinishedAtA=$afterA.FinishedAtUtc;FinishedAtB=$afterB.FinishedAtUtc;FinishedAtStable=($afterA.FinishedAtUtc -eq $repeatAResult.Response.Payload.FinishedAtUtc -and $afterB.FinishedAtUtc -eq $repeatBResult.Response.Payload.FinishedAtUtc);FinishEventsA=$finishEventsA;FinishEventsB=$finishEventsB}
        "END TC-054 at=$(Get-Date -Format o)"
    }finally{$a.Close();$b.Close()}
    return
}

if($BindingOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-022 binding tag=$tag at=$(Get-Date -Format o)"
    $clients=@();$rooms=@();$matchIds=@();$initials=@();$fingerprints=@()
    try{
        for($i=0;$i -lt 6;$i++){
            $c=New-Client;$clients+=,$c
            $null=Send-Request $c 0 @{PlayerId="binding-$i-$tag";PlayerName="Binding $i"}
        }
        $watch=[Diagnostics.Stopwatch]::StartNew()
        for($pair=0;$pair -lt 3;$pair++){
            $a=$clients[$pair*2];$b=$clients[$pair*2+1]
            $room=Send-Request $a 9 @{RoomName="Binding $tag $pair";Difficulty=1}
            if($room.Response.Envelope.Type -ne 9){throw "CreateRoom $pair failed: $($room.Response.Envelope.Error.Message)"}
            $roomId=[string]$room.Response.Payload.RoomId;$rooms+=,$roomId
            $null=Send-Request $b 10 @{RoomId=$roomId}
            $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=1;Duration=5}
            if($start.Response.Envelope.Type -ne 14){throw "Start $pair failed"}
            $matchIds+=,[string]$start.Response.Payload.MatchId
        }
        $startElapsed=$watch.Elapsed.TotalMilliseconds
        for($pair=0;$pair -lt 3;$pair++){
            $a=$clients[$pair*2];$b=$clients[$pair*2+1];$matchId=$matchIds[$pair]
            $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
            $initial=Status $a $matchId;$initials+=,$initial
            $bytes=[Text.Encoding]::UTF8.GetBytes(($initial.Puzzle -join ','))
            $sha=[Security.Cryptography.SHA256]::Create();try{$hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','').Substring(0,16)}finally{$sha.Dispose()}
            $fingerprints+=,$hash
            $solution=[SudokuTestSolver]::Solve([int[]]$initial.Puzzle)
            $index=0;while($initial.Puzzle[$index] -ne 0){$index++}
            $move=Send-Move $a $matchId $index $solution[$index]
            $afterA=Status $a $matchId;$afterB=Status $b $matchId
            $samePuzzle=(($initial.Puzzle -join ',') -eq ($afterA.Puzzle -join ','))
            [pscustomobject]@{TestCase='TC-022';Pair=$pair;RoomId=$rooms[$pair];MatchId=$matchId;Fingerprint=$hash;PuzzleStable=$samePuzzle;MoveAccepted=$move.Response.Accepted;MoveCorrect=$move.Response.IsCorrect;OwnCorrect=$afterA.OwnCorrectCount;OpponentCorrectB=$afterB.OpponentCorrectCount;StatusMatchA=$afterA.MatchId;StatusMatchB=$afterB.MatchId}|ConvertTo-Json -Compress -Depth 5
        }
        [pscustomobject]@{TestCase='TC-022';StartThreeMs=[math]::Round($startElapsed,3);UniqueMatchIds=@($matchIds|Sort-Object -Unique).Count;UniqueRoomIds=@($rooms|Sort-Object -Unique).Count;UniqueFingerprints=@($fingerprints|Sort-Object -Unique).Count;Fingerprints=$fingerprints}|ConvertTo-Json -Compress -Depth 5
        "END TC-022 at=$(Get-Date -Format o)"
    }finally{foreach($c in $clients){try{$c.Close()}catch{}}}
    return
}

if($ListUpdateOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-069 list-update tag=$tag at=$(Get-Date -Format o)"
    $lobby=New-Client;$a=New-Client;$b=New-Client
    try{
        $null=Send-Request $lobby 0 @{PlayerId="list-observer-$tag";PlayerName='Lobby Observer'}
        $null=Send-Request $a 0 @{PlayerId="list-a-$tag";PlayerName='List A'}
        $null=Send-Request $b 0 @{PlayerId="list-b-$tag";PlayerName='List B'}
        $initialList=Send-Request $lobby 8 @{}
        $room=Send-Request $a 9 @{RoomName="List update $tag";Difficulty=0}
        if($room.Response.Envelope.Type -ne 9){throw "CreateRoom failed: $($room.Response.Envelope.Error.Message)"}
        $roomId=[string]$room.Response.Payload.RoomId
        $createdList=Send-Request $lobby 8 @{}
        $null=Send-Request $b 10 @{RoomId=$roomId}
        $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=0;Duration=5}
        if($start.Response.Envelope.Type -ne 14){throw 'Start failed'}
        $matchId=[string]$start.Response.Payload.MatchId
        $preparingList=Send-Request $lobby 8 @{}
        $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        $ongoingList=Send-Request $lobby 8 @{}
        $initial=Status $a $matchId;$solution=[SudokuTestSolver]::Solve([int[]]$initial.Puzzle)
        for($i=0;$i -lt 81;$i++){if($initial.Puzzle[$i] -eq 0){$move=Send-Move $a $matchId $i $solution[$i];if(!$move.Response.Accepted -or !$move.Response.IsCorrect){throw "Completion failed at $i"}}}
        $finishedList=Send-Request $lobby 8 @{}
        $snapshots=@(
            @{Name='before-create';Response=$initialList},
            @{Name='created';Response=$createdList},
            @{Name='preparing';Response=$preparingList},
            @{Name='ongoing';Response=$ongoingList},
            @{Name='finished';Response=$finishedList}
        )
        foreach($snapshot in $snapshots){
            $roomState=@($snapshot.Response.Response.Payload.Rooms|Where-Object RoomId -eq $roomId)[0]
            [pscustomobject]@{TestCase='TC-069';Step=$snapshot.Name;RoomFound=($null -ne $roomState);HasActiveMatch=$roomState.HasActiveMatch;PlayerCount=if($roomState){@($roomState.Players).Count}else{0};EventTypes=@($snapshot.Response.Events|ForEach-Object{$_.Envelope.Type});LobbyConnectionOpen=$lobby.Connected}|ConvertTo-Json -Compress -Depth 5
        }
        "END TC-069 at=$(Get-Date -Format o)"
    }finally{$lobby.Close();$a.Close();$b.Close()}
    return
}

if($GraceTimeoutOrderOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-090 full-order tag=$tag at=$(Get-Date -Format o)"
    $clients=@();$matchIds=@();$first=@($null,$null);$finishEventCounts=@(0,0);$closed=@($false,$false)
    try{
        for($i=0;$i -lt 4;$i++){
            $c=New-Client;$clients+=,$c
            $null=Send-Request $c 0 @{PlayerId="order-$i-$tag";PlayerName="Order $i"}
        }
        for($pair=0;$pair -lt 2;$pair++){
            $a=$clients[$pair*2];$b=$clients[$pair*2+1]
            $room=Send-Request $a 9 @{RoomName="Order $tag $pair";Difficulty=0}
            if($room.Response.Envelope.Type -ne 9){throw "CreateRoom $pair failed: $($room.Response.Envelope.Error.Message)"}
            $roomId=[string]$room.Response.Payload.RoomId
            $null=Send-Request $b 10 @{RoomId=$roomId}
            $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=0;Duration=5}
            if($start.Response.Envelope.Type -ne 14){throw "Start $pair failed"}
            $matchId=[string]$start.Response.Payload.MatchId;$matchIds+=,$matchId
            $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        }
        $watch=[Diagnostics.Stopwatch]::StartNew()
        $clients[0].Close();$closed[0]=$true
        "CLOSE pair=0 elapsed=$([math]::Round($watch.Elapsed.TotalSeconds,3)) grace-would-end=180 match-timeout=300"
        $lastProgress=0
        while($watch.Elapsed.TotalSeconds -lt 378){
            $elapsed=$watch.Elapsed.TotalSeconds
            if(!$closed[1] -and $elapsed -ge 190){$clients[2].Close();$closed[1]=$true;"CLOSE pair=1 elapsed=$([math]::Round($elapsed,3)) grace-would-end=$([math]::Round($elapsed+180,3)) match-timeout=300"}
            for($pair=0;$pair -lt 2;$pair++){
                $result=Send-Request $clients[$pair*2+1] 17 @{MatchId=$matchIds[$pair]}
                $finishEventCounts[$pair]+=@($result.Events|Where-Object{$_.Envelope.Type -eq 22}).Count
                $status=$result.Response.Payload
                if($null -eq $first[$pair] -and $status.State -ge 2){
                    $first[$pair]=[pscustomobject]@{Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,2);State=$status.State;FinishReason=$status.FinishReason;Winner=$status.WinnerPlayerId;FinishedAtUtc=$status.FinishedAtUtc;TimeLeft=$status.TimeLeft}
                    [pscustomobject]@{TestCase='TC-090';Pair=$pair;Step='first-finish';Data=$first[$pair];FinishEvents=$finishEventCounts[$pair]}|ConvertTo-Json -Compress -Depth 5
                }
            }
            if($elapsed -ge $lastProgress+60){"PROGRESS elapsed=$([math]::Round($elapsed,1)) finished0=$($null -ne $first[0]) finished1=$($null -ne $first[1]) events0=$($finishEventCounts[0]) events1=$($finishEventCounts[1]) at=$(Get-Date -Format o)";$lastProgress=$elapsed}
            Start-Sleep -Seconds 2
        }
        for($pair=0;$pair -lt 2;$pair++){
            $statusResult=Send-Request $clients[$pair*2+1] 17 @{MatchId=$matchIds[$pair]}
            $finishEventCounts[$pair]+=@($statusResult.Events|Where-Object{$_.Envelope.Type -eq 22}).Count
            $status=$statusResult.Response.Payload
            [pscustomobject]@{TestCase='TC-090';Pair=$pair;Step='after-later-deadline';Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,2);First=$first[$pair];FinalState=$status.State;FinalReason=$status.FinishReason;FinalWinner=$status.WinnerPlayerId;FinalFinishedAtUtc=$status.FinishedAtUtc;FinalTimeLeft=$status.TimeLeft;FinishEvents=$finishEventCounts[$pair];SameFinishTimestamp=($first[$pair].FinishedAtUtc -eq $status.FinishedAtUtc);MatchId=$matchIds[$pair]}|ConvertTo-Json -Compress -Depth 6
        }
        "END TC-090 elapsed=$([math]::Round($watch.Elapsed.TotalSeconds,2)) at=$(Get-Date -Format o)"
    }finally{foreach($c in $clients){try{$c.Close()}catch{}}}
    return
}

if($ResultMemoryOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-060 all-reasons tag=$tag at=$(Get-Date -Format o)"
    $clients=@();$matchIds=@();$roomIds=@();$tokens=@();$firstCompleted=$null;$watch=[Diagnostics.Stopwatch]::new()
    try {
        for($i=0;$i -lt 6;$i++){$c=New-Client;$clients+=,$c;$hello=Send-Request $c 0 @{PlayerId="memory-$i-$tag";PlayerName="Memory $i"};$tokens+=,[string]$hello.Response.Payload.SessionToken}
        for($pair=0;$pair -lt 3;$pair++){
            $a=$clients[$pair*2];$b=$clients[$pair*2+1]
            $room=Send-Request $a 9 @{RoomName="Memory $tag $pair";Difficulty=0}
            if($room.Response.Envelope.Type -ne 9){throw "Create $pair failed"}
            $roomId=[string]$room.Response.Payload.RoomId;$roomIds+=,$roomId
            $null=Send-Request $b 10 @{RoomId=$roomId}
            $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=0;Duration=5}
            if($start.Response.Envelope.Type -ne 14){throw "Start $pair failed"}
            $matchId=[string]$start.Response.Payload.MatchId;$matchIds+=,$matchId
            $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        }
        $initial=Status $clients[0] $matchIds[0];$solution=[SudokuTestSolver]::Solve([int[]]$initial.Puzzle)
        $moves=0
        for($i=0;$i -lt 81;$i++){if($initial.Puzzle[$i] -eq 0){$move=Send-Move $clients[0] $matchIds[0] $i $solution[$i];if(!$move.Response.Accepted -or !$move.Response.IsCorrect){throw "Complete failed at $i"};$moves++}}
        $firstCompleted=Status $clients[0] $matchIds[0]
        $clients[4].Close();$watch.Start()
        "SETUP CompletedMoves=$moves DisconnectedA=pair2 TimeUp=pair1 at=$(Get-Date -Format o)"
        $last=0
        while($watch.Elapsed.TotalSeconds -lt 305){
            if($watch.Elapsed.TotalSeconds -ge $last+60){$s1=Status $clients[3] $matchIds[1];$s2=Status $clients[5] $matchIds[2];"PROGRESS elapsed=$([math]::Round($watch.Elapsed.TotalSeconds,1)) timeoutState=$($s1.State) disconnectState=$($s2.State) at=$(Get-Date -Format o)";$last=$watch.Elapsed.TotalSeconds}
            Start-Sleep -Seconds 2
        }
        $reconnected=New-Client;$clients+=,$reconnected
        $resume=Send-Request $reconnected 5 @{SessionToken=$tokens[4]}
        if($resume.Response.Envelope.Type -ne 6){throw 'Reconnect after grace failed'}
        $status=@()
        $status+=,@((Status $clients[0] $matchIds[0]),(Status $clients[1] $matchIds[0]))
        $status+=,@((Status $clients[2] $matchIds[1]),(Status $clients[3] $matchIds[1]))
        $status+=,@((Status $reconnected $matchIds[2]),(Status $clients[5] $matchIds[2]))
        for($pair=0;$pair -lt 3;$pair++){
            $a=$status[$pair][0];$b=$status[$pair][1]
            $samePuzzle=(($a.Puzzle|ConvertTo-Json -Compress) -eq ($b.Puzzle|ConvertTo-Json -Compress))
            $boardsMirror=(($a.OwnBoard|ConvertTo-Json -Compress) -eq ($b.OpponentBoard|ConvertTo-Json -Compress) -and ($a.OpponentBoard|ConvertTo-Json -Compress) -eq ($b.OwnBoard|ConvertTo-Json -Compress))
            $countersMirror=($a.OwnCorrectCount -eq $b.OpponentCorrectCount -and $b.OwnCorrectCount -eq $a.OpponentCorrectCount -and $a.OwnErrorCount -eq $b.OpponentErrorCount -and $b.OwnErrorCount -eq $a.OpponentErrorCount)
            $sameTime=($a.StartedAtUtc -eq $b.StartedAtUtc -and $a.EndsAtUtc -eq $b.EndsAtUtc -and $a.FinishedAtUtc -eq $b.FinishedAtUtc)
            [pscustomobject]@{TestCase='TC-060';Pair=$pair;ExpectedReason=$pair;MatchId=$matchIds[$pair];RoomId=$roomIds[$pair];MatchBinding=($a.MatchId -eq $matchIds[$pair] -and $b.MatchId -eq $matchIds[$pair]);RoomBinding=($a.RoomId -eq $roomIds[$pair] -and $b.RoomId -eq $roomIds[$pair]);StateA=$a.State;StateB=$b.State;ReasonA=$a.FinishReason;ReasonB=$b.FinishReason;WinnerA=$a.WinnerPlayerId;WinnerB=$b.WinnerPlayerId;SamePuzzle=$samePuzzle;BoardsMirror=$boardsMirror;CountersMirror=$countersMirror;SameTimes=$sameTime;FinishedAtUtc=$a.FinishedAtUtc;CorrectA=$a.OwnCorrectCount;CorrectB=$b.OwnCorrectCount;ErrorA=$a.OwnErrorCount;ErrorB=$b.OwnErrorCount;AReconnected=($pair -eq 2 -and $resume.Response.Envelope.Type -eq 6);CompletedResultStable=if($pair -eq 0){$firstCompleted.FinishedAtUtc -eq $a.FinishedAtUtc}else{$null}}|ConvertTo-Json -Compress -Depth 5
        }
        [pscustomobject]@{TestCase='TC-060';UniqueMatchIds=@($matchIds|Sort-Object -Unique).Count;UniqueRoomIds=@($roomIds|Sort-Object -Unique).Count;Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,2)}|ConvertTo-Json -Compress
        "END TC-060 at=$(Get-Date -Format o)"
    }finally{foreach($c in $clients){try{$c.Close()}catch{}}}
    return
}

if($EventOrderOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-101 tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client
    try {
        $null=Send-Request $a 0 @{PlayerId="event-a-$tag";PlayerName='Event A'}
        $null=Send-Request $b 0 @{PlayerId="event-b-$tag";PlayerName='Event B'}
        $room=Send-Request $a 9 @{RoomName="Event $tag";Difficulty=0}
        if($room.Response.Envelope.Type -ne 9){throw 'Create failed'}
        $roomId=[string]$room.Response.Payload.RoomId
        $null=Send-Request $b 10 @{RoomId=$roomId}
        $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=0;Duration=5}
        if($start.Response.Envelope.Type -ne 14){throw 'Start failed'}
        $matchId=[string]$start.Response.Payload.MatchId
        $null=Send-Request $a 15 @{MatchId=$matchId}
        $readyB=Send-Request $b 15 @{MatchId=$matchId}
        $sequence=@($readyB.Events|ForEach-Object{$_.Envelope.Type})
        $initial=Status $b $matchId;$solution=[SudokuTestSolver]::Solve([int[]]$initial.Puzzle)
        $index=0;while($initial.Puzzle[$index] -ne 0){$index++}
        $moveA=Send-Move $a $matchId $index $solution[$index]
        $bProgress=Send-Request $b 17 @{MatchId=$matchId}
        $sequence+=@($bProgress.Events|ForEach-Object{$_.Envelope.Type})
        $a.Close();Start-Sleep -Milliseconds 300
        $bDisconnected=Send-Request $b 17 @{MatchId=$matchId}
        $sequence+=@($bDisconnected.Events|ForEach-Object{$_.Envelope.Type})
        $lastMove=$null;$moves=0
        for($i=0;$i -lt 81;$i++){
            if($initial.Puzzle[$i] -ne 0){continue}
            $move=Send-Move $b $matchId $i $solution[$i]
            $sequence+=@($move.Events|ForEach-Object{$_.Envelope.Type})
            $lastMove=$move.Response;$moves++
        }
        $finished=Send-Request $b 17 @{MatchId=$matchId}
        $sequence+=@($finished.Events|Where-Object{$null -ne $_}|ForEach-Object{$_.Envelope.Type})
        $finishPoll=0
        while($sequence -notcontains 22 -and $finishPoll -lt 10){Start-Sleep -Milliseconds 100;$more=Send-Request $b 17 @{MatchId=$matchId};$sequence+=@($more.Events|Where-Object{$null -ne $_}|ForEach-Object{$_.Envelope.Type});$finishPoll++}
        $final=$finished.Response.Payload
        $after=Send-Move $b $matchId $index $solution[$index]
        $again=Status $b $matchId
        $atStart=[array]::IndexOf($sequence,16);$atProgress=[array]::IndexOf($sequence,21);$atDisconnect=[array]::IndexOf($sequence,7);$atFinished=[array]::IndexOf($sequence,22)
        [pscustomobject]@{TestCase='TC-101';MatchId=$matchId;Sequence=$sequence;MatchStartedIndex=$atStart;ProgressIndex=$atProgress;DisconnectedIndex=$atDisconnect;FinishedIndex=$atFinished;OrderCorrect=($atStart -ge 0 -and $atProgress -gt $atStart -and $atDisconnect -gt $atProgress -and $atFinished -gt $atDisconnect);FinishPolls=$finishPoll;Moves=$moves;LastMoveAccepted=$lastMove.Accepted;FinalState=$final.State;FinalReason=$final.FinishReason;FinalWinner=$final.WinnerPlayerId;MoveAfterFinishAccepted=$after.Response.Accepted;StateAfterRejectedMove=$again.State;FinishedAtStable=($final.FinishedAtUtc -eq $again.FinishedAtUtc);Timestamp=(Get-Date -Format o)}|ConvertTo-Json -Compress -Depth 6
        "END TC-101 at=$(Get-Date -Format o)"
    }finally{$a.Close();$b.Close()}
    return
}

if($RestoreOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-094 tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client;$a2=$null
    try{
        $hello=Send-Request $a 0 @{PlayerId="restore-a-$tag";PlayerName='Restore A'}
        $token=[string]$hello.Response.Payload.SessionToken
        $null=Send-Request $b 0 @{PlayerId="restore-b-$tag";PlayerName='Restore B'}
        $room=Send-Request $a 9 @{RoomName="Restore $tag";Difficulty=1}
        if($room.Response.Envelope.Type -ne 9){throw "CreateRoom setup failed: $($room.Response.Envelope.Error.Message)"}
        $roomId=[string]$room.Response.Payload.RoomId
        $null=Send-Request $b 10 @{RoomId=$roomId}
        $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=1;Duration=5}
        if($start.Response.Envelope.Type -ne 14){throw 'StartMatch setup failed'}
        $matchId=[string]$start.Response.Payload.MatchId
        $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        $initial=Status $a $matchId;$solution=[SudokuTestSolver]::Solve([int[]]$initial.Puzzle)
        $editable=@();for($i=0;$i -lt 81;$i++){if($initial.Puzzle[$i] -eq 0){$editable+=,$i}}
        $correctIndex=[int]$editable[0];$wrongIndex=[int]$editable[1]
        $correct=Send-Move $a $matchId $correctIndex $solution[$correctIndex]
        $wrong=if($solution[$wrongIndex] -eq 9){1}else{[int]$solution[$wrongIndex]+1}
        $mistake=Send-Move $a $matchId $wrongIndex $wrong
        $before=Status $a $matchId
        Show 'TC-094' 'before-disconnect' @{MatchId=$matchId;CorrectAccepted=$correct.Response.Accepted;MistakeAccepted=$mistake.Response.Accepted;Status=(Digest $before);TimeLeft=$before.TimeLeft;TokenIssued=![string]::IsNullOrWhiteSpace($token)}
        $bBefore=Status $b $matchId
        $a.Close();Start-Sleep -Seconds 2
        $bDuringResult=Send-Request $b 17 @{MatchId=$matchId}
        $bDuring=$bDuringResult.Response.Payload
        $a2=New-Client;$reconnect=Send-Request $a2 5 @{SessionToken=$token}
        $after=Status $a2 $matchId
        $bAfterResult=Send-Request $b 17 @{MatchId=$matchId}
        $bAfter=$bAfterResult.Response.Payload
        $sameOwn=(($before.OwnBoard|ConvertTo-Json -Compress) -eq ($after.OwnBoard|ConvertTo-Json -Compress))
        $sameOpp=(($before.OpponentBoard|ConvertTo-Json -Compress) -eq ($after.OpponentBoard|ConvertTo-Json -Compress))
        $sameCounters=($before.OwnCorrectCount -eq $after.OwnCorrectCount -and $before.OwnErrorCount -eq $after.OwnErrorCount -and $before.OwnHasUnresolvedMistake -eq $after.OwnHasUnresolvedMistake)
        $beforeTime=[Xml.XmlConvert]::ToTimeSpan([string]$before.TimeLeft)
        $afterTime=[Xml.XmlConvert]::ToTimeSpan([string]$after.TimeLeft)
        $timerDecrease=($afterTime.Ticks -le $beforeTime.Ticks -and $afterTime.Ticks -gt 0)
        Show 'TC-094' 'after-reconnect' @{ReconnectType=$reconnect.Response.Envelope.Type;EventTypes=@($reconnect.Events|ForEach-Object{$_.Envelope.Type});Status=(Digest $after);TimeLeft=$after.TimeLeft;SameOwnBoard=$sameOwn;SameOpponentBoard=$sameOpp;SameCounters=$sameCounters;TimerNotReset=$timerDecrease;TokenLogged=$false}
        Show 'TC-010' 'other-player-unaffected' @{Before=(Digest $bBefore);During=(Digest $bDuring);After=(Digest $bAfter);DisconnectEventTypes=@($bDuringResult.Events|ForEach-Object{$_.Envelope.Type});ReconnectEventTypes=@($bAfterResult.Events|ForEach-Object{$_.Envelope.Type});BStateStable=($bBefore.State -eq $bDuring.State -and $bDuring.State -eq $bAfter.State);BOwnBoardStable=(($bBefore.OwnBoard|ConvertTo-Json -Compress) -eq ($bAfter.OwnBoard|ConvertTo-Json -Compress));BOwnCountersStable=($bBefore.OwnCorrectCount -eq $bAfter.OwnCorrectCount -and $bBefore.OwnErrorCount -eq $bAfter.OwnErrorCount)}
        "END TC-094 at=$(Get-Date -Format o)"
    }finally{try{$a.Close()}catch{};try{$b.Close()}catch{};try{if($a2){$a2.Close()}}catch{}}
    return
}

if($AuthorityOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-105 tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client
    try{
        $aId="authority-a-$tag";$bId="authority-b-$tag"
        $null=Send-Request $a 0 @{PlayerId=$aId;PlayerName='Authority A'}
        $null=Send-Request $b 0 @{PlayerId=$bId;PlayerName='Authority B'}
        $room=Send-Request $a 9 @{RoomName="Authority $tag";Difficulty=1}
        if($room.Response.Envelope.Type -ne 9){throw "CreateRoom setup failed: $($room.Response.Envelope.Error.Message)"}
        $roomId=[string]$room.Response.Payload.RoomId
        $null=Send-Request $b 10 @{RoomId=$roomId}
        $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=1;Duration=5}
        $matchId=[string]$start.Response.Payload.MatchId
        $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        $before=Status $a $matchId;$solution=[SudokuTestSolver]::Solve([int[]]$before.Puzzle)
        $index=0;while($before.Puzzle[$index] -ne 0){$index++}
        $payload=@{MatchId=$matchId;MoveId=[guid]::NewGuid().ToString('N');Row=[math]::Floor($index/9);Column=$index%9;Value=$solution[$index];WinnerPlayerId='attacker';CorrectCount=9999;EndsAtUtc='2100-01-01T00:00:00Z';OwnBoard=@(9,9,9)}
        $response=Send-Request $a 19 $payload
        $afterA=Status $a $matchId;$afterB=Status $b $matchId
        Show 'TC-105' 'forged-move' @{MoveType=$response.Response.Envelope.Type;Accepted=$response.Response.Payload.Accepted;Correct=$response.Response.Payload.IsCorrect;BeforeEndsAt=$before.EndsAtUtc;AfterEndsAt=$afterA.EndsAtUtc;AfterOwnCorrect=$afterA.OwnCorrectCount;AfterOpponentCorrectB=$afterB.OpponentCorrectCount;WinnerA=$afterA.WinnerPlayerId;WinnerB=$afterB.WinnerPlayerId;State=$afterA.State;InjectedKeys=@('WinnerPlayerId','CorrectCount','EndsAtUtc','OwnBoard')}
        "END TC-105 at=$(Get-Date -Format o)"
    }finally{$a.Close();$b.Close()}
    return
}

if($BothDisconnectOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-059/091 both-disconnect tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client;$probe=$null
    try{
        $helloA=Send-Request $a 0 @{PlayerId="both-a-$tag";PlayerName='Both A'}
        $token=[string]$helloA.Response.Payload.SessionToken
        $null=Send-Request $b 0 @{PlayerId="both-b-$tag";PlayerName='Both B'}
        $room=Send-Request $a 9 @{RoomName="Both $tag";Difficulty=1}
        if($room.Response.Envelope.Type -ne 9){throw "CreateRoom setup failed: $($room.Response.Envelope.Error.Message)"}
        $roomId=[string]$room.Response.Payload.RoomId
        $null=Send-Request $b 10 @{RoomId=$roomId}
        $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=1;Duration=5}
        if($start.Response.Envelope.Type -ne 14){throw 'StartMatch setup failed'}
        $matchId=[string]$start.Response.Payload.MatchId
        $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        $before=Status $a $matchId
        Show 'TC-059/091' 'before-both-close' @{MatchId=$matchId;State=$before.State;TimeLeft=$before.TimeLeft;StartedAtUtc=$before.StartedAtUtc;EndsAtUtc=$before.EndsAtUtc;TokenIssued=![string]::IsNullOrWhiteSpace($token)}
        $watch=[Diagnostics.Stopwatch]::StartNew();$a.Close();$b.Close();$closeMs=$watch.Elapsed.TotalMilliseconds
        "CLOSED both-sockets-within-ms=$([math]::Round($closeMs,3)) at=$(Get-Date -Format o)"
        for($elapsed=30;$elapsed -le 180;$elapsed+=30){Start-Sleep -Seconds 30;"PROGRESS elapsed=$($watch.Elapsed.TotalSeconds) at=$(Get-Date -Format o)"}
        Start-Sleep -Seconds 5
        try{
            $probe=New-Client
            $reconnect=Send-Request $probe 5 @{SessionToken=$token}
            $status=if($reconnect.Response.Envelope.Type -eq 6){Status $probe $matchId}else{$null}
            Show 'TC-059/091' 'after-grace' @{Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,1);ReconnectType=$reconnect.Response.Envelope.Type;ReconnectError=$reconnect.Response.Envelope.Error;State=$status.State;FinishReason=$status.FinishReason;WinnerPlayerId=$status.WinnerPlayerId;TimeLeft=$status.TimeLeft;TokenLogged=$false}
        }catch{Show 'TC-059/091' 'probe-error' @{Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,1);Message=$_.Exception.Message}}
        "END TC-059/091 elapsed=$([math]::Round($watch.Elapsed.TotalSeconds,1)) at=$(Get-Date -Format o)"
    }finally{try{$a.Close()}catch{};try{$b.Close()}catch{};try{if($probe){$probe.Close()}}catch{}}
    return
}

if($IdempotencyOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-045 tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client
    try{
        $null=Send-Request $a 0 @{PlayerId="idem-a-$tag";PlayerName='Idem A'}
        $null=Send-Request $b 0 @{PlayerId="idem-b-$tag";PlayerName='Idem B'}
        $room=Send-Request $a 9 @{RoomName="Idem $tag";Difficulty=1}
        if($room.Response.Envelope.Type -ne 9){throw 'CreateRoom setup failed'}
        $roomId=[string]$room.Response.Payload.RoomId
        $null=Send-Request $b 10 @{RoomId=$roomId}
        $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=1;Duration=5}
        if($start.Response.Envelope.Type -ne 14){throw 'StartMatch setup failed'}
        $matchId=[string]$start.Response.Payload.MatchId
        $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        $initial=Status $a $matchId;$solution=[SudokuTestSolver]::Solve([int[]]$initial.Puzzle)
        $editable=@();for($j=0;$j -lt 81;$j++){if($initial.Puzzle[$j] -eq 0){$editable+=,$j}}
        $index=[int]$editable[0];$moveId=[guid]::NewGuid().ToString('N');$messageId=[guid]::NewGuid()
        $payload=@{MatchId=$matchId;MoveId=$moveId;Row=[math]::Floor($index/9);Column=$index%9;Value=$solution[$index]}
        $first=Send-RequestCustomId $a 19 $payload $messageId;$afterFirst=Status $a $matchId
        $same=Send-RequestCustomId $a 19 $payload $messageId;$afterSame=Status $a $matchId
        $newMessage=Send-RequestCustomId $a 19 $payload ([guid]::NewGuid());$afterNewMessage=Status $a $matchId
        Show 'TC-045' 'correct-replays' @{First=$first.Response.Payload;SameMessageId=$same.Response.Payload;SameMoveIdNewMessageId=$newMessage.Response.Payload;Counts=@($afterFirst.OwnCorrectCount,$afterSame.OwnCorrectCount,$afterNewMessage.OwnCorrectCount);BoardsEqual=(($afterFirst.OwnBoard|ConvertTo-Json -Compress) -eq ($afterNewMessage.OwnBoard|ConvertTo-Json -Compress))}
        $wrongIndex=[int]$editable[1];$wrong=if($solution[$wrongIndex] -eq 9){1}else{[int]$solution[$wrongIndex]+1}
        $wrongPayload=@{MatchId=$matchId;MoveId=[guid]::NewGuid().ToString('N');Row=[math]::Floor($wrongIndex/9);Column=$wrongIndex%9;Value=$wrong}
        $wrongMessage=[guid]::NewGuid();$wrongFirst=Send-RequestCustomId $a 19 $wrongPayload $wrongMessage;$wrongStatus1=Status $a $matchId
        $wrongAgain=Send-RequestCustomId $a 19 $wrongPayload ([guid]::NewGuid());$wrongStatus2=Status $a $matchId
        Show 'TC-045' 'wrong-replay' @{First=$wrongFirst.Response.Payload;Duplicate=$wrongAgain.Response.Payload;ErrorCounts=@($wrongStatus1.OwnErrorCount,$wrongStatus2.OwnErrorCount);BoardsEqual=(($wrongStatus1.OwnBoard|ConvertTo-Json -Compress) -eq ($wrongStatus2.OwnBoard|ConvertTo-Json -Compress))}
    }finally{$a.Close();$b.Close()}
    "END TC-045 at=$(Get-Date -Format o)"
    return
}

if($GraceOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-059/089 tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client;$a2=$null
    try{
        $helloA=Send-Request $a 0 @{PlayerId="grace-a-$tag";PlayerName='Grace A'}
        $token=[string]$helloA.Response.Payload.SessionToken
        $helloB=Send-Request $b 0 @{PlayerId="grace-b-$tag";PlayerName='Grace B'}
        Show 'SETUP' 'handshakes' @{AType=$helloA.Response.Envelope.Type;BType=$helloB.Response.Envelope.Type;TokenIssued=![string]::IsNullOrWhiteSpace($token)}
        $room=Send-Request $a 9 @{RoomName="Grace $tag";Difficulty=1}
        if($room.Response.Envelope.Type -ne 9){throw 'CreateRoom setup failed'}
        $roomId=[string]$room.Response.Payload.RoomId
        $null=Send-Request $b 10 @{RoomId=$roomId}
        $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=1;Duration=5}
        if($start.Response.Envelope.Type -ne 14){throw 'StartMatch setup failed'}
        $matchId=[string]$start.Response.Payload.MatchId
        $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
        $statusA=Status $a $matchId;$solution=[SudokuTestSolver]::Solve([int[]]$statusA.Puzzle)
        $index=0;while($statusA.Puzzle[$index] -ne 0){$index++}
        $correct=Send-Move $a $matchId $index $solution[$index]
        $beforeB=Status $b $matchId
        Show 'TC-059/089' 'before-disconnect' @{MatchId=$matchId;ACorrect=$correct.Response.CorrectCount;B=(Digest $beforeB);TimeLeft=$beforeB.TimeLeft}
        $a.Close();$clock=[Diagnostics.Stopwatch]::StartNew();$finished=$false;$lastLog=0
        while($clock.Elapsed.TotalSeconds -lt 210 -and -not $finished){
            $response=Send-Request $b 17 @{MatchId=$matchId}
            if($response.Events.Count -gt 0){Show 'TC-059/089' 'events' @{Elapsed=[math]::Round($clock.Elapsed.TotalSeconds,1);Types=@($response.Events|ForEach-Object{$_.Envelope.Type})}}
            if($response.Response.Payload.State -ge 2){$finished=$true;Show 'TC-059/089' 'finished' @{Elapsed=[math]::Round($clock.Elapsed.TotalSeconds,1);Status=$response.Response.Payload}}
            if($clock.Elapsed.TotalSeconds -ge $lastLog+30){"PROGRESS elapsed=$([math]::Round($clock.Elapsed.TotalSeconds,1)) finished=$finished at=$(Get-Date -Format o)";$lastLog=$clock.Elapsed.TotalSeconds}
            Start-Sleep -Seconds 2
        }
        $clock.Stop()
        $afterB=Status $b $matchId
        Show 'TC-059/089' 'after-grace-B' @{Elapsed=[math]::Round($clock.Elapsed.TotalSeconds,1);B=(Digest $afterB);FinishReason=$afterB.FinishReason;TimeLeft=$afterB.TimeLeft}
        $a2=New-Client
        $late=Send-Request $a2 5 @{SessionToken=$token}
        Show 'TC-089/095' 'late-reconnect' @{Type=$late.Response.Envelope.Type;Error=$late.Response.Envelope.Error;PlayerId=$late.Response.Payload.PlayerId;TokenLogged=$false}
        if($late.Response.Envelope.Type -eq 6){$lateStatus=Status $a2 $matchId;Show 'TC-089/095' 'late-status' @{State=$lateStatus.State;Winner=$lateStatus.WinnerPlayerId;Reason=$lateStatus.FinishReason}}
        "END TC-059/089 elapsed=$([math]::Round($clock.Elapsed.TotalSeconds,1)) at=$(Get-Date -Format o)"
    }finally{try{$a.Close()}catch{};try{$b.Close()}catch{};try{if($a2){$a2.Close()}}catch{}}
    return
}

if($CompletionOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-055/060 tag=$tag at=$(Get-Date -Format o)"
    $clients=@()
    try{
        for($pair=0;$pair -lt 2;$pair++){
            $a=New-Client;$b=New-Client;$clients+=,$a;$clients+=,$b
            $aId="completion-a-$pair-$tag";$bId="completion-b-$pair-$tag"
            $null=Send-Request $a 0 @{PlayerId=$aId;PlayerName=$aId}
            $null=Send-Request $b 0 @{PlayerId=$bId;PlayerName=$bId}
            $room=Send-Request $a 9 @{RoomName="Completion $tag $pair";Difficulty=0}
            if($room.Response.Envelope.Type -ne 9){throw "CreateRoom $pair failed"}
            $roomId=[string]$room.Response.Payload.RoomId
            $null=Send-Request $b 10 @{RoomId=$roomId}
            $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=0;Duration=5}
            if($start.Response.Envelope.Type -ne 14){throw "StartMatch $pair failed"}
            $matchId=[string]$start.Response.Payload.MatchId
            $null=Send-Request $a 15 @{MatchId=$matchId};$null=Send-Request $b 15 @{MatchId=$matchId}
            $winnerClient=if($pair -eq 0){$a}else{$b}
            $winnerId=if($pair -eq 0){$aId}else{$bId}
            $initial=Status $winnerClient $matchId
            $solution=[SudokuTestSolver]::Solve([int[]]$initial.Puzzle)
            $moves=0;$errors=0;$lastResponse=$null
            for($i=0;$i -lt 81;$i++){
                if($initial.Puzzle[$i] -ne 0){continue}
                $result=Send-Move $winnerClient $matchId $i $solution[$i]
                $lastResponse=$result.Response;$moves++
                if(-not $result.Response.Accepted -or -not $result.Response.IsCorrect){$errors++}
            }
            $finalA=Send-Request $a 17 @{MatchId=$matchId}
            $finalB=Send-Request $b 17 @{MatchId=$matchId}
            Show 'TC-055/060' "completion-pair-$pair" @{WinnerExpected=$winnerId;MatchId=$matchId;Moves=$moves;MoveErrors=$errors;LastMove=$lastResponse;AStatus=$finalA.Response.Payload;BStatus=$finalB.Response.Payload;AEvents=@($finalA.Events|ForEach-Object{$_.Envelope.Type});BEvents=@($finalB.Events|ForEach-Object{$_.Envelope.Type})}
        }
    }finally{foreach($c in $clients){try{$c.Close()}catch{}}}
    "END TC-055/060 at=$(Get-Date -Format o)"
    return
}

if ($ConcurrentOnly) {
    $tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
    "RUN TC-047/064/065 tag=$tag at=$(Get-Date -Format o)"
    $clients = @()
    for($i=0;$i -lt 4;$i++){$clients+=,(New-Client)}
    try {
        for ($i=0; $i -lt 4; $i++) {
            $hello=Send-Request $clients[$i] 0 @{PlayerId="concurrent-$tag-$i";PlayerName="Concurrent $i"}
            Show 'SETUP' "handshake-$i" @{Type=$hello.Response.Envelope.Type;TokenIssued=![string]::IsNullOrWhiteSpace($hello.Response.Payload.SessionToken)}
        }
        $matches=@()
        for ($pair=0; $pair -lt 2; $pair++) {
            $owner=$pair*2;$opponent=$owner+1
            $room=Send-Request $clients[$owner] 9 @{RoomName="Concurrent $tag $pair";Difficulty=1}
            if($room.Response.Envelope.Type -ne 9){throw "CreateRoom $pair failed"}
            $roomId=[string]$room.Response.Payload.RoomId
            $join=Send-Request $clients[$opponent] 10 @{RoomId=$roomId}
            if($join.Response.Envelope.Type -ne 10){throw "JoinRoom $pair failed"}
            $start=Send-Request $clients[$owner] 13 @{RoomId=$roomId;Difficulty=1;Duration=5}
            if($start.Response.Envelope.Type -ne 14){throw "StartMatch $pair failed"}
            $matchId=[string]$start.Response.Payload.MatchId
            $null=Send-Request $clients[$owner] 15 @{MatchId=$matchId}
            $null=Send-Request $clients[$opponent] 15 @{MatchId=$matchId}
            $matches+=,$matchId
            Show 'SETUP' "match-$pair" @{RoomId=$roomId;MatchId=$matchId;Owner=$owner;Opponent=$opponent}
        }
        $before=@();$solutions=@();$editable=@()
        for($i=0;$i -lt 4;$i++){
            $before+=,(Status $clients[$i] $matches[[math]::Floor($i/2)])
            $solutions+=,([SudokuTestSolver]::Solve([int[]]$before[$i].Puzzle))
            $indices=@();for($j=0;$j -lt 81;$j++){if($before[$i].Puzzle[$j] -eq 0){$indices+=,$j}}
            $editable+=,$indices
        }
        $results=@();$moveIndex=@{};$aAfterOwn=$null;$aAfterC=$null
        foreach($i in @(0,2,1,3)){
            $matchId=$matches[[math]::Floor($i/2)]
            $index=[int]$editable[$i][0]
            $value=if($i%2 -eq 0){[int]$solutions[$i][$index]}else{if($solutions[$i][$index] -eq 9){1}else{[int]$solutions[$i][$index]+1}}
            $move=Send-Move $clients[$i] $matchId $index $value
            $moveIndex[$i]=$index
            $results+=,[pscustomobject]@{Client=$i;MatchId=$matchId;Index=$index;Value=$value;Response=$move.Response}
            Show 'TC-047/064/065' "move-$i" $results[-1]
            if($i -eq 0){$aAfterOwn=Status $clients[0] $matches[0]}
            if($i -eq 2){$aAfterC=Status $clients[0] $matches[0]}
        }
        $after=@(0..3|ForEach-Object{Status $clients[$_] $matches[[math]::Floor($_/2)]})
        Show 'TC-047/064/065' 'isolation-after-moves' @{
            MatchIdsDistinct=($matches[0] -ne $matches[1]);
            ACorrect=$after[0].OwnCorrectCount;BOpponentCorrect=$after[1].OpponentCorrectCount;
            CCorrect=$after[2].OwnCorrectCount;DOpponentCorrect=$after[3].OpponentCorrectCount;
            BErrors=$after[1].OwnErrorCount;AOpponentErrors=$after[0].OpponentErrorCount;
            DErrors=$after[3].OwnErrorCount;COpponentErrors=$after[2].OpponentErrorCount;
            ABoardUnchangedByC=(($aAfterOwn.OwnBoard|ConvertTo-Json -Compress) -eq ($aAfterC.OwnBoard|ConvertTo-Json -Compress));
            AProgressUnchangedByC=($aAfterOwn.OwnCorrectCount -eq $aAfterC.OwnCorrectCount);
            Match1BoardAEqualsMatch2BoardC=(($after[0].OwnBoard|ConvertTo-Json -Compress) -eq ($after[2].OwnBoard|ConvertTo-Json -Compress))
        }
        foreach($i in @(1,3)){
            $matchId=$matches[[math]::Floor($i/2)]
            $index=[int]$moveIndex[$i]
            $erase=Send-Move $clients[$i] $matchId $index 0
            Show 'TC-065' "erase-$i" @{Response=$erase.Response;MatchId=$matchId;Index=$index}
        }
        $final=@(0..3|ForEach-Object{Status $clients[$_] $matches[[math]::Floor($_/2)]})
        Show 'TC-047/064/065' 'final-counters' @{A=(Digest $final[0]);B=(Digest $final[1]);C=(Digest $final[2]);D=(Digest $final[3])}
    }
    finally{foreach($client in $clients){try{$client.Close()}catch{}}}
    "END TC-047/064/065 at=$(Get-Date -Format o)"
    return
}

if ($TimeoutOnly) {
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-053/054/056/057/060 tag=$tag at=$(Get-Date -Format o)"
    $clients=@();$matches=@();$starts=@()
    try {
        for($i=0;$i -lt 6;$i++){
            $c=New-Client;$clients+=,$c
            $hello=Send-Request $c 0 @{PlayerId="timeout-$tag-$i";PlayerName="Timeout $i"}
            if($hello.Response.Envelope.Type -ne 1){throw "Handshake $i failed"}
        }
        for($pair=0;$pair -lt 3;$pair++){
            $a=$pair*2;$b=$a+1
            $room=Send-Request $clients[$a] 9 @{RoomName="Timeout $tag $pair";Difficulty=1}
            if($room.Response.Envelope.Type -ne 9){throw "CreateRoom $pair failed"}
            $roomId=[string]$room.Response.Payload.RoomId
            $null=Send-Request $clients[$b] 10 @{RoomId=$roomId}
            $start=Send-Request $clients[$a] 13 @{RoomId=$roomId;Difficulty=1;Duration=5}
            if($start.Response.Envelope.Type -ne 14){throw "StartMatch $pair failed"}
            $matchId=[string]$start.Response.Payload.MatchId;$matches+=,$matchId
            $null=Send-Request $clients[$a] 15 @{MatchId=$matchId}
            $null=Send-Request $clients[$b] 15 @{MatchId=$matchId}
            $initialA=Status $clients[$a] $matchId;$initialB=Status $clients[$b] $matchId
            $starts+=,$initialA.StartedAtUtc
            Show 'SETUP' "match-$pair" @{MatchId=$matchId;RoomId=$roomId;StartedAt=$initialA.StartedAtUtc;EndsAt=$initialA.EndsAtUtc;A=$a;B=$b}
            if($pair -in @(0,1)){
                $solA=[SudokuTestSolver]::Solve([int[]]$initialA.Puzzle)
                $editA=@();for($j=0;$j -lt 81;$j++){if($initialA.Puzzle[$j] -eq 0){$editA+=,$j}}
                $indexA=[int]$editA[0]
                Show 'TC-056/057' "pair-$pair-A-correct" (Send-Move $clients[$a] $matchId $indexA $solA[$indexA]).Response
            }
            if($pair -eq 1){
                $solB=[SudokuTestSolver]::Solve([int[]]$initialB.Puzzle)
                $editB=@();for($j=0;$j -lt 81;$j++){if($initialB.Puzzle[$j] -eq 0){$editB+=,$j}}
                $indexB=[int]$editB[0]
                Show 'TC-057' 'pair-1-B-correct' (Send-Move $clients[$b] $matchId $indexB $solB[$indexB]).Response
                $wrongIndex=[int]$editB[1]
                $wrong=if($solB[$wrongIndex] -eq 9){1}else{[int]$solB[$wrongIndex]+1}
                Show 'TC-057' 'pair-1-B-wrong' (Send-Move $clients[$b] $matchId $wrongIndex $wrong).Response
                Show 'TC-057' 'pair-1-B-erase' (Send-Move $clients[$b] $matchId $wrongIndex 0).Response
            }
        }
        $watch=[Diagnostics.Stopwatch]::StartNew();$finished=@($false,$false,$false);$lastLog=0
        while($watch.Elapsed.TotalSeconds -lt 340 -and ($finished -contains $false)){
            for($pair=0;$pair -lt 3;$pair++){
                if($finished[$pair]){continue}
                $response=Send-Request $clients[$pair*2] 17 @{MatchId=$matches[$pair]}
                if($response.Events.Count -gt 0){Show 'TC-053/056/057' "events-pair-$pair" @{Types=@($response.Events|ForEach-Object{$_.Envelope.Type});Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,1)}}
                if($response.Response.Payload.State -ge 2){$finished[$pair]=$true;Show 'TC-053/056/057/060' "finished-pair-$pair" @{Elapsed=[math]::Round($watch.Elapsed.TotalSeconds,1);Payload=$response.Response.Payload}}
            }
            if($watch.Elapsed.TotalSeconds -ge $lastLog+30){"PROGRESS elapsed=$([math]::Round($watch.Elapsed.TotalSeconds,1)) finished=$($finished -join ',') at=$(Get-Date -Format o)";$lastLog=$watch.Elapsed.TotalSeconds}
            Start-Sleep -Seconds 2
        }
        $watch.Stop()
        for($pair=0;$pair -lt 3;$pair++){
            $a=$pair*2;$b=$a+1;$matchId=$matches[$pair]
            $resultA=Send-Request $clients[$a] 17 @{MatchId=$matchId}
            $resultB=Send-Request $clients[$b] 17 @{MatchId=$matchId}
            Show 'TC-053/056/057/060' "final-pair-$pair" @{A=$resultA.Response.Payload;B=$resultB.Response.Payload;AEventTypes=@($resultA.Events|ForEach-Object{$_.Envelope.Type});BEventTypes=@($resultB.Events|ForEach-Object{$_.Envelope.Type})}
            $afterMove=Send-Move $clients[$a] $matchId 0 1
            Show 'TC-054' "move-after-finish-$pair" $afterMove.Response
        }
        "END TC-053/054/056/057/060 elapsed=$([math]::Round($watch.Elapsed.TotalSeconds,1)) at=$(Get-Date -Format o)"
    }
    finally{foreach($c in $clients){try{$c.Close()}catch{}}}
    return
}

$tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
"RUN tag=$tag at=$(Get-Date -Format o)"
$a = New-Client; $b = New-Client
try {
    foreach ($item in @(@($a, "game-a-$tag"), @($b, "game-b-$tag"))) {
        $hello = Send-Request $item[0] 0 @{ PlayerId = $item[1]; PlayerName = $item[1] }
        Show 'SETUP' "handshake-$($item[1])" @{ Type = $hello.Response.Envelope.Type; PlayerId = $hello.Response.Payload.PlayerId; TokenIssued = ![string]::IsNullOrWhiteSpace($hello.Response.Payload.SessionToken) }
    }
    $create = Send-Request $a 9 @{ RoomName = "Gameplay $tag"; Difficulty = 1 }
    Show 'SETUP' 'create-room' @{ Type = $create.Response.Envelope.Type; Error = $create.Response.Envelope.Error; Room = $create.Response.Payload }
    if ($create.Response.Envelope.Type -ne 9) { throw 'CreateRoom setup failed.' }
    $roomId = [string]$create.Response.Payload.RoomId
    $join = Send-Request $b 10 @{ RoomId = $roomId }
    Show 'SETUP' 'join-room' @{ Type = $join.Response.Envelope.Type; Error = $join.Response.Envelope.Error; Room = $join.Response.Payload }
    if ($join.Response.Envelope.Type -ne 10) { throw 'JoinRoom setup failed.' }
    $start = Send-Request $a 13 @{ RoomId = $roomId; Difficulty = 1; Duration = 5 }
    if ($start.Response.Envelope.Type -ne 14) { throw 'StartMatch setup failed.' }
    $matchId = [string]$start.Response.Payload.MatchId
    Show 'SETUP' 'start-match' @{ MatchId = $matchId; RoomId = $roomId; State = $start.Response.Payload.State }
    $readyA = Send-Request $a 15 @{ MatchId = $matchId }
    $readyB = Send-Request $b 15 @{ MatchId = $matchId }
    Show 'SETUP' 'ready' @{ A = $readyA.Response.Payload.State; B = $readyB.Response.Payload.State; EndsAtUtc = $readyB.Response.Payload.EndsAtUtc }

    $initialA = Status $a $matchId; $initialB = Status $b $matchId
    $solutionA = [SudokuTestSolver]::Solve([int[]]$initialA.Puzzle)
    $solutionB = [SudokuTestSolver]::Solve([int[]]$initialB.Puzzle)
    $editableA = @(0..80 | Where-Object { $initialA.Puzzle[$_] -eq 0 })
    $editableB = @(0..80 | Where-Object { $initialB.Puzzle[$_] -eq 0 })
    Show 'SETUP' 'puzzle-solved-locally' @{ MatchId = $matchId; EditableA = $editableA.Count; EditableB = $editableB.Count; PuzzleA = $initialA.Puzzle; PuzzleB = $initialB.Puzzle }

    $aIndex = [int]$editableA[0]; $bIndex = [int]$editableB[0]
    $aCorrect = Send-Move $a $matchId $aIndex $solutionA[$aIndex]
    $afterA = Status $a $matchId; $afterASeenByB = Status $b $matchId
    Show 'TC-025/040/046' 'A-correct' @{ Index = $aIndex; Value = $solutionA[$aIndex]; Response = $aCorrect.Response; AOwnChanged = ($afterA.OwnBoard[$aIndex] -eq $solutionA[$aIndex]); BOwnUnchanged = (($initialB.OwnBoard | ConvertTo-Json -Compress) -eq ($afterASeenByB.OwnBoard | ConvertTo-Json -Compress)); ACorrect = $afterA.OwnCorrectCount; BOpponentCorrect = $afterASeenByB.OpponentCorrectCount }

    $bCorrect = Send-Move $b $matchId $bIndex $solutionB[$bIndex]
    $afterB = Status $b $matchId; $afterBSeenByA = Status $a $matchId
    Show 'TC-026/040/046' 'B-correct' @{ Index = $bIndex; Value = $solutionB[$bIndex]; Response = $bCorrect.Response; BOwnChanged = ($afterB.OwnBoard[$bIndex] -eq $solutionB[$bIndex]); AOwnUnchanged = (($afterA.OwnBoard | ConvertTo-Json -Compress) -eq ($afterBSeenByA.OwnBoard | ConvertTo-Json -Compress)); BCorrect = $afterB.OwnCorrectCount; AOpponentCorrect = $afterBSeenByA.OpponentCorrectCount }

    $aWrongIndex = [int]$editableA[1]
    $aWrong = if ($solutionA[$aWrongIndex] -eq 9) { 1 } else { $solutionA[$aWrongIndex] + 1 }
    $wrongA = Send-Move $a $matchId $aWrongIndex $aWrong
    $wrongStatusA = Status $a $matchId; $wrongStatusB = Status $b $matchId
    Show 'TC-027/036/040' 'A-wrong' @{ Index = $aWrongIndex; Value = $aWrong; Response = $wrongA.Response; AErrorCount = $wrongStatusA.OwnErrorCount; BErrorCount = $wrongStatusB.OwnErrorCount; AUnresolved = $wrongStatusA.OwnHasUnresolvedMistake; BOwnUnchanged = (($afterB.OwnBoard | ConvertTo-Json -Compress) -eq ($wrongStatusB.OwnBoard | ConvertTo-Json -Compress)) }
    $locked = Send-Move $a $matchId ([int]$editableA[2]) $solutionA[[int]$editableA[2]]
    Show 'TC-037' 'A-other-cell-while-unresolved' @{ Response = $locked.Response }
    $eraseA = Send-Move $a $matchId $aWrongIndex 0
    $eraseStatusA = Status $a $matchId
    Show 'TC-027/038' 'A-erase-wrong' @{ Response = $eraseA.Response; Cell = $eraseStatusA.OwnBoard[$aWrongIndex]; ErrorCount = $eraseStatusA.OwnErrorCount; Unresolved = $eraseStatusA.OwnHasUnresolvedMistake }

    $bWrongIndex = [int]$editableB[1]
    $bWrong = if ($solutionB[$bWrongIndex] -eq 9) { 1 } else { $solutionB[$bWrongIndex] + 1 }
    $wrongB = Send-Move $b $matchId $bWrongIndex $bWrong
    $wrongStatusB2 = Status $b $matchId; $wrongStatusA2 = Status $a $matchId
    Show 'TC-027/036/040' 'B-wrong' @{ Index = $bWrongIndex; Value = $bWrong; Response = $wrongB.Response; BErrorCount = $wrongStatusB2.OwnErrorCount; AErrorCount = $wrongStatusA2.OwnErrorCount; BUnresolved = $wrongStatusB2.OwnHasUnresolvedMistake; AOwnUnchanged = (($eraseStatusA.OwnBoard | ConvertTo-Json -Compress) -eq ($wrongStatusA2.OwnBoard | ConvertTo-Json -Compress)) }
    $eraseB = Send-Move $b $matchId $bWrongIndex 0
    $eraseStatusB = Status $b $matchId
    Show 'TC-027/038' 'B-erase-wrong' @{ Response = $eraseB.Response; Cell = $eraseStatusB.OwnBoard[$bWrongIndex]; ErrorCount = $eraseStatusB.OwnErrorCount; Unresolved = $eraseStatusB.OwnHasUnresolvedMistake }

    $eraseCorrectA = Send-Move $a $matchId $aIndex 0
    $erasedCorrectA = Status $a $matchId
    $reenterA = Send-Move $a $matchId $aIndex $solutionA[$aIndex]
    $reenteredA = Status $a $matchId
    Show 'TC-033/046' 'A-correct-erase-reenter' @{ EraseResponse = $eraseCorrectA.Response; CorrectAfterErase = $erasedCorrectA.OwnCorrectCount; ReenterResponse = $reenterA.Response; CorrectAfterReenter = $reenteredA.OwnCorrectCount }

    foreach ($item in @(
        @{ Name = 'row-minus-1'; Row = -1; Column = 0; Value = 1 },
        @{ Name = 'row-9'; Row = 9; Column = 0; Value = 1 },
        @{ Name = 'column-minus-1'; Row = 0; Column = -1; Value = 1 },
        @{ Name = 'column-9'; Row = 0; Column = 9; Value = 1 },
        @{ Name = 'row-min-int'; Row = [int]::MinValue; Column = 0; Value = 1 },
        @{ Name = 'column-max-int'; Row = 0; Column = [int]::MaxValue; Value = 1 },
        @{ Name = 'value-minus-1'; Row = [math]::Floor($aWrongIndex / 9); Column = $aWrongIndex % 9; Value = -1 },
        @{ Name = 'value-10'; Row = [math]::Floor($aWrongIndex / 9); Column = $aWrongIndex % 9; Value = 10 },
        @{ Name = 'value-max-int'; Row = [math]::Floor($aWrongIndex / 9); Column = $aWrongIndex % 9; Value = [int]::MaxValue }
    )) {
        $result = Send-Request $a 19 @{ MatchId = $matchId; MoveId = [guid]::NewGuid().ToString('N'); Row = $item.Row; Column = $item.Column; Value = $item.Value }
        $caseId = if ($item.Name -like 'value-*') { 'TC-042' } else { 'TC-041' }
        Show $caseId $item.Name @{ Request = $item; Response = $result.Response.Payload }
    }
    $afterInvalid = Status $a $matchId
    Show 'TC-041/042' 'after-invalid-state' @{ BoardUnchanged = (($reenteredA.OwnBoard | ConvertTo-Json -Compress) -eq ($afterInvalid.OwnBoard | ConvertTo-Json -Compress)); CorrectUnchanged = ($reenteredA.OwnCorrectCount -eq $afterInvalid.OwnCorrectCount); ErrorsUnchanged = ($reenteredA.OwnErrorCount -eq $afterInvalid.OwnErrorCount) }

    $beforeDisconnectA = Status $a $matchId; $beforeDisconnectB = Status $b $matchId
    Show 'TC-088' 'before-abrupt-close' @{ MatchId = $matchId; A = (Digest $beforeDisconnectA); B = (Digest $beforeDisconnectB); TimeLeftA = $beforeDisconnectA.TimeLeft; TimeLeftB = $beforeDisconnectB.TimeLeft }
    $a.Close()
    Start-Sleep -Seconds 2
    $afterDisconnectB = Status $b $matchId
    Show 'TC-088' 'after-abrupt-close' @{ B = (Digest $afterDisconnectB); TimeLeftB = $afterDisconnectB.TimeLeft; BOwnBoardUnchanged = (($beforeDisconnectB.OwnBoard | ConvertTo-Json -Compress) -eq ($afterDisconnectB.OwnBoard | ConvertTo-Json -Compress)); OpponentBoardUnchanged = (($beforeDisconnectB.OpponentBoard | ConvertTo-Json -Compress) -eq ($afterDisconnectB.OpponentBoard | ConvertTo-Json -Compress)); OpponentCorrectUnchanged = ($beforeDisconnectB.OpponentCorrectCount -eq $afterDisconnectB.OpponentCorrectCount) }
}
finally {
    try { $a.Close() } catch {}
    try { $b.Close() } catch {}
}
"END at=$(Get-Date -Format o)"
