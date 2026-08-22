# Cirrus security

Cirrus 1.0 authenticates calling systems through replaceable authentication
providers. The built-in production provider uses API keys. Authentication is
separate from authorization: every provider must produce the same trusted
Cirrus identity containing one tenant, one stable actor and a set of
permissions.

## Security model

- Anonymous archive access is denied by a global authorization policy.
- Every machine identity belongs to exactly one tenant.
- The tenant in the authenticated identity is authoritative.
- A tenant ID in an API route must match the authenticated tenant or Cirrus
  returns `404 Not Found` without accessing the archive service.
- Server-side tenant filters and database constraints remain mandatory.
- Actors are derived from authenticated identities. `X-Actor` is ignored and
  must not be used.
- New authentication providers may be added through ASP.NET Core authentication
  schemes, but must emit the canonical Cirrus claims used by the common
  authorization policies.

## API key format and transport

API keys have this shape:

```text
cirrus_<key-id>.<256-bit-random-secret>
```

Send the key only in the standard authorization header over HTTPS:

```http
Authorization: ApiKey cirrus_<key-id>.<secret>
```

Keys in URLs, query strings, cookies, request bodies or custom headers are not
accepted. Cirrus stores only a SHA-256 verification value of the randomly
generated 256-bit secret and compares verification values in constant time.
The complete key is printed once during creation or rotation and cannot be
retrieved later.

## Machines and permissions

Credentials are separate from machine identities. Rotation therefore keeps a
stable audit actor such as `apikey:machine:42`.

Available permissions:

| Permission | Operations |
|---|---|
| `archive.read` | Metadata, HEAD, search, download and integrity status |
| `archive.write` | Archive upload |
| `archive.delete` | Logical deletion request |
| `archive.verify` | Manual integrity verification |

Permissions do not imply one another.

## Bootstrap and administration

Apply database migrations before creating identities. Configure the admin CLI
through `ConnectionStrings__ArchiveDatabase` or user secrets; do not place a
connection string containing a password in shell history.

```powershell
$env:ConnectionStrings__ArchiveDatabase = '<administrative connection string>'

dotnet run --project MTSM.Cirrus.Admin -- machine create `
  --tenant 1 `
  --name shipment-importer `
  --permission archive.read `
  --permission archive.write

dotnet run --project MTSM.Cirrus.Admin -- api-key create `
  --tenant 1 `
  --machine shipment-importer
```

Store the displayed key immediately in the calling system's secret store.

Other commands:

```text
machine list [--tenant ID]
machine disable --tenant ID --machine NAME
machine grant --tenant ID --machine NAME --permission PERMISSION
machine revoke --tenant ID --machine NAME --permission PERMISSION
api-key list --tenant ID --machine NAME
api-key rotate --key-id ID [--expires-at ISO-8601]
api-key revoke --key-id ID
```

Rotation immediately revokes the old key. Create a second key first when a
deployment requires an overlap period. Revocation and machine disabling are
idempotent. Credential-management changes are written to
`security_audit_event`.

## HTTP behavior

- Missing or invalid credential: `401 Unauthorized`
- Authenticated identity without the required permission: `403 Forbidden`
- Authenticated identity addressing another tenant: `404 Not Found`
- Rate limit exceeded: `429 Too Many Requests`

Authentication responses never reveal whether a key ID exists, a secret was
wrong, a credential expired or an identity was disabled.

## Health and API documentation

- `/health/live` is anonymous and intentionally contains no dependency detail.
- `/health/ready` is anonymous and intentionally returns only the aggregate
  dependency state needed by the Kubernetes readiness probe.
- OpenAPI and Scalar are only mapped in the Development environment.

Do not add diagnostic details to anonymous health responses. Restrict public
access to health routes at the Ingress or Gateway where practical.

## Operational requirements

- Terminate TLS either in Cirrus or at a trusted reverse proxy.
- Never log the `Authorization` header.
- Configure proxies to redact API keys and to reject direct access around the
  trusted TLS path.
- Back up the credential and audit tables with the archive database.
- Revoke unused keys and disable unused machine identities.
- Give each calling system its own identity; do not share keys.
- Grant only the permissions required by that system.
- Treat the database connection used by `MTSM.Cirrus.Admin` as an administrative
  security boundary.

## Adding another authentication provider

A future provider, such as JWT bearer or mutual TLS, must validate its own
credential and emit these canonical values:

- stable subject,
- one Cirrus tenant ID,
- trusted actor,
- provider name,
- known Cirrus permissions.

It must fail closed when any required value is missing or ambiguous. Tenant and
permission enforcement remain in the shared authorization layer; providers must
not implement alternative endpoint rules. Cirrus 1.0 ships no additional
provider and performs no dynamic plugin loading.

## Testing expectations

Security changes must cover at least:

- anonymous access,
- invalid, expired and revoked credentials,
- every endpoint permission,
- route-tenant mismatch,
- cross-tenant object IDs and searches,
- actor derivation,
- key rotation and revocation,
- absence of secrets in responses and logs,
- migration from the supported previous database version.
