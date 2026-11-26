using System;
using System.IO;
using Ambysto.Power.UiTests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Power.UiTests;

[TestClass]
[DoNotParallelize]
public sealed class AboutStarButtonTests
{
    private static readonly TimeSpan WinTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ElemTimeout = TimeSpan.FromSeconds(15);

    [TestMethod]
    [TestCategory("Console")]
    public void StarOnGitHubButtonIsOnTheAboutPageAndClickingItKeepsTheAppRunning()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"power-ui-star-{Guid.NewGuid():N}");
        var suiteDir = Path.Combine(tempRoot, "suite");
        Directory.CreateDirectory(suiteDir);

        try
        {
            // --no-shell-launch: the app must not hand anything to the shell, so no browser opens during the test.
            using var session = AppSession.Start("--no-shell-launch", "--suite-folder", suiteDir);
            var mainWin = session.MainWindow(WinTimeout);

            AppSession.Click(AppSession.Find(mainWin, "AboutNavButton", ElemTimeout));
            var appName = AppSession.Find(mainWin, "AboutAppNameText", ElemTimeout);
            Assert.IsFalse(appName.Current.IsOffscreen, "Expected the About view to be shown.");

            var starButton = AppSession.Find(mainWin, "StarOnGitHubButton", ElemTimeout);
            Assert.AreEqual("Star us on GitHub", starButton.Current.Name);
            Assert.IsTrue(starButton.Current.IsEnabled, "StarOnGitHubButton must be enabled.");
            Assert.IsTrue(starButton.Current.IsKeyboardFocusable, "StarOnGitHubButton must be reachable by keyboard.");

            var websiteButton = AppSession.Find(mainWin, "OpenWebsiteButton", ElemTimeout);
            Assert.IsFalse(websiteButton.Current.IsOffscreen, "Expected the Website button beside the Star button.");

            AppSession.Click(starButton);

            // Nothing is launched, so the app stays up and stays on the About page.
            Assert.IsFalse(session.Process.WaitForExit(1500), "The app exited after the Star button was invoked.");
            var appNameAfter = AppSession.Find(mainWin, "AboutAppNameText", ElemTimeout);
            Assert.IsFalse(appNameAfter.Current.IsOffscreen, "Expected the About view to still be shown after the click.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
