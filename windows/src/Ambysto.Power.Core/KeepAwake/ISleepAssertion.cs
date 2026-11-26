using System;

namespace Ambysto.Power.Core.KeepAwake;

public interface ISleepAssertion : IDisposable
{
    bool IsActive { get; }
    AssertionKind? CurrentKind { get; }
    string? CurrentName { get; }
    TimeSpan? CurrentTimeout { get; }

    void Acquire(AssertionKind kind, string assertionName, TimeSpan timeout);
    void Refresh(TimeSpan timeout);
    void Release();
}

public interface IExecutionStateNative
{
    uint SetExecutionState(uint flags);
}
