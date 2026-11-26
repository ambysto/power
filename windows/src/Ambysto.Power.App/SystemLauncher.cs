using System;
using System.Threading.Tasks;
using Windows.System;

namespace Ambysto.Power.App;

public static class SystemLauncher
{
    // QA: --no-shell-launch makes the launcher say yes without opening anything, so UI tests can click a link button.
    internal static bool SuppressExternalLaunch { get; set; }

    public static async Task<bool> OpenHttpsAsync(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (SuppressExternalLaunch)
        {
            return true;
        }

        return await Launcher.LaunchUriAsync(uri);
    }
}
