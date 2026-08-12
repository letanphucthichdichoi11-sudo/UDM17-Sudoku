namespace Sudoku.Server.Game
{
    internal sealed class GameApplicationServices
    {
        public RoomManager Rooms { get; private set; }
        public MatchManager Matches { get; private set; }
        public MatchCoordinator MatchCoordinator { get; private set; }

        public GameApplicationServices()
            : this(new InMemoryMatchRepository())
        {
        }

        public GameApplicationServices(IMatchRepository matchRepository)
        {
            Rooms = new RoomManager();
            Matches = new MatchManager(matchRepository);
            MatchCoordinator = new MatchCoordinator(
                new SudokuGenerator(),
                Rooms,
                Matches);
        }
    }
}
