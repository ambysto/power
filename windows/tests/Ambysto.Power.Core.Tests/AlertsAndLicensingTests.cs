using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ambysto.Power.Core.Alerts;
using Ambysto.Power.Core.Apps;
using Ambysto.Power.Core.Battery;
using Ambysto.Power.Core.KeepAwake;
using Ambysto.Power.Core.Licensing;
using Ambysto.Power.Core.Suite;
using Ambysto.Power.Core.Thermal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace Ambysto.Power.Core.Tests;

[TestClass]
public sealed class AlertsAndLicensingTests
{
    private sealed class FakeLicenseCheck : ILicenseCheck
    {
        public LicenseCheckResult Verify(string keyText)
        {
            if (string.Equals(keyText.Trim(), "VALID-POWER-KEY", StringComparison.Ordinal))
            {
                return new LicenseCheckResult(
                    new LicenseFacts("Ada Lovelace", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "power" }),
                    null);
            }

            if (string.Equals(keyText.Trim(), "VALID-OPTIMER-ONLY-KEY", StringComparison.Ordinal))
            {
                return new LicenseCheckResult(
                    new LicenseFacts("Ada Lovelace", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "optimer", "space" }),
                    "This key is for optimer, space, not Ambysto Power.");
            }

            return new LicenseCheckResult(null, "Invalid license key signature.");
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }

    [TestMethod]
    public void EntitlementServiceAcceptsPowerKeyRefusesOtherProductKeysAndEnforcesPurchaseReminderRules()
    {
        string tempDir = CreateTempDirectory();
        try
        {
            var check = new FakeLicenseCheck();

            // 1. PurchaseUrl == null -> ShouldShowPurchaseReminder is ALWAYS false.
            var serviceNoUrl = new EntitlementService(tempDir, check, devProOverride: false, purchaseUrl: null);
            Assert.IsFalse(serviceNoUrl.Current.IsPro);
            Assert.IsFalse(serviceNoUrl.ShouldShowPurchaseReminder(isKeepAwakeActive: false));

            // 2. Activate a key for another product ("optimer" / "space") -> refused, stays Free, but HasStoredKeyText == true!
            EntitlementSnapshot refused = serviceNoUrl.ActivateKey("VALID-OPTIMER-ONLY-KEY");
            Assert.IsFalse(refused.IsPro);
            Assert.IsTrue(refused.HasStoredKeyText);
            Assert.IsFalse(string.IsNullOrWhiteSpace(refused.StatusMessage));

            // 3. Activate a valid "power" key -> unlocks Pro and sets RegisteredTo.
            EntitlementSnapshot activated = serviceNoUrl.ActivateKey("VALID-POWER-KEY");
            Assert.IsTrue(activated.IsPro);
            Assert.AreEqual("Ada Lovelace", activated.RegisteredTo);
            Assert.IsTrue(activated.HasStoredKeyText);
            StringAssert.Contains(activated.StatusMessage ?? string.Empty, "Ada Lovelace", StringComparison.Ordinal);

            // 4. Deactivate -> returns to Free and clears stored key text.
            serviceNoUrl.DeactivateKey();
            Assert.IsFalse(serviceNoUrl.Current.IsPro);
            Assert.IsFalse(serviceNoUrl.Current.HasStoredKeyText);

            // 5. With PurchaseUrl != null in Free: never shows during active keep-awake, shows once when idle, false after dismiss.
            var serviceWithUrl = new EntitlementService(
                tempDir,
                check,
                devProOverride: false,
                purchaseUrl: new Uri("https://power.ambysto.com/buy"));
            Assert.IsFalse(serviceWithUrl.ShouldShowPurchaseReminder(isKeepAwakeActive: true));
            Assert.IsTrue(serviceWithUrl.ShouldShowPurchaseReminder(isKeepAwakeActive: false));
            serviceWithUrl.DismissPurchaseReminder();
            Assert.IsFalse(serviceWithUrl.ShouldShowPurchaseReminder(isKeepAwakeActive: false));

            // 6. PowerLicenseConfig rejects malformed key text offline.
            LicenseCheckResult badResult = PowerLicenseConfig.Instance.Verify("not-a-valid-key");
            Assert.IsNull(badResult.License);
            Assert.IsFalse(string.IsNullOrWhiteSpace(badResult.RefusalReason));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [TestMethod]
    [TestCategory("ProDebug")]
    public void EntitlementServiceDevProOverrideUnlocksProInDebugCategory()
    {
        string tempDir = CreateTempDirectory();
        try
        {
            var service = new EntitlementService(tempDir, new FakeLicenseCheck(), devProOverride: true);
            Assert.IsTrue(service.Current.IsPro);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [TestMethod]
    public void LoginItemTurnsOnAndOffAgainstIsolatedTestRegistrySubkey()
    {
        string subKeyPath = @"Software\Ambysto\PowerTests\" + Guid.NewGuid().ToString("N");
        try
        {
            var loginItem = new LoginItem(@"C:\Users\alice\AppData\Local\Programs\Ambysto Power\AmbystoPower.exe", subKeyPath);
            Assert.IsFalse(loginItem.IsOn);

            loginItem.TurnOn();
            Assert.IsTrue(loginItem.IsOn);

            loginItem.TurnOff();
            Assert.IsFalse(loginItem.IsOn);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    public async Task UpdateCheckerHandlesCurrentAvailableOversizedAndInsecureDownloadUrls()
    {
        // 1. Current when remote <= installed.
        using (var currentChecker = new UpdateChecker(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"version":"0.1.1","url":"https://power.ambysto.com/download"}""", Encoding.UTF8, "application/json")
        })))
        {
            UpdateResult current = await currentChecker.CheckAsync(new Version(0, 1, 1), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(UpdateState.Current, current.State);
        }

        // 2. Available when remote > installed and URL is HTTPS.
        using (var availableChecker = new UpdateChecker(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"version":"0.2.0","url":"https://power.ambysto.com/download/AmbystoPower.msi"}""", Encoding.UTF8, "application/json")
        })))
        {
            UpdateResult available = await availableChecker.CheckAsync(new Version(0, 1, 1), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(UpdateState.Available, available.State);
            Assert.AreEqual(new Version(0, 2, 0), available.Version);
            Assert.AreEqual(new Uri("https://power.ambysto.com/download/AmbystoPower.msi"), available.DownloadPage);
        }

        // 3. Refuses non-HTTPS download URL.
        using (var httpUrlChecker = new UpdateChecker(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"version":"0.2.0","url":"http://power.ambysto.com/download/AmbystoPower.msi"}""", Encoding.UTF8, "application/json")
        })))
        {
            UpdateResult insecure = await httpUrlChecker.CheckAsync(new Version(0, 1, 1), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(UpdateState.Failed, insecure.State);
        }

        // 4. Refuses oversized payload (> 16384 bytes).
        string oversizedJson = "{\"version\":\"0.2.0\",\"url\":\"https://power.ambysto.com/download\",\"pad\":\"" + new string('A', UpdateChecker.MaxManifestBytes + 100) + "\"}";
        using (var oversizedChecker = new UpdateChecker(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(oversizedJson, Encoding.UTF8, "application/json")
        })))
        {
            UpdateResult oversized = await oversizedChecker.CheckAsync(new Version(0, 1, 1), CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(UpdateState.Failed, oversized.State);
        }
    }

    [TestMethod]
    public void SuiteGlanceWriterWritesValidPowerGlanceJsonInTempFolder()
    {
        string tempDir = CreateTempDirectory();
        try
        {
            var writer = new SuiteGlanceWriter(tempDir);
            var nowUtc = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
            var battery = new BatteryReading(
                TimestampUtc: nowUtc,
                HasBattery: true,
                PowerSource: PowerSourceKind.Battery,
                State: BatteryChargingState.Discharging,
                ChargePercent: 80,
                TimeRemaining: TimeSpan.FromMinutes(120),
                CycleCount: 100,
                RatedCycleLimit: 1000,
                DesignCapacityMwh: 50_000,
                FullChargeCapacityMwh: 45_000,
                SystemMaxCapacityPercent: null,
                Condition: null,
                TemperatureCelsius: 30.0,
                VoltageMillivolts: 12_000,
                CurrentMilliamps: -1_000,
                DischargeRateMilliwatts: -12_000);

            writer.WriteGlance(battery, ThermalState.Nominal, keepAwakeActive: true, nowUtc);
            Assert.IsTrue(File.Exists(writer.GlanceFilePath));
            string json = File.ReadAllText(writer.GlanceFilePath);
            StringAssert.Contains(json, "power", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "AmbystoPowerLicenseTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
