{{- define "cirrus.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- define "cirrus.env" -}}
- name: ConnectionStrings__ArchiveDatabase
  valueFrom: { secretKeyRef: { name: {{ include "cirrus.secretName" . }}, key: connection-string } }
- name: S3__AccessKey
  valueFrom: { secretKeyRef: { name: {{ include "cirrus.secretName" . }}, key: s3-access-key } }
- name: S3__SecretKey
  valueFrom: { secretKeyRef: { name: {{ include "cirrus.secretName" . }}, key: s3-secret-key } }
{{- end }}
{{- define "cirrus.fullname" -}}
{{- default (printf "%s-%s" .Release.Name (include "cirrus.name" .)) .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- define "cirrus.labels" -}}
app.kubernetes.io/name: {{ include "cirrus.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
{{- end }}
{{- define "cirrus.secretName" -}}
{{- default (printf "%s-secret" (include "cirrus.fullname" .)) .Values.secrets.existingSecret }}
{{- end }}
