using System;
using System.Runtime.InteropServices;

namespace Ambysto.Power.Core.Thermal;

public sealed partial class WindowsThermalReader : IThermalReader, IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;
    private const string ThermalZoneCounterPath = @"\Thermal Zone Information(*)\Temperature";

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValue
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItemW
    {
        public IntPtr SzName;
        public PdhFmtCounterValue FmtValue;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW")]
    private static partial uint PdhOpenQueryW(IntPtr szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounterW(
        IntPtr hQuery,
        string szFullCounterPath,
        IntPtr dwUserData,
        out IntPtr phCounter);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCollectQueryData(IntPtr hQuery);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static partial uint PdhGetFormattedCounterArrayW(
        IntPtr hCounter,
        uint dwFormat,
        ref uint lpdwBufferSize,
        ref uint lpdwItemCount,
        IntPtr itemBuffer);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCloseQuery(IntPtr hQuery);

    private IntPtr _query;
    private IntPtr _counter;
    private bool _initialized;
    private bool _disposed;

    public ThermalReading ReadThermal()
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        double? maxKelvin = TryReadMaxThermalZoneKelvin();
        ThermalState state = SensorAggregator.EvaluateStateFromKelvin(maxKelvin);

        return new ThermalReading(
            TimestampUtc: nowUtc,
            State: state,
            CpuSpeedLimitPercent: null,
            ThermalWarningLevel: null,
            HasVerifiedSensorTable: false,
            RawSensors: []);
    }

    private double? TryReadMaxThermalZoneKelvin()
    {
        if (_disposed)
        {
            return null;
        }

        if (!_initialized)
        {
            _initialized = true;
            if (PdhOpenQueryW(IntPtr.Zero, IntPtr.Zero, out _query) != 0 || _query == IntPtr.Zero)
            {
                _query = IntPtr.Zero;
                return null;
            }

            if (PdhAddEnglishCounterW(_query, ThermalZoneCounterPath, IntPtr.Zero, out _counter) != 0 || _counter == IntPtr.Zero)
            {
                _ = PdhCloseQuery(_query);
                _query = IntPtr.Zero;
                _counter = IntPtr.Zero;
                return null;
            }
        }

        if (_query == IntPtr.Zero || _counter == IntPtr.Zero)
        {
            return null;
        }

        if (PdhCollectQueryData(_query) != 0)
        {
            return null;
        }

        uint bufferSize = 0;
        uint itemCount = 0;
        uint status = PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref bufferSize, ref itemCount, IntPtr.Zero);
        if (status != PdhMoreData || bufferSize == 0 || itemCount == 0 || bufferSize > 1_048_576)
        {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            status = PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref bufferSize, ref itemCount, buffer);
            if (status != 0 || itemCount == 0)
            {
                return null;
            }

            int itemStride = Marshal.SizeOf<PdhFmtCounterValueItemW>();
            double? maxKelvin = null;
            for (uint i = 0; i < itemCount; i++)
            {
                PdhFmtCounterValueItemW item = Marshal.PtrToStructure<PdhFmtCounterValueItemW>(buffer + (int)(i * (uint)itemStride));
                double val = item.FmtValue.DoubleValue;
                if (val > 200.0 && val < 500.0)
                {
                    if (maxKelvin is null || val > maxKelvin.Value)
                    {
                        maxKelvin = val;
                    }
                }
            }

            return maxKelvin;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_query != IntPtr.Zero)
        {
            _ = PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _counter = IntPtr.Zero;
        }

        _disposed = true;
    }
}
