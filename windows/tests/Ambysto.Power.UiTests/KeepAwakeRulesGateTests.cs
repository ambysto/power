using System;
using System.IO;
using Ambysto.Power.UiTests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Power.UiTests;

[TestClass]
[DoNotParallelize]
public sealed class KeepAwakeRulesGateTests
{
    private const string NoRulesText = "No rules saved.";
    private const string NotPartOfBuildText = "Automatic keep-awake rules are not part of this build.";

    private static readonly TimeSpan WinTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ElemTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    [TestCategory("Console")]
    [TestCategory("ProDebug")]
    public void SavingAKeepAwakeRuleNeedsTheModuleAsWellAsProEntitlement()
    {
        var hasModule = File.Exists(Path.Combine(Path.GetDirectoryName(AppSession.AppPath())!, "Ambysto.Power.Pro.dll"));
        AssertModuleMatchesTree(hasModule);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"power-ui-rules-{Guid.NewGuid():N}");
        var suiteDir = Path.Combine(tempRoot, "suite");
        Directory.CreateDirectory(suiteDir);

        try
        {
            // --pro: Debug builds only; the entitlement is on without a key. The module is a separate question.
            using var session = AppSession.Start("--pro", "--no-shell-launch", "--suite-folder", suiteDir, "--open", "keep-awake");
            var mainWin = session.MainWindow(WinTimeout);

            var saveButton = AppSession.Find(mainWin, "SaveRuleButton", ElemTimeout);
            var rulesText = AppSession.Find(mainWin, "RulesPlainEnglishText", ElemTimeout);

            // The rule app box holds "Code.exe" by default.
            AppSession.Click(saveButton);

            // Read the text inside the wait: the 15-second poll rewrites it to "No rules saved.". If a poll lands between the click
            // and a read, the text is back to the default and the click is repeated, so the message is set again.
            var text = NoRulesText;
            var changed = AppSession.WaitUntil(
                () =>
                {
                    text = rulesText.Current.Name;
                    if (text == NoRulesText)
                    {
                        AppSession.Click(saveButton);
                        return false;
                    }

                    return true;
                },
                UpdateTimeout);
            Assert.IsTrue(changed, $"RulesPlainEnglishText still reads '{text}' after saving a rule.");

            if (hasModule)
            {
                StringAssert.Contains(text, "Code.exe", "With the module, the saved rule is described in words.");
                Assert.IsFalse(text.Contains("not part of this build", StringComparison.Ordinal), $"Unexpected text with the module: '{text}'");
                Assert.IsFalse(text.Contains("require Ambysto Power Pro", StringComparison.Ordinal), $"Unexpected text with the module: '{text}'");
            }
            else
            {
                Assert.AreEqual(NotPartOfBuildText, text);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    // Where the private module folder exists in the tree, the built app must carry the module too (a lost project reference would
    // otherwise pass on the no-module branch); POWER_PRO_OFF=1 is the public variant built from the private tree on purpose.
    private static void AssertModuleMatchesTree(bool hasModule)
    {
        DirectoryInfo? dir = new(Path.GetDirectoryName(AppSession.AppPath())!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ambysto.Power.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null || string.Equals(Environment.GetEnvironmentVariable("POWER_PRO_OFF"), "1", StringComparison.Ordinal))
        {
            return;
        }

        bool folderExists = File.Exists(Path.Combine(dir.FullName, "pro", "Ambysto.Power.Pro", "Ambysto.Power.Pro.csproj"));
        Assert.AreEqual(folderExists, hasModule, "The module folder and the module DLL next to the app must agree.");
    }
}
