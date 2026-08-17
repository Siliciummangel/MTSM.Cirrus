# Core behavior guarantees

`MTSM.Cirrus.Core` owns the archive lifecycle and does not rely on the API layer
for correctness or input validation.

## Archival

- Input is validated and normalized before persistence.
- Database length constraints, retention policies and business-reference types
  are checked before object storage is called.
- Content is streamed from its current position while SHA-256 and byte count are
  calculated over exactly the bytes sent to object storage.
- A database record is created as `Pending` before the upload. It becomes
  `Active` only after the upload succeeds and its hash, size and storage version
  have been persisted.
- Cancellation or failure after creation of the pending record changes it to
  `Error` on a best-effort basis. Failure-state persistence has a bounded
  timeout and stores a stable, non-sensitive error message.
- The caller owns the input stream and remains responsible for disposing it.

## Download and integrity

- Only `Active` archive objects can be downloaded or integrity-checked.
- Opening or reading object storage failures are exposed as archive-domain
  failures. Provider details remain available only through the inner exception.
- The download event means that a readable storage stream was opened and handed
  to the caller. The caller owns and must dispose that returned stream.
- Integrity verification reads the complete object, checks SHA-256 and byte
  count, persists the result event and always disposes the storage stream.
- Client cancellation is propagated rather than converted into a technical
  archive failure.

## Storage diagnostics

- The archive service correlates storage failures with archive-object ID,
  bucket and object key.
- The S3 provider records operation, HTTP status, S3 error code and request ID
  without logging the complete provider exception.
- Object key, ETag and storage version are emitted by the provider only at
  `Debug` level.

## Search and deletion

- Search defaults to active objects, uses deterministic ordering and restricts
  page sizes to 500.
- Filename search treats `%` and `_` literally.
- Combined business-reference filters must match one and the same reference.
- Logical deletion is serialized with a database row lock. Repeated requests
  for `DeletionRequested` or `Purged` objects do not create another event and
  report `StateChanged = false`.
