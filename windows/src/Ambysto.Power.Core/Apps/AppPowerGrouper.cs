using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Ambysto.Kit.Processes;

namespace Ambysto.Power.Core.Apps;

public static class AppPowerGrouper
{
    public static string ResolveGroupKeyAndName(
        ProcessRecord process,
        IReadOnlyDictionary<int, ProcessRecord> byPid,
        Func<string, string?>? fileDescriptionLookup,
        out string displayName)
    {
        ArgumentNullException.ThrowIfNull(process);

        ProcessRecord root = RollUpToApplicationRoot(process, byPid);

        if (!string.IsNullOrWhiteSpace(root.ExecutablePath))
        {
            string execPath = NormalizePath(root.ExecutablePath);
            string? description = null;
            if (fileDescriptionLookup is not null)
            {
                description = fileDescriptionLookup(root.ExecutablePath);
            }
            else if (File.Exists(execPath))
            {
                try
                {
                    description = FileVersionInfo.GetVersionInfo(execPath).FileDescription;
                }
                catch
                {
                    description = null;
                }
            }

            displayName = !string.IsNullOrWhiteSpace(description)
                ? description.Trim()
                : StripExeSuffix(GetFileNameOnly(execPath));
            return execPath.ToLowerInvariant();
        }

        if (root.Arguments is { Count: > 0 } && !string.IsNullOrWhiteSpace(root.Arguments[0]))
        {
            string arg0 = NormalizePath(root.Arguments[0].Trim().Trim('"'));
            if (!string.IsNullOrEmpty(arg0))
            {
                displayName = StripExeSuffix(GetFileNameOnly(arg0));
                return arg0.ToLowerInvariant();
            }
        }

        if (!string.IsNullOrWhiteSpace(root.CommandLine))
        {
            string firstToken = ExtractFirstCommandToken(root.CommandLine);
            if (!string.IsNullOrEmpty(firstToken))
            {
                string normToken = NormalizePath(firstToken);
                displayName = StripExeSuffix(GetFileNameOnly(normToken));
                return normToken.ToLowerInvariant();
            }
        }

        string fallbackName = string.IsNullOrWhiteSpace(root.Name) ? "Unknown" : root.Name.Trim();
        string normName = NormalizePath(fallbackName);
        displayName = StripExeSuffix(GetFileNameOnly(normName));
        return normName.ToLowerInvariant();
    }

    private static ProcessRecord RollUpToApplicationRoot(
        ProcessRecord process,
        IReadOnlyDictionary<int, ProcessRecord>? byPid)
    {
        if (byPid is null || byPid.Count == 0)
        {
            return process;
        }

        ProcessRecord current = process;
        var visited = new HashSet<int> { current.Identity.ProcessId };

        while (current.ParentProcessId is int parentPid
            && visited.Add(parentPid)
            && byPid.TryGetValue(parentPid, out ProcessRecord? parent)
            && parent.IsReadable)
        {
            if (IsInSameOrSubApplicationFolder(current.ExecutablePath, parent.ExecutablePath))
            {
                current = parent;
            }
            else
            {
                break;
            }
        }

        return current;
    }

    private static bool IsInSameOrSubApplicationFolder(string? childExecPath, string? parentExecPath)
    {
        if (string.IsNullOrWhiteSpace(childExecPath) || string.IsNullOrWhiteSpace(parentExecPath))
        {
            return false;
        }

        string normChild = NormalizePath(childExecPath);
        string normParent = NormalizePath(parentExecPath);
        string? parentDir = GetDirectoryPrefix(normParent);

        if (string.IsNullOrEmpty(parentDir))
        {
            return false;
        }

        return normChild.StartsWith(parentDir, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractFirstCommandToken(string commandLine)
    {
        ReadOnlySpan<char> trimmed = commandLine.AsSpan().Trim();
        if (trimmed.IsEmpty)
        {
            return string.Empty;
        }

        if (trimmed[0] == '"')
        {
            ReadOnlySpan<char> rest = trimmed[1..];
            int endQuote = rest.IndexOf('"');
            return endQuote >= 0 ? rest[..endQuote].ToString() : rest.ToString();
        }

        int space = trimmed.IndexOf(' ');
        return space >= 0 ? trimmed[..space].ToString() : trimmed.ToString();
    }

    private static string NormalizePath(string path) =>
        path.Replace('/', '\\').Trim();

    private static string GetFileNameOnly(string normalizedPath)
    {
        int lastSlash = normalizedPath.LastIndexOf('\\');
        return lastSlash >= 0 ? normalizedPath[(lastSlash + 1)..] : normalizedPath;
    }

    private static string? GetDirectoryPrefix(string normalizedPath)
    {
        int lastSlash = normalizedPath.LastIndexOf('\\');
        return lastSlash > 0 ? normalizedPath[..(lastSlash + 1)] : null;
    }

    private static string StripExeSuffix(string fileName) =>
        fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;
}
