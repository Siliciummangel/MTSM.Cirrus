# Kubernetes deployment with Helm

The chart in `deploy/helm/cirrus` installs the Cirrus API, worker, Service and a
controlled migration Job. PostgreSQL and S3-compatible storage remain external
and must be available before installation.

## Prerequisites

- Kubernetes 1.29 or newer
- Helm 3.14 or newer
- A namespace, PostgreSQL database and S3-compatible bucket
- Immutable API, worker and migration images with the same version tag

## Configuration and secrets

Non-sensitive settings live in the chart's ConfigMap. The database connection
string and S3 credentials are read only from a Kubernetes Secret. For
production, create that Secret through the platform secret manager or an
External Secrets operator; never commit a populated values file.

```bash
kubectl create namespace cirrus
kubectl --namespace cirrus create secret generic cirrus-runtime \
  --from-literal=connection-string='Host=postgres;Database=cirrus;Username=cirrus;Password=REPLACE' \
  --from-literal=s3-access-key='REPLACE' \
  --from-literal=s3-secret-key='REPLACE'
```

Create a non-secret `values-production.yaml`:

```yaml
image:
  tag: "0.2.0"
config:
  s3ServiceUrl: "https://objects.example.net"
  allowedHosts: "archive.example.net"
secrets:
  existingSecret: cirrus-runtime
```

The chart can create a Secret from `secrets.*` values for disposable test
clusters, but those values become part of Helm release storage. An existing
Secret is therefore the production default and also lets the pre-install
migration finish before any application Deployment is created.

## Install

```bash
helm upgrade --install cirrus deploy/helm/cirrus \
  --namespace cirrus \
  --values values-production.yaml \
  --wait --timeout 10m
```

The migration is a `pre-install` and `pre-upgrade` Helm hook when an existing
Secret is used. A failed migration fails the Helm operation before API or worker
rollout. The Job is retained for inspection and removed before the next hook;
Kubernetes deletes completed Jobs automatically after 24 hours.

Verify the rollout:

```bash
kubectl --namespace cirrus rollout status deployment/cirrus-cirrus-api
kubectl --namespace cirrus rollout status deployment/cirrus-cirrus-worker
kubectl --namespace cirrus port-forward service/cirrus-cirrus-api 8080:80
curl --fail http://127.0.0.1:8080/health/live
curl --fail http://127.0.0.1:8080/health/ready
```

The API startup probe gives initialization up to 60 seconds. Liveness checks
only the process, while readiness checks PostgreSQL and removes unhealthy Pods
from the Service. The worker has no HTTP listener: its long-running process is
the health boundary, and Kubernetes restarts the Pod if that process exits.

## Upgrade and rollback

Set the new immutable image tag and run the same `helm upgrade --install`
command. The migration hook completes before Deployments are changed. Read the
release notes and verify backups first.

Application rollback is safe only when the release notes declare the migrated
schema backward compatible:

```bash
helm history cirrus --namespace cirrus
helm rollback cirrus REVISION --namespace cirrus --wait --timeout 10m
```

Helm rollback also invokes the migration hook from the target revision. For an
incompatible database change, restore PostgreSQL from the pre-upgrade backup
and follow the release-specific rollback notes; never edit the EF migration
history manually.

## Render and validate before applying

```bash
helm lint deploy/helm/cirrus --values values-production.yaml
helm template cirrus deploy/helm/cirrus \
  --namespace cirrus --values values-production.yaml > rendered.yaml
kubectl apply --dry-run=server --filename rendered.yaml
```

Ingress or Gateway resources are deliberately environment-specific. Route TLS
traffic to the chart Service and keep database, object storage administration
and health details off the public network.
