using System;
using Microsoft.Win32;

namespace Ambysto.Power.Core.Licensing;

public sealed class LoginItem
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Ambysto Power";

    private readonly string _executablePath;
    private readonly string _keyPath;

    public LoginItem(string executablePath, string keyPath = RunKeyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        _executablePath = executablePath;
        _keyPath = keyPath;
    }

    public bool IsOn
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: false);
            string? val = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
    }

    public void TurnOn()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        string quoted = _executablePath.StartsWith('"')
            ? _executablePath
            : $"\"{_executablePath}\"";
        key.SetValue(ValueName, quoted, RegistryValueKind.String);
    }

    public void TurnOff()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
