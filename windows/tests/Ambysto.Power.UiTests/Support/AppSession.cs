using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Power.UiTests.Support;

internal sealed class AppSession : IDisposable
{
    public const string MainWindowTitle = "Ambysto Power";

    private AppSession(Process process)
    {
        Process = process;
    }

    public Process Process { get; }

    public static string AppPath()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configDir = Path.GetDirectoryName(baseDir)!;
        var configuration = Path.GetFileName(configDir);
        var binDir = Path.GetDirectoryName(configDir)!;
        var projDir = Path.GetDirectoryName(binDir)!;
        var testsDir = Path.GetDirectoryName(projDir)!;
        var windowsDir = Path.GetDirectoryName(testsDir)!;
        return Path.Combine(windowsDir, "src", "Ambysto.Power.App", "bin", configuration, "net10.0-windows10.0.26100.0", "win-x64", "AmbystoPower.exe");
    }

    public static AppSession Start(params string[] arguments)
    {
        var path = AppPath();
        Assert.IsTrue(File.Exists(path), "Built app not found at " + path);
        var info = new ProcessStartInfo(path) { UseShellExecute = false };
        foreach (var arg in arguments)
        {
            info.ArgumentList.Add(arg);
        }

        var proc = Process.Start(info) ?? throw new InvalidOperationException("Failed to start AmbystoPower.exe");
        return new AppSession(proc);
    }

    public AutomationElement FindWindowByTitle(string title, TimeSpan timeout)
    {
        var window = WaitFor(() => AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, Process.Id),
                new PropertyCondition(AutomationElement.NameProperty, title))),
            timeout);
        Assert.IsNotNull(window, $"No window titled '{title}' within {timeout.TotalSeconds:F0}s (HasExited={Process.HasExited})");
        return window;
    }

    public AutomationElement MainWindow(TimeSpan timeout) => FindWindowByTitle(MainWindowTitle, timeout);

    public static AutomationElement Find(AutomationElement scope, string automationId, TimeSpan timeout)
    {
        var element = WaitFor(() => scope.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)),
            timeout);
        Assert.IsNotNull(element, $"No element with AutomationId '{automationId}' within {timeout.TotalSeconds:F0}s");
        return element;
    }

    public static AutomationElement? TryFind(AutomationElement scope, string automationId) =>
        scope.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

    public static AutomationElement[] FindAll(AutomationElement scope, string automationId) =>
        scope.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId))
            .Cast<AutomationElement>()
            .ToArray();

    public static void Click(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
            return;
        }

        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
        {
            ((SelectionItemPattern)select).Select();
            return;
        }

        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
        {
            ((TogglePattern)toggle).Toggle();
            return;
        }

        Assert.Fail($"Element '{element.Current.AutomationId}' does not support Invoke, SelectionItem, or Toggle pattern.");
    }

    public static ToggleState GetToggleState(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
        {
            return ((TogglePattern)toggle).Current.ToggleState;
        }

        Assert.Fail($"Element '{element.Current.AutomationId}' does not support TogglePattern.");
        return ToggleState.Indeterminate;
    }

    public static bool IsSelected(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
        {
            return ((SelectionItemPattern)select).Current.IsSelected;
        }

        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
        {
            return ((TogglePattern)toggle).Current.ToggleState == ToggleState.On;
        }

        Assert.Fail($"Element '{element.Current.AutomationId}' does not support SelectionItemPattern or TogglePattern.");
        return false;
    }

    public static AutomationElement? WaitFor(Func<AutomationElement?> find, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (find() is { } found)
                {
                    return found;
                }
            }
            catch (ElementNotAvailableException)
            {
            }

            if (sw.Elapsed >= timeout)
            {
                return null;
            }

            Thread.Sleep(100);
        }
    }

    public static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (condition())
                {
                    return true;
                }
            }
            catch (ElementNotAvailableException)
            {
            }

            if (sw.Elapsed >= timeout)
            {
                return false;
            }

            Thread.Sleep(100);
        }
    }

    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.CloseMainWindow();
                if (!Process.WaitForExit(5000))
                {
                    Process.Kill(entireProcessTree: true);
                    Process.WaitForExit(3000);
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            Process.Dispose();
        }
    }
}
