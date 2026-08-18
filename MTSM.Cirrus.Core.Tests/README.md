# Test suite

The project tests `MTSM.Cirrus.Core` behavior, integrity-check processing in
`MTSM.Cirrus.Worker` and the Entity Framework Core database migration chain. It
contains fast contract tests and PostgreSQL integration tests for behavior that
cannot be represented faithfully with EF InMemory or SQLite.

## Fast tests

Run without external services:

```powershell
dotnet test MTSM.Cirrus.Core.Tests/MTSM.Cirrus.Core.Tests.csproj `
  --configuration Release
```

When PostgreSQL is not configured, integration tests are reported as skipped on
developer machines.

## PostgreSQL integration tests

Provide a dedicated, disposable database through `CIRRUS_TEST_POSTGRES`. For
safety, its database name must be exactly `test`, start with `test_`, or end in
`_test`.

```powershell
$env:CIRRUS_TEST_POSTGRES = `
  'Host=localhost;Port=5432;Database=cirrus_test;Username=cirrus;Password=change-me'

dotnet test MTSM.Cirrus.Core.Tests/MTSM.Cirrus.Core.Tests.csproj `
  --configuration Release
```

The fixture drops and recreates only the `cirrus` schema in that guarded test
database, seeds reference data and removes the schema after the run. Migration
tests additionally rebuild that schema through the committed migration chain
and verify an upgrade from the previous database version while preserving
existing archive data. Never point the variable at a database containing data
that must be retained.

When `CI=true`, missing or unsafe PostgreSQL configuration fails the integration
tests instead of skipping them.

## Worker integration tests

The worker tests use the same guarded PostgreSQL fixture and exercise the real
integrity-check processor. They cover active and expired leases, exclusive
claims by concurrent workers, repeated failures and successful retry resolution,
and enforcement of the configured per-worker concurrency limit.

The fixture resets and seeds the `cirrus` schema before and after every worker
test. Worker tests therefore run in the serialized PostgreSQL test collection
and require no additional configuration beyond `CIRRUS_TEST_POSTGRES`.

## S3-compatible integration tests

The storage integration tests execute the real `S3ObjectStorage` implementation
against an S3-compatible service. Configure a dedicated disposable service with:

```powershell
docker run --rm --name cirrus-seaweedfs `
  --publish 8333:8333 `
  --env AWS_ACCESS_KEY_ID=cirrus-test-access `
  --env AWS_SECRET_ACCESS_KEY=cirrus-test-secret-key `
  --env S3_BUCKET=cirrus-test-bootstrap `
  chrislusf/seaweedfs:4.42
```

Then set the test configuration in a second terminal:

```powershell
$env:CIRRUS_TEST_S3_SERVICE_URL = 'http://127.0.0.1:8333'
$env:CIRRUS_TEST_S3_ACCESS_KEY = 'cirrus-test-access'
$env:CIRRUS_TEST_S3_SECRET_KEY = 'cirrus-test-secret-key'
$env:CIRRUS_TEST_S3_REGION = 'us-east-1'

dotnet test MTSM.Cirrus.Core.Tests/MTSM.Cirrus.Core.Tests.csproj `
  --configuration Release
```

Each test run creates a uniquely named bucket beginning with `cirrus-test-` and
removes its objects and bucket afterwards. Cleanup refuses to delete buckets
outside that namespace. Never configure credentials for storage containing data
that must be retained.

Without complete S3 test configuration, these tests are skipped on developer
machines. When `CI=true`, missing or invalid configuration fails the tests. CI
uses a pinned SeaweedFS container; the tests themselves remain independent of a
specific S3-compatible provider.

## Coverage

```powershell
dotnet test MTSM.Cirrus.Core.Tests/MTSM.Cirrus.Core.Tests.csproj `
  --configuration Release `
  --collect:"XPlat Code Coverage"
```
