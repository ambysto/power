#!/usr/bin/env bash
# Proves Power's Windows package allowlist check:
# 1. A pass tree passes (bin/ and obj/ skipped).
# 2. A KIT-LOCAL tree passes marked NOT MERGEABLE.
# 3. Every fail case fails.
# 4. windows/ itself passes.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
S="$ROOT/scripts/windows-packages.sh"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

PASS_DIR="$TMP/pass"
mkdir -p "$PASS_DIR/src/Core" "$PASS_DIR/tests/Tests" "$PASS_DIR/installer" "$PASS_DIR/src/Core/obj"
cat > "$PASS_DIR/src/Core/Core.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Ambysto.Kit.License" Version="0.3.0" />
    <PackageReference Include="Ambysto.Kit.Process" Version="0.3.0" />
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.5.1" />
  </ItemGroup>
</Project>
EOF
cat > "$PASS_DIR/tests/Tests/Tests.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="MSTest" Version="4.4.1" />
  </ItemGroup>
</Project>
EOF
cat > "$PASS_DIR/installer/Installer.wixproj" <<'EOF'
<Project Sdk="WixToolset.Sdk/5.0.2">
  <ItemGroup>
    <PackageReference Include="WixToolset.UI.wixext" Version="5.0.2" />
  </ItemGroup>
</Project>
EOF
cat > "$PASS_DIR/src/Core/obj/ignored.props" <<'EOF'
<Project>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
EOF

LOCAL_DIR="$TMP/pass-local"
mkdir -p "$LOCAL_DIR/src/Core"
touch "$LOCAL_DIR/KIT-LOCAL"
cat > "$LOCAL_DIR/src/Core/Core.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Ambysto.Kit.Process" Version="0.3.0-local.1" />
  </ItemGroup>
</Project>
EOF

bad=0
"$S" "$PASS_DIR" >/dev/null || { echo "WRONG: pass tree failed"; "$S" "$PASS_DIR"; bad=1; }
out=$("$S" "$LOCAL_DIR") && echo "$out" | grep -q "NOT MERGEABLE" || { echo "WRONG: KIT-LOCAL tree did not pass with NOT MERGEABLE"; echo "$out"; bad=1; }

make_fail() {
  local name="$1" rel="$2" content="$3"
  local dir="$TMP/fail/$name"
  mkdir -p "$(dirname "$dir/$rel")"
  printf '%s\n' "$content" > "$dir/$rel"
}

make_fail "disallowed-pkg" "src/Core/Core.csproj" '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.3" /></ItemGroup></Project>'
make_fail "mstest-in-src" "src/Core/Core.csproj" '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="MSTest" Version="4.4.1" /></ItemGroup></Project>'
make_fail "wix-in-src" "src/Core/Core.csproj" '<Project Sdk="WixToolset.Sdk/5.0.2"></Project>'
make_fail "wildcard-version" "src/Core/Core.csproj" '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="Ambysto.Kit.Process" Version="0.3.*" /></ItemGroup></Project>'
make_fail "prerelease-without-marker" "src/Core/Core.csproj" '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="Ambysto.Kit.Process" Version="0.3.0-local.1" /></ItemGroup></Project>'
make_fail "hintpath" "src/Core/Core.csproj" '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Reference Include="Foo"><HintPath>Foo.dll</HintPath></Reference></ItemGroup></Project>'

n=0
for d in "$TMP"/fail/*/; do
  n=$((n + 1))
  if "$S" "${d%/}" >/dev/null; then
    echo "WRONG: $(basename "$d") passed"
    bad=1
  fi
done

"$S" >/dev/null || { echo "WRONG: windows/ fails"; "$S"; bad=1; }
[ "$bad" = 0 ] && echo "PASS: pass tree passes, $n of $n fail cases fail, windows/ passes" || { echo "FAIL"; exit 1; }
