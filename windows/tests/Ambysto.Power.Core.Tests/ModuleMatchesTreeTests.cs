using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Power.Core.Tests;

// The Pro module's tests compile in only when POWER_PRO is defined. If the detection in
// windows/Directory.Build.props broke silently in the private tree, the Pro tests would drop out unnoticed.
[TestClass]
public sealed class ModuleMatchesTreeTests
{
    private const int MaxLevels = 12;

    [TestMethod]
    public void TheTestBuildHasTheProModuleExactlyWhereTheModuleFolderExists()
    {
        string root = FindRepoRoot();
        bool folderExists = File.Exists(Path.Combine(root, "windows", "pro", "Ambysto.Power.Pro", "Ambysto.Power.Pro.csproj"));

#if POWER_PRO
        // POWER_PRO is defined, so the module folder must be there and its assembly must load.
        Assert.IsTrue(folderExists, "POWER_PRO is defined but windows/pro/Ambysto.Power.Pro/Ambysto.Power.Pro.csproj does not exist.");
        Assert.IsNotNull(typeof(Ambysto.Power.Pro.ProModule).Assembly, "The Ambysto.Power.Pro assembly did not load.");
#else
        // The public variant may run from the private tree only on purpose (POWER_PRO_OFF=1).
        if (!string.Equals(Environment.GetEnvironmentVariable("POWER_PRO_OFF"), "1", StringComparison.Ordinal))
        {
            Assert.IsFalse(folderExists, "windows/pro exists but the tests were built without POWER_PRO: PowerPro detection is broken, or set POWER_PRO_OFF=1 on purpose");
        }

        string[] referenced = typeof(ModuleMatchesTreeTests).Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();

        Assert.IsFalse(
            referenced.Contains("Ambysto.Power.Pro", StringComparer.Ordinal),
            "POWER_PRO is not defined but the test assembly references Ambysto.Power.Pro.");
#endif
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (int level = 0; level < MaxLevels && dir is not null; level++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "windows", "Ambysto.Power.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail("Repo root (a folder holding windows/Ambysto.Power.slnx) not found within " + MaxLevels + " levels above " + AppContext.BaseDirectory + ".");
        return string.Empty;
    }
}
