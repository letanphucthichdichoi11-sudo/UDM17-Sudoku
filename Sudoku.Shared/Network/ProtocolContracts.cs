using System;
using System.Collections.Generic;
using Sudoku.Shared.Models;

namespace Sudoku.Shared.Network
{
    public sealed class EmptyPayload { }

    public sealed class HandshakeRequest
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
    }

    public sealed class HandshakeResponse
    {
        public string PlayerId { get; set; }
        public string SessionToken { get; set; }
    }

    public sealed class ReconnectRequest
    {
        public string SessionToken { get; set; }
    }

    public sealed class LobbyPlayerDto
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
    }

    public sealed class LobbyRoomDto
    {
        public Guid RoomId { get; set; }
        public string RoomName { get; set; }
        public List<LobbyPlayerDto> Players { get; set; }
        public bool HasActiveMatch { get; set; }
        public SudokuDifficultyLevel Difficulty { get; set; }
        public Guid? ActiveMatchId { get; set; }
        public MatchLifecycleState? MatchState { get; set; }
        public MatchDurationMinutes? Duration { get; set; }
        public bool IsChallengeRoom { get; set; }
    }

    public sealed class RoomListResponse
    {
        public List<LobbyRoomDto> Rooms { get; set; }
    }

    public enum LobbyPlayerState { Available, InRoom, InMatch }

    public sealed class OnlinePlayerDto
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public LobbyPlayerState State { get; set; }
        public string RoomName { get; set; }
        public Guid? MatchId { get; set; }
        public string OpponentName { get; set; }
        public SudokuDifficultyLevel? Difficulty { get; set; }
    }

    public sealed class OnlinePlayerListResponse
    {
        public List<OnlinePlayerDto> Players { get; set; } = new List<OnlinePlayerDto>();
    }

    public enum ChallengeStatus { Pending, Accepted, Declined, Cancelled, Expired, Invalidated }

    public sealed class ChallengeDto
    {
        public Guid ChallengeId { get; set; }
        public string ChallengerPlayerId { get; set; }
        public string ChallengerName { get; set; }
        public string TargetPlayerId { get; set; }
        public string TargetName { get; set; }
        public SudokuDifficultyLevel Difficulty { get; set; }
        public MatchDurationMinutes Duration { get; set; }
        public ChallengeStatus Status { get; set; }
        public Guid? RoomId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    public sealed class SendChallengeRequest
    {
        public string TargetPlayerId { get; set; }
        public SudokuDifficultyLevel Difficulty { get; set; }
        public MatchDurationMinutes Duration { get; set; }
    }

    public sealed class ChallengeIdRequest { public Guid ChallengeId { get; set; } }

    public sealed class ChallengeListResponse
    {
        public List<ChallengeDto> Challenges { get; set; } = new List<ChallengeDto>();
    }

    public sealed class SpectatorMatchState
    {
        public Guid MatchId { get; set; }
        public Guid RoomId { get; set; }
        public string PlayerAId { get; set; }
        public string PlayerAName { get; set; }
        public string PlayerBId { get; set; }
        public string PlayerBName { get; set; }
        public int[] PuzzleA { get; set; }
        public int[] PuzzleB { get; set; }
        public int[] BoardA { get; set; }
        public int[] BoardB { get; set; }
        public int CorrectCountA { get; set; }
        public int CorrectCountB { get; set; }
        public int ErrorCountA { get; set; }
        public int ErrorCountB { get; set; }
        public SudokuDifficultyLevel Difficulty { get; set; }
        public MatchDurationMinutes Duration { get; set; }
        public MatchLifecycleState State { get; set; }
        public DateTime ServerUtcNow { get; set; }
        public DateTime? EndsAtUtc { get; set; }
        public long Version { get; set; }
    }

    public sealed class CreateRoomRequest
    {
        public string RoomName { get; set; }
        public SudokuDifficultyLevel Difficulty { get; set; }
    }

    public sealed class RoomRequest
    {
        public Guid RoomId { get; set; }
    }

    public sealed class MatchIdRequest
    {
        public Guid MatchId { get; set; }
    }

    public sealed class SubmitMoveRequest
    {
        public Guid MatchId { get; set; }
        public string MoveId { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
        public int Value { get; set; }
    }

    public sealed class MoveResultResponse
    {
        public bool Accepted { get; set; }
        public bool IsCorrect { get; set; }
        public string ErrorCode { get; set; }
        public int CorrectCount { get; set; }
        public int ErrorCount { get; set; }
        public bool BoardChanged { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
        public int Value { get; set; }
    }
}
