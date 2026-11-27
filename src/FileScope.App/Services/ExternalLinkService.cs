using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace FileScope.App.Services;

public interface IExternalLinkService
{
    bool TryOpen(Uri uri);
}

/// <summary>Opens HTTPS project links in the default browser; replaceable without launching a browser in UI checks.</summary>
public sealed class ExternalLinkService : IExternalLinkService
{
    public bool TryOpen(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps) return false;
        try
        {
            // An existing browser may handle the URL without returning a new process.
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
            or NotSupportedException or SecurityException)
        {
            return false;
        }
    }
}
