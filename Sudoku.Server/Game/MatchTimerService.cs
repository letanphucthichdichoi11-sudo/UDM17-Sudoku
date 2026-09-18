using System;
using System.Threading;

namespace Sudoku.Server.Game
{
    internal sealed class MatchTimerService : IDisposable
    {
        private readonly MatchManager _matchManager;
        private readonly IClock _clock;
        private readonly Timer _timer;
        private int _processing;

        public MatchTimerService(MatchManager matchManager, IClock clock)
        {
            _matchManager = matchManager ?? throw new ArgumentNullException("matchManager");
            _clock = clock ?? throw new ArgumentNullException("clock");
            _timer = new Timer(ProcessTimers, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void ProcessTimers(object state)
        {
            if (Interlocked.Exchange(ref _processing, 1) != 0) return;
            try
            {
                _matchManager.ProcessDueTimers(_clock.UtcNow);
            }
            finally
            {
                Volatile.Write(ref _processing, 0);
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
