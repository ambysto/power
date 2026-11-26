using System;
using System.Globalization;
using Ambysto.Power.Core.Battery;
using Ambysto.Power.Core.KeepAwake;

namespace Ambysto.Power.App.Tray;

public sealed class TrayIconController : IDisposable
{
    private bool _disposed;

    public string TooltipText { get; private set; } = "Ambysto Power";
    public string StatusBadgeText { get; private set; } = "Mains";
    public bool IsMarked { get; private set; }

    public void Update(BatterySnapshot? battery, KeepAwakeSnapshot? keepAwake, bool permissionRefusedMark)
    {
        if (_disposed)
        {
            return;
        }

        IsMarked = (keepAwake?.IsTrayIconMarked ?? false) || permissionRefusedMark;

        string powerPart = "Mains";
        if (battery is { Current.HasBattery: true })
        {
            powerPart = battery.Current.ChargePercent is int pct
                ? string.Create(CultureInfo.InvariantCulture, $"{pct} %")
                : "Battery";
        }

        string awakePart = keepAwake?.ActiveSession is not null ? " · Awake" : string.Empty;
        string markPart = IsMarked ? " (!)" : string.Empty;

        StatusBadgeText = $"{powerPart}{awakePart}{markPart}";
        TooltipText = $"Ambysto Power — {StatusBadgeText}";
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
