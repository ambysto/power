using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ambysto.Power.Core.KeepAwake;

public sealed partial class ExecutionStateAssertion : ISleepAssertion
{
    public const uint EsContinuous = 0x80000000u;
    public const uint EsSystemRequired = 0x00000001u;
    public const uint EsDisplayRequired = 0x00000002u;
    public static readonly TimeSpan MaxLeaseTimeout = TimeSpan.FromMinutes(15);

    private readonly object _sync = new();
    private readonly IExecutionStateNative _native;
    private readonly DedicatedThreadExecutionStateNative? _ownedNative;
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset? _leaseExpiryUtc;
    private bool _disposed;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint SetThreadExecutionState(uint esFlags);

    public ExecutionStateAssertion(IExecutionStateNative? native = null, Func<DateTimeOffset>? clock = null)
    {
        if (native is null)
        {
            var dedicated = new DedicatedThreadExecutionStateNative();
            _native = dedicated;
            _ownedNative = dedicated;
        }
        else
        {
            _native = native;
        }

        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsActive { get; private set; }
    public AssertionKind? CurrentKind { get; private set; }
    public string? CurrentName { get; private set; }
    public TimeSpan? CurrentTimeout { get; private set; }

    public void Acquire(AssertionKind kind, string assertionName, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assertionName);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            TimeSpan clampedTimeout = ClampTimeout(timeout);
            if (IsActive && CurrentKind != kind)
            {
                _ = _native.SetExecutionState(EsContinuous);
            }

            uint flags = GetFlagsForKind(kind);
            _ = _native.SetExecutionState(flags);

            IsActive = true;
            CurrentKind = kind;
            CurrentName = assertionName;
            CurrentTimeout = clampedTimeout;
            _leaseExpiryUtc = _clock().Add(clampedTimeout);
        }
    }

    public void Refresh(TimeSpan timeout)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsActive || CurrentKind is not AssertionKind kind)
            {
                return;
            }

            TimeSpan clampedTimeout = ClampTimeout(timeout);
            uint flags = GetFlagsForKind(kind);
            _ = _native.SetExecutionState(flags);
            CurrentTimeout = clampedTimeout;
            _leaseExpiryUtc = _clock().Add(clampedTimeout);
        }
    }

    public void Release()
    {
        lock (_sync)
        {
            if (!IsActive)
            {
                return;
            }

            _ = _native.SetExecutionState(EsContinuous);
            IsActive = false;
            CurrentKind = null;
            CurrentName = null;
            CurrentTimeout = null;
            _leaseExpiryUtc = null;
        }
    }

    public void CheckWatchdog(DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            if (IsActive && _leaseExpiryUtc is DateTimeOffset expiry && nowUtc >= expiry)
            {
                Release();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            Release();
            _disposed = true;
        }

        _ownedNative?.Dispose();
    }

    private static TimeSpan ClampTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > MaxLeaseTimeout)
        {
            return MaxLeaseTimeout;
        }

        return timeout;
    }

    private static uint GetFlagsForKind(AssertionKind kind) =>
        kind == AssertionKind.SystemAndDisplay
            ? (EsContinuous | EsSystemRequired | EsDisplayRequired)
            : (EsContinuous | EsSystemRequired);

    private sealed class DedicatedThreadExecutionStateNative : IExecutionStateNative, IDisposable
    {
        private readonly BlockingCollection<WorkItem> _queue = [];
        private readonly Thread _thread;
        private bool _disposed;

        public DedicatedThreadExecutionStateNative()
        {
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "Ambysto.Power.ExecutionState"
            };
            _thread.Start();
        }

        public uint SetExecutionState(uint flags)
        {
            if (_disposed)
            {
                return 0;
            }

            using var item = new WorkItem(flags);
            _queue.Add(item);
            item.Done.Wait();
            return item.Result;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.CompleteAdding();
            if (_thread.IsAlive)
            {
                _ = _thread.Join(TimeSpan.FromSeconds(2));
            }

            _queue.Dispose();
        }

        private void ThreadMain()
        {
            foreach (WorkItem item in _queue.GetConsumingEnumerable())
            {
                try
                {
                    item.Result = SetThreadExecutionState(item.Flags);
                }
                finally
                {
                    item.Done.Set();
                }
            }
        }

        private sealed class WorkItem(uint flags) : IDisposable
        {
            public uint Flags { get; } = flags;
            public uint Result { get; set; }
            public ManualResetEventSlim Done { get; } = new(false);

            public void Dispose() => Done.Dispose();
        }
    }
}
