#!/usr/bin/env bash

set -eu

package_root=''

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

usage() {
    printf '%s\n' 'Usage: Rewrite-PackageDocumentLinks.sh --package-root PATH'
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --package-root)
            [ "$#" -ge 2 ] || fail 'Missing value for --package-root.'
            package_root=$2
            shift 2
            ;;
        --help)
            usage
            exit 0
            ;;
        *)
            fail "Unknown argument: '$1'."
            ;;
    esac
done

[ -n "$package_root" ] || fail 'The --package-root argument is required.'
[ -d "$package_root" ] || fail "Package directory does not exist: '$package_root'."

package_json_path="$package_root/package.json"
[ -f "$package_json_path" ] || fail "Package metadata does not exist: '$package_json_path'."

package_version=$(awk '
    match($0, /"version"[[:space:]]*:[[:space:]]*"[^"]*"/) {
        value = substr($0, RSTART, RLENGTH)
        sub(/^[^:]*:[[:space:]]*"/, "", value)
        sub(/"$/, "", value)
        print value
        exit
    }
' "$package_json_path")
printf '%s\n' "$package_version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$' ||
    fail "Package version is not semantic: '$package_version'."

repository_version_url="https://github.com/laicasaane/BakingSheet/blob/$package_version"
raw_repository_version_url="https://raw.githubusercontent.com/laicasaane/BakingSheet/$package_version"

for document_name in CHANGELOG.md README.md; do
    document_path="$package_root/$document_name"
    [ -f "$document_path" ] || continue

    temporary_path=$(mktemp "$document_path.XXXXXX")
    if sed -E \
        -e "s#(\]\(|\]:[[:space:]]*)(\./)?docs/#\1$repository_version_url/docs/#g" \
        -e "s#(\]\(|\]:[[:space:]]*)(\./)?\.github/images/#\1$raw_repository_version_url/.github/images/#g" \
        "$document_path" > "$temporary_path"; then
        if cmp -s "$document_path" "$temporary_path"; then
            rm -f "$temporary_path"
        else
            mv -f "$temporary_path" "$document_path"
        fi
    else
        rm -f "$temporary_path"
        exit 1
    fi
done
