using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Sudoku.Shared.Models;

namespace Sudoku.Server.Game
{
    internal class RoomManager
    {
        private readonly ConcurrentDictionary<Guid, Room> _rooms;

        public RoomManager()
        {
            _rooms = new ConcurrentDictionary<Guid, Room>();
        }

        public Room CreateRoom(
            string roomName,
            Player owner)
        {
            Guid roomId = Guid.NewGuid();

            Room room = new Room(
                roomId,
                roomName
            );

            room.Players.Add(owner);

            _rooms.TryAdd(roomId, room);

            Console.WriteLine(
                "[ROOM] Created: "
                + room.RoomId
            );

            return room;
        }

        public bool JoinRoom(
            Guid roomId,
            Player player)
        {
            if (!_rooms.TryGetValue(
                roomId,
                out Room room))
            {
                return false;
            }

            if (room.Players.Count >= room.MaxPlayers)
            {
                return false;
            }

            if (room.Players.Exists(
                p => p.PlayerId == player.PlayerId))
            {
                return false;
            }

            room.Players.Add(player);

            Console.WriteLine(
                "[ROOM] Player "
                + player.PlayerName
                + " joined room "
                + roomId
            );

            return true;
        }

        public bool LeaveRoom(
            Guid roomId,
            string playerId)
        {
            if (!_rooms.TryGetValue(
                roomId,
                out Room room))
            {
                return false;
            }

            Player player = room.Players.Find(
                p => p.PlayerId == playerId
            );

            if (player == null)
            {
                return false;
            }

            room.Players.Remove(player);

            Console.WriteLine(
                "[ROOM] Player "
                + player.PlayerName
                + " left room "
                + roomId
            );

            if (room.Players.Count == 0)
            {
                _rooms.TryRemove(
                    roomId,
                    out _
                );

                Console.WriteLine(
                    "[ROOM] Removed empty room: "
                    + roomId
                );
            }

            return true;
        }

        public Room GetRoom(Guid roomId)
        {
            _rooms.TryGetValue(
                roomId,
                out Room room
            );

            return room;
        }

        public List<Room> GetAllRooms()
        {
            return new List<Room>(
                _rooms.Values
            );
        }

        public bool RemoveRoom(Guid roomId)
        {
            return _rooms.TryRemove(
                roomId,
                out _
            );
        }
    }
}