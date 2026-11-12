# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-12 (mcp-r1, checkpoint mid master-merge)

## ⚠️ IN FLIGHT (2026-11-12) — read first
- **Branch updated from master, NOT pushed:** local merge commit `d269f0abe7` (origin/master into `work/spaarkeai-mcp-bff-api-r1`, 1229 commits, no conflicts). Pre-merge HEAD `8fd598c2a0`. BFF build 0/0; ArchTests 2491/2491.
- **5 failing tests, all `SignalRoutesContractBaselineTests` (decision.200.recorded, decision.200.replayed, decision.500.step-failed, decision.500.not-resumable, decision.500.record-write-failed).** Cause: master commit `90e8571c20` (ontology task 176 review round 2 B) added `warning` to `DecisionCommitStep`, so each step now serialises `"warning":null`. Baselines exist only on this branch (`tests/integration/contract/Api/Signals/Baselines/`, task 011 `b9d8f19b47`). **Next:** confirm every diff is only the added `"warning":null` per step, then re-record those 5 baselines (a master contract change, not ours: FR-09 "byte-for-byte" guards OUR rework), re-run the filtered tests (`Mcp|Signals|Decision|RouteAuthorization|Worklist`: 5688 pass + these 5), commit, push.
- **Auto-merge ON (merge commits)** for #2714 (instruction paths), #2720 (broken links, review fixes f9757c3eb5), #2692 (client-tests). All reviewed (adversarial reviewers: safe). After they merge: sync main checkout ff-only (merge-to-master A.6), remove `C:\wtpaths` via `Remove-WorktreeSafe.ps1`, tell code-reuse-r1.
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 002: A10 connector client registration (dev). Phase 0. Pipeline Step 5, option C. Phase A (011, 013, 014, 015, 016, 017, 018) is complete. |
| **Step** | (a) ✅ · (b) ✅ (registration re-created 2026-11-10, Base URL = full MCP URL) · (c) ✅ connector "Spaarke MCP" linked 2026-11-10; first authenticated MCP handshake 18:59Z · (d) **T5 pending** |
| **Status** | blocked on owner: T5 needs a licensed **non-admin** user (the 2026-11-10 attempt was the owner's admin account, a pre-check only; Copilot searched the Dataverse MCP instead, no `tools/call` reached the BFF) |
| **Next Action** | Owner runs T5: a licensed non-admin user, in Copilot Chat with **Spaarke MCP** explicitly selected, asks it to call `spike_whoami`. Verify in App Insights that a `tools/call` reached `/mcp/legal-operations` and the caller is that user (OBO). Record in `notes/phase0-registration-facts.md` (d). If sign-in fails with `AADSTS50011`, fix the Web redirect to the URI the error names. Then task 003; tell `uac-r2` before the 003 window. |

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
- **Dev BFF now runs `b3ca99cad5`** (2026-11-11, deployed by uac-r2): a LOCAL, never-pushed merge of origin/master `ddae82fa90` + spike tip `755987b385` (unchanged). One conflict, the `RouteAuthorizationGuardTests.Ledger.cs` endpoint-file count, resolved to 153 (master 152 + the spike file). uac-r2 reports ArchTests 2431/2431, /healthz 200, anonymous MCP POST 401. Earlier the same day: `dc4f28ded8` (master `df93d2029c` + spike). Spike tip `755987b385` itself was first deployed 2026-11-10 by mcp-r1. **Standing rule (confirmed to uac-r2 2026-11-11):** every uac-r2 dev deploy is a local merge with the spike tip until we declare Phase 0 done. The spike stays off master: master→spike merges only; PR #2615 is a draft and is never merged.

## Next Actions (after 002)
1. 003: Phase 0 live tests (T0–T14, E1–E5) into `notes/phase0-results.md`. The size-test widgets (1/3/6 MB) are not built yet and would need a new spike SHA. Notify `uac-r2` before the window and when Phase 0 is done.
2. Phase B: 020 → 021 → 022 (gated on 018, satisfied) / 023 / 024 → 010 → 025; then widgets 030–032; registration 040–043; docs 050–052; 090.
3. Owed: the dev connector **secret rotation runbook** (`docs/guides/`, with task 052) and the **ADR-028 A10 follow-up note** (the Dev Portal requires a secret; public client is not possible).

## Open Owner Questions / Blockers
- 002 (d): T5 with a licensed non-admin user (owner).
- The owner reviews PR #2622 (Compose fix, ISS-002 / #2620, ready since 2026-11-08).
- Billing review in Copilot (next slice) vs EB-D-194: does not block v1.

## Peer obligations
- **Ontology** (desktop session `ontology-r1` since 2026-11-09): APPROVED 018 (their D-212). **Ping them when PR #2587 merges to master**; their DI-143 (#2630) builds on `WorkItemPrivilege`. Not urgent for them.
- **uac-r2** (`uac-r2`, desktop): orchestrates dev deploys; the owner gives the per-SHA go there.
