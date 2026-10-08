# Defer / Issue Tracking — customer-provisioning-orchestration-r1

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label customer-provisioning-orchestration-r1` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it.

---

## Open (in priority order)

### ISS-001 — Hand-off owed by UAC-r2: how a new environment gets `sprk_noaccessentry`

| Field | Value |
|---|---|
| **Status** | Open — waiting on unified-access-control-r2 |
| **Urgency** | now (blocks T256 / T218 / T186) |
| **Filed** | 2026-10-07 |
| **Source** | 2026-10-06 conversion review; owner 2026-10-07 asked for an issue + message to UAC-r2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1364 |

**Description**

An environment without `sprk_noaccessentry` denies every read, so a freshly provisioned environment fails closed and
T186 cannot pass. UAC-r2 documents the table only in its own notes; nothing tells provisioning which solution carries
it, when it must exist, what to seed, or how H13 verifies it.

**Entry-points**

- `projects/unified-access-control-r2/` notes `DEPLOY-CHECKLIST.md`, `batch4-integration-steps.md`; `docs/data-model/INDEX.md`
- This project: T256 (INCOMING-145), T218 (solution package), H13 checks

**Suggested fix**

UAC-r2 answers the four questions in the issue (packaging, ordering, verification, upgrade); provisioning turns the
answer into T256 handler work and T218 package content.

### ISS-005 — Deploy-Release Phase 3 imports a 9-solution list that does not exist

| Field | Value |
|---|---|
| **Status** | Scheduled — task 218f (owner 2026-10-08: canonical package only, in every environment) |
| **Urgency** | before the next release to demo |
| **Filed** | 2026-10-07 (found in T218b) |
| **Source** | T218b — H6 left `Deploy-DataverseSolutions.ps1`; `Deploy-Release.ps1` still calls it |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1401 |

**Description**

`Deploy-Release.ps1` Phase 3 (the `deploy-new-release` skill, Spaarke's own environments) runs
`Deploy-DataverseSolutions.ps1`, whose list names 9 solutions — 6 exist nowhere — and whose zip filter
(`:443`) accepts every zip. A release to demo imports nothing useful or fails at the first missing solution. The legacy
`Provision-Customer.ps1` (step 7, `:915`) calls it too; `Load-DemoSampleData.ps1` and `scripts/README.md` point operators
to it.

**Suggested fix**

Package type per Spaarke environment (`config/environments.json`), then point the script at SpaarkeMaster with an
explicit type and fix the filter — or retire Phase 3 in favour of the CI-published SpaarkeMaster zips (T218d).

### ISS-003 — A second environment for the same customer overwrites the first one's BFF app registration

| Field | Value |
|---|---|
| **Status** | Open — not needed while owner D6 holds (one environment per customer) |
| **Urgency** | later (before per-customer staging/dev) |
| **Filed** | 2026-10-07 |
| **Source** | T240a review, verifier pass 2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1376 |

**Description**

The registration is per customer (D-13) but H3 sets its SPA redirect, FIC (`spaarke-uami-trust`) and pre-authorizations
per run, so a second environment for the same customer breaks the first's code-page sign-in and BFF credential.

**Suggested fix**

A per-stamp registration (`spaarke-bff-api-{customerId}-{env}`), or intake refusing a second environment until then.

### ISS-004 — Any Spaarke-tenant user can get any customer BFF token (no stamp-level token gate)

| Field | Value |
|---|---|
| **Status** | Open — investigate (known limit; no cross-customer data path found) |
| **Urgency** | before the first external customer |
| **Filed** | 2026-10-07 |
| **Source** | T240a review, verifier pass 2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1377 |

**Description**

All Model 1 BFF registrations are in Spaarke's tenant; any member or guest can get a token for any customer's BFF. Record
access is still bounded by the stamp's Dataverse/SPE authorization, but record-free endpoints (AI chat) may run on that
customer's OpenAI quota.

**Suggested fix**

`appRoleAssignmentRequired` per BFF service principal with `sprk-{customerId}-users` assigned — after checking the Type-2
external workforce plane (UAC-r2 task 141) and the External Access SPA (T240d).

### ISS-002 — Demo self-registration SPE grant: marker keyed to the demo Dataverse; expiry leaves it behind (UAC-r2 code)

| Field | Value |
|---|---|
| **Status** | Open — owned by unified-access-control-r2 (task 171 code) |
| **Urgency** | next-round (latent: "Demo 1" has no `sprk_specontainerid` today) |
| **Filed** | 2026-10-07 |
| **Source** | Adversarial verifier finding F8 on the 2026-10-07 master merge (`f442915e6`) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1363 |

**Description**

Step 8 records a standing-writer marker keyed by a systemuserid from the demo environment's Dataverse, which the
membership sync cannot resolve. Demo expiry deletes the grant directly (first page only, no `Prefer` header) and
leaves the marker. When the BFF's Dataverse and the demo's are the same, the standing pass sees a stale marker and can
re-grant writer to an expired demo user whose systemuser is still enabled.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Registration/DemoProvisioningService.cs:146`, `:169-185`, `:257-261`
- `src/server/api/Sprk.Bff.Api/Services/Registration/DemoExpirationService.cs:226`, `:308-350`
- `src/server/api/Sprk.Bff.Api/Services/Access/SpeContainerMembershipSync.cs:95-210`

**Suggested fix**

UAC-r2 decides the marker identity (or a demo prefix the standing pass ignores) and expires via
`RemoveMarkedGrantAsync`. Provisioning side (ours): configuring a demo container also needs it owned by the hosting
BFF (`SharePointEmbedded__OwnedContainerIds` or the `spaarkeCustomerId` marker), or Step 8 is skipped (T227d).

### ISS-006 — RAG `DefaultRagModel = Dedicated` reads an index nothing creates

| Field | Value |
|---|---|
| **Status** | Open — BFF code (not this project's surface) |
| **Urgency** | before anyone sets `Analysis__DefaultRagModel` on a stamp |
| **Filed** | 2026-10-08 (found in T235) |
| **Source** | T235 doc sweep — the BYOK guide told operators to set `Dedicated` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1432 |

**Description**

`Shared` (the default) reads `AiSearch:KnowledgeIndexName` (`spaarke-files-index`, created by H2b in the stamp's own
AI Search). `Dedicated` reads `{tenantId}-knowledge`, which nothing creates, so setting it breaks every RAG search that
does not name its index. No stamp sets it today. The two docs that advised it were corrected in T235.

**Suggested fix**

Remove `Dedicated` (needed → build, else remove) or have H2b create its index; optionally rename `Shared`.
`AnalysisOptions.cs` (enum), `KnowledgeDeploymentService.cs:288-320`.

---

## Resolved

<!-- Resolved entries move here with the resolution date and commit/PR. -->
