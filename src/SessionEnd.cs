// What a WM_QUERYENDSESSION / WM_ENDSESSION means for USB Ports. Pure, so it can be
// unit-tested; MainForm.WndProc feeds it the real message and system values.
//  - A plain sign-out leaves the drives attached to this PC: nothing to eject.
//  - A shutdown or restart is ejected for, once it is definite (WM_ENDSESSION, wParam TRUE).
//  - A shutdown from the sign-in or lock screen reaches apps flagged as a sign-out
//    (Windows signs the session out on the way), so a "sign-out" also counts as a
//    shutdown when Windows is shutting down or has just logged a shutdown request (1074).
//  - Restart Manager (an installer or Windows Update replacing a file the app uses)
//    closes apps with ENDSESSION_CLOSEAPP while Windows keeps running. Like a sign-out,
//    that is only a shutdown if Windows is shutting down or has just logged 1074.
using System;

static class SessionEnd
{
    // WinUser.h; see learn.microsoft.com/windows/win32/shutdown/wm-queryendsession and wm-endsession
    public const int WM_QUERYENDSESSION = 0x0011, WM_ENDSESSION = 0x0016;
    // lParam flags (wm-endsession: ENDSESSION_CLOSEAPP 0x1, ENDSESSION_LOGOFF 0x80000000);
    // lParam 0 = shutdown or restart
    const long ENDSESSION_CLOSEAPP = 0x1, ENDSESSION_LOGOFF = 0x80000000L;

    public class Result
    {
        public bool Ending;               // the session is ending (asked, or confirmed)
        public bool Shutdown;             // ... because of a shutdown or restart, not a sign-out
        public bool RaiseShutdownEnding;  // eject now: the shutdown is definite
        public string What;               // for app.log
    }

    // flags: the message's lParam. smShuttingDown: GetSystemMetrics(SM_SHUTTINGDOWN) != 0.
    // saw1074: reads the event log, so it is only asked when the flags leave it open.
    public static Result Decide(int msg, bool wParamTrue, long flags, bool smShuttingDown, Func<bool> saw1074)
    {
        var r = new Result();
        bool logoff = (flags & ENDSESSION_LOGOFF) != 0, closeApp = (flags & ENDSESSION_CLOSEAPP) != 0;
        r.Shutdown = !(logoff || closeApp) || smShuttingDown || saw1074();
        r.Ending = msg == WM_QUERYENDSESSION || wParamTrue;
        r.RaiseShutdownEnding = msg == WM_ENDSESSION && wParamTrue && r.Shutdown;
        r.What = r.Shutdown ? "shutdown or restart" : closeApp ? "an app update closing USB Ports (not a shutdown)" : "sign-out";
        return r;
    }
}
