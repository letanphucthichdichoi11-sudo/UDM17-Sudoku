param([string]$HostName = '127.0.0.1', [int]$Port = 5000)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
public static class SudokuSolver118 {
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
function Send-Move($client, $matchId, [int]$index, [int]$value, [string]$moveId = '') {
    if (-not $moveId) { $moveId = [guid]::NewGuid().ToString('N') }
    $result = Send-Request $client 19 @{ MatchId = $matchId; MoveId = $moveId; Row = [math]::Floor($index / 9); Column = ($index % 9); Value = $value }
    [pscustomobject]@{ Response = $result.Response.Payload; Events = @($result.Events | ForEach-Object { $_.Envelope.Type }); MoveId = $moveId }
}

$tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
"RUN TC-118 disconnect-e2e tag=$tag at=$(Get-Date -Format o)"
$a = New-Client; $b = New-Client; $a2 = $null
try {
    $helloA = Send-Request $a 0 @{ PlayerId = "e2e-a-$tag"; PlayerName = 'E2E A' }
    $tokenA = [string]$helloA.Response.Payload.SessionToken
    $helloB = Send-Request $b 0 @{ PlayerId = "e2e-b-$tag"; PlayerName = 'E2E B' }
    $tokenB = [string]$helloB.Response.Payload.SessionToken

    $room = Send-Request $a 9 @{ RoomName = "E2E $tag"; Difficulty = 0 }
    if ($room.Response.Envelope.Type -ne 9) { throw "CreateRoom failed: $($room.Response.Envelope.Error.Message)" }
    $roomId = [string]$room.Response.Payload.RoomId

    $join = Send-Request $b 10 @{ RoomId = $roomId }
    if ($join.Response.Envelope.Type -ne 10) { throw "JoinRoom failed: $($join.Response.Envelope.Error.Message)" }

    $start = Send-Request $a 13 @{ RoomId = $roomId; Difficulty = 0; Duration = 5 }
    if ($start.Response.Envelope.Type -ne 14) { throw "StartMatch failed" }
    $matchId = [string]$start.Response.Payload.MatchId

    $null = Send-Request $a 15 @{ MatchId = $matchId }
    $null = Send-Request $b 15 @{ MatchId = $matchId }

    $initialA = Status $a $matchId
    $initialB = Status $b $matchId
    $solutionA = [SudokuSolver118]::Solve([int[]]$initialA.Puzzle)
    $solutionB = [SudokuSolver118]::Solve([int[]]$initialB.Puzzle)

    # Find editable cells
    $editableA = @(); for ($i = 0; $i -lt 81; $i++) { if ($initialA.Puzzle[$i] -eq 0) { $editableA += ,$i } }
    $wrongIndex = [int]$editableA[0]
    $wrongVal = if ($solutionA[$wrongIndex] -eq 9) { 1 } else { [int]$solutionA[$wrongIndex] + 1 }

    # Step 1: A makes mistake
    $moveMistake = Send-Move $a $matchId $wrongIndex $wrongVal
    $statusA_mistake = Status $a $matchId
    Show 'TC-118' 'A-mistake' @{
        Index = $wrongIndex
        Value = $wrongVal
        Accepted = $moveMistake.Response.Accepted
        IsCorrect = $moveMistake.Response.IsCorrect
        OwnErrorCount = $statusA_mistake.OwnErrorCount
        HasUnresolved = $statusA_mistake.OwnHasUnresolvedMistake
    }

    # Step 2: A disconnects abruptly
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $a.Close()
    Start-Sleep -Seconds 2
    $statusB_during = Send-Request $b 17 @{ MatchId = $matchId }
    Show 'TC-118' 'A-disconnected' @{
        BEvents = @($statusB_during.Events | ForEach-Object { $_.Envelope.Type })
        BStatusState = $statusB_during.Response.Payload.State
        BOpponentError = $statusB_during.Response.Payload.OpponentErrorCount
        BOpponentUnresolved = $statusB_during.Response.Payload.OpponentHasUnresolvedMistake
    }

    # Step 3: A reconnects with session token within grace period
    $a2 = New-Client
    $reconnectA = Send-Request $a2 5 @{ SessionToken = $tokenA }
    $statusA_reconnected = Status $a2 $matchId
    Show 'TC-118' 'A-reconnected' @{
        ReconnectType = $reconnectA.Response.Envelope.Type
        PlayerId = $reconnectA.Response.Payload.PlayerId
        State = $statusA_reconnected.State
        OwnErrorCount = $statusA_reconnected.OwnErrorCount
        HasUnresolved = $statusA_reconnected.OwnHasUnresolvedMistake
        CellContainsMistake = ($statusA_reconnected.OwnBoard[$wrongIndex] -eq $wrongVal)
        TimerNotReset = ($statusA_reconnected.TimeLeft -ne $initialA.TimeLeft)
    }

    # Step 4: A erases mistake
    $eraseMove = Send-Move $a2 $matchId $wrongIndex 0
    $statusA_erased = Status $a2 $matchId
    Show 'TC-118' 'A-erase' @{
        Accepted = $eraseMove.Response.Accepted
        Cell = $statusA_erased.OwnBoard[$wrongIndex]
        OwnErrorCount = $statusA_erased.OwnErrorCount
        HasUnresolved = $statusA_erased.OwnHasUnresolvedMistake
    }

    # Step 5: B completes all cells to win
    $bMoves = 0
    for ($i = 0; $i -lt 81; $i++) {
        if ($initialB.Puzzle[$i] -eq 0) {
            $m = Send-Move $b $matchId $i $solutionB[$i]
            $bMoves++
        }
    }
    Start-Sleep -Milliseconds 500

    $finalA = Status $a2 $matchId
    $finalB = Status $b $matchId
    Show 'TC-118' 'final-result' @{
        MatchId = $matchId
        BMoves = $bMoves
        StateA = $finalA.State
        StateB = $finalB.State
        WinnerA = $finalA.WinnerPlayerId
        WinnerB = $finalB.WinnerPlayerId
        ReasonA = $finalA.FinishReason
        ReasonB = $finalB.FinishReason
        ConsistentResult = ($finalA.WinnerPlayerId -eq "e2e-b-$tag" -and $finalB.WinnerPlayerId -eq "e2e-b-$tag" -and $finalA.State -eq 2 -and $finalB.State -eq 2)
        AOwnErrors = $finalA.OwnErrorCount
        BOpponentErrors = $finalB.OpponentErrorCount
    }
    "END TC-118 at=$(Get-Date -Format o)"
} finally {
    try { $a.Close() } catch {}
    try { $b.Close() } catch {}
    try { if ($a2) { $a2.Close() } } catch {}
}
