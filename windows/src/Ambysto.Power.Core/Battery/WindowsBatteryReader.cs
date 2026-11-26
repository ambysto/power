using System;
using System.Runtime.InteropServices;

namespace Ambysto.Power.Core.Battery;

public sealed partial class WindowsBatteryReader : IBatteryReader
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint IoctlBatteryQueryTag = 0x00294040;
    private const uint IoctlBatteryQueryInformation = 0x00294044;
    private const uint IoctlBatteryQueryStatus = 0x0029404C;
    private const int BatteryInformationLevel = 0;
    private const int BatteryTemperatureLevel = 2;
    private const int BatteryDeviceNameLevel = 4;

    private static readonly Guid GuidDevClassBattery = new(0x72631e54, 0x78a4, 0x11d0, 0xbc, 0xf7, 0x00, 0xaa, 0x00, 0xb7, 0xb3, 0x2a);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint CbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryQueryInformation
    {
        public uint BatteryTag;
        public int InformationLevel;
        public int AtRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation
    {
        public uint Capabilities;
        public byte Technology;
        public byte Reserved0;
        public byte Reserved1;
        public byte Reserved2;
        public uint Chemistry;
        public uint DesignedCapacity;
        public uint FullChargedCapacity;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
        public uint CriticalBias;
        public uint CycleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryWaitStatus
    {
        public uint BatteryTag;
        public uint Timeout;
        public uint PowerState;
        public uint LowCapacity;
        public uint HighCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryStatus
    {
        public uint PowerState;
        public uint Capacity;
        public uint Voltage;
        public int Rate;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus lpSystemPowerStatus);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    private static partial IntPtr SetupDiGetClassDevsW(
        in Guid classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        in Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControlTag(
        IntPtr hDevice,
        uint dwIoControlCode,
        in uint lpInBuffer,
        uint nInBufferSize,
        out uint lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControlInfo(
        IntPtr hDevice,
        uint dwIoControlCode,
        in BatteryQueryInformation lpInBuffer,
        uint nInBufferSize,
        out BatteryInformation lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControlTemp(
        IntPtr hDevice,
        uint dwIoControlCode,
        in BatteryQueryInformation lpInBuffer,
        uint nInBufferSize,
        out uint lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControlBuffer(
        IntPtr hDevice,
        uint dwIoControlCode,
        in BatteryQueryInformation lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControlStatus(
        IntPtr hDevice,
        uint dwIoControlCode,
        in BatteryWaitStatus lpInBuffer,
        uint nInBufferSize,
        out BatteryStatus lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    public BatteryReading ReadBattery()
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (!GetSystemPowerStatus(out SystemPowerStatus status))
        {
            return new BatteryReading(
                TimestampUtc: nowUtc,
                HasBattery: false,
                PowerSource: PowerSourceKind.Mains,
                State: BatteryChargingState.Charged,
                ChargePercent: null,
                TimeRemaining: null,
                CycleCount: null,
                RatedCycleLimit: null,
                DesignCapacityMwh: null,
                FullChargeCapacityMwh: null,
                SystemMaxCapacityPercent: null,
                Condition: null,
                TemperatureCelsius: null,
                VoltageMillivolts: null,
                CurrentMilliamps: null,
                DischargeRateMilliwatts: null,
                ModelIdentifier: Environment.MachineName);
        }

        bool hasBattery = status.BatteryFlag != 255 && (status.BatteryFlag & 0x80) == 0;
        PowerSourceKind powerSource = status.ACLineStatus == 0 && hasBattery
            ? PowerSourceKind.Battery
            : PowerSourceKind.Mains;

        if (!hasBattery)
        {
            return new BatteryReading(
                TimestampUtc: nowUtc,
                HasBattery: false,
                PowerSource: PowerSourceKind.Mains,
                State: BatteryChargingState.Charged,
                ChargePercent: null,
                TimeRemaining: null,
                CycleCount: null,
                RatedCycleLimit: null,
                DesignCapacityMwh: null,
                FullChargeCapacityMwh: null,
                SystemMaxCapacityPercent: null,
                Condition: null,
                TemperatureCelsius: null,
                VoltageMillivolts: null,
                CurrentMilliamps: null,
                DischargeRateMilliwatts: null,
                ModelIdentifier: Environment.MachineName);
        }

        int? chargePercent = status.BatteryLifePercent <= 100
            ? status.BatteryLifePercent
            : null;

        BatteryChargingState chargingState;
        if ((status.BatteryFlag & 0x08) != 0)
        {
            chargingState = BatteryChargingState.Charging;
        }
        else if (powerSource == PowerSourceKind.Battery)
        {
            chargingState = BatteryChargingState.Discharging;
        }
        else
        {
            chargingState = BatteryChargingState.Charged;
        }

        TimeSpan? remaining = status.BatteryLifeTime > 0
            ? TimeSpan.FromSeconds(status.BatteryLifeTime)
            : null;

        IoctlBatteryDetails details = TryReadBatteryIoctlDetails();

        return new BatteryReading(
            TimestampUtc: nowUtc,
            HasBattery: true,
            PowerSource: powerSource,
            State: chargingState,
            ChargePercent: chargePercent,
            TimeRemaining: remaining,
            CycleCount: details.CycleCount,
            RatedCycleLimit: null,
            DesignCapacityMwh: details.DesignCapacityMwh,
            FullChargeCapacityMwh: details.FullChargeCapacityMwh,
            SystemMaxCapacityPercent: null,
            Condition: null,
            TemperatureCelsius: details.TemperatureCelsius,
            VoltageMillivolts: details.VoltageMillivolts,
            CurrentMilliamps: null,
            DischargeRateMilliwatts: details.DischargeRateMilliwatts,
            ModelIdentifier: details.DeviceName ?? Environment.MachineName);
    }

    private static IoctlBatteryDetails TryReadBatteryIoctlDetails()
    {
        Guid batteryGuid = GuidDevClassBattery;
        IntPtr deviceInfoSet = SetupDiGetClassDevsW(
            in batteryGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            return default;
        }

        try
        {
            var interfaceData = new SpDeviceInterfaceData
            {
                CbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>()
            };

            if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, in batteryGuid, 0, ref interfaceData))
            {
                return default;
            }

            _ = SetupDiGetDeviceInterfaceDetailW(
                deviceInfoSet,
                ref interfaceData,
                IntPtr.Zero,
                0,
                out uint requiredSize,
                IntPtr.Zero);

            if (requiredSize < 8 || requiredSize > 4096)
            {
                return default;
            }

            IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
            string? devicePath;
            try
            {
                Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetailW(
                    deviceInfoSet,
                    ref interfaceData,
                    detailBuffer,
                    requiredSize,
                    out _,
                    IntPtr.Zero))
                {
                    return default;
                }

                devicePath = Marshal.PtrToStringUni(detailBuffer + 4);
            }
            finally
            {
                Marshal.FreeHGlobal(detailBuffer);
            }

            if (string.IsNullOrWhiteSpace(devicePath))
            {
                return default;
            }

            IntPtr handle = CreateFileW(
                devicePath,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                handle = CreateFileW(
                    devicePath,
                    GenericRead,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    0,
                    IntPtr.Zero);
            }

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return default;
            }

            try
            {
                uint waitTimeout = 0;
                if (!DeviceIoControlTag(
                    handle,
                    IoctlBatteryQueryTag,
                    in waitTimeout,
                    sizeof(uint),
                    out uint batteryTag,
                    sizeof(uint),
                    out _,
                    IntPtr.Zero) || batteryTag == 0)
                {
                    return default;
                }

                uint queryInfoSize = (uint)Marshal.SizeOf<BatteryQueryInformation>();
                uint battInfoSize = (uint)Marshal.SizeOf<BatteryInformation>();

                int? designCap = null;
                int? fullCap = null;
                int? cycles = null;

                var queryInfo = new BatteryQueryInformation
                {
                    BatteryTag = batteryTag,
                    InformationLevel = BatteryInformationLevel,
                    AtRate = 0
                };
                if (DeviceIoControlInfo(
                    handle,
                    IoctlBatteryQueryInformation,
                    in queryInfo,
                    queryInfoSize,
                    out BatteryInformation battInfo,
                    battInfoSize,
                    out _,
                    IntPtr.Zero))
                {
                    if (battInfo.DesignedCapacity > 0 && battInfo.DesignedCapacity < int.MaxValue)
                    {
                        designCap = (int)battInfo.DesignedCapacity;
                    }

                    if (battInfo.FullChargedCapacity > 0 && battInfo.FullChargedCapacity < int.MaxValue)
                    {
                        fullCap = (int)battInfo.FullChargedCapacity;
                    }

                    if (battInfo.CycleCount > 0 && battInfo.CycleCount < int.MaxValue)
                    {
                        cycles = (int)battInfo.CycleCount;
                    }
                }

                double? tempCelsius = null;
                var tempQuery = new BatteryQueryInformation
                {
                    BatteryTag = batteryTag,
                    InformationLevel = BatteryTemperatureLevel,
                    AtRate = 0
                };
                if (DeviceIoControlTemp(
                    handle,
                    IoctlBatteryQueryInformation,
                    in tempQuery,
                    queryInfoSize,
                    out uint decikelvins,
                    sizeof(uint),
                    out _,
                    IntPtr.Zero) && decikelvins > 2000 && decikelvins < 4500)
                {
                    tempCelsius = Math.Round((decikelvins / 10.0) - 273.15, 1);
                }

                string? deviceName = null;
                var nameQuery = new BatteryQueryInformation
                {
                    BatteryTag = batteryTag,
                    InformationLevel = BatteryDeviceNameLevel,
                    AtRate = 0
                };
                IntPtr nameBuffer = Marshal.AllocHGlobal(256);
                try
                {
                    if (DeviceIoControlBuffer(
                        handle,
                        IoctlBatteryQueryInformation,
                        in nameQuery,
                        queryInfoSize,
                        nameBuffer,
                        256,
                        out uint nameBytes,
                        IntPtr.Zero) && nameBytes >= sizeof(char))
                    {
                        string? rawName = Marshal.PtrToStringUni(nameBuffer, (int)(nameBytes / sizeof(char)))?.TrimEnd('\0').Trim();
                        if (!string.IsNullOrWhiteSpace(rawName))
                        {
                            deviceName = rawName;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(nameBuffer);
                }

                int? voltageMv = null;
                int? rateMw = null;
                var waitStatus = new BatteryWaitStatus
                {
                    BatteryTag = batteryTag,
                    Timeout = 0,
                    PowerState = 0,
                    LowCapacity = 0,
                    HighCapacity = 0
                };
                if (DeviceIoControlStatus(
                    handle,
                    IoctlBatteryQueryStatus,
                    in waitStatus,
                    (uint)Marshal.SizeOf<BatteryWaitStatus>(),
                    out BatteryStatus battStatus,
                    (uint)Marshal.SizeOf<BatteryStatus>(),
                    out _,
                    IntPtr.Zero))
                {
                    if (battStatus.Voltage > 0 && battStatus.Voltage < 100_000)
                    {
                        voltageMv = (int)battStatus.Voltage;
                    }

                    if (battStatus.Rate != unchecked((int)0x80000000))
                    {
                        rateMw = battStatus.Rate;
                    }
                }

                return new IoctlBatteryDetails(
                    designCap,
                    fullCap,
                    cycles,
                    tempCelsius,
                    voltageMv,
                    rateMw,
                    deviceName);
            }
            finally
            {
                _ = CloseHandle(handle);
            }
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private readonly record struct IoctlBatteryDetails(
        int? DesignCapacityMwh,
        int? FullChargeCapacityMwh,
        int? CycleCount,
        double? TemperatureCelsius,
        int? VoltageMillivolts,
        int? DischargeRateMilliwatts,
        string? DeviceName);
}
