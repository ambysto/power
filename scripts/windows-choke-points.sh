#!/usr/bin/env bash
# Power's tripwire for Windows (intent "Repository"; CLAUDE.md Windows rules).
# Fails when windows/src names a forbidden API anywhere, or a module-bound API outside its allowed file/folder.
# Usage: scripts/windows-choke-points.sh [source-dir]   (default: windows/src; paths below are relative to it)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="${1:-$ROOT/windows/src}"
[ -d "$SRC" ] || { echo "FAIL: no source folder $SRC"; exit 1; }
INCLUDES=(--include='*.[cC][sS]' --include='*.[xX][aA][mM][lL]')

FORBIDDEN=(
  # deleting outside HistoryStore.cs (File.Delete and Directory.Delete are banned everywhere; HistoryStore uses DeleteFileW)
  '\b(File|Directory|FileSystem)\.Delete[A-Za-z]*\b'
  '\.Delete\s*\('
  '\bRemoveDirectory[A-Za-z]*\b'
  '\bIFileOperation[A-Za-z]*\b|\bSHFileOperation[A-Za-z]*\b|\bSHEmptyRecycleBin[A-Za-z]*\b|\bFOFX_RECYCLEONDELETE\b'
  '\bFileDispositionInfo[A-Za-z]*\b|\bFILE_DISPOSITION_[A-Z_]+\b|\bDeleteOnClose\b|\bFILE_FLAG_DELETE_ON_CLOSE\b'
  # FileInfo/DirectoryInfo carry Delete, MoveTo, Replace and CopyTo
  '\b(FileInfo|DirectoryInfo|FileSystemInfo)\b'
  '\bFile\.Replace\b|\bReplaceFile[A-Za-z]*\b|\bMoveTo\s*\('
  # WinRT storage and shell scripting objects
  '\b(Delete|Move|Rename|MoveAndReplace|Copy|CopyAndReplace)Async\b|\bStorageDeleteOption\b'
  'Shell\.Application|\bInvokeVerb[A-Za-z]*\b|\bMoveHere\b|\bCopyHere\b|\bShell32\.(Shell|Folder)[A-Za-z]*\b'
  '\bSetFileInformationByHandle\b|\b(Nt|Zw)SetInformationFile\b|\bFileRenameInfo[A-Za-z]*\b'
  # process killing, signals, or process spawning (Power never kills or spawns processes)
  '\.Kill\s*\(|\bTerminateProcess\b|\bEndTask\b|\bWM_CLOSE\b|\bWM_QUIT\b|\bPostMessage[A-Za-z]*\b|\bSendMessage[A-Za-z]*\b'
  '\bCreateProcess[A-Za-z]*\b|\bShellExecute[A-Za-z]*\b|\bWinExec\b|\bProcess\.Start\b|\bProcessStartInfo\b|\.StartInfo\b|\bnew\s+Process\s*[({]'
  '"[Rr][Uu][Nn][Aa][Ss]"|\bRm(Shutdown|Restart)\b'
  # power plan & sleep setting modification (Power only sets thread execution state in ExecutionStateAssertion.cs)
  '\bpowercfg\b|\bPowerSetActiveScheme\b|\bPowerWrite(AC|DC)ValueIndex\b|\bPowerSettingAccessCheck\b|\bCallNtPowerInformation\b|\bSetSuspendState\b|\bSetSystemPowerState\b'
  '\bNavigateUri\b'
  # names hidden behind using static or an alias
  '\busing\s+static\s+(System\.IO\.(File|Directory|FileInfo|DirectoryInfo)|System\.Diagnostics\.Process|Microsoft\.VisualBasic|Windows\.System\.Launcher|Windows\.Storage)\b'
  '\busing\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*(global::)?(System\.IO\.(File|Directory|FileInfo|DirectoryInfo)|System\.Diagnostics\.Process|Microsoft\.VisualBasic|Windows\.System\.Launcher|Windows\.Storage)\b'
)

BOUND=(
  'Ambysto.Power.Core/KeepAwake/ExecutionStateAssertion.cs|\bSetThreadExecutionState\b|\bES_CONTINUOUS\b|\bES_SYSTEM_REQUIRED\b|\bES_DISPLAY_REQUIRED\b|\bES_AWAYMODE_REQUIRED\b|\bPower(Create|Set|Clear)Request\b'
  'Ambysto.Power.Core/History/HistoryStore.cs|\bDeleteFile[A-Za-z]*\b'
  'Ambysto.Power.Core/Licensing/LoginItem.cs|\.(SetValue|CreateSubKey|DeleteValue|DeleteSubKey[A-Za-z]*|SetAccessControl)\s*\(|OpenSubKey\([^)]*(writable:\s*true|,\s*true\s*\))|\bRegistry\.SetValue\b|\bReg(Set|Create|Delete|Save|Restore|Replace|Load|Unload|Rename|Copy)[A-Za-z]*\b|\bRegistryKeyPermissionCheck\.ReadWrite'
  'Ambysto.Power.App/SystemLauncher.cs|\bLauncher\b|\bLaunch(Uri|Folder|File)[A-Za-z]*\b'
)

hits=0
report() { echo "$1"; hits=$((hits + $(echo "$1" | wc -l))); }
uncommented() { grep -vE '^[^:]+:[0-9]+:\s*(//|<!--)'; }
for p in "${FORBIDDEN[@]}"; do
  if out=$(grep -rnE "${INCLUDES[@]}" "$p" "$SRC" | uncommented); then report "$out"; fi
done
for rule in "${BOUND[@]}"; do
  place="${rule%%|*}"; p="${rule#*|}"
  if out=$(grep -rnE "${INCLUDES[@]}" "$p" "$SRC" | uncommented | while IFS= read -r line; do
             rel="${line#"$SRC"/}"; file="${rel%%:*}"
             if [[ "$place" == */ ]]; then [[ "$file" == "$place"* ]] && continue; else [[ "$file" == "$place" ]] && continue; fi
             echo "$line"
           done | grep .); then
    report "$out"
  fi
done
if [ "$hits" -gt 0 ]; then echo "FAIL: $hits forbidden or misplaced use(s) in $SRC"; exit 1; fi
echo "PASS: no forbidden or misplaced use in $SRC"
