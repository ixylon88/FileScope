namespace FileScope.TestSupport;

internal static class TestDirectory
{
    internal static Task DeleteAsync(string path) =>
        RetrySharingViolationAsync(() => System.IO.Directory.Delete(path, recursive: true));

    // A temporary reader can deny writes or deletion on Windows. A lasting lock still fails the test.
    internal static async Task RetrySharingViolationAsync(Action changeFixture)
    {
        const int sharingViolation = unchecked((int)0x80070020);
        const int lockViolation = unchecked((int)0x80070021);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                changeFixture();
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
