# U-CB-5 — Key Vault Secret Rotation Cascading to BFF Restart (Customer Communication Template)

> **Purpose**: Plain-text operator-facing template to notify a customer that a Key Vault secret used by the Spaarke BFF is being rotated, and the rotation cascades to a BFF App Service restart (slot-swap) that produces a brief drain window.
> **Applies when**: A KV secret consumed by the BFF is rotated — most commonly the Dataverse S2S client secret, the Graph client secret, or a downstream integration secret. The BFF's in-memory MSAL token cache holds tokens acquired with the OLD secret; a restart is required to pick up the new secret.
> **Owner**: Spaarke Platform Operations (release manager / on-call for scheduled rotations; incident responder for emergency rotations).
> **Delivery format**: Plain-text markdown — copy into the operator's chosen channel. No HTML, no branded styling.
> **Related**: `../version-compatibility-matrix.md` · `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` · `projects/customer-provisioning-orchestration-r1/design.md` §14A.4 U-CB-5 · §7.9 KV pre-check gate · Handler H4 secret-rotation flow

---

## 1. Summary

Spaarke is rotating a Key Vault secret in your environment (`{customerName}` / `{environmentName}`) and applying the rotation via a **BFF App Service restart** (blue-green slot-swap). During the swap window, in-flight BFF requests may briefly retry.

Secret being rotated in this maintenance:

- Secret name: `{secretName}` (e.g. `Dataverse-ClientSecret`, `BFF-API-ClientSecret`, `{integrationSecretName}`)
- Rotation reason: `{rotationReason}` (e.g. "24-month scheduled rotation per H4", "compromise-response emergency rotation", "compliance-driven credential refresh")
- Downstream impact: `{secretConsumerScope}` (e.g. "Dataverse authentication", "Graph app-only calls", "SPE container operations")

**This is a breaking change (U-CB-5)** in the operational sense that the BFF is restarted — during the restart window, tokens held in the BFF's in-memory MSAL cache are discarded and re-acquired against the new secret. If the rotation is mis-configured, BFF loses ability to authenticate to the downstream system.

## 2. Trigger conditions (why you are receiving this)

You are receiving this notice because ONE of the following triggered the rotation:

- **Scheduled rotation** — 24-month cadence per Handler H4 (`{secretName}` last rotated `{lastRotationDate}`, due `{scheduledRotationDate}`).
- **Compromise response** — Spaarke security team has cause to believe the secret may be exposed; immediate rotation required.
- **Compliance mandate** — customer or regulatory requirement (`{complianceContext}`) requires proof of recent rotation.
- **Post-incident hardening** — a related incident (`{incidentReference}`) prompted preventive rotation.

## 3. Customer impact

- **Downtime**: `{expectedDrainWindowSeconds}` seconds during slot-swap (typically 30–120 seconds). End-user visible URL is uninterrupted; in-flight requests may retry.
- **Authentication**: Users signed into Spaarke surfaces are NOT signed out — the customer-facing auth cookies/tokens are separate from the BFF-to-downstream service credentials being rotated.
- **Long-running operations**: Background jobs (playbook runs, document analysis) started before slot-swap are checkpointed via Service Bus + idempotency and resume on the new slot. No customer action required.
- **What could fail**: If the new secret was NOT correctly written to KV before the swap (T1 trap), BFF fails to authenticate to `{secretConsumerScope}` after swap. Spaarke's H4 handler explicitly pre-checks the new secret against a live token call BEFORE swap; the risk is minimal but not zero.

## 4. Timeline

| Milestone | Target date/time (all times {timezone}) |
|---|---|
| This notice sent | `{noticeSentDate}` |
| New secret written to KV (H4 pre-check pass) | `{secretWrittenDate}` |
| Slot-swap START | `{swapStartTime}` |
| Slot-swap COMPLETE (drain window ends) | `{swapCompleteTime}` |
| Post-swap smoke test | `{smokeTestTime}` |
| Old secret marked disabled in KV (grace period start) | `{swapCompleteTime}` |
| Old secret DELETED from KV (grace period end) | `{secretDeletionDate}` (default: `{swapCompleteTime}` + 7 days) |

Grace period: the OLD secret remains in KV as `disabled` for `{gracePeriodDays}` days after swap. This allows fast rollback if the new secret triggers a defect. After the grace period, the old secret is permanently deleted.

**BINDING pre-check gate** (per design.md §7.9): Spaarke NEVER swaps without first confirming (a) the new secret exists in KV, (b) a live token acquisition against the new secret succeeds against `{secretConsumerScope}`, and (c) the target App Service `keyVaultReferenceIdentity` is set correctly.

## 5. Required customer action

For most rotations, no customer action is required — Spaarke owns the KV, the BFF, and the downstream credentials, and executes the rotation end-to-end. Exceptions:

1. **If `{secretName}` is a shared secret** (e.g. an integration key you also use in a non-Spaarke system), Spaarke will coordinate with `{customerContact}` before rotation — the customer must confirm the new secret value has been propagated to your non-Spaarke consumer.
2. **Emergency rotations** — Spaarke will send this notice concurrently with the rotation (not in advance) and confirm impact retrospectively.
3. **Non-standard maintenance windows** — if the target swap window (`{swapStartTime}`) conflicts with a customer-critical operation, reply within `{noticeToWindowLeadHours}` hours to reschedule.

## 6. Confirmation of receipt (required)

Please reply to `{operatorEmail}` (or acknowledge in `{acknowledgementChannel}`) with:

> "`{customerName}` acknowledges receipt of U-CB-5 notice for environment `{environmentName}` dated `{noticeSentDate}`. We are informed that secret `{secretName}` will be rotated with a slot-swap window at `{swapStartTime}` and understand a `{expectedDrainWindowSeconds}`-second drain window may occur. `{ifSharedSecretConfirmationText}`"

For **scheduled rotations**, acknowledgement is best-effort — Spaarke proceeds on the schedule even absent reply, because the security posture of running long past a scheduled rotation exceeds the risk of proceeding without acknowledgement.

For **emergency rotations**, acknowledgement is post-hoc — Spaarke will confirm the rotation was applied in a follow-up communication.

## 7. Rollback semantics

The blue-green slot-swap model provides fast rollback:

1. **Re-swap (fastest)** — if the smoke test after swap fails, Spaarke immediately re-swaps back to the previous slot (which is still running the previous BFF version with the OLD secret still resolvable — the old secret remains in KV as `disabled` for the grace period, and Spaarke's H4 rollback flow re-enables it). Total window: `{expectedDrainWindowSeconds}` additional seconds.
2. **Re-swap + secret restore** — if the OLD secret has already been fully deleted (grace period expired) and rollback is required, Spaarke restores the OLD secret from a soft-delete recovery (KV soft-delete retention: `{kvSoftDeleteDays}` days) then re-swaps. Total window: minutes.
3. **Full rebuild** — if both new AND old secrets are unrecoverable, Spaarke generates a NEW credential end-to-end (in the downstream identity provider), writes it to KV, and proceeds forward as a new rotation. This is not "rollback" — it is forward-recovery. Total window: `{fullRebuildEstimateHours}` hours depending on downstream identity provider.

Full rotation + rollback procedure: `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` → H4 Secret Rotation Flow.

---

*Template last reviewed: 2026-08-17 · Author when editing: Spaarke Platform Operations · Change record: track in git.*
