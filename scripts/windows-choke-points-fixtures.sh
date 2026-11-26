#!/usr/bin/env bash
# Proves Power's Windows tripwire:
# 1. A pass tree (every module-bound API inside its allowed module, plus comments) passes.
# 2. Every fail case (one forbidden or misplaced use each) fails on line 6.
# 3. windows/src itself passes.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
S="$ROOT/scripts/windows-choke-points.sh"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

PASS_DIR="$TMP/pass"
mkdir -p \
  "$PASS_DIR/Ambysto.Power.Core/KeepAwake" \
  "$PASS_DIR/Ambysto.Power.Core/History" \
  "$PASS_DIR/Ambysto.Power.Core/Licensing" \
  "$PASS_DIR/Ambysto.Power.App"

cat > "$PASS_DIR/Ambysto.Power.Core/KeepAwake/ExecutionStateAssertion.cs" <<'EOF'
// SetThreadExecutionState allowed only here
namespace Ambysto.Power.Core.KeepAwake;
public sealed class ExecutionStateAssertion {
    // powercfg /requests comment only
    internal static uint Call(uint flags) => SetThreadExecutionState(flags);
}
EOF

cat > "$PASS_DIR/Ambysto.Power.Core/History/HistoryStore.cs" <<'EOF'
namespace Ambysto.Power.Core.History;
public sealed class HistoryStore {
    internal static bool Prune(string path) => DeleteFileW(path);
}
EOF

cat > "$PASS_DIR/Ambysto.Power.Core/Licensing/LoginItem.cs" <<'EOF'
namespace Ambysto.Power.Core.Licensing;
public sealed class LoginItem {
    public void Write(Microsoft.Win32.RegistryKey k) {
        k.SetValue("Ambysto Power", "x");
        k.DeleteValue("Ambysto Power");
    }
}
EOF

cat > "$PASS_DIR/Ambysto.Power.App/SystemLauncher.cs" <<'EOF'
namespace Ambysto.Power.App;
internal static class SystemLauncher {
    internal static void Open() {
        _ = Windows.System.Launcher.LaunchUriAsync(null!);
    }
}
EOF

bad=0
"$S" "$PASS_DIR" >/dev/null || { echo "WRONG: pass tree failed"; "$S" "$PASS_DIR"; bad=1; }

FAIL_CASES=(
  'file-delete|Ambysto.Power.Core/History/HistoryStore.cs|File.Delete("a");'
  'dir-delete|Ambysto.Power.Core/History/HistoryStore.cs|Directory.Delete("a", true);'
  'removedir|Ambysto.Power.Core/History/HistoryStore.cs|RemoveDirectoryW("a");'
  'fileinfo|Ambysto.Power.Core/History/HistoryStore.cs|var f = new FileInfo("a");'
  'dirinfo|Ambysto.Power.Core/History/HistoryStore.cs|var d = new DirectoryInfo("a");'
  'process-kill|Ambysto.Power.Core/Apps/PowerEstimator.cs|proc.Kill();'
  'terminate-proc|Ambysto.Power.Core/Apps/PowerEstimator.cs|TerminateProcess(h, 0);'
  'process-start|Ambysto.Power.App/SystemLauncher.cs|Process.Start("cmd.exe");'
  'create-process|Ambysto.Power.App/SystemLauncher.cs|CreateProcessW(null, null);'
  'powercfg|Ambysto.Power.Core/Battery/WindowsBatteryReader.cs|var cmd = "powercfg /batteryreport";'
  'power-write|Ambysto.Power.Core/KeepAwake/ExecutionStateAssertion.cs|PowerWriteACValueIndex(0, 0, 0, 0, 0);'
  'suspend-state|Ambysto.Power.Core/KeepAwake/ExecutionStateAssertion.cs|SetSuspendState(false, false, false);'
  'runas|Ambysto.Power.App/SystemLauncher.cs|var v = "runas";'
  'exec-state-misplaced|Ambysto.Power.Core/KeepAwake/KeepAwakeController.cs|SetThreadExecutionState(0x80000000u);'
  'power-request-misplaced|Ambysto.Power.Core/KeepAwake/KeepAwakeController.cs|PowerCreateRequest(0);'
  'deletefilew-misplaced|Ambysto.Power.Core/Licensing/EntitlementService.cs|DeleteFileW("x");'
  'reg-write-misplaced|Ambysto.Power.App/Program.cs|key.SetValue("a", "b");'
  'launcher-misplaced|Ambysto.Power.App/Program.cs|Windows.System.Launcher.LaunchUriAsync(null!);'
)

n=0
for spec in "${FAIL_CASES[@]}"; do
  n=$((n + 1))
  name="${spec%%|*}"
  rest="${spec#*|}"
  rel="${rest%%|*}"
  code="${rest#*|}"
  case_dir="$TMP/fail/$name"
  mkdir -p "$(dirname "$case_dir/$rel")"
  printf '// line 1\n// line 2\n// line 3\n// line 4\nnamespace Test {\n    %s\n}\n' "$code" > "$case_dir/$rel"
  if out=$("$S" "$case_dir"); then
    echo "WRONG: $name passed"
    bad=1
  elif echo "$out" | grep -E '^/' | grep -vqE ':6:'; then
    echo "WRONG: $name failed on another line"
    echo "$out"
    bad=1
  fi
done

"$S" >/dev/null || { echo "WRONG: windows/src fails"; "$S"; bad=1; }
[ "$bad" = 0 ] && echo "PASS: pass tree passes, $n of $n fail cases fail on line 6, windows/src passes" || { echo "FAIL"; exit 1; }
