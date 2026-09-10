# Environment-variable reference

This document is the complete reference for environment variables used by
Cirrus itself, the supplied Docker Compose deployments and the automated test
suite. Values in the tracked `*.example` files are defaults or placeholders;
never put production secrets in Git.

## Configuration naming

.NET maps a double underscore to a configuration section separator. For
example, `StorageProcessing__BatchSize` overrides
`StorageProcessing:BatchSize` from `appsettings.json`.

The Compose files expose deployment-friendly `CIRRUS_*` variables from their
ENV files and map them to the corresponding .NET names inside the containers.
When Cirrus is started directly, use the .NET name shown in the tables. The
three templates serve different deployments:

| Template | Compose file | Purpose |
|---|---|---|
| `.env.example` | `compose.yaml` | Local development with bundled PostgreSQL and SeaweedFS |
| `.env.production.example` | `compose.production.yaml` | Production with external PostgreSQL and S3-compatible storage |
| `.env.standalone.example` | `compose.standalone.yaml` | Self-contained single-host installation |

## Deployment and infrastructure

These values configure Compose rather than an application options class.

| Compose variable | Templates | Default/example | Effect |
|---|---|---|---|
| `CIRRUS_VERSION` | All | Production/standalone: `0.2.0`; local: `0.0.0-local` | Immutable release image tag in production/standalone; local builds use it as image metadata. |
| `CIRRUS_IMAGE_PREFIX` | Production, standalone | `ghcr.io/siliciummangel/mtsm-cirrus` | Image repository prefix; role suffixes such as `-api` are appended. |
| `CIRRUS_BIND_ADDRESS` | Production, standalone | `127.0.0.1` | Host address on which the API port is published. Keep loopback when a host reverse proxy is used. |
| `CIRRUS_API_PORT` | All | `8080` | Host port mapped to API container port `8080`. |
| `CIRRUS_ALLOWED_HOSTS` | All | `*` locally; public hostname in production | ASP.NET Core host allow-list. Use semicolon-separated host names when more than one is required. |
| `CIRRUS_POSTGRES_PORT` | Local only | `5432` | Loopback host port for bundled PostgreSQL. |
| `CIRRUS_S3_PORT` | Local only | `8333` | Loopback host port for the bundled SeaweedFS S3 endpoint. |
| `CIRRUS_POSTGRES_DATABASE` | Local, standalone | `cirrus` | Database created by the bundled PostgreSQL container and used in the generated connection string. |
| `CIRRUS_POSTGRES_USER` | Local, standalone | `cirrus` | User created by bundled PostgreSQL and used in the generated connection string. |
| `CIRRUS_POSTGRES_PASSWORD` | Local, standalone | local placeholder | Password for bundled PostgreSQL. Replace it in standalone deployments. |
| `CIRRUS_DATABASE_CONNECTION_STRING` | Production only | no default | Complete Npgsql connection string for externally operated PostgreSQL. Required. |

The direct application variable is
`ConnectionStrings__ArchiveDatabase`. It is required by API, worker, migration
and administration runtimes. The database user used for migrations needs
schema-change privileges; ordinary runtime users need read/write access to the
`cirrus` schema.

## S3-compatible object storage

| Compose variable | Direct .NET variable | Default | Effect |
|---|---|---:|---|
| `CIRRUS_S3_SERVICE_URL` | `S3__ServiceUrl` | no application default | Absolute HTTP(S) S3 endpoint. Required; local and standalone templates use `http://storage:8333`. |
| `CIRRUS_S3_ACCESS_KEY` | `S3__AccessKey` | none | Required access key. |
| `CIRRUS_S3_SECRET_KEY` | `S3__SecretKey` | none | Required secret key. |
| `CIRRUS_S3_REGION` | `S3__Region` | `us-east-1` | AWS Signature V4 signing region. |
| `CIRRUS_S3_FORCE_PATH_STYLE` | `S3__ForcePathStyle` | `true` | Uses `endpoint/bucket/object` instead of virtual-hosted bucket addressing. Bundled SeaweedFS uses `true`. |
| `CIRRUS_S3_CREATE_BUCKET_IF_MISSING` | `S3__CreateBucketIfMissing` | application: `true`; production template: `false` | Allows Cirrus to create a missing tenant bucket during a write. Bundled SeaweedFS templates use `true`. |
| `CIRRUS_S3_USE_CHUNK_ENCODING` | `S3__UseChunkEncoding` | application: `true` | Enables HTTP chunked upload encoding. Bundled SeaweedFS templates use `false`. |
| `CIRRUS_S3_DISABLE_DEFAULT_CHECKSUM_VALIDATION` | `S3__DisableDefaultChecksumValidation` | `false` | Disables AWS SDK default checksum validation for incompatible providers. Bundled SeaweedFS templates use `true`. |

`ServiceUrl` must be an absolute HTTP or HTTPS URL. Access key, secret key and
region must not be blank.

## API

| Compose variable | Direct .NET variable | Default | Validation and effect |
|---|---|---:|---|
| `CIRRUS_API_MAX_MULTIPART_UPLOAD_SIZE_BYTES` | `Api__MaxMultipartUploadSizeBytes` | `1073741824` (1 GiB) | Must be positive. Limits the complete multipart request in Kestrel, form handling and the streaming multipart reader. Align reverse-proxy limits with it. |
| `CIRRUS_API_MAX_UPLOAD_METADATA_SIZE_BYTES` | `Api__MaxUploadMetadataSizeBytes` | `65536` (64 KiB) | Must be positive. Limits the JSON `metadata` multipart section buffered in memory. |
| `CIRRUS_API_RATE_LIMIT_PERMIT_COUNT` | `Api__RateLimitPermitCount` | `300` | Must be positive. Maximum requests per fixed window and authenticated identity (or remote IP before authentication). No queue is used. |
| `CIRRUS_API_RATE_LIMIT_WINDOW_SECONDS` | `Api__RateLimitWindowSeconds` | `60` | Must be positive. Length of the fixed rate-limit window. |

## Storage processing and pack maintenance

The worker first verifies staged uploads, then chunks them with FastCDC,
deduplicates chunks per tenant, compresses new chunks with Zstandard, writes
pack objects and publishes a manifest. Downloads continue to use the staging
object until manifest publication and staging cleanup complete.

| Compose variable | Direct .NET variable | Default | Validation and effect |
|---|---|---:|---|
| `CIRRUS_STORAGE_PROCESSING_ENABLED` | `StorageProcessing__Enabled` | `true` | Enables staging verification, packing and pack maintenance. Disabling it leaves new uploads readable from staging but stops conversion to manifests/packs. |
| `CIRRUS_STORAGE_PROCESSING_POLLING_INTERVAL_SECONDS` | `StorageProcessing__PollingIntervalSeconds` | `10` | Must be positive. Idle delay when storage work does not fill a batch. |
| `CIRRUS_STORAGE_PROCESSING_BATCH_SIZE` | `StorageProcessing__BatchSize` | `10` | `1`–`1000`. Maximum staged objects claimed in one processing/packing cycle. |
| `CIRRUS_STORAGE_PROCESSING_MAX_CONCURRENCY` | `StorageProcessing__MaxConcurrency` | `2` | `1`–`100` and no greater than `BatchSize`. Maximum parallel staging verifications per worker. |
| `CIRRUS_STORAGE_PROCESSING_LEASE_DURATION_MINUTES` | `StorageProcessing__LeaseDurationMinutes` | `30` | At least `3`. Recovery deadline for storage-processing and pack-maintenance claims. |
| `CIRRUS_STORAGE_PROCESSING_INITIAL_RETRY_DELAY_SECONDS` | `StorageProcessing__InitialRetryDelaySeconds` | `30` | Must be positive. First delay after a staging-verification failure and retry delay after a packing failure. |
| `CIRRUS_STORAGE_PROCESSING_MAXIMUM_RETRY_DELAY_MINUTES` | `StorageProcessing__MaximumRetryDelayMinutes` | `60` | Must be positive. Ceiling for exponential staging-processing retry delay. |
| `CIRRUS_STORAGE_PROCESSING_MAXIMUM_ATTEMPTS` | `StorageProcessing__MaximumAttempts` | `10` | Must be positive. Processing failures become terminal after this attempt count. |
| `CIRRUS_STORAGE_PROCESSING_MINIMUM_CHUNK_SIZE_BYTES` | `StorageProcessing__MinimumChunkSizeBytes` | `524288` (512 KiB) | Must be positive and no greater than average size. FastCDC minimum chunk size. Changing the profile affects newly processed content only. |
| `CIRRUS_STORAGE_PROCESSING_AVERAGE_CHUNK_SIZE_BYTES` | `StorageProcessing__AverageChunkSizeBytes` | `2097152` (2 MiB) | Must be at least the minimum and no greater than the maximum. FastCDC target chunk size. |
| `CIRRUS_STORAGE_PROCESSING_MAXIMUM_CHUNK_SIZE_BYTES` | `StorageProcessing__MaximumChunkSizeBytes` | `8388608` (8 MiB) | Must be at least the average and no greater than `TargetPackSizeBytes`. Bounds chunk buffers. |
| `CIRRUS_STORAGE_PROCESSING_TARGET_PACK_SIZE_BYTES` | `StorageProcessing__TargetPackSizeBytes` | `268435456` (256 MiB) | Must be at least the maximum chunk size. Target stored/compressed bytes per pack; packs split only between chunks. |
| `CIRRUS_STORAGE_PROCESSING_MAXIMUM_BATCH_WAIT_SECONDS` | `StorageProcessing__MaximumBatchWaitSeconds` | `15` | Must be zero or positive. Maximum wait used while forming a packing batch; `0` avoids an intentional wait. |
| `CIRRUS_STORAGE_PROCESSING_LEASE_HEARTBEAT_SECONDS` | `StorageProcessing__LeaseHeartbeatSeconds` | `30` | Must be positive and shorter than the lease duration. Renewal interval for long-running packing and maintenance leases. |
| `CIRRUS_STORAGE_PROCESSING_ZSTD_COMPRESSION_LEVEL` | `StorageProcessing__ZstdCompressionLevel` | `3` | `-5`–`22`. Zstandard level for newly stored chunks; higher levels generally trade CPU for size. |
| `CIRRUS_STORAGE_PROCESSING_PACK_MAINTENANCE_ENABLED` | `StorageProcessing__PackMaintenanceEnabled` | `true` | Enables unreachable-content pruning, orphan/garbage collection and low-utilization pack compaction. |
| `CIRRUS_STORAGE_PROCESSING_PACK_MAINTENANCE_BATCH_SIZE` | `StorageProcessing__PackMaintenanceBatchSize` | `10` | `1`–`1000`. Maximum maintenance candidates claimed per cycle. |
| `CIRRUS_STORAGE_PROCESSING_ORPHAN_GRACE_PERIOD_MINUTES` | `StorageProcessing__OrphanGracePeriodMinutes` | `60` | Age before unreferenced `Uploaded` or `Orphaned` packs may be deleted. Not currently startup-validated; use zero or a positive value. Increase it when storage/database operations can remain in flight for a long time. |
| `CIRRUS_STORAGE_PROCESSING_COMPACTION_MINIMUM_AGE_MINUTES` | `StorageProcessing__CompactionMinimumAgeMinutes` | `60` | Minimum age of a committed pack before compaction eligibility. Not currently startup-validated; use zero or a positive value. |
| `CIRRUS_STORAGE_PROCESSING_COMPACTION_UTILIZATION_PERCENT` | `StorageProcessing__CompactionUtilizationPercent` | `70` | `1`–`99`. Packs whose reachable-byte utilization is below this percentage are compaction candidates. |

Chunk-size, compression and pack-size changes do not rewrite existing content
immediately. Existing packs remain readable; eligible packs may later be
compacted without changing chunk compression.

## Scheduled integrity checks

| Compose variable | Direct .NET variable | Default | Validation and effect |
|---|---|---:|---|
| `CIRRUS_INTEGRITY_CHECKS_ENABLED` | `IntegrityChecks__Enabled` | `true` | Enables scheduled full-content verification. |
| `CIRRUS_INTEGRITY_INITIAL_VERIFICATION_DELAY_HOURS` | `IntegrityChecks__InitialVerificationDelayHours` | `24` | Zero or greater. Delay from archival to the first scheduled check. |
| `CIRRUS_INTEGRITY_REVERIFICATION_INTERVAL_DAYS` | `IntegrityChecks__ReverificationIntervalDays` | `180` | Must be positive. Interval after a completed check. |
| `CIRRUS_INTEGRITY_FAILURE_RETRY_DELAY_MINUTES` | `IntegrityChecks__FailureRetryDelayMinutes` | `60` | Must be positive. Delay after a technical failure. |
| `CIRRUS_INTEGRITY_POLLING_INTERVAL_SECONDS` | `IntegrityChecks__PollingIntervalSeconds` | `60` | Must be positive. Idle worker polling delay. |
| `CIRRUS_INTEGRITY_BATCH_SIZE` | `IntegrityChecks__BatchSize` | `10` | `1`–`1000`. Maximum checks claimed per cycle. |
| `CIRRUS_INTEGRITY_MAX_CONCURRENT_CHECKS` | `IntegrityChecks__MaxConcurrentChecks` | `2` | `1`–`100` and no greater than `BatchSize`. Maximum parallel checks per worker. |
| `CIRRUS_INTEGRITY_LEASE_DURATION_MINUTES` | `IntegrityChecks__LeaseDurationMinutes` | `30` | At least `3`. Claim recovery deadline, renewed while content is read. |
| `CIRRUS_INTEGRITY_WORKER_INSTANCE_ID` | `IntegrityChecks__WorkerInstanceId` | Compose/Helm: `cirrus-worker`; application: host name | Non-blank prefix up to 180 characters. Every process appends a random suffix, so replicas remain distinct. Direct deployments can omit it to use the host or container name. |

## Retention-aware purge

| Compose variable | Direct .NET variable | Default | Validation and effect |
|---|---|---:|---|
| `CIRRUS_PURGE_ENABLED` | `Purge__Enabled` | `true` | Enables automatic expiry requests and physical deletion of eligible objects. Disabling it pauses processing without changing state. |
| `CIRRUS_PURGE_POLLING_INTERVAL_SECONDS` | `Purge__PollingIntervalSeconds` | `60` | Must be positive. Idle polling delay. |
| `CIRRUS_PURGE_BATCH_SIZE` | `Purge__BatchSize` | `10` | `1`–`1000`. Maximum purge items claimed per cycle. |
| `CIRRUS_PURGE_MAX_CONCURRENT_DELETES` | `Purge__MaxConcurrentDeletes` | `2` | `1`–`100` and no greater than `BatchSize`. Maximum parallel storage deletes per worker. |
| `CIRRUS_PURGE_LEASE_DURATION_MINUTES` | `Purge__LeaseDurationMinutes` | `30` | At least `3`. Claim recovery deadline. |
| `CIRRUS_PURGE_INITIAL_RETRY_DELAY_MINUTES` | `Purge__InitialRetryDelayMinutes` | `5` | Must be positive. First delay after a deletion failure. |
| `CIRRUS_PURGE_MAXIMUM_RETRY_DELAY_MINUTES` | `Purge__MaximumRetryDelayMinutes` | `1440` | Must be at least the initial delay. Exponential-backoff ceiling. |

## Logging

| Compose variable | Direct .NET variable | Default | Effect |
|---|---|---:|---|
| `CIRRUS_LOG_LEVEL_DEFAULT` | `Logging__LogLevel__Default` | `Information` | Default minimum .NET log level. |
| `CIRRUS_LOG_LEVEL_ASPNETCORE` | `Logging__LogLevel__Microsoft.AspNetCore` | `Warning` | ASP.NET Core category override. |
| `CIRRUS_LOG_LEVEL_EF_COMMANDS` | `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command` | local: `Information`; production: `Warning` | SQL-command category override. Use `Warning` in production to reduce volume and metadata exposure. |

Valid standard levels are `Trace`, `Debug`, `Information`, `Warning`, `Error`,
`Critical` and `None`.

## Runtime variables set by the deployment

The supplied deployment files set these values themselves; they do not belong
in the user ENV templates:

| Variable | Use |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | Selects API environment-specific configuration; Compose uses `Development` locally and `Production` otherwise. |
| `DOTNET_ENVIRONMENT` | Selects worker, migration and admin environment-specific configuration. |
| `ASPNETCORE_HTTP_PORTS` | Makes the API listen on container port `8080`. |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Enables forwarded-header handling in production Compose behind a reverse proxy. |
| `HOSTNAME` | Used as the worker identity prefix when `IntegrityChecks__WorkerInstanceId` is omitted. |

## Test-only variables

These variables are read only by `MTSM.Cirrus.Core.Tests`; they do not configure
a deployed Cirrus service.

| Variable | Required | Effect |
|---|---:|---|
| `CIRRUS_TEST_POSTGRES` | For PostgreSQL integration tests | Connection string for a disposable database named exactly `test`, starting with `test_`, or ending with `_test`. The suite recreates its `cirrus` schema. |
| `CIRRUS_TEST_S3_SERVICE_URL` | For S3 integration tests | Endpoint of a disposable S3-compatible test service. |
| `CIRRUS_TEST_S3_ACCESS_KEY` | For S3 integration tests | Test access key. |
| `CIRRUS_TEST_S3_SECRET_KEY` | For S3 integration tests | Test secret key. |
| `CIRRUS_TEST_S3_REGION` | No | Test signing region; defaults to `us-east-1`. |
| `CI` | In CI | When `true`, missing or unsafe integration configuration fails rather than skips tests. |

## Precedence and safe changes

Environment variables override `appsettings.json`. Application options are
validated during startup, so an invalid value prevents the affected runtime
from starting. Run `docker compose ... config --quiet` before a deployment;
this validates interpolation and Compose structure, while application-level
range validation happens when the containers start.

Treat database connection strings, PostgreSQL passwords and S3 credentials as
secrets. Changing a credential requires updating the backing service and all
Cirrus roles consistently. Changing storage endpoint, bucket-related tenant
data or encryption identifiers can make existing content unreachable and must
be planned as a migration rather than a routine tuning change.
