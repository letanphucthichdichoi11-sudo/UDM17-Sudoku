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
    }

    public sealed class RoomListResponse
    {
        public List<LobbyRoomDto> Rooms { get; set; }
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
