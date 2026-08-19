# Archive retention and deletion lifecycle

This document is the normative Cirrus contract for retention and physical
deletion. It describes application behavior, not legal advice. Operators remain
responsible for choosing policies appropriate to their jurisdiction and data.

## Business rules

1. `RetentionUntil` is mandatory and is interpreted as a UTC calendar date.
2. The object is protected for the complete `RetentionUntil` date. It becomes
   purge-eligible at 00:00 UTC on the following day.
3. An authenticated caller with `archive.delete` may request deletion during
   retention. The request immediately changes `Active` to
   `DeletionRequested`, makes regular download unavailable, and is audited, but
   never permits an early physical purge.
4. Repeated requests in `DeletionRequested` or `Purged` are idempotent and do
   not create another transition event.
5. A retention policy with `DeleteAfterExpiry = true` causes the worker to
   create an audited deletion request after expiry. A policy with the flag off,
   or an object without such a policy, requires an explicit request.
6. `RetentionUntil` is checked while work is claimed and again immediately
   before storage deletion.
7. Cirrus retains metadata, business references and audit events after physical
   deletion. `Purged` means that the addressed storage object version no longer
   exists; it does not mean that the database record was erased.

## Processing order and recoverability

The existing `archive-worker` hosts both scheduled integrity checks and purge
processing. It does not start a separate worker service.

For each eligible object it performs this order:

1. claim the database row using `FOR UPDATE SKIP LOCKED` and a time-limited
   purge lease;
2. re-read the claimed row and re-check retention;
3. delete the exact stored object version when a version ID is available;
4. in one database transaction, set `ArchiveStatus = Purged`, set `PurgedAt`,
   clear worker leases, resolve pending purge errors and append `Purged`.

Storage and PostgreSQL cannot participate in one atomic transaction. A worker
can therefore stop after step 3 and before step 4. The lease eventually expires
and another worker retries. The retry observes a definite storage `Not Found`
and completes step 4 as an idempotent success. The `Purged` event records
whether this recovery path was used.

A storage response is treated as `Not Found` only when the provider identifies
it unambiguously as HTTP 404 / `NoSuchKey` / `NotFound`. Authentication,
authorization, timeout, object-lock and other failures are not converted into
success.

## Retry behavior

Technical deletion failures leave the object in `DeletionRequested`. The
worker clears its lease, upserts one unresolved `PURGE_FAILED` queue entry and
appends a `PurgeFailed` event. Retry delay uses bounded exponential backoff:

`min(MaximumRetryDelayMinutes, InitialRetryDelayMinutes × 2^(retryCount-1))`

There is intentionally no automatic terminal retry count. An archive remains
recoverable and visible to operations until deletion succeeds or an operator
corrects the external cause. Healthy concurrent workers cannot claim the same
object simultaneously; an expired lease is recoverable after process death.

## Configuration

| Setting | Default | Meaning |
|---|---:|---|
| `Purge:Enabled` | `true` | Enables automatic requests and physical purge |
| `Purge:PollingIntervalSeconds` | `60` | Idle polling interval |
| `Purge:BatchSize` | `10` | Maximum objects claimed per cycle |
| `Purge:MaxConcurrentDeletes` | `2` | Maximum concurrent storage deletions |
| `Purge:LeaseDurationMinutes` | `30` | Claim recovery deadline |
| `Purge:InitialRetryDelayMinutes` | `5` | First failure delay |
| `Purge:MaximumRetryDelayMinutes` | `1440` | Backoff ceiling |

Disabling the processor pauses enforcement; it does not alter lifecycle state.

## WORM and Object Lock

`RetentionPolicy.WormRequired`, `ArchiveObject.IsWormProtected` and a retention
date are metadata. They do not configure or prove S3 Object Lock. The current
S3 adapter does not create Object-Lock-enabled buckets, select governance or
compliance mode, set per-object retain-until dates, manage legal holds, verify
lock state, or prevent privileged storage administrators from changing storage
configuration.

When the storage provider rejects deletion because an actual lock is active,
Cirrus records a normal purge failure and retries. A value of
`IsWormProtected = true` must not be presented as evidence that storage is
immutable.

End-to-end WORM support requires a separate capability covering provider
compatibility, bucket provisioning, versioning, lock mode, retention-date
alignment, legal holds, privileged-operation controls and evidence collection.
Until then, `WormRequired` is an unmet policy declaration, not a compliance
guarantee.

## Approval model

Regular lifecycle deletion has no human approval step. Cirrus does not support
early-retention overrides, legal-hold overrides or other exceptional deletion
paths. If such administrative exceptions are introduced later, their approval
and authorization model must be designed separately.

## Guarantees and non-guarantees

Cirrus technically guarantees, subject to correct deployment and available
PostgreSQL/S3-compatible infrastructure:

- no worker-driven purge on or before `RetentionUntil`;
- storage deletion before the `Purged` database transition;
- retryable state after ordinary failures and worker termination;
- idempotent recovery when the storage object is already absent;
- an archive event for deletion requests, failed attempts and completed purges;
- cooperative concurrency control between Cirrus worker instances.

Cirrus does **not** by itself guarantee regulatory compliance, immutable/WORM
storage, legal-hold management, deletion within a fixed SLA, protection from a
privileged database or storage administrator, backup deletion, replica deletion
outside the configured provider, or cryptographic proof of erasure.
