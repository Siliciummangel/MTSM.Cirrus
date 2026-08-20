# Local Docker Compose environment

The Compose environment runs the complete Cirrus stack locally:

- PostgreSQL 18
- SeaweedFS with its S3-compatible endpoint
- The one-time Cirrus migration process
- The Cirrus API
- The Cirrus worker
- The on-demand Cirrus administration CLI

This file is for development. End users should choose either the
[production deployment with external services](production-compose.md) or the
[self-contained standalone deployment](standalone-compose.md).

## First start

Create the local environment file once:

```powershell
Copy-Item .env.example .env
```

The example values are intended only for local development. `.env` is excluded
from Git and the Docker build context. Change its credentials if the services
are exposed beyond the local development machine.

Build the Cirrus images and start the environment:

```powershell
docker compose up --build --detach --wait
```

Compose waits for PostgreSQL and SeaweedFS, runs all database migrations, and
starts the API and worker only after migration succeeds.

The default local endpoints are:

| Component | URL |
|---|---|
| Cirrus API | `http://localhost:8080` |
| API liveness | `http://localhost:8080/health/live` |
| Scalar API reference | `http://localhost:8080/scalar` |
| S3 endpoint | `http://localhost:8333` |
| PostgreSQL | `localhost:5432` |

Ports, credentials, API limits, worker schedules, concurrency and logging can be
changed in `.env`. The tracked `.env.example` is the complete reference for all
values consumed by the local Compose environment.

## Daily operation

Show the current state:

```powershell
docker compose ps --all
```

Follow API and worker logs:

```powershell
docker compose logs --follow api worker
```

Stop and remove the containers while preserving database and object-storage
data:

```powershell
docker compose down
```

Start the existing environment again:

```powershell
docker compose up --detach --wait
```

## Administration CLI

The administration CLI is not a permanent service. Run it only when needed:

```powershell
docker compose run --rm admin --help
```

Arguments after `admin` are passed directly to the CLI. The temporary container
uses the same database configuration as the other Cirrus roles and is removed
after the command finishes.

## Reset all local data

The following command removes the containers and both named data volumes:

```powershell
docker compose down --volumes
```

This permanently deletes the local Cirrus PostgreSQL database and every object
stored in the local SeaweedFS volume. The next `docker compose up` creates a
fresh environment and reapplies all migrations.
