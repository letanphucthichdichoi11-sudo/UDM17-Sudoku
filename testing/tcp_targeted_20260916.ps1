param([string]$HostName = '127.0.0.1', [int]$Port = 5000, [switch]$ProtocolOnly, [switch]$InvalidIdsOnly, [switch]$AuthorizationOnly, [switch]$HealthOnly, [switch]$MatchIdsOnly, [switch]$LifecycleOnly, [switch]$EmptyPayloadOnly, [switch]$RoomOnly, [switch]$DurationOnly, [switch]$RateLimitOnly, [switch]$StartRaceOnly, [switch]$TokenOnly, [switch]$JoinRaceOnly, [switch]$WrongRoomOnly, [switch]$RoomBroadcastOnly, [switch]$SessionOnly, [switch]$LeaveRoomOnly, [switch]$CorrelationPushOnly)
$ErrorActionPreference = 'Stop'

function New-Client {
    $client = [Net.Sockets.TcpClient]::new()
    $client.ReceiveTimeout = 5000
    $client.SendTimeout = 5000
    $client.Connect($HostName, $Port)
    $client
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
    $header = Read-Exact $stream 4
    $length = ([int]$header[0] -shl 24) -bor ([int]$header[1] -shl 16) -bor ([int]$header[2] -shl 8) -bor [int]$header[3]
    $json = [Text.Encoding]::UTF8.GetString((Read-Exact $stream $length))
    $message = $json | ConvertFrom-Json
    $payload = if ($message.Payload) { $message.Payload | ConvertFrom-Json } else { $null }
    [pscustomobject]@{ Envelope = $message; Payload = $payload; Raw = $json }
}
function Send-Request($client, [int]$type, $payload, [int]$version = 1) {
    $id = [guid]::NewGuid()
    $inner = $payload | ConvertTo-Json -Compress -Depth 20
    $envelope = [ordered]@{ ProtocolVersion = $version; MessageId = $id; Type = $type; Payload = $inner }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($envelope | ConvertTo-Json -Compress -Depth 20))
    $header = [byte[]]@([byte](($bytes.Length -shr 24) -band 255), [byte](($bytes.Length -shr 16) -band 255), [byte](($bytes.Length -shr 8) -band 255), [byte]($bytes.Length -band 255))
    $stream = $client.GetStream()
    $stream.Write($header, 0, 4)
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    $events = @()
    while ($true) {
        $message = Read-Message $client
        if ($message.Envelope.CorrelationId -eq $id) {
            return [pscustomobject]@{ Response = $message; Events = $events }
        }
        $events += $message
    }
}
function Send-Frame($client,[int]$type,$payload){
    $id=[guid]::NewGuid();$inner=$payload|ConvertTo-Json -Compress -Depth 10
    $json=[ordered]@{ProtocolVersion=1;MessageId=$id;Type=$type;Payload=$inner}|ConvertTo-Json -Compress -Depth 10
    $bytes=[Text.Encoding]::UTF8.GetBytes($json)
    $header=[byte[]]@([byte](($bytes.Length-shr24)-band255),[byte](($bytes.Length-shr16)-band255),[byte](($bytes.Length-shr8)-band255),[byte]($bytes.Length-band255))
    $stream=$client.GetStream();$stream.Write($header,0,4);$stream.Write($bytes,0,$bytes.Length);$stream.Flush();$id
}
function Await-Frame($client,[guid]$id){
    $events=@();while($true){$m=Read-Message $client;if($m.Envelope.CorrelationId -eq $id){return [pscustomobject]@{Response=$m;Events=$events}};$events+=,$m}
}
function Show-Result($id, $step, $result) {
    $response = $result.Response
    $safePayload = $response.Payload
    if ($response.Envelope.Type -eq 1 -and $safePayload) {
        $safePayload = [pscustomobject]@{
            PlayerId = $safePayload.PlayerId
            SessionTokenIssued = ![string]::IsNullOrWhiteSpace($safePayload.SessionToken)
        }
    }
    [pscustomobject]@{
        TestCase = $id
        Step = $step
        Type = $response.Envelope.Type
        Error = $response.Envelope.Error
        Payload = $safePayload
        EventTypes = @($result.Events | ForEach-Object { $_.Envelope.Type })
    } | ConvertTo-Json -Compress -Depth 20
}
function Snapshot($client, $matchId) {
    $result = Send-Request $client 17 @{ MatchId = $matchId }
    $payload = $result.Response.Payload
    [pscustomobject]@{
        State = $payload.State
        MatchId = $payload.MatchId
        OwnBoard = @($payload.OwnBoard)
        OpponentBoard = @($payload.OpponentBoard)
        OwnCorrectCount = $payload.OwnCorrectCount
        OpponentCorrectCount = $payload.OpponentCorrectCount
        OwnErrorCount = $payload.OwnErrorCount
        OpponentErrorCount = $payload.OpponentErrorCount
        StartedAtUtc = $payload.StartedAtUtc
        EndsAtUtc = $payload.EndsAtUtc
        WinnerPlayerId = $payload.WinnerPlayerId
    }
}

if($RoomBroadcastOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-014 room-broadcast tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client
    try{
        $null=Send-Request $a 0 @{PlayerId="broadcast-a-$tag";PlayerName='Broadcast A'}
        $null=Send-Request $b 0 @{PlayerId="broadcast-b-$tag";PlayerName='Broadcast B'}
        $created=Send-Request $a 9 @{RoomName="Broadcast $tag";Difficulty=1}
        if($created.Response.Envelope.Type -ne 9){throw "CreateRoom setup failed: $($created.Response.Envelope.Error.Message)"}
        $roomId=[string]$created.Response.Payload.RoomId
        $null=Send-Request $a 8 @{}
        $joined=Send-Request $b 10 @{RoomId=$roomId}
        $aList=Send-Request $a 8 @{}
        $bList=Send-Request $b 8 @{}
        $aRoom=@($aList.Response.Payload.Rooms|Where-Object RoomId -eq $roomId)[0]
        $bRoom=@($bList.Response.Payload.Rooms|Where-Object RoomId -eq $roomId)[0]
        [pscustomobject]@{TestCase='TC-014';CreateType=$created.Response.Envelope.Type;JoinType=$joined.Response.Envelope.Type;RoomId=$roomId;AEventTypes=@($aList.Events|ForEach-Object{$_.Envelope.Type});BJoinEventTypes=@($joined.Events|ForEach-Object{$_.Envelope.Type});BListEventTypes=@($bList.Events|ForEach-Object{$_.Envelope.Type});APlayers=@($aRoom.Players|ForEach-Object{$_.PlayerId});BPlayers=@($bRoom.Players|ForEach-Object{$_.PlayerId});SameRoom=($aRoom.RoomId -eq $bRoom.RoomId);Difficulty=$aRoom.Difficulty}|ConvertTo-Json -Compress -Depth 8
        "END TC-014 at=$(Get-Date -Format o)"
    }finally{$a.Close();$b.Close()}
    return
}

if($SessionOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    $playerId="session-$tag"
    "RUN TC-006 TCP-session tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$duplicate=New-Client;$resumed=New-Client
    try{
        $first=Send-Request $a 0 @{PlayerId=$playerId;PlayerName='Session A'}
        $token=[string]$first.Response.Payload.SessionToken
        $duplicateResult=Send-Request $duplicate 0 @{PlayerId=$playerId;PlayerName='Duplicate A'}
        $resume=Send-Request $resumed 5 @{SessionToken=$token}
        $newStatus=Send-Request $resumed 8 @{}
        $oldUsable=$null
        try{$oldResult=Send-Request $a 8 @{};$oldUsable=($oldResult.Response.Envelope.Type -eq 8)}catch{$oldUsable=$false}
        [pscustomobject]@{TestCase='TC-006';FirstType=$first.Response.Envelope.Type;TokenIssued=![string]::IsNullOrWhiteSpace($token);DuplicateType=$duplicateResult.Response.Envelope.Type;DuplicateError=$duplicateResult.Response.Envelope.Error;ResumeType=$resume.Response.Envelope.Type;ResumedPlayerId=$resume.Response.Payload.PlayerId;SamePlayer=($resume.Response.Payload.PlayerId -eq $playerId);NewConnectionStatusType=$newStatus.Response.Envelope.Type;OldConnectionUsable=$oldUsable;TokenLogged=$false}|ConvertTo-Json -Compress -Depth 5
        "END TC-006 at=$(Get-Date -Format o)"
    }finally{$a.Close();$duplicate.Close();$resumed.Close()}
    return
}

if($TokenOnly){
    "RUN TC-095 token-validation at=$(Get-Date -Format o)"
    foreach($variant in @('empty','fake')){
        $client=New-Client
        try{
            $value=if($variant -eq 'empty'){''}else{[guid]::NewGuid().ToString('N')}
            $result=Send-Request $client 5 @{SessionToken=$value}
            [pscustomobject]@{TestCase='TC-095';Variant=$variant;Type=$result.Response.Envelope.Type;Error=$result.Response.Envelope.Error;ReturnedPlayerId=$result.Response.Payload.PlayerId}|ConvertTo-Json -Compress -Depth 5
        }finally{$client.Close()}
    }
    "END TC-095 at=$(Get-Date -Format o)"
    return
}

if($JoinRaceOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-015 join-race tag=$tag at=$(Get-Date -Format o)"
    $owner=New-Client;$b=New-Client;$c=New-Client
    try{
        $null=Send-Request $owner 0 @{PlayerId="join-owner-$tag";PlayerName='Owner'}
        $null=Send-Request $b 0 @{PlayerId="join-b-$tag";PlayerName='Join B'}
        $null=Send-Request $c 0 @{PlayerId="join-c-$tag";PlayerName='Join C'}
        $room=Send-Request $owner 9 @{RoomName="Join race $tag";Difficulty=1}
        if($room.Response.Envelope.Type -ne 9){throw "CreateRoom setup failed: $($room.Response.Envelope.Error.Message)"}
        $roomId=[string]$room.Response.Payload.RoomId
        $watch=[Diagnostics.Stopwatch]::StartNew()
        $idB=Send-Frame $b 10 @{RoomId=$roomId};$idC=Send-Frame $c 10 @{RoomId=$roomId}
        $sendMs=$watch.Elapsed.TotalMilliseconds
        $rB=Await-Frame $b $idB;$rC=Await-Frame $c $idC
        $watch.Stop()
        $snapshot=Send-Request $owner 8 @{}
        $roomState=@($snapshot.Response.Payload.Rooms|Where-Object RoomId -eq $roomId)[0]
        [pscustomobject]@{TestCase='TC-015';RoomId=$roomId;SendTwoFramesMs=[math]::Round($sendMs,3);ElapsedMs=[math]::Round($watch.Elapsed.TotalMilliseconds,3);BType=$rB.Response.Envelope.Type;BError=$rB.Response.Envelope.Error;CType=$rC.Response.Envelope.Type;CError=$rC.Response.Envelope.Error;PlayerCount=@($roomState.Players).Count;Players=@($roomState.Players|ForEach-Object{$_.PlayerId})}|ConvertTo-Json -Compress -Depth 8
        "END TC-015 at=$(Get-Date -Format o)"
    }finally{$owner.Close();$b.Close();$c.Close()}
    return
}

if($WrongRoomOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-017 wrong-room tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client
    try{
        $null=Send-Request $a 0 @{PlayerId="wrong-room-a-$tag";PlayerName='Owner'}
        $null=Send-Request $b 0 @{PlayerId="wrong-room-b-$tag";PlayerName='Guest'}
        $room=Send-Request $a 9 @{RoomName="Wrong room $tag";Difficulty=1}
        if($room.Response.Envelope.Type -ne 9){throw "CreateRoom setup failed: $($room.Response.Envelope.Error.Message)"}
        $roomId=[string]$room.Response.Payload.RoomId
        $null=Send-Request $b 10 @{RoomId=$roomId}
        $wrongId=[guid]::NewGuid().ToString()
        $wrong=Send-Request $a 13 @{RoomId=$wrongId;Difficulty=1;Duration=5}
        $list=Send-Request $a 8 @{}
        $state=@($list.Response.Payload.Rooms|Where-Object RoomId -eq $roomId)[0]
        [pscustomobject]@{TestCase='TC-017';Variant='wrong-room';Type=$wrong.Response.Envelope.Type;Error=$wrong.Response.Envelope.Error;RealRoomId=$roomId;RealRoomActive=$state.HasActiveMatch;RealRoomPlayers=@($state.Players).Count}|ConvertTo-Json -Compress -Depth 6
        "END TC-017 at=$(Get-Date -Format o)"
    }finally{$a.Close();$b.Close()}
    return
}

if ($MatchIdsOnly) {
    $tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
    "RUN TC-043/085 tag=$tag at=$(Get-Date -Format o)"
    $a = New-Client; $b = New-Client
    try {
        Show-Result 'SETUP' 'handshake-A' (Send-Request $a 0 @{ PlayerId = "ids-a-$tag"; PlayerName = 'Ids A' })
        Show-Result 'SETUP' 'handshake-B' (Send-Request $b 0 @{ PlayerId = "ids-b-$tag"; PlayerName = 'Ids B' })
        $create = Send-Request $a 9 @{ RoomName = "IDs $tag"; Difficulty = 1 }
        Show-Result 'SETUP' 'create-room' $create
        if ($create.Response.Envelope.Type -ne 9) { throw 'CreateRoom setup failed.' }
        $roomId = [string]$create.Response.Payload.RoomId
        Show-Result 'SETUP' 'join-room' (Send-Request $b 10 @{ RoomId = $roomId })
        $start = Send-Request $a 13 @{ RoomId = $roomId; Difficulty = 1; Duration = 5 }
        Show-Result 'SETUP' 'start-match' $start
        if ($start.Response.Envelope.Type -ne 14) { throw 'StartMatch setup failed.' }
        $matchId = [string]$start.Response.Payload.MatchId
        $before = Snapshot $a $matchId
        foreach ($idCase in @(
            @{ Name='empty'; Id='' },
            @{ Name='malformed'; Id='not-a-guid' },
            @{ Name='nonexistent'; Id=[guid]::NewGuid().ToString() }
        )) {
            foreach ($command in @(
                @{ Name='ready'; Type=15; Payload=@{ MatchId=$idCase.Id } },
                @{ Name='status'; Type=17; Payload=@{ MatchId=$idCase.Id } },
                @{ Name='move'; Type=19; Payload=@{ MatchId=$idCase.Id; MoveId=[guid]::NewGuid().ToString('N'); Row=0; Column=0; Value=1 } }
            )) {
                try { Show-Result 'TC-043/085' "$($idCase.Name)-$($command.Name)" (Send-Request $a $command.Type $command.Payload) }
                catch { "TC-043/085 $($idCase.Name)-$($command.Name) exception=$($_.Exception.GetType().Name): $($_.Exception.Message)" }
            }
        }
        $after = Snapshot $a $matchId
        "TC-043/085 state-unchanged=$((($before | ConvertTo-Json -Compress -Depth 10) -eq ($after | ConvertTo-Json -Compress -Depth 10)))"
        "TC-043/085 before-state=$($before.State) after-state=$($after.State)"
        Show-Result 'SETUP' 'legitimate-status-still-works' (Send-Request $a 17 @{ MatchId=$matchId })
    }
    finally { $a.Close(); $b.Close() }
    "END TC-043/085 at=$(Get-Date -Format o)"
    return
}

if ($LifecycleOnly) {
    $tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
    "RUN TC-087 tag=$tag at=$(Get-Date -Format o)"
    $a = New-Client; $b = New-Client
    try {
        Show-Result 'SETUP' 'handshake-A' (Send-Request $a 0 @{ PlayerId="life-a-$tag"; PlayerName='Life A' })
        Show-Result 'SETUP' 'handshake-B' (Send-Request $b 0 @{ PlayerId="life-b-$tag"; PlayerName='Life B' })
        $create = Send-Request $a 9 @{ RoomName="Life $tag"; Difficulty=1 }
        Show-Result 'SETUP' 'create-room' $create
        if ($create.Response.Envelope.Type -ne 9) { throw 'CreateRoom setup failed.' }
        $roomId = [string]$create.Response.Payload.RoomId
        Show-Result 'SETUP' 'join-room' (Send-Request $b 10 @{ RoomId=$roomId })
        $start = Send-Request $a 13 @{ RoomId=$roomId; Difficulty=1; Duration=5 }
        Show-Result 'SETUP' 'start-match' $start
        if ($start.Response.Envelope.Type -ne 14) { throw 'StartMatch setup failed.' }
        $matchId = [string]$start.Response.Payload.MatchId
        Show-Result 'TC-087' 'move-in-preparing' (Send-Request $a 19 @{ MatchId=$matchId; MoveId=[guid]::NewGuid().ToString('N'); Row=0; Column=0; Value=1 })
        Show-Result 'TC-087' 'start-duplicate-preparing' (Send-Request $a 13 @{ RoomId=$roomId; Difficulty=1; Duration=5 })
        Show-Result 'SETUP' 'ready-A' (Send-Request $a 15 @{ MatchId=$matchId })
        $beforeDuplicate = Snapshot $a $matchId
        Show-Result 'TC-087' 'ready-A-duplicate-preparing' (Send-Request $a 15 @{ MatchId=$matchId })
        $afterDuplicate = Snapshot $a $matchId
        "TC-087 preparing-unchanged=$((($beforeDuplicate | ConvertTo-Json -Compress -Depth 10) -eq ($afterDuplicate | ConvertTo-Json -Compress -Depth 10)))"
        Show-Result 'SETUP' 'ready-B' (Send-Request $b 15 @{ MatchId=$matchId })
        $ongoingBefore = Snapshot $a $matchId
        Show-Result 'TC-087' 'ready-A-duplicate-ongoing' (Send-Request $a 15 @{ MatchId=$matchId })
        Show-Result 'TC-087' 'start-duplicate-ongoing' (Send-Request $a 13 @{ RoomId=$roomId; Difficulty=1; Duration=5 })
        $ongoingAfter = Snapshot $a $matchId
        "TC-087 ongoing-board-unchanged=$((($ongoingBefore.OwnBoard | ConvertTo-Json -Compress) -eq ($ongoingAfter.OwnBoard | ConvertTo-Json -Compress)))"
        "TC-087 ongoing-deadline-unchanged=$($ongoingBefore.EndsAtUtc -eq $ongoingAfter.EndsAtUtc)"
    }
    finally { $a.Close(); $b.Close() }
    "END TC-087 at=$(Get-Date -Format o)"
    return
}

if ($EmptyPayloadOnly) {
    $tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
    "RUN TC-081 tag=$tag at=$(Get-Date -Format o)"
    $client = New-Client
    try {
        foreach ($scenario in @(
            @{ Name='handshake-null'; Type=0; Payload=$null },
            @{ Name='handshake-empty'; Type=0; Payload=@{} },
            @{ Name='handshake-missing-name'; Type=0; Payload=@{ PlayerId="empty-$tag" } }
        )) {
            try { Show-Result 'TC-081' $scenario.Name (Send-Request $client $scenario.Type $scenario.Payload) }
            catch { "TC-081 $($scenario.Name) exception=$($_.Exception.GetType().Name): $($_.Exception.Message)" }
        }
        Show-Result 'SETUP' 'valid-handshake' (Send-Request $client 0 @{ PlayerId="empty-$tag"; PlayerName='Empty Payload Test' })
        $roomsBefore = (Send-Request $client 8 @{}).Response.Payload.Rooms | ConvertTo-Json -Compress -Depth 10
        foreach ($command in @(
            @{ Name='listrooms-null'; Type=8 },
            @{ Name='joinroom-null'; Type=10 },
            @{ Name='leaveroom-null'; Type=11 },
            @{ Name='startmatch-null'; Type=13 },
            @{ Name='ready-null'; Type=15 },
            @{ Name='status-null'; Type=17 },
            @{ Name='move-null'; Type=19 }
        )) {
            foreach ($variant in @(@{ Label='null'; Payload=$null }, @{ Label='empty'; Payload=@{} })) {
                try { Show-Result 'TC-081' "$($command.Name)-$($variant.Label)" (Send-Request $client $command.Type $variant.Payload) }
                catch { "TC-081 $($command.Name)-$($variant.Label) exception=$($_.Exception.GetType().Name): $($_.Exception.Message)" }
            }
        }
        $roomsAfter = (Send-Request $client 8 @{}).Response.Payload.Rooms | ConvertTo-Json -Compress -Depth 10
        "TC-081 rooms-unchanged=$($roomsBefore -eq $roomsAfter)"
        Show-Result 'SETUP' 'valid-listrooms-after-invalid' (Send-Request $client 8 @{})
    }
    finally { $client.Close() }
    "END TC-081 at=$(Get-Date -Format o)"
    return
}

if ($CorrelationPushOnly) {
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-100 tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client
    try {
        $null=Send-Request $a 0 @{PlayerId="corr-a-$tag";PlayerName='Corr A'}
        $null=Send-Request $b 0 @{PlayerId="corr-b-$tag";PlayerName='Corr B'}
        $idList=Send-Frame $b 8 @{}
        $idBeat=Send-Frame $b 2 @{}
        $create=Send-Request $a 9 @{RoomName="Corr $tag";Difficulty=1}
        if($create.Response.Envelope.Type -ne 9){throw 'Create failed'}
        $roomId=[string]$create.Response.Payload.RoomId
        $idJoin=Send-Frame $b 10 @{RoomId=$roomId}
        $expected=@{};$expected[[string]$idList]=8;$expected[[string]$idBeat]=3;$expected[[string]$idJoin]=10
        $seen=@{};$events=@();$read=0
        while($seen.Count -lt 3 -and $read -lt 12){
            $message=Read-Message $b;$read++
            $cid=[string]$message.Envelope.CorrelationId
            if($expected.ContainsKey($cid)){$seen[$cid]=[int]$message.Envelope.Type}
            else{$events+=,[int]$message.Envelope.Type}
        }
        $allCorrect=($seen.Count -eq 3 -and $seen[[string]$idList] -eq 8 -and $seen[[string]$idBeat] -eq 3 -and $seen[[string]$idJoin] -eq 10)
        [pscustomobject]@{TestCase='TC-100';Step='three-pipelined-requests-with-push';Sent=3;Responses=$seen.Count;AllCorrelationTypesCorrect=$allCorrect;ListResponseType=$seen[[string]$idList];HeartbeatResponseType=$seen[[string]$idBeat];JoinResponseType=$seen[[string]$idJoin];UncorrelatedEventTypes=$events;RoomUpdatedObserved=($events -contains 12);ReadFrames=$read;Timestamp=(Get-Date -Format o)}|ConvertTo-Json -Compress
    }
    finally{$a.Close();$b.Close()}
    return
}

if ($LeaveRoomOnly) {
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-009 tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client
    try {
        $aid="leave-a-$tag";$bid="leave-b-$tag"
        $ha=Send-Request $a 0 @{PlayerId=$aid;PlayerName='Leave A'}
        $hb=Send-Request $b 0 @{PlayerId=$bid;PlayerName='Leave B'}
        if($ha.Response.Envelope.Type -ne 1 -or $hb.Response.Envelope.Type -ne 1){throw 'Handshake failed'}
        $create=Send-Request $a 9 @{RoomName="Leave $tag";Difficulty=1}
        if($create.Response.Envelope.Type -ne 9){throw 'Create failed'}
        $roomId=[string]$create.Response.Payload.RoomId
        $join=Send-Request $b 10 @{RoomId=$roomId}
        if($join.Response.Envelope.Type -ne 10){throw 'Join failed'}
        $before=@((Send-Request $a 8 @{}).Response.Payload.Rooms|Where-Object RoomId -eq $roomId)
        $leaveB=Send-Request $b 11 @{RoomId=$roomId}
        $afterB=@((Send-Request $a 8 @{}).Response.Payload.Rooms|Where-Object RoomId -eq $roomId)
        $leaveA=Send-Request $a 11 @{RoomId=$roomId}
        $afterA=@((Send-Request $a 8 @{}).Response.Payload.Rooms|Where-Object RoomId -eq $roomId)
        [pscustomobject]@{TestCase='TC-009';Step='leave-lifecycle';RoomId=$roomId;BeforeRooms=$before.Count;BeforePlayers=if($before.Count){@($before[0].Players).Count}else{0};LeaveBType=$leaveB.Response.Envelope.Type;AfterBRooms=$afterB.Count;AfterBPlayers=if($afterB.Count){@($afterB[0].Players).Count}else{0};AfterBOnlyA=if($afterB.Count){[string]$afterB[0].Players[0].PlayerId -eq $aid}else{$false};LeaveAType=$leaveA.Response.Envelope.Type;AfterARooms=$afterA.Count;End=(Get-Date -Format o)}|ConvertTo-Json -Compress
    }
    finally{$a.Close();$b.Close()}
    return
}

if ($RoomOnly) {
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-014/015/017 tag=$tag at=$(Get-Date -Format o)"
    $clients=@();for($i=0;$i -lt 4;$i++){$clients+=,(New-Client)}
    try {
        for($i=0;$i -lt 4;$i++){Show-Result 'SETUP' "handshake-$i" (Send-Request $clients[$i] 0 @{PlayerId="room-$tag-$i";PlayerName="Room $i"})}
        $roomsBefore=(Send-Request $clients[0] 8 @{}).Response.Payload.Rooms.Count
        Show-Result 'TC-015' 'join-nonexistent' (Send-Request $clients[1] 10 @{RoomId=[guid]::NewGuid().ToString()})
        foreach($difficulty in @(0,1,2)){
            $room=Send-Request $clients[0] 9 @{RoomName="Room $tag $difficulty";Difficulty=$difficulty}
            Show-Result 'TC-014' "create-difficulty-$difficulty" $room
            if($room.Response.Envelope.Type -ne 9){throw "CreateRoom difficulty $difficulty failed"}
            $roomId=[string]$room.Response.Payload.RoomId
            $beforeJoin=(Send-Request $clients[0] 8 @{}).Response.Payload.Rooms | Where-Object RoomId -eq $roomId
            Show-Result 'TC-017' "start-one-player-$difficulty" (Send-Request $clients[0] 13 @{RoomId=$roomId;Difficulty=$difficulty;Duration=5})
            $join=Send-Request $clients[1] 10 @{RoomId=$roomId}
            Show-Result 'TC-014' "join-difficulty-$difficulty" $join
            $duplicate=Send-Request $clients[1] 10 @{RoomId=$roomId}
            Show-Result 'TC-015' "duplicate-join-$difficulty" $duplicate
            $full=Send-Request $clients[2] 10 @{RoomId=$roomId}
            Show-Result 'TC-015' "full-join-$difficulty" $full
            $afterJoin=(Send-Request $clients[0] 8 @{}).Response.Payload.Rooms | Where-Object RoomId -eq $roomId
            "TC-014 room-$difficulty before-players=$($beforeJoin.Players.Count) after-players=$($afterJoin.Players.Count) same-id=$($afterJoin.RoomId -eq $roomId) difficulty=$($afterJoin.Difficulty)"
            if($difficulty -eq 0){Show-Result 'TC-017' 'outsider-start' (Send-Request $clients[2] 13 @{RoomId=$roomId;Difficulty=$difficulty;Duration=5})}
            $start=Send-Request $clients[0] 13 @{RoomId=$roomId;Difficulty=$difficulty;Duration=5}
            Show-Result 'TC-014/020' "legitimate-start-$difficulty" $start
            if($start.Response.Envelope.Type -eq 14){
                $puzzle=@($start.Response.Payload.Puzzle)
                $zeros=@($puzzle|Where-Object{$_ -eq 0}).Count
                "TC-020 difficulty=$difficulty room-difficulty=$($afterJoin.Difficulty) puzzle-length=$($puzzle.Count) blanks=$zeros"
            }
            if($difficulty -eq 0){
                Show-Result 'TC-017' 'duplicate-start' (Send-Request $clients[0] 13 @{RoomId=$roomId;Difficulty=$difficulty;Duration=5})
            }
            if($difficulty -ne 0){
                Show-Result 'SETUP' "leave-B-$difficulty" (Send-Request $clients[1] 11 @{RoomId=$roomId})
                Show-Result 'SETUP' "leave-A-$difficulty" (Send-Request $clients[0] 11 @{RoomId=$roomId})
            }else{
                Show-Result 'SETUP' 'leave-B-first' (Send-Request $clients[1] 11 @{RoomId=$roomId})
                Show-Result 'SETUP' 'leave-A-first' (Send-Request $clients[0] 11 @{RoomId=$roomId})
            }
        }
        $roomsAfter=(Send-Request $clients[0] 8 @{}).Response.Payload.Rooms.Count
        "TC-014/015 rooms-before=$roomsBefore rooms-after=$roomsAfter"
    }
    finally{foreach($client in $clients){try{$client.Close()}catch{}}}
    "END TC-014/015/017 at=$(Get-Date -Format o)"
    return
}

if ($DurationOnly) {
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-051 tag=$tag at=$(Get-Date -Format o)"
    $allClients=@()
    try {
        foreach($duration in @(5,10,15,0,16)){
            $a=New-Client;$b=New-Client;$allClients+=,$a;$allClients+=,$b
            $null=Send-Request $a 0 @{PlayerId="duration-a-$duration-$tag";PlayerName="Duration A $duration"}
            $null=Send-Request $b 0 @{PlayerId="duration-b-$duration-$tag";PlayerName="Duration B $duration"}
            $room=Send-Request $a 9 @{RoomName="Duration $duration $tag";Difficulty=1}
            if($room.Response.Envelope.Type -ne 9){throw "CreateRoom failed duration $duration"}
            $roomId=[string]$room.Response.Payload.RoomId
            $null=Send-Request $b 10 @{RoomId=$roomId}
            $start=Send-Request $a 13 @{RoomId=$roomId;Difficulty=1;Duration=$duration}
            Show-Result 'TC-051' "start-duration-$duration" $start
            if($duration -in @(5,10,15)){
                if($start.Response.Envelope.Type -ne 14){continue}
                $matchId=[string]$start.Response.Payload.MatchId
                $null=Send-Request $a 15 @{MatchId=$matchId}
                $null=Send-Request $b 15 @{MatchId=$matchId}
                $statusA=Snapshot $a $matchId;$statusB=Snapshot $b $matchId
                $startA=[DateTime]::Parse([string]$statusA.StartedAtUtc);$endA=[DateTime]::Parse([string]$statusA.EndsAtUtc)
                "TC-051 duration-$duration A-state=$($statusA.State) B-state=$($statusB.State) minutes=$(($endA-$startA).TotalMinutes) same-start=$($statusA.StartedAtUtc -eq $statusB.StartedAtUtc) same-end=$($statusA.EndsAtUtc -eq $statusB.EndsAtUtc)"
            }
        }
    }
    finally{foreach($c in $allClients){try{$c.Close()}catch{}}}
    "END TC-051 at=$(Get-Date -Format o)"
    return
}

if($RateLimitOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-102 tag=$tag at=$(Get-Date -Format o)"
    $a=New-Client;$b=New-Client
    try{
        Show-Result 'SETUP' 'handshake-burst' (Send-Request $a 0 @{PlayerId="burst-$tag";PlayerName='Burst'})
        Show-Result 'SETUP' 'handshake-control' (Send-Request $b 0 @{PlayerId="control-$tag";PlayerName='Control'})
        $roomsBefore=(Send-Request $b 8 @{}).Response.Payload.Rooms|ConvertTo-Json -Compress -Depth 10
        $watch=[Diagnostics.Stopwatch]::StartNew();$accepted=0;$rejected=0;$errorCodes=@{}
        for($i=0;$i -lt 125;$i++){
            try{$r=Send-Request $a 2 @{};if($r.Response.Envelope.Type -eq 3){$accepted++}else{$rejected++;$code=[string]$r.Response.Envelope.Error.Code;$errorCodes[$code]=1+[int]$errorCodes[$code]}}
            catch{$rejected++;$code=$_.Exception.GetType().Name;$errorCodes[$code]=1+[int]$errorCodes[$code]}
        }
        $watch.Stop()
        Show-Result 'TC-102' 'control-after-burst' (Send-Request $b 2 @{})
        $roomsAfter=(Send-Request $b 8 @{}).Response.Payload.Rooms|ConvertTo-Json -Compress -Depth 10
        [pscustomobject]@{TestCase='TC-102';BurstRequests=125;ElapsedSeconds=[math]::Round($watch.Elapsed.TotalSeconds,3);Accepted=$accepted;Rejected=$rejected;ErrorCodes=$errorCodes;RoomsUnchanged=($roomsBefore -eq $roomsAfter)}|ConvertTo-Json -Compress -Depth 5
    }finally{$a.Close();$b.Close()}
    "END TC-102 at=$(Get-Date -Format o)"
    return
}

if($StartRaceOnly){
    $tag=[guid]::NewGuid().ToString('N').Substring(0,8)
    "RUN TC-067 tag=$tag at=$(Get-Date -Format o)"
    $clients=@();$rooms=@();$ids=@();$responses=@()
    try{
        for($i=0;$i -lt 4;$i++){$c=New-Client;$clients+=,$c;$null=Send-Request $c 0 @{PlayerId="race-$tag-$i";PlayerName="Race $i"}}
        for($pair=0;$pair -lt 2;$pair++){
            $a=$pair*2;$b=$a+1
            $room=Send-Request $clients[$a] 9 @{RoomName="Race $tag $pair";Difficulty=1}
            if($room.Response.Envelope.Type -ne 9){throw "CreateRoom $pair failed"}
            $roomId=[string]$room.Response.Payload.RoomId;$rooms+=,$roomId
            $join=Send-Request $clients[$b] 10 @{RoomId=$roomId}
            if($join.Response.Envelope.Type -ne 10){throw "JoinRoom $pair failed"}
            "SETUP room-$pair=$roomId"
        }
        $watch=[Diagnostics.Stopwatch]::StartNew()
        for($i=0;$i -lt 4;$i++){$pair=[math]::Floor($i/2);$ids+=,(Send-Frame $clients[$i] 13 @{RoomId=$rooms[$pair];Difficulty=1;Duration=5})}
        $sendMs=$watch.Elapsed.TotalMilliseconds
        for($i=0;$i -lt 4;$i++){
            $r=Await-Frame $clients[$i] $ids[$i];$responses+=,$r
            Show-Result 'TC-067' "start-$i" $r
        }
        $watch.Stop()
        $roomStates=(Send-Request $clients[0] 8 @{}).Response.Payload.Rooms|Where-Object{$_.RoomId -in $rooms}
        $summary=@()
        for($pair=0;$pair -lt 2;$pair++){
            $a=$pair*2;$b=$a+1
            $pairResponses=@($responses[$a],$responses[$b])
            $success=@($pairResponses|Where-Object{$_.Response.Envelope.Type -eq 14}).Count
            $errors=@($pairResponses|Where-Object{$_.Response.Envelope.Type -eq 4}).Count
            $roomState=$roomStates|Where-Object RoomId -eq $rooms[$pair]
            $summary+=,[pscustomobject]@{Pair=$pair;Success=$success;Errors=$errors;RoomActive=$roomState.HasActiveMatch;RoomId=$rooms[$pair]}
        }
        [pscustomobject]@{TestCase='TC-067';SendFourFramesMs=[math]::Round($sendMs,3);ElapsedMs=[math]::Round($watch.Elapsed.TotalMilliseconds,3);Summary=$summary}|ConvertTo-Json -Compress -Depth 5
    }finally{foreach($c in $clients){try{$c.Close()}catch{}}}
    "END TC-067 at=$(Get-Date -Format o)"
    return
}

if ($InvalidIdsOnly) {
    $tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
    "RUN TC-085 tag=$tag at=$(Get-Date -Format o)"
    foreach ($scenario in @(
        @{ Name = 'blank-player-id'; PlayerId = ''; PlayerName = 'Valid Name' },
        @{ Name = 'blank-player-name'; PlayerId = "id-$tag"; PlayerName = '' },
        @{ Name = 'long-player-id'; PlayerId = ('i' * 101); PlayerName = 'Valid Name' },
        @{ Name = 'long-player-name'; PlayerId = "long-name-$tag"; PlayerName = ('n' * 101) }
    )) {
        $client = New-Client
        try {
            $result = Send-Request $client 0 @{ PlayerId = $scenario.PlayerId; PlayerName = $scenario.PlayerName }
            Show-Result 'TC-085' $scenario.Name $result
        }
        finally { $client.Close() }
    }
    $client = New-Client
    try {
        $hello = Send-Request $client 0 @{ PlayerId = "valid-$tag"; PlayerName = 'Valid Name' }
        Show-Result 'SETUP' 'valid-handshake' $hello
        $roomsBefore = @((Send-Request $client 8 @{}).Response.Payload.Rooms)
        foreach ($item in @(
            @{ Name = 'empty-guid'; RoomId = '00000000-0000-0000-0000-000000000000' },
            @{ Name = 'malformed-guid'; RoomId = 'not-a-guid' },
            @{ Name = 'unknown-guid'; RoomId = [guid]::NewGuid().ToString() }
        )) {
            $result = Send-Request $client 10 @{ RoomId = $item.RoomId }
            Show-Result 'TC-085' $item.Name $result
        }
        $roomsAfter = @((Send-Request $client 8 @{}).Response.Payload.Rooms)
        [pscustomobject]@{TestCase='TC-085';Step='room-state-after-invalid-ids';RoomsBefore=$roomsBefore.Count;RoomsAfter=$roomsAfter.Count;SameRoomIds=(($roomsBefore.RoomId|Sort-Object|ConvertTo-Json -Compress) -eq ($roomsAfter.RoomId|Sort-Object|ConvertTo-Json -Compress))}|ConvertTo-Json -Compress
    }
    finally { $client.Close() }
    $retryClient = New-Client
    try { Show-Result 'TC-085' 'valid-name-after-blank-name-attempt' (Send-Request $retryClient 0 @{PlayerId="id-$tag";PlayerName='Valid Name'}) }
    finally { $retryClient.Close() }
    "END TC-085 at=$(Get-Date -Format o)"
    return
}

if ($HealthOnly) {
    $tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
    "RUN HEALTH tag=$tag at=$(Get-Date -Format o)"
    $a = New-Client
    $b = New-Client
    try {
        Show-Result 'HEALTH' 'handshake-A' (Send-Request $a 0 @{ PlayerId = "health-a-$tag"; PlayerName = 'Health A' })
        Show-Result 'HEALTH' 'handshake-B' (Send-Request $b 0 @{ PlayerId = "health-b-$tag"; PlayerName = 'Health B' })
        $create = Send-Request $a 9 @{ RoomName = "Health Room $tag"; Difficulty = 1 }
        Show-Result 'HEALTH' 'create-room' $create
        if ($create.Response.Envelope.Type -ne 9) { throw 'Health CreateRoom did not succeed.' }
        $join = Send-Request $b 10 @{ RoomId = [string]$create.Response.Payload.RoomId }
        Show-Result 'HEALTH' 'join-room' $join
        if ($join.Response.Envelope.Type -ne 10) { throw 'Health JoinRoom did not succeed.' }
    }
    finally {
        $a.Close()
        $b.Close()
    }
    "END HEALTH at=$(Get-Date -Format o)"
    return
}

$tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
"RUN tag=$tag at=$(Get-Date -Format o)"
if (-not $ProtocolOnly) {
$a = New-Client
$b = New-Client
$c = New-Client
try {
    foreach ($item in @(@($a, "target-a-$tag"), @($b, "target-b-$tag"), @($c, "target-c-$tag"))) {
        $hello = Send-Request $item[0] 0 @{ PlayerId = $item[1]; PlayerName = $item[1] }
        Show-Result 'SETUP' "handshake-$($item[1])" $hello
    }

    $create = Send-Request $a 9 @{ RoomName = "Targeted $tag"; Difficulty = 1 }
    $roomId = [string]$create.Response.Payload.RoomId
    Show-Result 'SETUP' 'create-room-A' $create
    if ($create.Response.Envelope.Type -ne 9) { throw 'CreateRoom setup failed; authorization scenario was not executed.' }
    $join = Send-Request $b 10 @{ RoomId = $roomId }
    Show-Result 'SETUP' 'join-room-B' $join
    if ($join.Response.Envelope.Type -ne 10) { throw 'JoinRoom setup failed; authorization scenario was not executed.' }

    $outsideStart = Send-Request $c 13 @{ RoomId = $roomId; Difficulty = 1; Duration = 5 }
    Show-Result 'TC-103' 'outsider-start' $outsideStart
    $roomBeforeLegit = (Send-Request $a 8 @{}).Response.Payload.Rooms | Where-Object RoomId -eq $roomId
    "TC-103 room-before-legitimate-start=$($roomBeforeLegit | ConvertTo-Json -Compress -Depth 10)"

    $start = Send-Request $a 13 @{ RoomId = $roomId; Difficulty = 1; Duration = 5 }
    $matchId = [string]$start.Response.Payload.MatchId
    Show-Result 'SETUP' 'legitimate-start-A' $start
    $outsideReady = Send-Request $c 15 @{ MatchId = $matchId }
    Show-Result 'TC-104' 'outsider-ready-before-players' $outsideReady
    $readyA = Send-Request $a 15 @{ MatchId = $matchId }
    Show-Result 'SETUP' 'ready-A' $readyA
    $readyB = Send-Request $b 15 @{ MatchId = $matchId }
    Show-Result 'SETUP' 'ready-B' $readyB
    $beforeA = Snapshot $a $matchId
    $beforeB = Snapshot $b $matchId
    "TC-104 status-before-A=$($beforeA | ConvertTo-Json -Compress -Depth 10)"
    "TC-104 status-before-B=$($beforeB | ConvertTo-Json -Compress -Depth 10)"

    $outsideStatus = Send-Request $c 17 @{ MatchId = $matchId }
    Show-Result 'TC-104' 'outsider-status' $outsideStatus
    $outsideMove = Send-Request $c 19 @{ MatchId = $matchId; MoveId = [guid]::NewGuid().ToString('N'); Row = 0; Column = 0; Value = 1 }
    Show-Result 'TC-104' 'outsider-move' $outsideMove
    $afterA = Snapshot $a $matchId
    $afterB = Snapshot $b $matchId
    "TC-104 status-after-A=$($afterA | ConvertTo-Json -Compress -Depth 10)"
    "TC-104 status-after-B=$($afterB | ConvertTo-Json -Compress -Depth 10)"
    "TC-104 A-state-unchanged=$((($beforeA | ConvertTo-Json -Compress -Depth 10) -eq ($afterA | ConvertTo-Json -Compress -Depth 10)))"
    "TC-104 B-state-unchanged=$((($beforeB | ConvertTo-Json -Compress -Depth 10) -eq ($afterB | ConvertTo-Json -Compress -Depth 10)))"
}
finally {
    $a.Close()
    $b.Close()
    $c.Close()
}
}

if (-not $AuthorizationOnly) {
$monitor = New-Client
try {
    $monitorHello = Send-Request $monitor 0 @{ PlayerId = "monitor-$tag"; PlayerName = 'Protocol Monitor' }
    Show-Result 'SETUP' 'handshake-monitor' $monitorHello
    $roomsBefore = (Send-Request $monitor 8 @{}).Response.Payload.Rooms | ConvertTo-Json -Compress -Depth 20

foreach ($scenario in @(
    @{ Name = 'version-zero'; Type = 8; Version = 0 },
    @{ Name = 'version-two'; Type = 8; Version = 2 },
    @{ Name = 'unknown-command'; Type = 999; Version = 1 },
    @{ Name = 'server-event-command'; Type = 14; Version = 1 }
)) {
    $client = New-Client
    try {
        $hello = Send-Request $client 0 @{ PlayerId = "protocol-$($scenario.Name)-$tag"; PlayerName = 'Protocol Tester' }
        Show-Result 'SETUP' "handshake-$($scenario.Name)" $hello
        $response = Send-Request $client $scenario.Type @{} $scenario.Version
        Show-Result 'TC-084' $scenario.Name $response
    }
    catch {
        "TC-084 $($scenario.Name) exception=$($_.Exception.GetType().Name): $($_.Exception.Message)"
    }
    finally {
        $client.Close()
    }
}
    $roomsAfter = (Send-Request $monitor 8 @{}).Response.Payload.Rooms | ConvertTo-Json -Compress -Depth 20
    "TC-084 rooms-unchanged=$(($roomsBefore -eq $roomsAfter))"
}
finally { $monitor.Close() }
}
"END at=$(Get-Date -Format o)"
