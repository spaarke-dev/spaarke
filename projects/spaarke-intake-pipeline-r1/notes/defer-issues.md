# Deferred work and found issues — spaarke-intake-pipeline-r1

Every row here has a GitHub issue, and every issue filed for this project has a row here.

| Date | Issue | Found by | What | Owner |
|---|---|---|---|---|
| 2026-11-25 | #3162 | design §10.1 item 5b | Email joins the ingestor seam (out of scope for r1) | — |
| 2026-11-25 | [#3200](https://github.com/spaarke-dev/spaarke/issues/3200) | task 003 | `build-provisioning-sidecar.yml`: unfixed Trivy findings never reach the Security tab, though its header says they do (`--ignore-unfixed` filters SARIF too). Fix = the two-scan split already used in `build-intake-engine-image.yml` | provisioning sidecar |
