// SessionEnd.Decide: which session ends are a shutdown or restart (eject), and
// when the event log (1074) may be asked.
using System;

static class SessionEndTests
{
    const int Query = SessionEnd.WM_QUERYENDSESSION, End = SessionEnd.WM_ENDSESSION;
    const long Logoff = 0x80000000L, Critical = 0x40000000L;

    static Func<bool> Never { get { return delegate { throw new CheckFailed("the event log (1074) must not be queried here"); }; } }
    static Func<bool> Saw(bool value) { return delegate { return value; }; }

    [Unit]
    public static void ShutdownIsAskedThenConfirmed()
    {
        SessionEnd.Result asked = SessionEnd.Decide(Query, false, 0, false, Never);
        Check.True(asked.Ending, "asked: ending");
        Check.True(asked.Shutdown, "asked: shutdown");
        Check.False(asked.RaiseShutdownEnding, "asked: eject");   // could still be cancelled
        SessionEnd.Result confirmed = SessionEnd.Decide(End, true, 0, false, Never);
        Check.True(confirmed.Ending, "confirmed: ending");
        Check.True(confirmed.Shutdown, "confirmed: shutdown");
        Check.True(confirmed.RaiseShutdownEnding, "confirmed: eject");
    }

    // Windows can't tell a restart from a shutdown (lParam 0 for both); both eject.
    [Unit]
    public static void RestartLooksLikeShutdown()
    {
        Check.True(SessionEnd.Decide(End, true, 0, false, Never).RaiseShutdownEnding, "restart");
    }

    [Unit]
    public static void ForcedShutdownStillEjects()
    {
        Check.True(SessionEnd.Decide(End, true, Critical, false, Never).RaiseShutdownEnding, "ENDSESSION_CRITICAL");
    }

    [Unit]
    public static void SignOutDoesNotEject()
    {
        SessionEnd.Result r = SessionEnd.Decide(End, true, Logoff, false, Saw(false));
        Check.True(r.Ending, "ending");
        Check.False(r.Shutdown, "shutdown");
        Check.False(r.RaiseShutdownEnding, "eject");
    }

    // From the sign-in or lock screen a shutdown arrives flagged as a sign-out (safety rule 6).
    [Unit]
    public static void LockScreenShutdownEjects()
    {
        SessionEnd.Result r = SessionEnd.Decide(End, true, Logoff, false, Saw(true));
        Check.True(r.Shutdown, "shutdown");
        Check.True(r.RaiseShutdownEnding, "eject");
    }

    [Unit]
    public static void ShuttingDownMetricCountsWithoutAskingTheLog()
    {
        SessionEnd.Result r = SessionEnd.Decide(End, true, Logoff, true, Never);
        Check.True(r.RaiseShutdownEnding, "eject");
    }

    [Unit]
    public static void CancelledShutdownIsNotEnding()
    {
        SessionEnd.Result r = SessionEnd.Decide(End, false, 0, false, Never);
        Check.False(r.Ending, "ending");
        Check.False(r.RaiseShutdownEnding, "eject");
    }

    // Restart Manager (an installer or Windows Update replacing a file the app uses) closes
    // apps with ENDSESSION_CLOSEAPP while Windows keeps running: never eject for that.
    [Unit]
    public static void RestartManagerCloseDoesNotEject()
    {
        const long CloseApp = 0x1;
        SessionEnd.Result r = SessionEnd.Decide(End, true, CloseApp, false, Saw(false));
        Check.False(r.Shutdown, "shutdown");
        Check.False(r.RaiseShutdownEnding, "eject");
        Check.True(SessionEnd.Decide(End, true, CloseApp, true, Never).RaiseShutdownEnding, "during a real shutdown");
        Check.True(SessionEnd.Decide(End, true, CloseApp, false, Saw(true)).RaiseShutdownEnding, "after a shutdown request (1074)");
    }

    [Unit]
    public static void QueryNeverEjects()
    {
        Check.False(SessionEnd.Decide(Query, true, 0, false, Never).RaiseShutdownEnding, "query, wParam 1");
        Check.False(SessionEnd.Decide(Query, false, Logoff, false, Saw(true)).RaiseShutdownEnding, "query, lock-screen shutdown");
    }
}
