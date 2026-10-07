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

---

## Resolved

<!-- Resolved entries move here with the resolution date and commit/PR. -->
