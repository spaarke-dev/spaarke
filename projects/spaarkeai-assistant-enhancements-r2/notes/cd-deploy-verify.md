# Deploy + Verify — Workstreams D + C (tasks 039 + 043, batched)

**Date**: 2026-08-06
**Target**: Azure `spaarke-bff-dev` (rg-spaarke-dev) + Dataverse `sprk_spaarkeai` web resource (spaarkedev1)
**Deployed from**: `origin/master @ 688e26582` (worktree updated to master first — includes compose-r6 PR #745 co-resident; combined build verified green before deploy)

## What was deployed

| Component | Ships (this project) | Result |
|---|---|---|
| BFF (`spaarke-bff-dev`) | Phase D 030–034 (awaited messages[0], 404-on-missing history, stored title+rename, per-doc TTL, server-side ADR-024 regarding write) + Phase C 041/041b (`WorkspaceTabVisibleState.Email` + `EmailTabWidgetData` carrier + `TryDeriveVisibleState`) | ✅ `Deploy-BffApi.ps1`; Release build; package **48.41 MB** (< 60 ceiling); **4/4 critical files SHA-256 verified**; `/healthz` 200 |
| SpaarkeAi code page (`sprk_spaarkeai`) | Phase D 035/036/037/038 (rich restore, attachment rehydrate, HistoryOverlay rebuild, Reanalyze chip) + Phase C 040/042a-c/042b/042c-fr-c4 (email carrier + producer + getVisibleState + FR-C4 SprkChat send seam + email-summarize chip + focus-stamp fix) | ✅ `Deploy-SpaarkeAi.ps1`; web resource `5206a442-3451-f111-bec7-7ced8d1dc988` updated + published; bundle **5186 KB** |

Shared libs rebuilt before the code-page bundle: `@spaarke/ui-components` 2.4.0 (FR-C4 seam), `Spaarke.AI.Widgets` (Phase C carrier/derivations), `Spaarke.Communication.Components` (042b producer), `Spaarke.Compose.Components` (DI-02 flush; DI-02 verified surviving compose-r6's rewrite).

## Automated smoke checks (done)
- BFF: `/healthz` 200, `/ping` 200, `POST /api/ai/chat/sessions/{id}/suggest` → **401** (route registered + auth-gated, complete package — not 404).
- Code-page bundle: `"Summarize this email"` present (FR-C4 chip reached bundle); `pendingOutboundMessage`/`onOutboundConsumed` present (FR-C4 SprkChat seam); `"Set related record"`/`"Rename"` present (FR-D HistoryOverlay).

## Manual verification — OWNER (pending)
Same handoff pattern as A/B. In the SpaarkeAI workspace on spaarkedev1:

**Phase C (email visibility):**
1. Open an **email** tab, focus it, ask the Assistant about it → it states the email's **subject/sender/date** (FR-C1 — server-derived from the persisted `EmailTabWidgetData`).
2. On the focused email tab, click the **"Summarize this email"** chip → the Assistant fetches the full body on-demand (one turn) and summarizes it (FR-C4). Confirm the full body is NOT re-injected on the next unrelated turn.
3. Browse to a different email within the same tab, then summarize → confirm it targets the NEWLY selected email (focus-stamp re-broadcast fix).

**Phase D (history & true resume):**
4. Reopen a History session → chat + tabs + document + attachment chip + redline all restore (rich path).
5. First turn survives a simulated Redis eviction; History rows show title + preview + tab-summary, rename/delete work, no up-arrow.
6. "Set related record" files an analysis to a matter's Analyses tab; resumable after >90 days (filed → ttl=-1).

Report back and we'll mark D+C verification closed (as with A/B).

## Note
compose-r6 (PR #745) merged to master just before this deploy and had already co-deployed its own BFF + code page (UAT passed on a real Corteva NDA per its commit log). This deploy is from the combined master, so it (re)ships the current Compose subsystem alongside this project's Phase C/D additions — expected, master is the source of truth.
