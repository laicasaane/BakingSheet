#!/usr/bin/env bash

set -eu

repository_root="$(cd "$(dirname "$0")/../.." && pwd -P)"
test_root="$(mktemp -d "${TMPDIR:-/tmp}/BakingSheet.Copy.XXXXXX")"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

write_fixture_file() {
    relative_path=$1
    content=$2
    path="$test_root/$relative_path"
    mkdir -p "${path%/*}"
    printf '%s' "$content" > "$path"
}

assert_missing() {
    [ ! -e "$test_root/$1" ] || fail "Expected '$test_root/$1' to be absent."
}

assert_content() {
    path="$test_root/$1"
    [ -f "$path" ] || fail "Expected '$path' to exist."
    actual="$(cat "$path")"
    [ "$actual" = "$2" ] || fail "Expected '$path' to contain '$2', but found '$actual'."
}

cleanup() {
    if [ -n "${test_root:-}" ] && [ -d "$test_root" ]; then
        rm -rf "$test_root"
    fi
}

trap cleanup EXIT

mkdir -p "$test_root/.vscode/scripts"
cp "$repository_root/copy.sh" "$test_root/copy.sh"
cp "$repository_root/.vscode/scripts/Sync-UnityFiles.sh" "$test_root/.vscode/scripts/Sync-UnityFiles.sh"
cp "$repository_root/.vscode/scripts/Rewrite-PackageDocumentLinks.sh" "$test_root/.vscode/scripts/Rewrite-PackageDocumentLinks.sh"

write_fixture_file 'BakingSheet/Src/Current.cs' 'new-core'
write_fixture_file 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Current.cs' 'old-core'
write_fixture_file 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Current.cs.meta' 'core-guid'
write_fixture_file 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Stale.cs' 'stale-core'
write_fixture_file 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Stale.cs.meta' 'stale-core-guid'

for converter in Excel Google Csv Json; do
    write_fixture_file "BakingSheet.Converters.$converter/Current.cs" "new-$converter"
    write_fixture_file "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Current.cs" "old-$converter"
    write_fixture_file "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Current.cs.meta" "$converter-guid"
    write_fixture_file "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Package.asmdef" "unity-only-$converter"
done

write_fixture_file 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Excel/Stale.cs' 'stale-excel'
write_fixture_file 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Excel/Stale.cs.meta' 'stale-excel-guid'
write_fixture_file 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Google/link.xml' 'unity-linker'
version_url='https://github.com/laicasaane/BakingSheet/blob/6.3.1-pre.3+build.7'
write_fixture_file 'README.md' '[docs](docs/guide.md)
![image](.github/images/sample.png)'
write_fixture_file 'CHANGELOG.md' '[image]: ./.github/images/change.png'
write_fixture_file 'Plan.md' 'do-not-copy'
write_fixture_file 'UnityProject/Packages/com.laicasaane.bakingsheet/package.json' '{ "version": "6.3.1-pre.3+build.7" }'
write_fixture_file 'UnityProject/Packages/com.laicasaane.bakingsheet/README.md' 'old-readme'
write_fixture_file 'UnityProject/Packages/com.laicasaane.bakingsheet/PackageOnly.md' 'package-only'

(cd "$test_root" && bash ./copy.sh)

assert_content 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Current.cs' 'new-core'
assert_content 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Current.cs.meta' 'core-guid'
assert_missing 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Stale.cs'
assert_missing 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Stale.cs.meta'

for converter in Excel Google Csv Json; do
    assert_content "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Current.cs" "new-$converter"
    assert_content "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Current.cs.meta" "$converter-guid"
    assert_content "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Package.asmdef" "unity-only-$converter"
done

assert_missing 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Excel/Stale.cs'
assert_missing 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Excel/Stale.cs.meta'
assert_content 'UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Google/link.xml' 'unity-linker'
assert_content 'UnityProject/Packages/com.laicasaane.bakingsheet/README.md' "[docs]($version_url/docs/guide.md)
![image]($version_url/.github/images/sample.png)"
assert_content 'UnityProject/Packages/com.laicasaane.bakingsheet/CHANGELOG.md' "[image]: $version_url/.github/images/change.png"
assert_missing 'UnityProject/Packages/com.laicasaane.bakingsheet/Plan.md'
assert_content 'UnityProject/Packages/com.laicasaane.bakingsheet/PackageOnly.md' 'package-only'

printf '%s\n' 'copy.sh tests passed.'
