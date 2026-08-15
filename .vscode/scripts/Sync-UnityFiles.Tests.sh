#!/usr/bin/env bash

set -eu

script_path="$(cd "$(dirname "$0")" && pwd -P)/Sync-UnityFiles.sh"
test_root="$(mktemp -d "${TMPDIR:-/tmp}/BakingSheet.SyncUnityFiles.XXXXXX")"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

assert_missing() {
    [ ! -e "$1" ] || fail "Expected '$1' to be absent."
}

assert_content() {
    [ -f "$1" ] || fail "Expected '$1' to exist."
    actual="$(cat "$1")"
    [ "$actual" = "$2" ] || fail "Expected '$1' to contain '$2', but found '$actual'."
}

cleanup() {
    if [ -n "${test_root:-}" ] && [ -d "$test_root" ]; then
        rm -rf "$test_root"
    fi
}

trap cleanup EXIT

tree_source="$test_root/tree-source"
tree_destination="$test_root/tree-destination"
mkdir -p "$tree_source/CurrentFolder"
mkdir -p "$tree_destination/CurrentFolder"
mkdir -p "$tree_destination/StaleFolder"

printf '%s' 'new-current' > "$tree_source/Current.cs"
printf '%s' 'new-nested' > "$tree_source/CurrentFolder/Nested.cs"
printf '%s' 'old-current' > "$tree_destination/Current.cs"
printf '%s' 'current-guid' > "$tree_destination/Current.cs.meta"
printf '%s' 'folder-guid' > "$tree_destination/CurrentFolder.meta"
printf '%s' 'stale' > "$tree_destination/Stale.cs"
printf '%s' 'stale-guid' > "$tree_destination/Stale.cs.meta"
printf '%s' 'orphan-guid' > "$tree_destination/Orphan.cs.meta"
printf '%s' 'gone' > "$tree_destination/StaleFolder/Gone.cs"
printf '%s' 'gone-guid' > "$tree_destination/StaleFolder/Gone.cs.meta"
printf '%s' 'stale-folder-guid' > "$tree_destination/StaleFolder.meta"

bash "$script_path" \
    --source "$tree_source" \
    --destination "$tree_destination" \
    --recurse

assert_content "$tree_destination/Current.cs" 'new-current'
assert_content "$tree_destination/CurrentFolder/Nested.cs" 'new-nested'
assert_content "$tree_destination/Current.cs.meta" 'current-guid'
assert_content "$tree_destination/CurrentFolder.meta" 'folder-guid'
assert_missing "$tree_destination/Stale.cs"
assert_missing "$tree_destination/Stale.cs.meta"
assert_missing "$tree_destination/Orphan.cs.meta"
assert_missing "$tree_destination/StaleFolder"
assert_missing "$tree_destination/StaleFolder.meta"

file_source="$test_root/file-source"
file_destination="$test_root/file-destination"
mkdir -p "$file_source" "$file_destination"

printf '%s' 'new-current' > "$file_source/Current.cs"
printf '%s' 'new-file' > "$file_source/New.cs"
printf '%s' 'not-owned' > "$file_source/Project.csproj"
printf '%s' 'old-current' > "$file_destination/Current.cs"
printf '%s' 'current-guid' > "$file_destination/Current.cs.meta"
printf '%s' 'stale' > "$file_destination/Stale.cs"
printf '%s' 'stale-guid' > "$file_destination/Stale.cs.meta"
printf '%s' 'unity-only' > "$file_destination/Package.asmdef"
printf '%s' 'asmdef-guid' > "$file_destination/Package.asmdef.meta"
printf '%s' 'unity-linker' > "$file_destination/link.xml"
printf '%s' 'link-guid' > "$file_destination/link.xml.meta"

bash "$script_path" \
    --source "$file_source" \
    --destination "$file_destination" \
    --include '*.cs'

assert_content "$file_destination/Current.cs" 'new-current'
assert_content "$file_destination/New.cs" 'new-file'
assert_content "$file_destination/Current.cs.meta" 'current-guid'
assert_missing "$file_destination/Stale.cs"
assert_missing "$file_destination/Stale.cs.meta"
assert_missing "$file_destination/Project.csproj"
assert_content "$file_destination/Package.asmdef" 'unity-only'
assert_content "$file_destination/Package.asmdef.meta" 'asmdef-guid'
assert_content "$file_destination/link.xml" 'unity-linker'
assert_content "$file_destination/link.xml.meta" 'link-guid'

list_source="$test_root/list-source"
list_destination="$list_source/Package"
mkdir -p "$list_destination"

printf '%s' 'new-approved' > "$list_source/Approved.md"
printf '%s' 'do-not-copy' > "$list_source/Plan.md"
printf '%s' 'old-approved' > "$list_destination/Approved.md"
printf '%s' 'stale-approved' > "$list_destination/Removed.md"
printf '%s' 'stale-approved-guid' > "$list_destination/Removed.md.meta"
printf '%s' 'package-only' > "$list_destination/PackageOnly.md"

bash "$script_path" \
    --source "$list_source" \
    --destination "$list_destination" \
    --include 'Approved.md|Removed.md'

assert_content "$list_destination/Approved.md" 'new-approved'
assert_missing "$list_destination/Removed.md"
assert_missing "$list_destination/Removed.md.meta"
assert_missing "$list_destination/Plan.md"
assert_content "$list_destination/PackageOnly.md" 'package-only'

dry_source="$test_root/dry-source"
dry_destination="$test_root/dry-destination"
mkdir -p "$dry_source" "$dry_destination"
printf '%s' 'new-current' > "$dry_source/Current.cs"
printf '%s' 'old-current' > "$dry_destination/Current.cs"
printf '%s' 'stale' > "$dry_destination/Stale.cs"

bash "$script_path" \
    --source "$dry_source" \
    --destination "$dry_destination" \
    --include '*.cs' \
    --dry-run

assert_content "$dry_destination/Current.cs" 'old-current'
assert_content "$dry_destination/Stale.cs" 'stale'

printf '%s\n' 'Sync-UnityFiles Bash tests passed.'
