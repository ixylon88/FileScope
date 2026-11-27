namespace FileScope.TestSupport;

internal static class TestDirectory
{
    // A newly written fixture can briefly be open elsewhere without FileShare.Delete.
    // Retry only Windows sharing/lock violations. The final failure reaches the test runner.
    internal static async Task DeleteAsync(string path)
    {
        const int sharingViolation = unchecked((int)0x80070020);
        const int lockViolation = unchecked((int)0x80070021);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                System.IO.Directory.Delete(path, recursive: true);
                return;
            }
            catch (System.IO.IOException ex) when (OperatingSystem.IsWindows() && attempt < 5
                && ex.HResult is sharingViolation or lockViolation)
            {
                await Task.Delay(50 << attempt).ConfigureAwait(false);
            }
        }
    }
}
