#!/usr/bin/env bash

set -eu

source_path=''
destination_path=''
include_patterns='*'
recurse=0
dry_run=0

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

usage() {
    printf '%s\n' \
        'Usage: Sync-UnityFiles.sh --source PATH --destination PATH [--include PATTERNS] [--recurse] [--dry-run]' \
        '' \
        'PATTERNS is a pipe-separated list of shell filename patterns.'
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --source)
            [ "$#" -ge 2 ] || fail 'Missing value for --source.'
            source_path=$2
            shift 2
            ;;
        --destination)
            [ "$#" -ge 2 ] || fail 'Missing value for --destination.'
            destination_path=$2
            shift 2
            ;;
        --include)
            [ "$#" -ge 2 ] || fail 'Missing value for --include.'
            include_patterns=$2
            shift 2
            ;;
        --recurse)
            recurse=1
            shift
            ;;
        --dry-run)
            dry_run=1
            shift
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

[ -n "$source_path" ] || fail 'The --source argument is required.'
[ -n "$destination_path" ] || fail 'The --destination argument is required.'
[ -n "$include_patterns" ] || fail 'At least one include pattern is required.'
[ -d "$source_path" ] || fail "Source directory does not exist: '$source_path'."
[ -d "$destination_path" ] || fail "Destination directory does not exist: '$destination_path'."

source_root=$(cd "$source_path" && pwd -P)
destination_root=$(cd "$destination_path" && pwd -P)

[ "$source_root" != '/' ] || fail 'The filesystem root cannot be synchronized.'
[ "$destination_root" != '/' ] || fail 'The filesystem root cannot be synchronized.'
[ "$source_root" != "$destination_root" ] || fail 'Source and destination must be separate directory trees.'

is_nested_path() {
    local parent=$1
    local child=$2

    case "$child" in
        "$parent"/*) return 0 ;;
        *) return 1 ;;
    esac
}

if is_nested_path "$destination_root" "$source_root"; then
    fail 'Source cannot be inside destination.'
fi

if [ "$recurse" -eq 1 ] && is_nested_path "$source_root" "$destination_root"; then
    fail 'A recursive destination cannot be inside source.'
fi

matches_include() {
    local name=$1
    local remaining_patterns=$include_patterns
    local pattern

    while :; do
        case "$remaining_patterns" in
            *'|'*)
                pattern=${remaining_patterns%%|*}
                remaining_patterns=${remaining_patterns#*|}
                ;;
            *)
                pattern=$remaining_patterns
                remaining_patterns=''
                ;;
        esac

        if [ -n "$pattern" ]; then
            case "$name" in
                $pattern) return 0 ;;
            esac
        fi

        [ -n "$remaining_patterns" ] || break
    done

    return 1
}

list_files() {
    local root=$1
    local path

    if [ "$recurse" -eq 1 ]; then
        find "$root" -type f -print0
        return
    fi

    for path in "$root"/* "$root"/.[!.]* "$root"/..?*; do
        [ -f "$path" ] && printf '%s\0' "$path"
    done
}

remove_destination_item() {
    local path=$1

    if [ ! -e "$path" ] && [ ! -L "$path" ]; then
        return
    fi

    case "$path" in
        "$destination_root"/*) ;;
        *) fail "Refusing to remove path outside destination '$destination_root': '$path'." ;;
    esac

    if [ "$dry_run" -eq 1 ]; then
        printf 'Would remove %s\n' "$path"
    else
        rm -rf "$path"
    fi
}

create_destination_directory() {
    local path=$1

    if [ -d "$path" ]; then
        return
    fi

    if [ "$dry_run" -eq 1 ]; then
        printf 'Would create directory %s\n' "$path"
    else
        mkdir -p "$path"
    fi
}

if [ "$recurse" -eq 1 ] && [ "$include_patterns" = '*' ]; then
    find "$destination_root" -depth -type d -print0 |
        while IFS= read -r -d '' destination_directory; do
            [ "$destination_directory" != "$destination_root" ] || continue
            relative_path=${destination_directory#"$destination_root"/}
            source_directory="$source_root/$relative_path"

            if [ ! -d "$source_directory" ]; then
                remove_destination_item "$destination_directory"
                remove_destination_item "$destination_directory.meta"
            fi
        done
fi

list_files "$destination_root" |
    while IFS= read -r -d '' destination_file; do
        file_name=${destination_file##*/}

        case "$file_name" in
            *.meta) continue ;;
        esac

        matches_include "$file_name" || continue
        relative_path=${destination_file#"$destination_root"/}
        source_file="$source_root/$relative_path"

        if [ ! -f "$source_file" ]; then
            remove_destination_item "$destination_file"
            remove_destination_item "$destination_file.meta"
        fi
    done

list_files "$destination_root" |
    while IFS= read -r -d '' metadata_file; do
        case "$metadata_file" in
            *.meta) ;;
            *) continue ;;
        esac

        asset_path=${metadata_file%.meta}
        asset_name=${asset_path##*/}
        matches_include "$asset_name" || continue
        relative_asset_path=${asset_path#"$destination_root"/}
        source_asset_path="$source_root/$relative_asset_path"

        if [ ! -e "$source_asset_path" ]; then
            remove_destination_item "$metadata_file"
        fi
    done

if [ "$recurse" -eq 1 ]; then
    find "$source_root" -type d -print0 |
        while IFS= read -r -d '' source_directory; do
            [ "$source_directory" != "$source_root" ] || continue
            relative_path=${source_directory#"$source_root"/}
            create_destination_directory "$destination_root/$relative_path"
        done
fi

list_files "$source_root" |
    while IFS= read -r -d '' source_file; do
        file_name=${source_file##*/}

        case "$file_name" in
            *.meta) continue ;;
        esac

        matches_include "$file_name" || continue
        relative_path=${source_file#"$source_root"/}
        destination_file="$destination_root/$relative_path"
        destination_directory=${destination_file%/*}
        create_destination_directory "$destination_directory"

        if [ "$dry_run" -eq 1 ]; then
            printf 'Would copy %s to %s\n' "$source_file" "$destination_file"
        else
            cp -f "$source_file" "$destination_file"
        fi
    done
