#!/usr/bin/env bash
set -euo pipefail

readonly deploy_dir=/opt/ohmysplitwise
readonly backup_dir="$deploy_dir/backups"
readonly mode=${1:-}
readonly version=${2:-}
readonly image_size=${3:-}
readonly archive_size=${4:-}
readonly backup_headroom=$((16 * 1024 * 1024))
readonly docker_headroom=$((32 * 1024 * 1024))

if [[ "$mode" != "--check" && "$mode" != "--deploy" ]]; then
    echo "Usage: $0 --check|--deploy VERSION IMAGE_SIZE_BYTES ARCHIVE_SIZE_BYTES" >&2
    exit 2
fi
if [[ ! "$version" =~ ^[0-9a-f]{40}$ ]]; then
    echo "Version must be a full Git commit SHA" >&2
    exit 2
fi
if [[ ! "$image_size" =~ ^[0-9]+$ ]] || ((image_size == 0)); then
    echo "Image size must be a positive byte count" >&2
    exit 2
fi
if [[ ! "$archive_size" =~ ^[0-9]+$ ]] || ((archive_size == 0)); then
    echo "Archive size must be a positive byte count" >&2
    exit 2
fi

cd "$deploy_dir"
previous_version=$(awk -F= '$1 == "APP_VERSION" { print $2 }' .env)
domain=$(awk -F= '$1 == "DOMAIN" { print $2 }' .env)
https_port=$(awk -F= '$1 == "HTTPS_PORT" { print $2 }' .env)
https_port=${https_port:-443}
health_url="https://$domain:$https_port/health"

if [[ -z "$previous_version" ]]; then
    echo "APP_VERSION is missing from $deploy_dir/.env" >&2
    exit 1
fi
if ! docker image inspect "ohmysplitwise:$previous_version" >/dev/null 2>&1; then
    echo "Rollback image ohmysplitwise:$previous_version is not available" >&2
    exit 1
fi

cleanup_stale_images() {
    local image
    while read -r image; do
        case "$image" in
            "ohmysplitwise:$version"|"ohmysplitwise:$previous_version") ;;
            *) docker image rm "$image" >/dev/null 2>&1 || true ;;
        esac
    done < <(docker image ls ohmysplitwise --format '{{.Repository}}:{{.Tag}}')
    docker image prune -f >/dev/null
}

check_capacity() {
    local database_size docker_root backup_device docker_device
    local backup_available docker_available backup_required import_estimate docker_required required

    cleanup_stale_images
    install -d -m 700 "$backup_dir"

    database_size=$(docker compose exec -T postgres psql -U splitmoney -d splitmoney -Atqc "SELECT pg_database_size('splitmoney')" </dev/null)
    if [[ ! "$database_size" =~ ^[0-9]+$ ]]; then
        echo "Could not measure the PostgreSQL database" >&2
        exit 1
    fi

    docker_root=$(docker info --format '{{.DockerRootDir}}')
    backup_device=$(stat -c '%d' "$backup_dir")
    docker_device=$(stat -c '%d' "$docker_root")
    backup_available=$(df --output=avail -B1 "$backup_dir" | awk 'END { print $1 }')
    docker_available=$(df --output=avail -B1 "$docker_root" | awk 'END { print $1 }')
    backup_required=$((database_size + backup_headroom))
    # Existing rollback layers remain allocated; reserve at least the full new image plus import headroom.
    import_estimate=$((archive_size * 2))
    if ((import_estimate < image_size)); then
        import_estimate=$image_size
    fi
    docker_required=$((import_estimate + docker_headroom))

    if [[ "$backup_device" == "$docker_device" ]]; then
        required=$((backup_required + docker_required))
        if ((backup_available < required)); then
            echo "Insufficient free space: ${backup_available} bytes available, ${required} required for the measured database, image, and headroom" >&2
            exit 1
        fi
    else
        if ((backup_available < backup_required)); then
            echo "Insufficient backup space: ${backup_available} bytes available, ${backup_required} required" >&2
            exit 1
        fi
        if ((docker_available < docker_required)); then
            echo "Insufficient Docker space: ${docker_available} bytes available, ${docker_required} required" >&2
            exit 1
        fi
    fi

    echo "Capacity check passed: database=${database_size} bytes, archive=${archive_size} bytes, image=${image_size} bytes, estimated import=${import_estimate} bytes, backup filesystem available=${backup_available} bytes, Docker filesystem available=${docker_available} bytes"
}

wait_for_health() {
    timeout 120 bash -c "until curl --fail --silent --show-error '$health_url' >/dev/null; do sleep 5; done"
}

rollback() {
    local reason=$1

    echo "$reason; rolling back application image to $previous_version" >&2
    sed -i "s/^APP_VERSION=.*/APP_VERSION=$previous_version/" .env
    if docker compose up -d --no-deps --force-recreate app && wait_for_health; then
        echo "Application rollback to $previous_version is healthy" >&2
    else
        echo "Application rollback to $previous_version failed its health check" >&2
    fi
    exit 1
}

check_capacity
if [[ "$mode" == "--check" ]]; then
    exit 0
fi

install -d -m 700 "$backup_dir"
backup="$backup_dir/splitmoney-$version.dump"
backup_tmp="$backup.tmp"
rm -f "$backup_tmp"
if ! docker compose exec -T postgres pg_dump -U splitmoney -d splitmoney --format=custom </dev/null > "$backup_tmp"; then
    rm -f "$backup_tmp"
    exit 1
fi
chmod 600 "$backup_tmp"
mv "$backup_tmp" "$backup"

gzip -dc | docker load
if ! docker image inspect "ohmysplitwise:$version" >/dev/null 2>&1; then
    echo "The streamed archive did not contain ohmysplitwise:$version" >&2
    exit 1
fi
sed -i "s/^APP_VERSION=.*/APP_VERSION=$version/" .env
if ! docker compose up -d --no-deps --force-recreate app; then
    rollback "Application startup failed"
fi
if ! wait_for_health; then
    rollback "Application health check failed"
fi

printf '%s\n' "$previous_version" > .previous-version
cleanup_stale_images
docker compose ps
