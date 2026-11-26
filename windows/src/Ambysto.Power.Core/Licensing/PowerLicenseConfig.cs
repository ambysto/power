using System;
using System.Collections.Generic;
using System.Linq;
using Ambysto.Kit.Licensing;

namespace Ambysto.Power.Core.Licensing;

public sealed record LicenseFacts(string Licensee, IReadOnlySet<string> ProductIds);
public sealed record LicenseCheckResult(LicenseFacts? License, string? RefusalReason);

public interface ILicenseCheck
{
    LicenseCheckResult Verify(string keyText);
}

public sealed class PowerLicenseConfig : ILicenseCheck
{
    public const string ProductId = "power";
    public static PowerLicenseConfig Instance { get; } = new();

    private readonly LicenseVerifier _verifier = new();

    public LicenseCheckResult Verify(string keyText)
    {
        if (string.IsNullOrWhiteSpace(keyText))
        {
            return new LicenseCheckResult(null, "Please enter a license key.");
        }

        LicenseResult result = _verifier.Verify(keyText);
        if (!result.IsValid)
        {
            return new LicenseCheckResult(null, $"Invalid license key ({result.Reason}).");
        }

        HashSet<string> productIds = result.License.Products
            .Select(p => p.Id())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var facts = new LicenseFacts(result.License.Licensee, productIds);
        if (!result.License.Covers(Product.Power))
        {
            string coveredNames = string.Join(", ", result.License.Products.Select(p => p.DisplayName()));
            return new LicenseCheckResult(
                facts,
                $"This key is for {coveredNames}, not Ambysto Power.");
        }

        return new LicenseCheckResult(facts, null);
    }
}
