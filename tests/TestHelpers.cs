// Child-process modes of UsbPorts.Tests.exe, used by integration tests that need
// a second process. TestRunner.Main hands the command line here first.
using System;

static class TestHelpers
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        return false;
    }
}
