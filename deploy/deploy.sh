#!/usr/bin/env bash
set -euo pipefail

readonly deploy_dir=/opt/ohmysplitwise
readonly backup_dir="$deploy_dir/backups"
readonly archive=${1:?Image archive path is required}
readonly version=${2:?Image version is required}

if [[ ! "$version" =~ ^[0-9a-f]{40}$ ]]; then
    echo "Version must be a full Git commit SHA" >&2
    exit 2
fi
if [[ "$archive" != "/tmp/ohmysplitwise-$version.tar.gz" || ! -f "$archive" || -L "$archive" ]]; then
    echo "Unexpected image archive path" >&2
    exit 2
fi

cd "$deploy_dir"
previous_version=$(awk -F= '$1 == "APP_VERSION" { print $2 }' .env)

cleanup_archive() {
    rm -f "$archive"
}
trap cleanup_archive EXIT

install -d -m 700 "$backup_dir"
backup="$backup_dir/splitmoney-$version.dump"
backup_tmp="$backup.tmp"
rm -f "$backup_tmp"
if ! docker compose exec -T postgres pg_dump -U splitmoney -d splitmoney --format=custom > "$backup_tmp"; then
    rm -f "$backup_tmp"
    exit 1
fi
chmod 600 "$backup_tmp"
mv "$backup_tmp" "$backup"

gzip -dc "$archive" | docker load
sed -i "s/^APP_VERSION=.*/APP_VERSION=$version/" .env
docker compose up -d --no-deps --force-recreate app

if ! timeout 120 bash -c 'until curl --fail --silent --show-error https://bot.ohmysplitwise.ru/health >/dev/null; do sleep 5; done'; then
    echo "Health check failed, rolling back to $previous_version" >&2
    sed -i "s/^APP_VERSION=.*/APP_VERSION=$previous_version/" .env
    docker compose up -d --no-deps --force-recreate app
    exit 1
fi

printf '%s\n' "$previous_version" > .previous-version
while read -r image; do
    case "$image" in
        "ohmysplitwise:$version"|"ohmysplitwise:$previous_version") ;;
        *) docker image rm "$image" >/dev/null 2>&1 || true ;;
    esac
done < <(docker image ls ohmysplitwise --format '{{.Repository}}:{{.Tag}}')
docker image prune -f >/dev/null
docker compose ps
