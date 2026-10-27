using System;
using System.Collections.Generic;

namespace Selene
{
    // One identity per press of Play. Delayed callbacks must never act on a later attempt.
    public sealed class LaunchAttempt
    {
        private readonly HashSet<int> existing;
        private volatile bool dispatched, cancelled, completed;
        public Guid GameId { get; private set; }
        public bool Dispatched { get { return dispatched; } }
        public bool Cancelled { get { return cancelled; } }
        public bool Completed { get { return completed; } }
        public bool CanCancel { get { return !cancelled && !completed; } }
        public LaunchAttempt(Guid gameId, IEnumerable<int> processesBeforeLaunch) { GameId = gameId; existing = new HashSet<int>(processesBeforeLaunch); }
        public bool TryDispatch(LaunchAttempt current)
        {
            if (!ReferenceEquals(this, current) || cancelled || completed || dispatched) return false;
            dispatched = true; return true;
        }
        public bool Cancel() { if (!CanCancel) return false; cancelled = true; return true; }
        public void Complete() { completed = true; }
        public bool CanCloseProcess(int pid) { return dispatched && cancelled && !existing.Contains(pid); }
    }
}
