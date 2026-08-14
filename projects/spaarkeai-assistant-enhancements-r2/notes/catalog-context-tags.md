# Task 021 — Context-type tag column + BFF contract + seed (Option C)

**Date**: 2026-08-05 · **Env**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`) + BFF `spaarke-bff-dev`
**FR**: FR-B2 (context-type tags) + FR-D11 (Reanalyze binding, data half)
**Decision**: §6.5 Path A (owner-approved) — Option C, a first-class column, overriding FR-B2 "no deploy". See `notes/deviations.md`.

## Placement justification (BFF §10)
Additive field on the existing `Binding` contract + existing `Columns` ColumnSet. **No** new endpoint, DI registration, package, or background work. The column mirrors the existing `sprk_surfaces` CSV pattern end-to-end (schema → `Columns` array → `MapBinding` via the generic `ParseSurfaces` splitter → `Binding.ContextTypeTags`). BFF stays the single backend; no AI-internal type leaked. Publish size unchanged (see below).

## Schema
- **Column**: `sprk_contexttypetags` on `sprk_playbookconsumer` — `StringAttributeMetadata`, MaxLength 200, RequiredLevel None. Unmanaged (Web API creation, ADR-022).
- **Script**: `scripts/Add-BindingContextTypeTagsColumn.ps1` (idempotent, `Test-AttributeExists`-guarded; publishes + verifies). Mirrors `scripts/Create-PlaybookTriggerFields.ps1`.
- **Vocabulary** (closed set, aligned to client `WidgetContextType`, task 020): `email | document | compose-doc | matter-grid | dashboard | calendar`. Stored as trimmed CSV. Empty = relevant to ANY context.
- Verified present via MCP `describe` + Web API `$select=sprk_contexttypetags` round-trip.

## BFF contract
- `Binding.cs`: new `public IReadOnlyList<string> ContextTypeTags { get; init; } = Array.Empty<string>();` (after `Surfaces`).
- `ConsumerRoutingService.cs`: `"sprk_contexttypetags"` added to the single `Columns` array (feeds all 5 query paths); `MapBinding` sets `ContextTypeTags = ParseSurfaces(...)` (null/empty → empty list; legacy-row tolerant, never throws).
- **No selection/filter logic added** — ADR-039 compliant (deterministic pre-filter DATA only; the active-tab filter that consumes this is client **task 022**).
- Test: `ConsumerRoutingServiceBindingContractTests` — populated-row + legacy-defaults assertions added for `ContextTypeTags` (25/25 pass). Contract test's "maps every field" invariant restored.

## Seed (13 records)
**Reanalyze Binding (created)** — `9c29b488-4291-f111-b8db-7ced8ddc4a05`
- consumer-type `chat-summarize`, consumer-code `reanalyze`, tag `document`, disposition Informational.
- Reuses the existing summarize Action `eeb05bfd-1260-f111-ab0b-70a8a59455f4` (same pattern as the matter-summary work-product leg) — **no new Action authored** (FR-D11 "reuses the document's playbook"; escalation trigger did not fire).

**`document`** (3 existing tagged): `651194cd…` Chat Summarize · `ed92d769…` Agreement Classify · `121194cd…` AI Summary (Document Profile).

**`compose-doc`** (9 existing tagged): `30374f2f` Compare to Playbook · `32374f2f` Defined Terms · `b1c4d38a` Draft Alternative · `05a7132f` Explain Clause · `65549e51` Make Concise · `b11aaf8b` Revise Document · `904f2d53` Rewrite by Instruction · `986799ad` Whole-Doc Summarize · `0aa7132f` Summarize Word Changes.

**Intentionally left untagged** (empty = any context): `create-*` (matter/project/task/todo — cross-context surface launchers), `compose-draft-document` (general "draft a new doc" entrypoint, usable from any context), `chat-classify` / `document-profile` (event-rule members, not proactive chips), `daily-briefing-*` (scheduler-only), `matter-summary` work-product leg (matter-hosted save). Analysts can extend tags anytime with **no deploy** (the point of the column).

> Seeding is a correct starter set, not exhaustive. Task 022's dev-visible trace (FR-B6/024) will reveal whether more tagging is warranted; analysts adjust in-place.

## Deploy
- BFF `Deploy-BffApi.ps1` → `spaarke-bff-dev`: package **48.25 MB** (Δ **0.00 MB** vs ~48.25 baseline; ≪ 60 MB ceiling — no §10 escalation), 4/4 SHA-256 hash-verified, `/healthz` 200.
- Column name confirmed correct against live Dataverse (the `$select` query uses the same read path as the BFF `RetrieveMultiple` ColumnSet), so the live ColumnSet change is safe.

## 021/022 boundary
021 = **carrier + data** (column, `Binding.ContextTypeTags`, seed, Reanalyze row). The active-tab candidate **filter** (chips scoped to the focused tab's `contextType` against `ContextTypeTags`) is **task 022** (opus/xhigh, client proactive turn via `useConsumerChips`/ConversationPane). Deferred here per §11 (021 builds no selection surface it doesn't itself consume).
