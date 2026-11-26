# Deferred work and found issues — spaarke-intake-pipeline-r1

Every row here has a GitHub issue, and every issue filed for this project has a row here.

| Date | Issue | Found by | What | Owner |
|---|---|---|---|---|
| 2026-11-25 | #3162 | design §10.1 item 5b | Email joins the ingestor seam (out of scope for r1) | — |
| 2026-11-25 | [#3200](https://github.com/spaarke-dev/spaarke/issues/3200) | task 003 | `build-provisioning-sidecar.yml`: unfixed Trivy findings never reach the Security tab, though its header says they do (`--ignore-unfixed` filters SARIF too). Fix = the two-scan split already used in `build-intake-engine-image.yml` | provisioning sidecar |
| 2026-11-25 | [#3210](https://github.com/spaarke-dev/spaarke/issues/3210) | owner question ("how do we ensure it is part of provisioning?") | Provisioning never deploys the intake engine today (H2a passes no intake params). Owed: H2a params + optional outputs, AcrPull on the platform ACR for the engine identity, H4b `ServiceBus__IntakeEngineCompletionQueueName`, `Microsoft.App` in subscription readiness, H13 least-privilege probe, Model 2 image import. Sent to session `provisioning-r1` 2026-11-25. **Gates task 008's dev deploy only if dev is provisioned through the control plane**; otherwise 008 deploys by hand with the same parameters | customer-provisioning-orchestration-r1 |
