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

---

## UAT round 1 (2026-08-07) — 2 defects fixed + REDEPLOYED
Owner UAT found: (1)(2) Assistant couldn't see the open email/document tab; (4) history restore didn't load the document + Workspace pane spun (blob:404). Root causes + fixes (commit `fc60c259a`, merged to master `c564d4974`):
- **Fix #1 (server, issues 1+2)** — `BuildWorkspaceStateBlock` now makes the ACTIVE tab (focus-stamp) content-visible regardless of `visibleToAssistant` (completes owner-approved ADR-015 Path A active-tab-as-consent; every user tab defaults false + no UI flips it, so the active tab was filtered out before the FR-A3 hoist). Background non-opted-in tabs stay excluded. +2 tests (36/36).
- **Fix #2 (client, issue 4)** — DocumentViewer was dispatched with a `fetchPreviewUrl` CLOSURE that JSON round-trip strips on restore; now re-derives from stable `documentId` via `GET /api/documents/{id}/preview-url`, treats blob:/data: as absent, resolves null on failure (no infinite spinner). +5 tests (19/19). CAVEAT: the specific `blob:…ERR_FILE_NOT_FOUND` lines could NOT be tied to persisted widgetData — if they persist post-redeploy, capture DevTools (which element holds each blob: src) → residual source likely Compose/MDA chunk. SignalR notifications 401 is a separate pre-existing auth-timing item (not this bug).

**Redeploy 2026-08-07**: BFF 48.45 MB (hash-verified, healthz 200); code page `sprk_spaarkeai` 5187 KB (publish needed one retry — transient `0x80071151` concurrent-publish from another project). From master `c564d4974` (incl. email-communication-intelligence-r2 +40, merged clean). Smoke: healthz 200, `preview-url` re-fetch in bundle. **Owner re-UAT pending** (esp. items 1/2/4 + the blob:404 DevTools check).
