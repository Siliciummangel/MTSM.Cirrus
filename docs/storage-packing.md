# Streaming pack uploads

Packing and compaction stream pack content into object storage. Neither path
creates a local temporary pack file. The staging object is read incrementally;
packing compresses individual chunks and compaction verifies and copies existing
compressed chunks. Pack format, chunk hashes and restore semantics are unchanged.

## Buffer bounds

`StreamingPackWriter` passes content to `IObjectStorage.WriteAsync` through a
non-seekable stream. Its queue holds one 64-KiB block, with at most another block
being consumed. A full queue stops the producer until the consumer makes room.
The S3 adapter fills a single 5-MiB multipart buffer and uploads parts
sequentially. The shared array pool can rent an array larger than the requested
5 MiB; that allocation is reused for the entire pack.

Chunking, compression and verification additionally retain a bounded number of
chunk-sized buffers and compression workspace. Chunk sizes are controlled by
`StorageProcessing:MaximumChunkSizeBytes` (8 MiB by default). Compaction uses
the sizes recorded for existing chunks, including chunks written with previous
settings. Concurrent operations each have their own buffers.

Payload memory does not grow with archive, batch or target-pack size. Metadata
such as chunk plans, deduplication maps and compaction locations can grow with
the number of chunks.

`StorageProcessing:TargetPackSizeBytes` remains a target for **stored bytes**
(256 MiB by default), independent of multipart buffer size. Packs split between
chunks using their compressed lengths. A single compressed chunk larger than
the target is written as its own pack. An archive can span any number of packs.

## Completion and failure

1. Finish the multipart upload, then obtain the pack length, SHA-256, version and
   ETag. Only an explicit successful writer completion exposes end-of-stream.
2. Register the complete pack as `Uploaded` in PostgreSQL.
3. Publish manifests, chunk locations and `Committed` pack status in the existing
   database transaction. Compaction swaps locations atomically before marking
   old packs `GarbagePending`.

An upload error wakes a producer waiting for buffer capacity. A producer error
or cancellation disposes the writer, cancels the upload and waits for cleanup.
The S3 adapter attempts to abort an incomplete multipart upload using a separate
30-second cancellation budget, even when the operation token is already
cancelled. Failed abort attempts are logged.

If a later pack or the manifest transaction fails, earlier complete packs can
remain `Uploaded`, without published references. The existing garbage collector
can collect these unreferenced records and objects after the orphan grace period.
The staged archive remains available for retry. Failed compaction leaves the
old locations intact.

PostgreSQL and S3 do not share a transaction. A crash or lost response can leave
an incomplete multipart upload, or a completed S3 object without a database
record. Such storage-side orphans are preferable to publishing references to
missing content. The database-driven garbage collector cannot discover objects
without records; storage-side reconciliation/cleanup is a separate operational
concern. This change does not add an S3 orphan scanner.
