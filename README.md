# MTSM.Cirrus

MTSM.Cirrus is a lightweight technical archiving service for applications.

It stores file content in an S3-compatible object storage system and persists searchable metadata in PostgreSQL. Cirrus exposes a REST API for archiving files, retrieving metadata, downloading archived content, and requesting logical deletion.

Cirrus is designed as an infrastructure component for application-generated files and raw business data. It is intentionally not a document management system.

> **Project status:** `0.2.0` in development / pre-release
> **Completed milestone:** MVP feature set (`0.1.0`)

---

## Table of contents

- [Overview](#overview)
- [Goals](#goals)
- [Non-goals](#non-goals)
- [MVP scope](#mvp-scope)
- [Current limitations](#current-limitations)
- [Architecture](#architecture)
- [Runtime roles](#runtime-roles)
- [Archive lifecycle](#archive-lifecycle)
- [Technology stack](#technology-stack)
- [Repository structure](#repository-structure)
- [Prerequisites](#prerequisites)
- [Configuration](#configuration)
  - [PostgreSQL configuration](#postgresql-configuration)
  - [Archive configuration](#archive-configuration)
  - [S3 configuration](#s3-configuration)
  - [API configuration](#api-configuration)
  - [Worker integrity-check configuration](#worker-integrity-check-configuration)
  - [Environment variables](#environment-variables)
  - [Secrets](#secrets)
- [Local setup](#local-setup)
  - [1. Start PostgreSQL](#1-start-postgresql)
  - [2. Start an S3-compatible object store](#2-start-an-s3-compatible-object-store)
  - [3. Configure Cirrus](#3-configure-cirrus)
  - [4. Run database migrations](#4-run-database-migrations)
  - [5. Start the API](#5-start-the-api)
  - [6. Start the worker](#6-start-the-worker)
- [API documentation](#api-documentation)
- [API usage](#api-usage)
  - [Archive a file](#archive-a-file)
  - [Retrieve metadata](#retrieve-metadata)
  - [Check whether an archive object exists](#check-whether-an-archive-object-exists)
  - [Download a file](#download-a-file)
  - [Search archive metadata](#search-archive-metadata)
  - [Verify archive integrity](#verify-archive-integrity)
  - [Retrieve integrity-check status](#retrieve-integrity-check-status)
  - [Request logical deletion](#request-logical-deletion)
- [Actor tracking](#actor-tracking)
- [Business references](#business-references)
- [Retention metadata](#retention-metadata)
- [Deletion behavior](#deletion-behavior)
- [Health checks](#health-checks)
- [Error responses](#error-responses)
- [Logging](#logging)
- [Security considerations](#security-considerations)
- [Deployment model](#deployment-model)
  - [Docker Compose](#docker-compose)
  - [Kubernetes](#kubernetes)
- [Development](#development)
  - [Build](#build)
  - [Test](#test)
  - [Database migrations](#database-migrations)
- [Terminology](#terminology)
- [Versioning](#versioning)
- [Roadmap](#roadmap)
- [License](#license)

---

## Overview

Applications frequently need to retain generated files or source data for long periods without requiring the features and complexity of a full document management system.

MTSM.Cirrus provides a small and focused archive service for this purpose.

The service separates binary content from metadata:

- File content is written to S3-compatible object storage.
- Archive metadata is stored in PostgreSQL.
- Applications interact with the archive through a REST API.
- SHA-256 hashes are calculated while content is archived.
- Archive operations are recorded as events.
- Business references can be attached to archived files.
- Objects can be marked for future deletion without immediately removing data.

Cirrus is intended to run as a stateless application service. Multiple API or worker instances can use the same PostgreSQL database and object storage backend.

---

## Goals

The primary goals of Cirrus are:

- Provide a small technical archive for application-generated files.
- Keep file storage independent from application databases.
- Store binary content in S3-compatible object storage.
- Store structured and searchable metadata in PostgreSQL.
- Offer a simple REST API.
- Support streaming uploads and downloads.
- Calculate a SHA-256 checksum during archival.
- Preserve technical and business references.
- Record relevant archive lifecycle events.
- Support horizontal scaling of stateless runtime roles.
- Remain suitable for Docker Compose and Kubernetes.
- Avoid unnecessary domain-specific workflow logic.

---

## Non-goals

Cirrus is intentionally **not** a document management system.

The following capabilities are outside the scope of the project:

- Document editing
- Document previews
- OCR
- Full-text extraction
- Full-text document search
- Approval workflows
- Document routing
- Task management
- Case management
- Records management user interfaces
- Collaborative document handling
- Version comparison
- Electronic signatures
- User-facing document portals
- Domain-specific business processes

Applications using Cirrus remain responsible for their own business logic and user interfaces.

Cirrus archives files and metadata. It does not manage the business process around those files.

---

## MVP scope

The MVP focuses on the smallest complete technical archive workflow.

The MVP includes:

- Archiving a file through the REST API
- Streaming file content into S3-compatible object storage
- Storing archive metadata in PostgreSQL
- Calculating and storing a SHA-256 checksum
- Retrieving archive metadata by archive object ID
- Downloading archived file content
- Checking whether an archive object exists
- Searching archive metadata
- Manually verifying archive content integrity
- Associating business references with an archive object
- Recording archive events
- Requesting logical deletion of an archive object
- OpenAPI documentation
- Scalar API reference in the development environment
- Liveness and readiness health checks
- A separate database migration runtime
- A deployable worker runtime placeholder

### Explicitly outside the MVP

The following features were intentionally deferred from the MVP. Scheduled
integrity verification is the first feature being added for `0.2.0`:

- Physical deletion of archived content
- Processing deletion requests
- Automatic purge execution
- Retention enforcement
- Automatic deletion after retention expiry
- WORM enforcement
- Immutable object storage enforcement
- Object-lock management
- Automatic retry processing for failed operations
- Garbage collection of orphaned database or storage records
- Authentication and authorization
- Administrative user interface
- Storage replication management
- Backup orchestration

Some database fields or entities may already prepare for these capabilities. Their presence does not mean that the corresponding feature is active.

---

## Current limitations

Cirrus is currently an early pre-release project.

Important limitations include:

- No authentication or authorization is currently enforced by the API.
- Actor information is supplied by the calling application.
- Deletion requests do not result in physical deletion.
- Retention information is stored but not actively enforced.
- WORM-related metadata does not provide WORM guarantees.
- The worker currently performs scheduled integrity checks only.
- Operational deployment manifests are not yet included.
- Backward compatibility is not guaranteed before version `1.0.0`.
- The API and worker contracts may still change before `0.2.0`.

Do not use the current development version as the sole mechanism for legal or regulatory compliance.

---

## Architecture

Cirrus separates file content, metadata, and application processing.

```text
                      ┌──────────────────────┐
                      │ Calling application  │
                      └──────────┬───────────┘
                                 │
                                 │ HTTP / REST
                                 ▼
                      ┌──────────────────────┐
                      │     archive-api      │
                      │                      │
                      │ Upload               │
                      │ Download             │
                      │ Metadata             │
                      │ Search               │
                      │ Deletion requests    │
                      └───────┬───────┬──────┘
                              │       │
                Metadata      │       │ File content
                              ▼       ▼
                    ┌─────────────┐  ┌─────────────────┐
                    │ PostgreSQL  │  │ S3-compatible   │
                    │             │  │ object storage  │
                    │ Metadata    │  │                 │
                    │ Events      │  │ Binary content  │
                    │ References  │  │                 │
                    └──────┬──────┘  └────────┬────────┘
                           │                  │
                           └────────┬─────────┘
                                    │
                                    ▼
                          ┌──────────────────┐
                          │  archive-worker  │
                          │                  │
                          │ Integrity checks │
                          │                  │
                          │ Future tasks:    │
                          │ Purge            │
                          │ Retention        │
                          └──────────────────┘
```

Database schema changes are applied by a separate migration runtime:

```text
┌──────────────────┐
│ archive-migrate  │
└────────┬─────────┘
         │
         ▼
  ┌─────────────┐
  │ PostgreSQL  │
  └─────────────┘
```

---

## Runtime roles

All runtime roles use the same codebase and database model but serve different operational purposes.

### `archive-api`

The API runtime exposes the REST interface.

Responsibilities:

- Accept file uploads
- Validate archive requests
- Stream files to object storage
- Calculate SHA-256 checksums
- Persist archive metadata
- Return metadata
- Stream archived files to clients
- Accept logical deletion requests
- Expose OpenAPI documentation
- Expose health-check endpoints

The API is stateless and can be deployed with multiple replicas.

### `archive-worker`

The worker performs scheduled integrity checks for active archive objects.
It recalculates the SHA-256 hash and content size after an initial delay and
then repeats the verification at a configurable interval.

Multiple worker instances can operate on the same database. Due checks are
claimed using PostgreSQL row locking with `SKIP LOCKED` and a renewable lease.
The lease identifies the active worker and becomes claimable again if a worker
terminates unexpectedly. Completed and failed checks record the worker instance
as the archive event actor.

The worker remains reserved for additional background tasks such as:

- Processing deletion requests
- Enforcing retention rules
- Physically purging archive content
- Detecting inconsistent archive records
- Retrying recoverable operations
- Cleaning up orphaned data

Integrity checks use at-least-once processing semantics. A check can therefore
be repeated after a worker or network failure, but concurrent healthy workers
do not intentionally process the same archive object.

### `archive-migrate`

The migration runtime applies Entity Framework Core migrations to PostgreSQL.

It is intended to run:

- Manually during local development
- As a dedicated deployment step
- As a Kubernetes Job
- As an init or release task in a deployment pipeline

The API and worker should not automatically apply database migrations during normal startup.

---

## Archive lifecycle

An archive object can move through several technical states.

```text
Pending
   │
   ├── successful storage and metadata persistence
   │
   ▼
 Active
   │
   ├── logical deletion requested
   │
   ▼
DeletionRequested
```

Failure handling may move an object into an error state:

```text
Pending ────────► Error
```

Additional lifecycle states may exist in the data model as preparation for post-MVP functionality.

### MVP lifecycle

Within the MVP:

- `Pending` represents an archive operation that has not yet completed.
- `Active` represents a successfully archived and available object.
- `Error` represents an archive operation that failed.
- `DeletionRequested` represents an object that has been logically marked for future deletion.

A deletion request does not remove:

- The file from object storage
- The archive metadata from PostgreSQL
- The event history
- The business references

Physical deletion is not implemented in the MVP.

---

## Technology stack

Cirrus currently uses:

- [.NET 10](https://dotnet.microsoft.com/)
- ASP.NET Core
- Entity Framework Core
- PostgreSQL
- Npgsql
- AWS SDK for .NET
- S3-compatible object storage
- OpenAPI
- Scalar API Reference

The service is designed for:

- Local development
- Docker Compose
- Kubernetes

---

## Repository structure

```text
MTSM.Cirrus/
├── MTSM.Cirrus.API/
│   ├── Config/
│   ├── Contracts/
│   ├── Controllers/
│   ├── Extensions/
│   ├── Mapping/
│   ├── Middleware/
│   ├── Program.cs
│   └── appsettings.json
│
├── MTSM.Cirrus.Core/
│   ├── Abstractions/
│   ├── Config/
│   ├── Data/
│   ├── Entities/
│   ├── Enums/
│   ├── Exceptions/
│   ├── Models/
│   ├── Services/
│   └── Storage/
│
├── MTSM.Cirrus.Migration/
│   ├── Data/
│   │   └── Migrations/
│   └── Program.cs
│
├── MTSM.Cirrus.Worker/
│   ├── Program.cs
│   └── Worker.cs
│
├── MTSM.Cirrus.Core.Tests/
│
├── LICENSE.txt
├── MTSM.Cirrus.slnx
└── README.md
```

### Project responsibilities

| Project | Responsibility |
|---|---|
| `MTSM.Cirrus.API` | REST API, HTTP contracts, middleware, OpenAPI and health endpoints |
| `MTSM.Cirrus.Core` | Archive domain logic, persistence, entities and object-storage integration |
| `MTSM.Cirrus.Migration` | Database migration execution |
| `MTSM.Cirrus.Worker` | Scheduled integrity verification and future background processing |
| `MTSM.Cirrus.Core.Tests` | Automated Core, worker and database migration tests |

---

## Prerequisites

For local development, the following components are required:

- .NET 10 SDK
- PostgreSQL
- An S3-compatible object storage service
- Git

Optional tools:

- Docker
- Docker Compose
- `curl`
- PostgreSQL client tools
- An S3 administration client

### Supported storage services

Cirrus communicates with object storage through an S3-compatible API.

Possible storage backends include:

- Garage
- SeaweedFS
- Ceph Object Gateway
- AWS S3
- Other sufficiently compatible S3 implementations

Compatibility can vary between providers, especially regarding:

- Chunked transfer encoding
- Checksum headers
- Path-style addressing
- Bucket creation
- Object versioning
- Object locking

The S3 compatibility options described below can be adjusted for the selected provider.

---

## Configuration

Configuration follows the standard .NET configuration model.

Values can be provided through:

- `appsettings.json`
- `appsettings.{Environment}.json`
- User Secrets
- Environment variables
- Command-line arguments
- Kubernetes Secrets and ConfigMaps

The API currently uses four main configuration sections:

```json
{
  "ConnectionStrings": {
    "ArchiveDatabase": "..."
  },
  "Archive": {
    "BucketName": "...",
    "ObjectKeyPrefix": "..."
  },
  "S3": {
    "ServiceUrl": "...",
    "AccessKey": "...",
    "SecretKey": "...",
    "Region": "...",
    "ForcePathStyle": true,
    "CreateBucketIfMissing": true,
    "UseChunkEncoding": false,
    "DisableDefaultChecksumValidation": true
  },
  "Api": {
    "MaxMultipartUploadSizeBytes": 1073741824
  }
}
```

---

### PostgreSQL configuration

The PostgreSQL connection string is configured as:

```json
{
  "ConnectionStrings": {
    "ArchiveDatabase": "Host=localhost;Port=5432;Database=cirrus;Username=cirrus;Password=change-me"
  }
}
```

Example environment variable:

```bash
ConnectionStrings__ArchiveDatabase="Host=localhost;Port=5432;Database=cirrus;Username=cirrus;Password=change-me"
```

Example connection string fields:

| Field | Description |
|---|---|
| `Host` | PostgreSQL server hostname |
| `Port` | PostgreSQL port, normally `5432` |
| `Database` | Cirrus metadata database |
| `Username` | PostgreSQL user |
| `Password` | PostgreSQL password |

Cirrus stores its database objects in the `cirrus` schema.

The configured PostgreSQL user must have sufficient permissions to:

- Connect to the database
- Create or modify the Cirrus schema during migrations
- Read and write archive metadata during normal API operation

For production environments, separate migration and application database users are recommended.

---

### Archive configuration

The `Archive` section controls the storage location used for archived files.

```json
{
  "Archive": {
    "BucketName": "cirrus-archive",
    "ObjectKeyPrefix": "objects"
  }
}
```

| Setting | Required | Default | Description |
|---|---:|---|---|
| `BucketName` | Yes | — | S3 bucket in which archived files are stored |
| `ObjectKeyPrefix` | No | `objects` | Prefix used when generating object keys |

Environment variables:

```bash
Archive__BucketName="cirrus-archive"
Archive__ObjectKeyPrefix="objects"
```

The generated object key is an internal storage identifier. Calling applications should store and use the returned `archiveObjectId` rather than constructing object keys themselves.

---

### S3 configuration

Example:

```json
{
  "S3": {
    "ServiceUrl": "http://localhost:8333",
    "AccessKey": "cirrus",
    "SecretKey": "change-me",
    "Region": "us-east-1",
    "ForcePathStyle": true,
    "CreateBucketIfMissing": true,
    "UseChunkEncoding": false,
    "DisableDefaultChecksumValidation": true
  }
}
```

| Setting | Required | Default | Description |
|---|---:|---|---|
| `ServiceUrl` | Yes | — | Base URL of the S3-compatible API |
| `AccessKey` | Yes | — | S3 access key |
| `SecretKey` | Yes | — | S3 secret key |
| `Region` | No | `us-east-1` | Signing region used for AWS Signature Version 4 |
| `ForcePathStyle` | No | `true` | Uses `endpoint/bucket/object` addressing |
| `CreateBucketIfMissing` | No | `true` | Creates the archive bucket during the first write if it does not exist |
| `UseChunkEncoding` | No | `true` | Enables HTTP chunked transfer encoding for uploads |
| `DisableDefaultChecksumValidation` | No | `false` | Disables AWS SDK default checksum validation |

Environment variables:

```bash
S3__ServiceUrl="http://localhost:8333"
S3__AccessKey="cirrus"
S3__SecretKey="change-me"
S3__Region="us-east-1"
S3__ForcePathStyle="true"
S3__CreateBucketIfMissing="true"
S3__UseChunkEncoding="false"
S3__DisableDefaultChecksumValidation="true"
```

#### Compatibility notes

Many self-hosted S3-compatible services require path-style addressing:

```json
{
  "S3": {
    "ForcePathStyle": true
  }
}
```

Some providers do not fully support chunked upload encoding:

```json
{
  "S3": {
    "UseChunkEncoding": false
  }
}
```

Some providers do not fully support the checksum behavior expected by newer AWS SDK versions:

```json
{
  "S3": {
    "DisableDefaultChecksumValidation": true
  }
}
```

These compatibility switches should only be enabled when required by the selected object storage provider.

---

### API configuration

The maximum multipart upload request size is configured in bytes:

```json
{
  "Api": {
    "MaxMultipartUploadSizeBytes": 1073741824
  }
}
```

The default value is:

```text
1 GiB
```

Equivalent environment variable:

```bash
Api__MaxMultipartUploadSizeBytes="1073741824"
```

This limit applies to the complete multipart request, not only to the raw file content.

Reverse proxies, ingress controllers and load balancers may enforce additional request-size limits. Their configuration must allow at least the same maximum body size.

Examples include:

- NGINX Ingress `proxy-body-size`
- Traefik buffering middleware
- HAProxy request limits
- Cloud load-balancer limits

---

### Worker integrity-check configuration

Scheduled integrity verification is configured in the worker:

```json
{
  "IntegrityChecks": {
    "Enabled": true,
    "InitialVerificationDelayHours": 24,
    "ReverificationIntervalDays": 180,
    "FailureRetryDelayMinutes": 60,
    "PollingIntervalSeconds": 60,
    "BatchSize": 10,
    "MaxConcurrentChecks": 2,
    "LeaseDurationMinutes": 30,
    "WorkerInstanceId": null
  }
}
```

| Setting | Default | Description |
|---|---:|---|
| `Enabled` | `true` | Enables scheduled integrity verification |
| `InitialVerificationDelayHours` | `24` | Delay after archival before the first check |
| `ReverificationIntervalDays` | `180` | Delay between completed checks |
| `FailureRetryDelayMinutes` | `60` | Delay after a technical verification failure |
| `PollingIntervalSeconds` | `60` | Delay when no full batch is available |
| `BatchSize` | `10` | Maximum number of checks claimed at once |
| `MaxConcurrentChecks` | `2` | Maximum checks processed concurrently per worker |
| `LeaseDurationMinutes` | `30` | Initial and renewed ownership period for a check |
| `WorkerInstanceId` | — | Optional stable prefix; hostname is used when omitted |

Each process adds a random suffix to its instance ID. This keeps two processes
on the same host distinguishable. In Kubernetes the hostname normally contains
the pod name, so logs and archive event actors identify the responsible pod.

Leases are renewed while content is being read. If a worker terminates, another
worker can claim the check after the lease expires. Processing is at least once;
after failures a verification may therefore be repeated.

Environment-variable examples:

```bash
IntegrityChecks__InitialVerificationDelayHours="24"
IntegrityChecks__ReverificationIntervalDays="180"
IntegrityChecks__MaxConcurrentChecks="2"
IntegrityChecks__WorkerInstanceId="cirrus-worker"
```

Increasing concurrency increases object-storage reads, database activity and
network bandwidth. Keep it conservative for large archive objects.

---

### Environment variables

.NET maps double underscores to configuration section separators.

Example:

```bash
export ConnectionStrings__ArchiveDatabase="Host=localhost;Port=5432;Database=cirrus;Username=cirrus;Password=change-me"

export Archive__BucketName="cirrus-archive"
export Archive__ObjectKeyPrefix="objects"

export S3__ServiceUrl="http://localhost:8333"
export S3__AccessKey="cirrus"
export S3__SecretKey="change-me"
export S3__Region="us-east-1"
export S3__ForcePathStyle="true"
export S3__CreateBucketIfMissing="true"
export S3__UseChunkEncoding="false"
export S3__DisableDefaultChecksumValidation="true"

export Api__MaxMultipartUploadSizeBytes="1073741824"

export IntegrityChecks__InitialVerificationDelayHours="24"
export IntegrityChecks__ReverificationIntervalDays="180"
```

---

### Secrets

Do not commit production credentials to the repository.

Sensitive values include:

- PostgreSQL passwords
- S3 access keys
- S3 secret keys
- Future authentication credentials
- Encryption keys

For local development, use .NET User Secrets where possible.

Example for the API project:

```bash
dotnet user-secrets set \
  "ConnectionStrings:ArchiveDatabase" \
  "Host=localhost;Port=5432;Database=cirrus;Username=cirrus;Password=change-me" \
  --project MTSM.Cirrus.API
```

```bash
dotnet user-secrets set \
  "S3:AccessKey" \
  "cirrus" \
  --project MTSM.Cirrus.API
```

```bash
dotnet user-secrets set \
  "S3:SecretKey" \
  "change-me" \
  --project MTSM.Cirrus.API
```

For Kubernetes, use Kubernetes Secrets or an external secrets management system.

---

## Local setup

The following steps describe a local development setup.

### 1. Start PostgreSQL

Create a PostgreSQL database and application user.

Example SQL:

```sql
CREATE ROLE cirrus
    WITH LOGIN
    PASSWORD 'change-me';

CREATE DATABASE cirrus
    OWNER cirrus;
```

Verify the connection:

```bash
psql \
  --host localhost \
  --port 5432 \
  --username cirrus \
  --dbname cirrus
```

For production environments:

- Use a strong password.
- Restrict network access.
- Enable TLS.
- Back up the database.
- Use a dedicated migration role if appropriate.

---

### 2. Start an S3-compatible object store

Start or provide an S3-compatible object storage endpoint.

The default development configuration expects:

```text
Endpoint:   http://localhost:8333
Access key: cirrus
Secret key: change-me
Bucket:     cirrus-archive
Region:     us-east-1
```

When `CreateBucketIfMissing` is enabled, Cirrus attempts to create the configured archive bucket during the first write.

The configured S3 credentials need permission to:

- Check whether the bucket exists
- Create the bucket when enabled
- Upload objects
- Download objects
- Read object metadata

Physical object deletion is not required by the MVP.

---

### 3. Configure Cirrus

Review:

```text
MTSM.Cirrus.API/appsettings.json
```

At minimum, configure:

- `ConnectionStrings:ArchiveDatabase`
- `Archive:BucketName`
- `S3:ServiceUrl`
- `S3:AccessKey`
- `S3:SecretKey`

Do not store production secrets in `appsettings.json`.

---

### 4. Run database migrations

Run the migration project from the repository root:

```bash
dotnet run \
  --project MTSM.Cirrus.Migration
```

The migration runtime reads the `ArchiveDatabase` connection string from the standard .NET configuration sources.

Using an environment variable:

```bash
ConnectionStrings__ArchiveDatabase="Host=localhost;Port=5432;Database=cirrus;Username=cirrus;Password=change-me" \
dotnet run \
  --project MTSM.Cirrus.Migration
```

A successful migration run updates the PostgreSQL schema to the current version and then exits.

Database migrations should be completed before starting a newly deployed API or worker version.

---

### 5. Start the API

Run:

```bash
dotnet run \
  --project MTSM.Cirrus.API
```

The default development launch profile exposes:

```text
HTTP:  http://localhost:5190
HTTPS: https://localhost:7040
```

The exact address is shown in the application logs during startup.

In the development environment, the following endpoints are available:

```text
OpenAPI document:
http://localhost:5190/openapi/v1.json

Scalar API reference:
http://localhost:5190/scalar
```

---

### 6. Start the worker

Run:

```bash
dotnet run \
  --project MTSM.Cirrus.Worker
```

The worker schedules the first integrity check 24 hours after archival by
default and repeats it every 180 days. Start the migration runtime before using
the `0.2.0` worker so the scheduling and lease columns exist.

The worker currently does not:

- Process deletion requests
- Delete files
- Enforce retention

---

## API documentation

Cirrus exposes an OpenAPI document in the development environment.

Default endpoints:

```text
GET /openapi/v1.json
GET /scalar
```

The Scalar interface provides an interactive overview of:

- Endpoints
- Request formats
- Response formats
- Status codes
- Validation rules

OpenAPI and Scalar are currently enabled only when the API runs in the `Development` environment.

---

## API usage

The examples below use:

```bash
BASE_URL="http://localhost:5190"
```

---

### Archive a file

Endpoint:

```http
POST /api/archive
```

Content type:

```text
multipart/form-data
```

Example:

```bash
curl --request POST \
  "${BASE_URL}/api/archive" \
  --form "file=@./example.pdf;type=application/pdf" \
  --form "fileType=invoice" \
  --form "sourceSystem=example-application" \
  --form "partner=example-partner" \
  --form "tenant=example-tenant" \
  --form "receivedAt=2026-07-27T18:30:00Z" \
  --form "createdBy=max@example.org" \
  --form "retentionUntil=2036-07-27"
```

Required fields:

| Field | Type | Description |
|---|---|---|
| `file` | File | File content to archive |
| `fileType` | String | Technical or business file type |
| `sourceSystem` | String | Application that submitted the file |
| `tenant` | String | Tenant owning the archive object |
| `createdBy` | String | Actor or system that initiated archival |

Optional fields:

| Field | Type | Description |
|---|---|---|
| `partner` | String | Optional partner identifier |
| `receivedAt` | Date and time | Time at which the source system received the file |
| `retentionPolicyId` | Integer | Optional reference to a retention policy |
| `retentionUntil` | Date | Explicit retention date |
| `businessReferences` | Collection | Structured business references |

When `receivedAt` is omitted, the API uses the current UTC time.

Example response:

```json
{
  "archiveObjectId": 16,
  "objectKey": "objects/...",
  "sha256Hash": "3c0f8f7d7f5f8f0d5f2db4d5e1e04ef74e2df263d1e50fe4cf2d937bc4ef8273",
  "sizeBytes": 42,
  "archivedAt": "2026-07-27T18:30:01.2345678+00:00"
}
```

Successful status:

```text
201 Created
```

The `Location` response header points to the archive metadata endpoint.

#### Archive behavior

During archival, Cirrus:

1. Validates the request.
2. Creates an archive metadata record.
3. Streams the file into object storage.
4. Calculates the SHA-256 checksum.
5. Stores the object size and checksum.
6. Persists business references.
7. Records archive events.
8. Marks the archive object as active.
9. Returns the generated archive object ID.

The calling application should persist the returned `archiveObjectId`.

---

### Retrieve metadata

Endpoint:

```http
GET /api/archive/{archiveObjectId}/metadata
```

Example:

```bash
curl \
  "${BASE_URL}/api/archive/16/metadata"
```

Example response:

```json
{
  "archiveObjectId": 16,
  "objectKey": "objects/...",
  "bucketName": "cirrus-archive",
  "fileType": "invoice",
  "mimeType": "application/pdf",
  "sourceSystem": "example-application",
  "partner": "example-partner",
  "originalFilename": "example.pdf",
  "sha256Hash": "3c0f8f7d7f5f8f0d5f2db4d5e1e04ef74e2df263d1e50fe4cf2d937bc4ef8273",
  "sizeBytes": 42,
  "receivedAt": "2026-07-27T18:30:00+00:00",
  "archivedAt": "2026-07-27T18:30:01.2345678+00:00",
  "retentionUntil": "2036-07-27",
  "retentionPolicyId": null,
  "archiveStatus": "Active",
  "deletionRequestedAt": null,
  "deletionRequestedBy": null,
  "purgedAt": null,
  "storageVersionId": null,
  "encryptionKeyId": null,
  "isWormProtected": false,
  "createdBy": "max@example.org",
  "businessReferences": [],
  "events": []
}
```

Successful status:

```text
200 OK
```

Missing archive object:

```text
404 Not Found
```

Metadata retrieval does not download the archived file content.

---

### Check whether an archive object exists

Endpoint:

```http
HEAD /api/archive/{archiveObjectId}
```

Example:

```bash
curl \
  --head \
  "${BASE_URL}/api/archive/16"
```

Successful status:

```text
200 OK
```

Missing archive object:

```text
404 Not Found
```

The response can include:

| Header | Description |
|---|---|
| `Content-Type` | Stored MIME type or `application/octet-stream` |
| `Content-Length` | Stored file size |
| `ETag` | SHA-256 checksum |
| `X-Content-SHA256` | SHA-256 checksum |
| `X-Archive-Status` | Current archive status |
| `Last-Modified` | Archival timestamp |

The `HEAD` endpoint checks metadata existence. It does not independently verify that the S3 object is currently readable.

---

### Download a file

Endpoint:

```http
GET /api/archive/{archiveObjectId}
```

The caller must provide the `X-Actor` header.

Example:

```bash
curl \
  --header "X-Actor: max@example.org" \
  --output downloaded-example.pdf \
  "${BASE_URL}/api/archive/16"
```

Successful status:

```text
200 OK
```

The response includes:

| Header | Description |
|---|---|
| `Content-Type` | Stored MIME type |
| `Content-Disposition` | Original filename |
| `Content-Length` | File size when available |
| `ETag` | SHA-256 checksum |
| `X-Archive-Object-Id` | Cirrus archive object ID |
| `X-Content-SHA256` | Stored SHA-256 checksum |

Range processing is enabled. Clients may request partial content where supported by the ASP.NET Core response pipeline.

Example actor header:

```http
X-Actor: example-application
```

The actor is recorded in the archive event history.

Possible responses:

| Status | Meaning |
|---|---|
| `200 OK` | Download started successfully |
| `400 Bad Request` | Invalid ID or missing actor |
| `404 Not Found` | Archive object does not exist |
| `409 Conflict` | Archive object is not available for download |
| `500 Internal Server Error` | Storage or archive operation failed |

---

### Search archive metadata

Endpoint:

```http
GET /api/archive/search
```

All filters are optional. Supported query parameters:

- `archiveObjectId`
- `tenant`
- `fileType`
- `sourceSystem`
- `partner`
- `originalFilename`
- `sha256Hash`
- `archiveStatus`
- `receivedFrom` and `receivedUntil`
- `archivedFrom` and `archivedUntil`
- `businessReferenceTypeId`
- `businessReferenceValue`
- `businessType`
- `pageNumber` (default: `1`)
- `pageSize` (default: `50`, maximum: `500`)

Example:

```bash
curl --get \
  "${BASE_URL}/api/archive/search" \
  --data-urlencode "tenant=example-tenant" \
  --data-urlencode "sourceSystem=example-application" \
  --data-urlencode "archiveStatus=Active" \
  --data-urlencode "pageNumber=1" \
  --data-urlencode "pageSize=50"
```

Successful status:

```text
200 OK
```

Example response:

```json
{
  "items": [
    {
      "archiveObjectId": 16,
      "fileType": "invoice",
      "mimeType": "application/pdf",
      "sourceSystem": "example-application",
      "partner": null,
      "originalFilename": "example.pdf",
      "sha256Hash": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "sizeBytes": 12345,
      "receivedAt": "2026-07-27T18:30:00Z",
      "archivedAt": "2026-07-27T18:30:01Z",
      "retentionUntil": "2036-07-27",
      "archiveStatus": "Active",
      "deletionRequestedAt": null,
      "purgedAt": null,
      "businessReferences": []
    }
  ],
  "pageNumber": 1,
  "pageSize": 50,
  "totalCount": 1,
  "totalPages": 1
}
```

Search results are ordered by `receivedAt` and then by
`archiveObjectId`, both descending. An empty result returns an empty
`items` array and `totalPages` set to `0`.

Invalid pagination, date ranges, hashes or enum values result in
`400 Bad Request`.

---

### Verify archive integrity

Endpoint:

```http
POST /api/archive/{archiveObjectId}/verify-integrity
```

The caller must provide the `X-Actor` header.

Example:

```bash
curl \
  --request POST \
  --header "X-Actor: max@example.org" \
  "${BASE_URL}/api/archive/16/verify-integrity"
```

The operation reads the complete storage object and recalculates its
SHA-256 hash and size. Both values are compared with the stored archive
metadata. The result is recorded as an archive event.

Example successful verification:

```json
{
  "archiveObjectId": 16,
  "isValid": true,
  "expectedSha256Hash": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "actualSha256Hash": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "expectedSizeBytes": 12345,
  "actualSizeBytes": 12345,
  "verifiedAt": "2026-08-16T12:00:00Z"
}
```

Successful status:

```text
200 OK
```

`200 OK` means that the verification was completed. It does not necessarily
mean that the stored content is valid. Callers must inspect `isValid`. A hash
or size mismatch returns `isValid: false` and records an
`IntegrityCheckFailed` event.

Possible responses:

| Status | Meaning |
|---|---|
| `200 OK` | Verification completed; inspect `isValid` |
| `400 Bad Request` | Invalid ID or missing actor |
| `404 Not Found` | Archive object does not exist |
| `409 Conflict` | Archive object is not active or cannot be verified |
| `500 Internal Server Error` | Storage or archive operation failed |

The endpoint performs a synchronous full read and may take significant time
and storage bandwidth for large archive objects. Scheduled verification is
performed separately by the `0.2.0` worker.

---

### Retrieve integrity-check status

Endpoint:

```http
GET /api/archive/{archiveObjectId}/integrity-status
```

Example:

```bash
curl "${BASE_URL}/api/archive/16/integrity-status"
```

Example response:

```json
{
  "archiveObjectId": 16,
  "lastCheckedAt": "2026-08-16T12:00:00Z",
  "lastCheckIsValid": true,
  "lastCheckActor": "archive-worker/cirrus-worker-7d3f",
  "nextCheckAt": "2027-02-12T12:00:00Z",
  "isCheckInProgress": false,
  "leaseOwner": null,
  "leaseUntil": null
}
```

The last-check fields include both manually requested and worker-triggered
checks. Before any check has completed, they are `null`.

The worker materializes the initial schedule during polling. Shortly after a
new object is archived, `nextCheckAt` is therefore set to the archival time plus
`InitialVerificationDelayHours`.

While a scheduled check is running, `isCheckInProgress` is `true` and the lease
fields identify the responsible worker claim. An expired lease may remain
visible temporarily, but `isCheckInProgress` will be `false`; another worker can
then claim the object.

Possible responses:

| Status | Meaning |
|---|---|
| `200 OK` | Integrity-check status returned |
| `400 Bad Request` | Invalid archive object ID |
| `404 Not Found` | Archive object does not exist |
| `500 Internal Server Error` | Status lookup failed |

---

### Request logical deletion

Endpoint:

```http
DELETE /api/archive/{archiveObjectId}
```

The caller must provide the `X-Actor` header.

Example:

```bash
curl \
  --request DELETE \
  --header "X-Actor: max@example.org" \
  "${BASE_URL}/api/archive/16"
```

Example response:

```json
{
  "archiveObjectId": 16,
  "archiveStatus": "DeletionRequested",
  "deletionRequestedAt": "2026-07-27T19:00:00.0000000+00:00",
  "deletionRequestedBy": "max@example.org",
  "purgedAt": null,
  "stateChanged": true
}
```

Successful status:

```text
202 Accepted
```

The endpoint records a logical deletion request.

It does **not**:

- Delete the file from object storage
- Delete archive metadata
- Delete business references
- Delete event history
- Schedule a currently active purge process
- Guarantee future physical deletion

The archive status is changed to:

```text
DeletionRequested
```

Repeated deletion requests are handled idempotently where possible. The response field `stateChanged` indicates whether the request caused a new state transition.

Example:

```json
{
  "archiveObjectId": 16,
  "archiveStatus": "DeletionRequested",
  "deletionRequestedAt": "2026-07-27T19:00:00.0000000+00:00",
  "deletionRequestedBy": "max@example.org",
  "purgedAt": null,
  "stateChanged": false
}
```

A future post-MVP worker implementation may process deletion requests. No such processing is part of the current MVP.

---

## Actor tracking

Some operations require an actor to be supplied through the HTTP header:

```http
X-Actor: actor-name
```

The header is currently required for:

- File download
- Manual integrity verification
- Logical deletion requests

The actor can represent:

- A user
- A service account
- A source application
- A scheduled process

Examples:

```http
X-Actor: max@example.org
```

```http
X-Actor: invoice-service
```

```http
X-Actor: application/backend-job
```

Cirrus currently trusts the value supplied by the caller.

The `X-Actor` header is **not an authentication mechanism**.

Once authentication is introduced, actor information should be derived or validated against the authenticated identity.

---

## Business references

Business references connect an archive object to identifiers from an external business domain.

Examples include:

- Invoice number
- Shipment number
- Order number
- Customer number
- Tour ID
- Case ID
- Message ID

A business reference consists of:

| Field | Description |
|---|---|
| `businessReferenceTypeId` | Reference to the configured reference type |
| `referenceValue` | External identifier |
| `businessType` | Business context |
| `tenant` | Tenant context |

Conceptual example:

```json
{
  "businessReferenceTypeId": 1,
  "referenceValue": "INV-2026-000123",
  "businessType": "invoice",
  "tenant": "example-tenant"
}
```

Multiple business references may be assigned to one archive object.

Business references are metadata only. Cirrus does not validate whether the referenced business object exists in the source system.

---

## Retention metadata

Cirrus can store retention-related metadata:

- `retentionPolicyId`
- `retentionUntil`

This allows applications to record how long an archive object is intended to be retained.

Example:

```text
retentionUntil = 2036-07-27
```

Important:

- Retention metadata is stored.
- Retention expiry is not automatically evaluated.
- Expired archive objects are not automatically deleted.
- Retention rules are not enforced by the MVP.
- Cirrus does not currently prevent deletion requests before retention expiry unless explicitly implemented in the service logic.
- The calling application remains responsible for validating regulatory requirements.

Retention enforcement is planned as post-MVP worker functionality.

---

## Deletion behavior

Deletion in the MVP is intentionally limited to a logical deletion request.

### What happens

When a valid deletion request is accepted:

1. The archive object is located.
2. Its state is changed to `DeletionRequested`.
3. The request timestamp is stored.
4. The requesting actor is stored.
5. A corresponding archive event is recorded.
6. The updated state is returned with `202 Accepted`.

### What does not happen

The MVP does not:

- Remove the S3 object
- Remove the PostgreSQL record
- Remove business references
- Remove archive events
- Verify retention expiry
- Execute a purge
- Guarantee that deletion will happen at a particular time

### Why logical deletion is separated from physical deletion

Separating the request from physical deletion enables future implementations to:

- Validate retention rules
- Apply legal holds
- Retry failed deletions
- Record every lifecycle step
- Avoid data loss during partial failures
- Coordinate database and object-storage changes
- Run deletion asynchronously
- Require additional approval where needed

The physical purge lifecycle is deliberately deferred until after the MVP.

---

## Health checks

Cirrus exposes separate liveness and readiness endpoints.

### Liveness

```http
GET /health/live
```

Example:

```bash
curl \
  "${BASE_URL}/health/live"
```

Purpose:

- Confirms that the application process is running.
- Does not check external dependencies.
- Suitable for Kubernetes liveness probes.

Expected successful status:

```text
200 OK
```

### Readiness

```http
GET /health/ready
```

Example:

```bash
curl \
  "${BASE_URL}/health/ready"
```

Purpose:

- Indicates whether the application is ready to serve requests.
- Includes health checks tagged with `ready`.

Current readiness dependencies should be verified against the active health-check registrations.

Kubernetes example:

```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: http
  initialDelaySeconds: 5
  periodSeconds: 10

readinessProbe:
  httpGet:
    path: /health/ready
    port: http
  initialDelaySeconds: 5
  periodSeconds: 10
```

---

## Error responses

API errors use the ASP.NET Core `ProblemDetails` format.

Example:

```json
{
  "type": "https://httpstatuses.com/404",
  "title": "Archive object not found",
  "status": 404,
  "detail": "Archive object 999 does not exist.",
  "instance": "/api/archive/999",
  "traceId": "0HN..."
}
```

Common status codes:

| Status | Meaning |
|---|---|
| `400 Bad Request` | Invalid input or missing required header |
| `404 Not Found` | Archive object does not exist |
| `409 Conflict` | Archive object exists but is unavailable for the requested operation |
| `500 Internal Server Error` | Archive, database or object-storage operation failed |

The `traceId` field can be used to correlate a client error with server logs.

Internal exception details are not returned for unexpected server errors.

---

## Logging

Cirrus uses the standard .NET logging abstractions.

Example logging configuration:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

In production environments, consider changing Entity Framework command logging to `Warning` to reduce log volume and avoid unintentionally logging sensitive query information.

Recommended production configuration:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  }
}
```

Important operations are logged, including:

- Archive requests
- Storage failures
- Download failures
- Request cancellation
- Client errors
- Unexpected server errors
- Migration execution
- Worker startup and shutdown

Never log:

- S3 secret keys
- Database passwords
- Raw file contents
- Authentication tokens
- Encryption keys

---

## Security considerations

The MVP does not yet provide complete production security controls.

Before exposing Cirrus outside a trusted network, consider:

- Adding authentication
- Adding authorization
- Validating tenant access
- Deriving actors from authenticated identities
- Enforcing TLS
- Restricting database network access
- Restricting object-storage network access
- Rotating S3 credentials
- Using short-lived or workload identities where available
- Applying request-rate limits
- Validating accepted file types where required
- Scanning uploaded content where required
- Limiting maximum upload sizes
- Disabling development OpenAPI endpoints in production
- Protecting logs from sensitive metadata leakage
- Encrypting backups
- Defining backup and restore procedures

### Tenant isolation

The presence of a tenant field does not automatically guarantee tenant isolation.

Until explicit authorization is implemented, callers may potentially request archive objects belonging to another tenant if they know the archive object ID.

Do not treat the current MVP API as a secure multi-tenant boundary.

### Integrity

Cirrus calculates a SHA-256 hash during archival and stores it as metadata.

This provides a reference checksum but does not currently mean:

- Every download is automatically rehashed
- The checksum is cryptographically signed
- The metadata itself is tamper-proof

The `0.2.0` worker periodically reads active storage objects and compares their
SHA-256 hash and size with the stored metadata. A mismatch is recorded as an
`IntegrityCheckFailed` event. Technical failures are queued and retried after a
configurable delay. Cirrus records detected mismatches but does not currently
repair, quarantine or replicate affected content automatically.

---

## Deployment model

Cirrus is designed for:

- Docker Compose
- Kubernetes

The primary target platform is Kubernetes.

### Recommended service topology

```text
archive-api
  Replicas: 1 or more
  Type: long-running service
  Network access:
    - PostgreSQL
    - S3-compatible object storage

archive-worker
  Replicas: 1 or more
  Type: long-running background service
  Current behavior: scheduled integrity verification
  Network access:
    - PostgreSQL
    - S3-compatible object storage

archive-migrate
  Replicas: 1 per deployment
  Type: one-time job
  Network access:
    - PostgreSQL
```

---

### Docker Compose

A future Docker Compose setup is expected to include:

- PostgreSQL
- S3-compatible object storage
- `archive-migrate`
- `archive-api`
- `archive-worker`

Conceptual startup order:

```text
PostgreSQL and object storage
            │
            ▼
     archive-migrate
            │
            ▼
 archive-api + archive-worker
```

The migration process should complete successfully before the API and worker are updated to a schema-dependent application version.

> Docker Compose files are planned as part of deployment polishing and may not yet be included in the repository.

---

### Kubernetes

The intended Kubernetes deployment model includes:

- API `Deployment`
- API `Service`
- Worker `Deployment`
- Migration `Job`
- `ConfigMap` for non-sensitive configuration
- `Secret` or external secret provider for credentials
- Liveness and readiness probes
- Resource requests and limits
- Ingress or Gateway API resource
- Optional PodDisruptionBudget
- Optional NetworkPolicies

Conceptual migration flow:

```text
1. Deploy or update configuration
2. Run archive-migrate as a Kubernetes Job
3. Wait for successful completion
4. Roll out archive-api
5. Roll out archive-worker
```

Cirrus does not require local persistent volumes for the API or worker because archive content is stored externally in S3-compatible object storage.

---

## Development

### Build

Build the complete solution:

```bash
dotnet build MTSM.Cirrus.slnx
```

Restore dependencies explicitly:

```bash
dotnet restore MTSM.Cirrus.slnx
```

Build in Release configuration:

```bash
dotnet build \
  MTSM.Cirrus.slnx \
  --configuration Release
```

---

### Test

Automated tests currently cover `MTSM.Cirrus.Core`, the integrity-check processing
in `MTSM.Cirrus.Worker`, S3-compatible object storage integration and the Entity
Framework Core database migration chain. The suite contains fast contract tests
plus PostgreSQL and S3-compatible integration tests for persistence, storage
round trips, searching, row locking, concurrent processing and schema upgrades.

Run the Core test suite in Release configuration:

```bash
dotnet test \
  MTSM.Cirrus.Core.Tests/MTSM.Cirrus.Core.Tests.csproj \
  --configuration Release
```

The fast tests do not require external services. PostgreSQL integration tests
use the `CIRRUS_TEST_POSTGRES` environment variable:

```bash
export CIRRUS_TEST_POSTGRES='Host=localhost;Port=5432;Database=cirrus_test;Username=cirrus;Password=change-me'

dotnet test MTSM.Cirrus.Core.Tests/MTSM.Cirrus.Core.Tests.csproj \
  --configuration Release
```

For safety, the configured database name must be exactly `test`, start with
`test_`, or end with `_test`. The test fixture recreates the `cirrus` schema in
that database, so the connection must point to a dedicated disposable test
database.

Without `CIRRUS_TEST_POSTGRES`, integration tests are reported as skipped on a
developer machine. When `CI=true`, missing or unsafe PostgreSQL configuration
causes them to fail instead, preventing an incomplete CI test run from passing.

CI also audits all direct and transitive NuGet dependencies against the
vulnerability data supplied by the configured package sources. Any reported
vulnerability fails the job. Run the same audit locally after restoring the
solution:

```bash
dotnet package list \
  --project MTSM.Cirrus.slnx \
  --vulnerable \
  --include-transitive \
  --no-restore
```

The test suite covers:

- Successful archival
- SHA-256 calculation
- Metadata persistence
- Object-storage upload
- S3-compatible write, existence and read behavior
- S3-compatible missing-object error handling
- Download behavior
- Metadata retrieval
- Search behavior
- Business references
- Concurrent and idempotent deletion requests
- Database rollback behavior
- Object-storage failures
- PostgreSQL failures
- Request cancellation
- Validation errors
- Migration from an empty PostgreSQL database
- Upgrade from the previous database version
- Preservation of existing archive data during upgrades
- Active and expired worker leases
- Exclusive claims across concurrently running workers
- Integrity-check retry scheduling, incrementing and resolution
- Enforcement of the configured worker concurrency limit
- HTTP contracts for every archive API endpoint
- Multipart binding, response mapping and archive response headers
- Validation and standardized `ProblemDetails` error responses
- Safe mapping of domain and unexpected exceptions to HTTP status codes

See the [test-suite documentation](MTSM.Cirrus.Core.Tests/README.md) for
PowerShell examples, database setup details and code-coverage commands.

---

### Database migrations

Create a new Entity Framework Core migration:

```bash
dotnet ef migrations add <MigrationName> \
  --project MTSM.Cirrus.Migration \
  --startup-project MTSM.Cirrus.Migration
```

Example:

```bash
dotnet ef migrations add AddArchiveFeature \
  --project MTSM.Cirrus.Migration \
  --startup-project MTSM.Cirrus.Migration
```

Apply migrations using the dedicated migration runtime:

```bash
dotnet run \
  --project MTSM.Cirrus.Migration
```

Remove the most recent unapplied migration:

```bash
dotnet ef migrations remove \
  --project MTSM.Cirrus.Migration \
  --startup-project MTSM.Cirrus.Migration
```

Generate an idempotent SQL migration script:

```bash
dotnet ef migrations script \
  --idempotent \
  --project MTSM.Cirrus.Migration \
  --startup-project MTSM.Cirrus.Migration \
  --output cirrus-migration.sql
```

Review generated migrations before committing them.

Do not edit an already deployed migration unless the deployment history is fully controlled. Create a new corrective migration instead.

---

## Terminology

The project uses the following terms.

### Archive object

An **archive object** is the complete logical record managed by Cirrus.

It includes:

- The file content stored in object storage
- Metadata stored in PostgreSQL
- Business references
- Archive events
- Lifecycle state

In source code, this concept is represented by:

```csharp
ArchiveObject
```

### Archived file

The **archived file** is the binary content submitted by the calling application.

It is stored in S3-compatible object storage.

### Storage object

A **storage object** is the physical S3 object containing the archived file content.

It is identified internally by:

- Bucket name
- Object key
- Optional storage version ID

### Metadata

**Metadata** is the structured information stored in PostgreSQL.

Examples:

- Original filename
- File type
- MIME type
- SHA-256 hash
- File size
- Source system
- Tenant
- Retention date
- Archive status
- Business references
- Archive events

### Archive object ID

The **archive object ID** is the primary identifier exposed to calling applications.

Example:

```text
16
```

API consumers should use the archive object ID rather than depending on internal S3 bucket names or object keys.

### Deletion request

A **deletion request** is the logical marking of an archive object for possible future physical deletion.

It does not mean that content has already been deleted.

### Purge

A **purge** is the future physical removal of archived content and the corresponding lifecycle handling.

Purge is not part of the MVP.

---

## Versioning

Cirrus follows Semantic Versioning:

```text
MAJOR.MINOR.PATCH
```

Example:

```text
0.2.0
```

Meaning:

- `MAJOR`: Incompatible API or behavior changes after the project reaches stable versioning
- `MINOR`: New backward-compatible functionality
- `PATCH`: Backward-compatible bug fixes

### Pre-1.0 policy

Before version `1.0.0`:

- The API may still change.
- Database schemas may still change significantly.
- Configuration keys may be renamed.
- Backward compatibility is not guaranteed.
- Release notes should explicitly describe breaking changes.

The MVP feature milestone is:

```text
0.1.0
```

Potential later versions:

```text
0.1.1  Bug fixes for the initial MVP
0.2.0  Scheduled worker integrity verification (in development)
0.3.0  Additional operational features
1.0.0  Stable supported contract
```

The application version should be defined centrally for all projects.

---

## License

MTSM.Cirrus is licensed under the MIT License.

See:

```text
LICENSE.txt
```

Copyright:

```text
Copyright (c) 2026 Maximilian Thater
```

The MIT License permits use, copying, modification, distribution, sublicensing and commercial use, subject to the conditions stated in the license text.

---

## Project status

Cirrus is under active development.

The MVP feature set was completed as the `0.1.0` milestone. Current development
targets `0.2.0`, beginning with distributed scheduled integrity verification in
the worker runtime.

Feedback, issue reports and contributions will be welcome once the public contribution process is defined.
