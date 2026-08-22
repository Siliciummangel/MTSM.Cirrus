# Cirrus releases

Cirrus uses Semantic Versioning, Conventional Commits, automated container
builds, and a reviewed release pull request. The current application version is
stored in [`version.txt`](../version.txt) and is applied to every .NET project
through [`Directory.Build.props`](../Directory.Build.props).

## Versions and Git tags

Application releases use `vMAJOR.MINOR.PATCH` Git tags, for example `v0.1.0`.
Prereleases use canonical SemVer suffixes such as `v1.0.0-rc.1`. Release Please
normally determines the next version from Conventional Commits:

| Commit | Version change |
|---|---|
| `fix(api): ...` | Patch |
| `feat(api): ...` | Minor |
| `feat(api)!: ...` or `BREAKING CHANGE:` | Major |

Before `1.0.0`, fixes increase Patch. Features and breaking changes increase
Minor because the public contract is still evolving. Version `1.0.0` is created
only by an intentional release decision once Cirrus is considered production
ready. Any intentional version selection can be made in a commit body:

```text
chore(release): prepare 0.5.0

Release-As: 0.5.0
```

## Container images

The public Linux AMD64 images are:

```text
ghcr.io/siliciummangel/mtsm-cirrus-api
ghcr.io/siliciummangel/mtsm-cirrus-worker
ghcr.io/siliciummangel/mtsm-cirrus-migrate
ghcr.io/siliciummangel/mtsm-cirrus-admin
```

The Dockerfiles do not contain a fallback application version. Automated builds
must pass `VERSION` explicitly. The local Compose environment does this with
`0.0.0-local` unless `CIRRUS_VERSION` is set, preventing a local image from being
mistaken for an official release.

An ordinary successful `main` build publishes `latest` and `sha-<commit>`.
`latest` is the current development state and must not be used for production
deployments.

A stable `v1.2.3` release publishes `1.2.3`, `1.2`, `1`, `stable`, and the SHA
tag. Exact version tags such as `1.2.3` and SHA tags are immutable. `stable`, major, minor, and
`latest` are moving tags. A prerelease only publishes its exact prerelease tag
and SHA tag; it does not move stable tags.

## Automated checks

Pull requests build all four images without publishing them. Each image must
pass:

- OCI label and version verification
- role-specific smoke testing
- Dockerfile configuration scanning
- scanning for fixable High and Critical OS or library vulnerabilities
- migration execution twice against PostgreSQL for idempotency

Published images additionally receive an SPDX JSON SBOM, a signed GitHub build
provenance attestation, and a signed SBOM attestation. The workflow verifies the
published attestations before it succeeds.

## Release pull request

Release Please creates or updates one release pull request from commits after
the configured bootstrap commit. Internal `chore`, `test`, `ci`, `build`,
`refactor`, and `style` commits are hidden from the public changelog.

The release PR is the human approval point. Review at least:

1. Proposed version
2. Generated changelog
3. Database and configuration changes
4. Upgrade and rollback information
5. All CI results

Merging the release PR creates a draft GitHub Release. The release workflow then
builds, tests, scans, publishes, and attests every versioned image. It publishes
the GitHub Release only after every image job succeeds. Failed publication
leaves a draft release for diagnosis and retry.

## Manual release notes

Before merging a release PR, edit
[`docs/release-notes/current.md`](release-notes/current.md) on its branch. Remove
the surrounding comment and add only relevant sections:

- Highlights
- Upgrade notes
- Database migrations
- Rollback notes
- Known issues

Non-comment content is prepended to the generated changelog. Clear or replace
the file after a release to prevent old notes from being reused.

## Database migrations

The container smoke test proves that the migration image can initialize a fresh
database and run again without pending work. The .NET integration suite also
tests the repository's declared previous schema baseline and data preservation.

Every release with an irreversible migration must state one of these outcomes:

```text
Backward-compatible migration
Irreversible migration - database restore required for rollback
```

Irreversible migrations require backup, upgrade, and rollback instructions in
the manual release notes. Do not manually edit the EF migrations history table.

## Verifying an image

Use an immutable version or digest in production. A published attestation can be
verified with GitHub CLI:

```bash
gh attestation verify \
  oci://ghcr.io/siliciummangel/mtsm-cirrus-api:0.1.0 \
  --repo Siliciummangel/MTSM.Cirrus
```

## Repository settings

The release workflow uses the repository `GITHUB_TOKEN`; no long-lived registry
password is stored. Repository Actions settings must grant read/write workflow
permissions and allow Actions to create pull requests. Packages created on the
first run must be checked once for public visibility and repository linkage.

GitHub suppresses additional workflow events for pull requests created with the
built-in token. The source commit has already passed the `main` workflows, and
the release workflow performs its versioned container gates directly before it
publishes the draft release. A GitHub App token can be introduced later if CI is
also required to run independently on the generated release PR itself.
