namespace Sudoku.Server.Game
{
    internal sealed class GameApplicationServices : System.IDisposable
    {
        public RoomManager Rooms { get; private set; }
        public MatchManager Matches { get; private set; }
        public MatchCoordinator MatchCoordinator { get; private set; }
        private readonly MatchTimerService _timerService;

        public GameApplicationServices()
            : this(new InMemoryMatchRepository())
        {
        }

        public GameApplicationServices(IMatchRepository matchRepository)
        {
            IClock clock = new SystemClock();
            Rooms = new RoomManager();
            Matches = new MatchManager(matchRepository, clock);
            MatchCoordinator = new MatchCoordinator(
                new SudokuGenerator(),
                Rooms,
                Matches,
                clock);
            _timerService = new MatchTimerService(Matches, clock);
        }

        public void Dispose()
        {
            _timerService.Dispose();
        }
    }
}
