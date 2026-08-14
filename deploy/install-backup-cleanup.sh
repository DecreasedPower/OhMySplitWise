#!/usr/bin/env bash
set -euo pipefail

readonly repository=DecreasedPower/OhMySplitWise
readonly revision=${1:?Full Git commit SHA is required}

if [[ ! "$revision" =~ ^[0-9a-f]{40}$ ]]; then
    echo "Revision must be a full Git commit SHA" >&2
    exit 2
fi

readonly base_url="https://raw.githubusercontent.com/$repository/$revision/deploy"
temp_dir=$(mktemp -d)
cleanup() {
    rm -rf "$temp_dir"
}
trap cleanup EXIT

curl -fsSL "$base_url/cleanup-backups.sh" -o "$temp_dir/cleanup-backups.sh"
curl -fsSL "$base_url/ohmysplitwise-backup-cleanup.service" -o "$temp_dir/ohmysplitwise-backup-cleanup.service"
curl -fsSL "$base_url/ohmysplitwise-backup-cleanup.timer" -o "$temp_dir/ohmysplitwise-backup-cleanup.timer"

install -o root -g root -m 755 "$temp_dir/cleanup-backups.sh" /opt/ohmysplitwise/cleanup-backups.sh
install -o root -g root -m 644 "$temp_dir/ohmysplitwise-backup-cleanup.service" /etc/systemd/system/ohmysplitwise-backup-cleanup.service
install -o root -g root -m 644 "$temp_dir/ohmysplitwise-backup-cleanup.timer" /etc/systemd/system/ohmysplitwise-backup-cleanup.timer
systemctl daemon-reload
/opt/ohmysplitwise/cleanup-backups.sh --dry-run
