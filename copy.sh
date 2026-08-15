#!/usr/bin/env bash

set -eu

script_root="$(cd "$(dirname "$0")" && pwd -P)"
sync_script="$script_root/.vscode/scripts/Sync-UnityFiles.sh"
package_root="$script_root/UnityProject/Packages/com.laicasaane.bakingsheet"

bash "$sync_script" \
    --source "$script_root/BakingSheet/Src" \
    --destination "$package_root/Runtime/Core" \
    --recurse

bash "$sync_script" \
    --source "$script_root/BakingSheet.Converters.Excel" \
    --destination "$package_root/Runtime/Converters/Excel" \
    --include '*.cs'

bash "$sync_script" \
    --source "$script_root/BakingSheet.Converters.Google" \
    --destination "$package_root/Runtime/Converters/Google" \
    --include '*.cs'

bash "$sync_script" \
    --source "$script_root/BakingSheet.Converters.Csv" \
    --destination "$package_root/Runtime/Converters/Csv" \
    --include '*.cs'

bash "$sync_script" \
    --source "$script_root/BakingSheet.Converters.Json" \
    --destination "$package_root/Runtime/Converters/Json" \
    --include '*.cs'

bash "$sync_script" \
    --source "$script_root" \
    --destination "$package_root" \
    --include 'CHANGELOG.md|LICENSE.md|LICENSE.Original.md|README.md|Third Party Notices.md'
