using System;
using System.IO;

namespace Ambysto.Power.Core.Licensing;

public sealed record EntitlementSnapshot(
    bool IsPro,
    string? RegisteredTo,
    string? StatusMessage,
    Uri? PurchaseUrl,
    bool ShowPurchaseReminder,
    bool HasStoredKeyText);

public sealed class EntitlementService
{
    public const string ProductId = "power";
    private const string LicenseFileName = "license.key";
    private const string ReminderDismissedFileName = "reminder-dismissed";

    private readonly string _storageDirectory;
    private readonly ILicenseCheck _licenseCheck;
    private readonly bool _devProOverride;
    private readonly Uri? _purchaseUrl;
    private bool _reminderShownThisInstall;

    public EntitlementService(
        string storageDirectory,
        ILicenseCheck? licenseCheck = null,
        bool devProOverride = false,
        Uri? purchaseUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        _storageDirectory = storageDirectory;
        _licenseCheck = licenseCheck ?? PowerLicenseConfig.Instance;
#if DEBUG
        _devProOverride = devProOverride;
#else
        _devProOverride = false;
#endif
        _purchaseUrl = purchaseUrl;
        Directory.CreateDirectory(_storageDirectory);
        _reminderShownThisInstall = File.Exists(Path.Combine(_storageDirectory, ReminderDismissedFileName));
        Current = EvaluateCurrentState();
    }

    public EntitlementSnapshot Current { get; private set; }

    public EntitlementSnapshot ActivateKey(string rawKeyText)
    {
        string trimmed = rawKeyText?.Trim() ?? string.Empty;
        Directory.CreateDirectory(_storageDirectory);
        File.WriteAllText(GetKeyFilePath(), trimmed);
        Current = EvaluateCurrentState();
        return Current;
    }

    public void DeactivateKey()
    {
        string path = GetKeyFilePath();
        if (File.Exists(path))
        {
            File.WriteAllText(path, string.Empty);
        }

        Current = EvaluateCurrentState();
    }

    public bool ShouldShowPurchaseReminder(bool isKeepAwakeActive)
    {
        if (Current.IsPro || _purchaseUrl is null || isKeepAwakeActive || _reminderShownThisInstall)
        {
            return false;
        }

        return true;
    }

    public void DismissPurchaseReminder()
    {
        _reminderShownThisInstall = true;
        Directory.CreateDirectory(_storageDirectory);
        File.WriteAllText(Path.Combine(_storageDirectory, ReminderDismissedFileName), "1");
        Current = EvaluateCurrentState();
    }

    private EntitlementSnapshot EvaluateCurrentState()
    {
        string storedText = ReadStoredKeyText();
        bool hasStoredKey = !string.IsNullOrWhiteSpace(storedText);

        if (_devProOverride)
        {
            return new EntitlementSnapshot(
                IsPro: true,
                RegisteredTo: "Developer Override",
                StatusMessage: "Ambysto Power Pro — Developer Override",
                PurchaseUrl: _purchaseUrl,
                ShowPurchaseReminder: false,
                HasStoredKeyText: hasStoredKey);
        }

        if (!hasStoredKey)
        {
            bool showReminder = _purchaseUrl is not null && !_reminderShownThisInstall;
            return new EntitlementSnapshot(
                IsPro: false,
                RegisteredTo: null,
                StatusMessage: "Ambysto Power Free",
                PurchaseUrl: _purchaseUrl,
                ShowPurchaseReminder: showReminder,
                HasStoredKeyText: false);
        }

        LicenseCheckResult result = _licenseCheck.Verify(storedText);
        if (result.License is not null
            && result.RefusalReason is null
            && result.License.ProductIds.Contains(ProductId))
        {
            return new EntitlementSnapshot(
                IsPro: true,
                RegisteredTo: result.License.Licensee,
                StatusMessage: $"Ambysto Power Pro — Registered to {result.License.Licensee}",
                PurchaseUrl: _purchaseUrl,
                ShowPurchaseReminder: false,
                HasStoredKeyText: true);
        }

        string refusal = !string.IsNullOrWhiteSpace(result.RefusalReason)
            ? result.RefusalReason
            : "This key is not valid for Ambysto Power.";

        return new EntitlementSnapshot(
            IsPro: false,
            RegisteredTo: null,
            StatusMessage: refusal,
            PurchaseUrl: _purchaseUrl,
            ShowPurchaseReminder: false,
            HasStoredKeyText: true);
    }

    private string GetKeyFilePath() => Path.Combine(_storageDirectory, LicenseFileName);

    private string ReadStoredKeyText()
    {
        string path = GetKeyFilePath();
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
