# Core test suite

The project tests only `MTSM.Cirrus.Core`. It contains fast contract tests and
PostgreSQL integration tests for behavior that cannot be represented faithfully
with EF InMemory or SQLite.

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
database, seeds reference data and removes the schema after the run. Never point
the variable at a database containing data that must be retained.

When `CI=true`, missing or unsafe PostgreSQL configuration fails the integration
tests instead of skipping them.

## Coverage

```powershell
dotnet test MTSM.Cirrus.Core.Tests/MTSM.Cirrus.Core.Tests.csproj `
  --configuration Release `
  --collect:"XPlat Code Coverage"
```
