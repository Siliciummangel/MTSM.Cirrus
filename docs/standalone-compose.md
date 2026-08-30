# Standalone deployment with Docker Compose

This guide installs the complete Cirrus stack on one Linux server:

- Cirrus API
- Cirrus worker
- Controlled Cirrus database migration
- On-demand Cirrus administration CLI
- PostgreSQL 18
- SeaweedFS 4.42 in single-node `mini` mode with an S3-compatible endpoint

This variant is intended for small installations that need a self-contained
deployment. It is not highly available: the Linux host, its Docker daemon and
its storage are shared failure domains for the application, database and object
storage. Use the external-services production deployment or Kubernetes when
multi-node availability is required.

## Prerequisites

- A supported Linux server with Docker Engine and the Compose plugin
- A DNS name and TLS certificate
- At least 4 CPU cores and 8 GiB RAM as an initial baseline
- Dedicated persistent storage sized for database growth and archived objects
- Synchronized UTC time
- An independent off-host backup destination

Actual capacity depends primarily on archive volume, file sizes, upload rate,
integrity-check concurrency and retention. Measure the workload in staging and
adjust resource limits before production use.

## Installation

Create a root-owned deployment directory:

```bash
sudo install -d -m 0750 -o root -g docker /opt/mtsm-cirrus
cd /opt/mtsm-cirrus
```

Place these files from the selected Cirrus release in the directory:

- `compose.standalone.yaml`
- `.env.standalone.example`

Create and protect the active configuration:

```bash
sudo cp .env.standalone.example .env.standalone
sudo chown root:docker .env.standalone compose.standalone.yaml
sudo chmod 0640 .env.standalone compose.standalone.yaml
sudoedit .env.standalone
```

Replace all placeholder credentials and configure the public hostname. Use long,
independent random secrets for PostgreSQL and S3. Never use `latest` as
`CIRRUS_VERSION`.

The active environment file contains credentials. Do not commit it, attach it to
support requests or include it in unencrypted backups. Users with Docker daemon
access can inspect container environment variables and must be treated as host
administrators.

## Network exposure

PostgreSQL and SeaweedFS are available only on the internal Compose network. The
Compose file publishes no database, S3 or administration ports. Only the Cirrus
API is published, and by default only at `127.0.0.1:8080`.

Place a reverse proxy such as Caddy, nginx or HAProxy on the host. It must:

- Terminate TLS and redirect public HTTP to HTTPS
- Proxy to `http://127.0.0.1:8080`
- Forward `Host`, `X-Forwarded-For` and `X-Forwarded-Proto`
- Stream large request bodies where possible
- Permit at least `CIRRUS_API_MAX_MULTIPART_UPLOAD_SIZE_BYTES`
- Apply suitable request and idle timeouts

Do not expose the Docker socket to the reverse proxy or public network.

## Validate and start

Validate the configuration before every deployment:

```bash
docker compose \
  --env-file .env.standalone \
  --file compose.standalone.yaml \
  config --quiet
```

Pull and start the exact release:

```bash
docker compose \
  --env-file .env.standalone \
  --file compose.standalone.yaml \
  pull

docker compose \
  --env-file .env.standalone \
  --file compose.standalone.yaml \
  up --detach --wait
```

The first start creates persistent volumes for PostgreSQL and SeaweedFS.
PostgreSQL and SeaweedFS must become healthy before migration runs. API and
worker start only after migration exits successfully.

Verify the deployment:

```bash
docker compose \
  --env-file .env.standalone \
  --file compose.standalone.yaml \
  ps --all

curl --fail http://127.0.0.1:8080/health/live
```

The `migrate` service should show `Exited (0)`. PostgreSQL, SeaweedFS, API and
worker should remain running.

## Persistent data

The Compose project owns two named volumes:

| Volume suffix | Content |
|---|---|
| `postgres-data` | Database schema, metadata, identities and audit events |
| `storage-data` | SeaweedFS metadata and archived binary content |

`docker compose down` preserves these volumes. Never use `down --volumes` on a
production installation unless the complete installation is intentionally being
destroyed and verified backups exist.

## Backup requirements

A valid Cirrus backup contains a mutually consistent recovery point for both
PostgreSQL and SeaweedFS. Backing up only one side can leave metadata referring
to missing objects or stored objects without matching metadata.

At minimum:

1. Create scheduled PostgreSQL backups with `pg_dump` or the organization's
   PostgreSQL backup system.
2. Replicate or back up archived S3 objects to storage outside the Docker host.
3. Protect the environment file in an encrypted configuration backup.
4. Record the deployed Cirrus, PostgreSQL and SeaweedFS versions.
5. Test a full restore regularly on a different host.

For a consistent maintenance-window backup, stop API and worker first so no
archive or purge operation can change PostgreSQL or S3 during the backup:

```bash
docker compose \
  --env-file .env.standalone \
  --file compose.standalone.yaml \
  stop api worker
```

After both database and object-storage backups finish, start the application
roles again:

```bash
docker compose \
  --env-file .env.standalone \
  --file compose.standalone.yaml \
  start api worker
```

Do not treat a raw copy of a live Docker volume as a verified backup. Use
application-aware PostgreSQL tooling and a SeaweedFS/S3-aware copy or a storage
snapshot procedure whose consistency has been tested.

## Administration CLI

Run administrative commands only when needed:

```bash
docker compose \
  --env-file .env.standalone \
  --file compose.standalone.yaml \
  run --rm admin --help
```

The temporary CLI container uses the internal PostgreSQL connection and is
removed after the command completes.

## Upgrade

Before upgrading:

1. Read the Cirrus release notes.
2. Produce and verify an off-host backup of PostgreSQL and SeaweedFS.
3. Record the current version and image digests.
4. Confirm adequate free disk space for old and new images.

Change `CIRRUS_VERSION` in `.env.standalone`, then run `pull` followed by
`up --detach --wait`. Compose runs the new migration image first and replaces API
and worker only after migration succeeds. Verify migration logs, liveness and an
authenticated archive round trip.

PostgreSQL and SeaweedFS upgrades are separate maintenance operations. Do not
change their major versions as part of an ordinary Cirrus application upgrade.
Review their upstream upgrade procedures and test them on restored data first.

## Rollback

An application rollback is safe only when the newer database schema remains
backward compatible. Follow the release-specific notes:

1. Set `CIRRUS_VERSION` back to the recorded version.
2. Pull that version and run `up --detach --wait`.
3. Verify API and worker behavior.

For an incompatible schema migration, restore the coordinated pre-upgrade
PostgreSQL and SeaweedFS recovery point. Never edit the EF migrations history
table manually.

## Monitoring

Monitor at least:

- Cirrus liveness and authenticated readiness
- Container restarts and failed migrations
- PostgreSQL health, connections, size and backup age
- SeaweedFS health, volume capacity and backup/replication status
- Host disk space, inode usage, memory pressure and Docker log growth
- Integrity-check and purge failures

The supplied limits are conservative starting values, not universal production
sizing guarantees.
