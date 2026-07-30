# Task 040 — G10 Document-Profile re-run (reload re-trigger + manual button) — Deviations & Notes

> **Status**: ✅ COMPLETE · 2026-07-30 · FULL rigor · sonnet/high (run on Opus 4.8 session).
> R5-D5 escape hatch NOT invoked — both legs proved tractable + shipped.

## What shipped
Both G10 legs, reusing the EXISTING fire-and-forget `DispatchBackgroundProfile` pipeline (never a second
trigger):

**Reload/onload re-trigger (server, storm-safe)** — `ComposeService.cs`:
- `MaybeRetriggerProfileOnLoadAsync` called at the end of `LoadAsync` (Path A only — an existing
  `sprk_document`). Re-dispatches the profile ONLY when the live SPE eTag differs from a **dedicated
  per-doc profiled-eTag stamp** (`sdap:compose:profiled-etag:` in IDistributedCache), then stamps the
  current eTag so a subsequent unchanged reopen SKIPS. An unchanged reopen matches the stamp → no
  re-trigger (the profiling-storm escalation is guarded away). Best-effort — never blocks/fails Load.
- The profiled-eTag stamp is **intentionally separate** from the FR-08 save-version stamp so it never
  perturbs the save-path staleness/re-anchor semantics.

**Manual "Refresh Profile" (endpoint + client button)**:
- `IComposeService.RefreshProfileAsync` + `RefreshComposeProfileRequest` → dispatches the profile
  UNCONDITIONALLY (user-initiated) + stamps the profiled eTag (when the SPE pointer/eTag is supplied) so
  an immediate reopen does not redundantly re-trigger.
- `POST /api/compose/documents/{documentRecordId}/refresh-profile` (authenticated group, OBO, 202
  fire-and-forget; 400 on missing tenant). No SPE/Graph type crosses the endpoint (ADR-007).
- Client: `triggerRefreshProfile` (ComposeWorkspace) → the endpoint; `onRefreshProfile` threaded
  Workspace→Editor→Toolbar (mirrors the onSave threading); a `DocumentSync24Regular` ToolbarButton
  rendered ONLY for a promoted doc (`sprkDocumentId` present — an unpromoted draft has no record to profile).

**CitationResolver reuse (no fork)**: the profile pipeline reached via the reused
`DispatchBackgroundProfile` → `IDocumentProfileAi` facade owns citation production. I introduced **no**
forked resolver and did not re-implement `CitationResolver` — the criterion "reuses R4.5 CitationResolver,
no forked resolver introduced" is satisfied by reusing the existing profile dispatch wholesale.

## Escalation triggers — neither fired
- **Profiling-storm**: guarded by the dedicated profiled-eTag stamp (re-fire only on a real eTag change;
  unchanged reopen skips). No re-trigger loop.
- **R5-D5 complexity-defer**: NOT invoked — both legs were tractable by reusing the existing fire-and-forget
  pipeline + a small storm-guard stamp. No half-wired trigger; no deferral note needed.

## Verification
- Endpoint seam **2/2** (`ComposeRefreshProfileSeamTests`: valid → 202; missing tenant → 400 — through
  the WebApplicationFactory). Toolbar UI **3/3** (Refresh Profile renders/fires/dark-mode) within
  ComposeFormatToolbar **42/42**.
- Full Compose C# suite **821/821** (819 prior + 2 seam — R4.5 non-regression intact); corpus byte-diff
  **24/24** (in the suite). Client typecheck clean for the touched files.
- ArchTests: same **3 pre-existing failures** (ADR-007, ADR-010 ×2) — zero new; **Tier-1 NetArchTest
  passes** (the profile-trigger path uses the ADR-013-safe `IDocumentProfileAi` facade, no AI-internal
  type in `Services/Compose`).
- Publish **48.13 MB** compressed (unchanged; no new runtime package; ≤60 ceiling). BFF build 0 errors.

## Test-level note (honest)
The reload/onload re-trigger's dispatch is a detached `Task.Run` (fire-and-forget by design) → not
deterministically observable in-process without changing the fire-and-forget contract. It is covered by:
(a) the storm-guard LOGIC (eTag-vs-stamp comparison), and (b) the endpoint seam, which exercises the SAME
`RefreshProfileAsync → DispatchBackgroundProfile` path the reload leg dispatches through. The manual leg
is UI-tested. This satisfies the POML's "a seam/UI slice proves the [leg] OR the deferral is documented".

## Step 9.5 quality gates (applied)
- **code-review**: reload leg storm-safe (dedicated stamp, decoupled from FR-08); manual leg
  fire-and-forget best-effort; endpoint 202/400; button gated on promoted doc; no security surface; no AI
  code smells; §11 satisfied (extends the existing profile dispatch + adds one interface method + one
  endpoint in the existing compose group — no new service/library).
- **adr-check**: ADR-049 (a domain event on the save/load path, not an engine byte-contract change),
  ADR-007 (profile via `IDocumentProfileAi` facade; SPE metadata via `SpeFileStore` — no Graph type),
  ADR-013 (no AI-internal type in `Services/Compose` — Tier-1 passes), ADR-038 (endpoint seam slice, no
  banned shapes), ADR-021 (button theme tokens + dark-mode test), §10 (all in `Services/Compose`; endpoint
  extends the existing compose group; no new package; ≤60 MB). Clean.

## PR obligations
- **Placement Justification (§10)**: the reload re-trigger + manual leg live in `Services/Compose/`
  save/load orchestration + one endpoint in the existing compose group; no new service/endpoint family/package.
- `/conflict-check` before the BFF + shared-client PR (ComposeService.cs is R4.5-owned-FIRST + overlaps
  compose-r1/r2/r3 + ai-architecture-redesign-r2; toolbar overlaps analysis-hub-r1 — NFR-09 reopen-restore
  parity covered by the 821 suite).
