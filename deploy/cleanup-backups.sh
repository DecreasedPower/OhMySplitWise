#!/usr/bin/env bash
set -euo pipefail

readonly backup_dir=${OHMYSPLITWISE_BACKUP_DIR:-/opt/ohmysplitwise/backups}
readonly keep_count=3

dry_run=false
case "${1:-}" in
    "") ;;
    --dry-run) dry_run=true ;;
    *)
        echo "Usage: $0 [--dry-run]" >&2
        exit 2
        ;;
esac

if [[ ! -d "$backup_dir" ]]; then
    echo "Backup directory does not exist: $backup_dir"
    exit 0
fi

backups=()
for backup in "$backup_dir"/splitmoney-*.dump; do
    filename=${backup##*/}
    if [[ -f "$backup" && ! -L "$backup" && "$filename" =~ ^splitmoney-[0-9a-f]{40}\.dump$ ]]; then
        index=${#backups[@]}
        while ((index > 0)) && [[ "$backup" -nt "${backups[index - 1]}" ]]; do
            backups[index]=${backups[index - 1]}
            index=$((index - 1))
        done
        backups[index]=$backup
    fi
done

echo "Found ${#backups[@]} backup(s); keeping the newest $keep_count."
for ((index = keep_count; index < ${#backups[@]}; index++)); do
    backup=${backups[index]#* }
    if $dry_run; then
        echo "Would remove $backup"
    else
        rm -f -- "$backup"
        echo "Removed $backup"
    fi
done
