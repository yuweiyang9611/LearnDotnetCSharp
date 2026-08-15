#!/bin/sh

set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
expected_root=$(CDPATH= cd -- "$script_directory/.." && pwd -P)
git_repository_root=$(git -C "$expected_root" rev-parse --show-toplevel)
repository_root=$(CDPATH= cd -- "$git_repository_root" && pwd -P)

if [ "$repository_root" != "$expected_root" ]; then
    printf '%s\n' "This script must run from its LearnDotnetCSharp clone: $expected_root" >&2
    exit 1
fi

hook_path="$repository_root/.githooks/pre-commit"
if [ ! -f "$hook_path" ]; then
    printf '%s\n' "The tracked pre-commit hook was not found: $hook_path" >&2
    exit 1
fi

noreply_email=${1-}
if [ -n "$noreply_email" ]; then
    case "$noreply_email" in
        *@users.noreply.github.com|noreply@github.com)
            ;;
        *)
            printf '%s\n' 'The email must be a GitHub noreply address.' >&2
            exit 1
            ;;
    esac
fi

git -C "$repository_root" config --local core.hooksPath .githooks
git -C "$repository_root" config --local user.useConfigOnly true

if [ -n "$noreply_email" ]; then
    git -C "$repository_root" config --local user.email "$noreply_email"
fi

configured_email=$(git -C "$repository_root" config --local --get user.email || true)
case "$configured_email" in
    *@users.noreply.github.com|noreply@github.com)
        ;;
    *)
        printf '%s\n' 'Hooks are enabled, but commits remain blocked until this clone has a GitHub noreply email.' >&2
        printf '%s\n' 'Run this script again with YOUR_ID+YOUR_USERNAME@users.noreply.github.com as its argument.' >&2
        exit 1
        ;;
esac

chmod +x "$hook_path"

configured_hooks_path=$(git -C "$repository_root" config --local --get core.hooksPath)
use_config_only=$(git -C "$repository_root" config --local --get user.useConfigOnly)
if [ "$configured_hooks_path" != '.githooks' ] || [ "$use_config_only" != 'true' ]; then
    printf '%s\n' 'Git privacy configuration verification failed.' >&2
    exit 1
fi

printf '%s\n' "Git hooks enabled for $repository_root"
printf '%s\n' "hooksPath=$configured_hooks_path"
printf '%s\n' "user.useConfigOnly=$use_config_only"
printf '%s\n' "user.email=$configured_email"
