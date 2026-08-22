# Production deployment with Docker Compose

This guide deploys Cirrus on a single Linux server for an end-user environment.
It runs the API, worker, controlled database migration and on-demand admin CLI
from versioned GHCR images.

This is the recommended variant when PostgreSQL and S3-compatible storage are
already operated independently. For a self-contained single-server installation,
see [Standalone deployment with Docker Compose](standalone-compose.md).

Docker Compose is a single-host deployment model. It does not provide automatic
multi-host failover. Environments requiring high availability, automatic
scheduling across nodes or zero-downtime orchestration should use the Cirrus
Kubernetes deployment instead.

## Production topology

The production Compose file deliberately does not run PostgreSQL or object
storage. Use PostgreSQL and S3-compatible storage that are operated, monitored
and backed up independently. They may run on the same server for a small
installation, but their lifecycle must remain separate from the Cirrus
application deployment.

```text
Internet
   |
   | HTTPS
   v
Reverse proxy on the Linux host
   |
   | HTTP on 127.0.0.1:8080
   v
Cirrus API ---- PostgreSQL
      |
      +-------- S3-compatible storage

Cirrus worker - PostgreSQL + S3
Cirrus migrate  PostgreSQL only, once per deployment
```

## Prerequisites

- A supported Linux server with synchronized UTC time
- Docker Engine and the Docker Compose plugin
- A DNS name and TLS certificate
- A supported PostgreSQL database with a dedicated database and user
- An S3-compatible bucket and dedicated credentials
- Backup and restore procedures for PostgreSQL and object storage
- Outbound network access from the containers to PostgreSQL and S3

Install Docker Engine from the official repository for the Linux distribution.
Do not expose the Docker socket or add untrusted users to the `docker` group;
Docker access is effectively root-level access to the host.

## Installation directory

Create a root-owned deployment directory:

```bash
sudo install -d -m 0750 -o root -g docker /opt/mtsm-cirrus
cd /opt/mtsm-cirrus
```

Place these release files in that directory:

- `compose.production.yaml`
- `.env.production.example`

Create the active configuration and restrict access to it:

```bash
sudo cp .env.production.example .env.production
sudo chown root:docker .env.production compose.production.yaml
sudo chmod 0640 .env.production compose.production.yaml
sudoedit .env.production
```

The active environment file contains credentials and must never be committed,
included in backups without encryption, or shared in support tickets. Compose
passes these values to the containers as environment variables; users with
Docker daemon access can inspect them. On hosts with stronger secret-management
requirements, inject the same variables from the platform's secret manager.

Set at least:

- `CIRRUS_VERSION` to an existing immutable Cirrus release
- `CIRRUS_DATABASE_CONNECTION_STRING`
- `CIRRUS_S3_SERVICE_URL`
- `CIRRUS_S3_ACCESS_KEY`
- `CIRRUS_S3_SECRET_KEY`

Use a version such as `0.2.0`, never `latest`. Keep the API bound to
`127.0.0.1` unless a firewall-protected external load balancer must connect
directly.

### Runtime configuration

The production environment template exposes every deployment-relevant setting
instead of relying on values embedded in `appsettings.json`.

| Area | Variables |
|---|---|
| Release | `CIRRUS_VERSION`, `CIRRUS_IMAGE_PREFIX` |
| Listener | `CIRRUS_BIND_ADDRESS`, `CIRRUS_API_PORT`, `CIRRUS_ALLOWED_HOSTS` |
| PostgreSQL | `CIRRUS_DATABASE_CONNECTION_STRING` |
| S3 | `CIRRUS_S3_SERVICE_URL`, `CIRRUS_S3_ACCESS_KEY`, `CIRRUS_S3_SECRET_KEY`, `CIRRUS_S3_REGION`, `CIRRUS_S3_FORCE_PATH_STYLE`, `CIRRUS_S3_CREATE_BUCKET_IF_MISSING`, `CIRRUS_S3_USE_CHUNK_ENCODING`, `CIRRUS_S3_DISABLE_DEFAULT_CHECKSUM_VALIDATION` |
| API | `CIRRUS_API_MAX_MULTIPART_UPLOAD_SIZE_BYTES`, `CIRRUS_API_RATE_LIMIT_PERMIT_COUNT`, `CIRRUS_API_RATE_LIMIT_WINDOW_SECONDS` |
| Integrity checks | `CIRRUS_INTEGRITY_CHECKS_ENABLED`, `CIRRUS_INTEGRITY_INITIAL_VERIFICATION_DELAY_HOURS`, `CIRRUS_INTEGRITY_REVERIFICATION_INTERVAL_DAYS`, `CIRRUS_INTEGRITY_FAILURE_RETRY_DELAY_MINUTES`, `CIRRUS_INTEGRITY_POLLING_INTERVAL_SECONDS`, `CIRRUS_INTEGRITY_BATCH_SIZE`, `CIRRUS_INTEGRITY_MAX_CONCURRENT_CHECKS`, `CIRRUS_INTEGRITY_LEASE_DURATION_MINUTES` |
| Purge | `CIRRUS_PURGE_ENABLED`, `CIRRUS_PURGE_POLLING_INTERVAL_SECONDS`, `CIRRUS_PURGE_BATCH_SIZE`, `CIRRUS_PURGE_MAX_CONCURRENT_DELETES`, `CIRRUS_PURGE_LEASE_DURATION_MINUTES`, `CIRRUS_PURGE_INITIAL_RETRY_DELAY_MINUTES`, `CIRRUS_PURGE_MAXIMUM_RETRY_DELAY_MINUTES` |
| Logging | `CIRRUS_LOG_LEVEL_DEFAULT`, `CIRRUS_LOG_LEVEL_ASPNETCORE`, `CIRRUS_LOG_LEVEL_EF_COMMANDS` |

`IntegrityChecks:WorkerInstanceId` is intentionally not exposed in the standard
template. Each worker generates a unique runtime identity from its container
hostname and a random suffix. Setting one shared static identity across several
workers would make operational diagnostics less reliable.

The reverse proxy upload limit must be at least
`CIRRUS_API_MAX_MULTIPART_UPLOAD_SIZE_BYTES`; otherwise the proxy rejects large
uploads before Cirrus can process them. Concurrency and memory limits should be
tuned together because each concurrent integrity check or purge operation uses
database, network and memory resources.

## GHCR authentication

Public Cirrus images can be pulled without authentication. If the GHCR package
is private, authenticate using a GitHub token with read access to packages:

```bash
printf '%s' "$GHCR_TOKEN" | docker login ghcr.io --username YOUR_GITHUB_USER --password-stdin
unset GHCR_TOKEN
```

Do not place the GHCR token in `.env.production`.

## Validate before deployment

Always render and validate the configuration before changing containers:

```bash
docker compose \
  --env-file .env.production \
  --file compose.production.yaml \
  config --quiet
```

This command validates the Compose model. It does not test database or object
storage connectivity.

## First deployment

Pull the exact release and start it:

```bash
docker compose \
  --env-file .env.production \
  --file compose.production.yaml \
  pull

docker compose \
  --env-file .env.production \
  --file compose.production.yaml \
  up --detach --wait
```

The deployment order is controlled:

1. `migrate` connects to PostgreSQL and applies pending EF Core migrations.
2. Compose waits for `migrate` to exit successfully.
3. Only then are `api` and `worker` started.

If migration fails, API and worker are not updated. Inspect the failure before
retrying:

```bash
docker compose \
  --env-file .env.production \
  --file compose.production.yaml \
  logs migrate
```

Verify the final state and local liveness endpoint:

```bash
docker compose \
  --env-file .env.production \
  --file compose.production.yaml \
  ps --all

curl --fail http://127.0.0.1:8080/health/live
```

The migration container should show `Exited (0)`. API and worker should remain
running.

## TLS reverse proxy

Cirrus listens on plain HTTP at `127.0.0.1:8080`. A production reverse proxy
such as Caddy, nginx, HAProxy or an external load balancer must:

- Terminate TLS with a trusted certificate
- Redirect public HTTP to HTTPS
- Forward `Host`, `X-Forwarded-For` and `X-Forwarded-Proto`
- Permit the configured maximum upload size
- Stream request bodies instead of buffering large archive uploads where possible
- Apply suitable request and idle timeouts
- Expose only the intended API routes and health endpoints

Do not publish PostgreSQL, S3 administration endpoints or the Docker socket to
the public network. The production Compose file publishes only the Cirrus API
and binds it to the loopback interface by default.

## Administration CLI

Run administrative commands as short-lived containers:

```bash
docker compose \
  --env-file .env.production \
  --file compose.production.yaml \
  run --rm admin --help
```

The CLI receives database access only for the lifetime of that command. Its
actions are still security-sensitive; restrict Docker access and record who
performed each operation.

## Upgrade

Before every upgrade:

1. Read the release notes, especially database and configuration changes.
2. Verify recent PostgreSQL and object-storage backups.
3. Test restoration, not only backup creation.
4. Record the currently deployed version and image digests.

Change only `CIRRUS_VERSION` in `.env.production`, then run:

```bash
docker compose \
  --env-file .env.production \
  --file compose.production.yaml \
  pull

docker compose \
  --env-file .env.production \
  --file compose.production.yaml \
  up --detach --wait
```

Check migration logs, liveness and one authenticated API operation after the
upgrade.

## Rollback

Application rollback and database rollback are different operations. Never run
an older application version against a migrated database unless the release
notes explicitly state that the schema remains backward compatible.

For an application-only rollback:

1. Set `CIRRUS_VERSION` back to the recorded version.
2. Pull and run `up --detach --wait` again.
3. Verify API and worker behavior.

If the release contains an incompatible database migration, restore PostgreSQL
from the pre-upgrade backup according to the release-specific rollback plan.
Object storage must be restored or reconciled as described by the same release
notes. Do not attempt to repair the EF migrations table manually.

## Routine operation

Follow application logs:

```bash
docker compose \
  --env-file .env.production \
  --file compose.production.yaml \
  logs --follow api worker
```

Stop Cirrus without deleting application state owned by external services:

```bash
docker compose \
  --env-file .env.production \
  --file compose.production.yaml \
  down
```

Configure monitoring for at least:

- Container restart count and availability
- `/health/live`
- `/health/ready` (aggregate status only)
- PostgreSQL availability, capacity and backup status
- Object-storage availability, capacity, replication and backup status
- Disk capacity for Docker logs and images
- Failed migration, integrity-check and purge operations

The Compose file limits container log rotation, CPU, memory and process counts.
Adjust these limits from measured staging usage before production load.
