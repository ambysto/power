using System;
using System.Globalization;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Ambysto.Power.App;

public static class Program
{
    internal static bool DevProFlag { get; private set; }
    internal static bool UpdateQaFlag { get; private set; }
    internal static string? OpenTabOption { get; private set; }
    internal static string? SuiteFolderOption { get; private set; }
    internal static int? QuitAfterSecondsOption { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args is not null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
#if DEBUG
                if (string.Equals(arg, "--pro", StringComparison.Ordinal))
                {
                    DevProFlag = true;
                    continue;
                }

                if (string.Equals(arg, "--update-qa", StringComparison.Ordinal))
                {
                    UpdateQaFlag = true;
                    continue;
                }
#endif
                if (string.Equals(arg, "--no-shell-launch", StringComparison.OrdinalIgnoreCase))
                {
                    SystemLauncher.SuppressExternalLaunch = true;
                }
                else if (string.Equals(arg, "--open", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    OpenTabOption = args[++i];
                }
                else if (string.Equals(arg, "--suite-folder", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    SuiteFolderOption = args[++i];
                }
                else if (string.Equals(arg, "--quit-after", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) && seconds > 0)
                    {
                        QuitAfterSecondsOption = seconds;
                    }
                }
            }
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
