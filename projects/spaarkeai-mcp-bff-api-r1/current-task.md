# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-09 (by context-handoff — session moving from VS Code `spaarke-wt-spaarkeai-mcp-bff-api-r1-68` to Claude Code desktop session **`mcp-r1`**)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 002: A10 connector client registration (dev). Phase 0. Pipeline Step 5, option C. Phase A (011, 013, 014, 015, 016, 017, 018) is complete. |
| **Step** | (a) ✅ done · (b) **in progress, waiting on the owner** · (c) and (d) pending |
| **Status** | blocked on owner (Developer Portal UI step) |
| **Next Action** | Wait for the owner to send: the Dev Portal **OAuth client registration ID**, the **redirect URI**, and the client-secret **expiry date**. NEVER accept or handle the secret value. Then: (1) `az ad app update --id 04f37fe4-5714-46f9-8796-5363a7a52244 --web-redirect-uris <portal redirect>` (Web platform, not Mobile and desktop) and verify with `az ad app show`; (2) fill (b) in `notes/phase0-registration-facts.md` (registration ID, redirect, expiry, owner); (3) guide (c), the M365 admin center connector "Spaarke" pointing at `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations` with OAuth 2.0 and the (b) registration ID, never Entra SSO; (4) (d) T5 with a licensed non-admin user, then task 003. Tell `uac-r2` before the 003 test window. |

### Files Modified This Session (all committed and pushed)
- `notes/phase0-registration-facts.md`: created; facts for (a) and the secret decision for (b).
- `spec.md`: ADR Tensions row, ADR-028 A10 Path A (dev secret expiry up to 24 months).
- `notes/decisions.md`: ontology approved 018; 002 (b) secret / Path A.
- `CLAUDE.md`, `current-task.md`: peer addresses, standing items.

### Critical Context
- Connector app `Spaarke Copilot Connector – dev`, appId `04f37fe4-5714-46f9-8796-5363a7a52244`:
  - single tenant, **confidential** (public client flows switched off 2026-11-08), no credential created by the agent;
  - admin consent for `user_impersonation`;
  - pre-authorized on the BFF app (10 → 11 entries).
- The Dev Portal **requires a client secret**. The owner confirmed it, creates it in Entra and pastes it straight into the portal. Path A: the dev expiry may run up to 24 months.
- `23610794-67de-4e6c-be61-ff80cc8cbe7f` is the **External Access** Teams app (`src/client/external-spa/appPackage/manifest.json`), NOT ours. Never modify it.

---

## Active Lanes
- None running. Runbook: `notes/drafts/connector-registration-runbook.md` (its step (b) says Mobile and desktop and no secret; that is **superseded**: Web platform plus a secret, see decisions 2026-11-08).
- **Spike on dev:** `6fbb3a7642` (= master `23567b2f93` + spike), live since 2026-11-08 04:00Z. Every `uac-r2` dev deploy carries the spike until we declare Phase 0 done. PR #2615 is never merged.

## Next Actions (after 002)
1. 003: Phase 0 live tests (T0–T14, E1–E5) into `notes/phase0-results.md`. The size-test widgets (1/3/6 MB) are not built yet and would need a new spike SHA. Notify `uac-r2` before the window and when Phase 0 is done.
2. Phase B: 020 → 021 → 022 (gated on 018, satisfied) / 023 / 024 → 010 → 025; then widgets 030–032; registration 040–043; docs 050–052; 090.
3. Owed: the dev connector **secret rotation runbook** (`docs/guides/`, with task 052) and the **ADR-028 A10 follow-up note** (the Dev Portal requires a secret; public client is not possible).

## Open Owner Questions / Blockers
- 002 (b): the owner is completing the Dev Portal registration.
- The owner reviews PR #2622 (Compose fix, ISS-002 / #2620, ready since 2026-11-08).
- Billing review in Copilot (next slice) vs EB-D-194: does not block v1.

## Peer obligations
- **Ontology** (`spaarke-wt-spaarke-ontology-platform-r1-38`): APPROVED 018 (their D-212). **Ping them when PR #2587 merges to master**; their DI-143 (#2630) builds on `WorkItemPrivilege`. Not urgent for them.
- **uac-r2** (`uac-r2`, desktop): orchestrates dev deploys; the owner gives the per-SHA go there.
