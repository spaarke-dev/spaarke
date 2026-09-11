# Spaarke Office Add-in (Word + Outlook) — r1

> **Portfolio**: [Project #945](https://github.com/spaarke-dev/spaarke/issues/945) · Parent [Epic #424 — DOCUMENT INTELLIGENCE](https://github.com/spaarke-dev/spaarke/issues/424) · [Board](https://github.com/users/spaarke-dev/projects/2)

> **Status**: Initialized (tasks generated, not started)
> **Branch**: `work/spaarkeai-word-add-in-r1`
> **Created**: 2026-09-04

## Quick Links

- [`spec.md`](spec.md) — AI-optimized specification (the contract)
- [`design.md`](design.md) — original design + owner decisions
- [`plan.md`](plan.md) — implementation plan and WBS
- [`CLAUDE.md`](CLAUDE.md) — AI context for this project
- [`current-task.md`](current-task.md) — active task state
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker
- [`ADDIN-CONTEXT-FROM-EMAIL-R2.md`](ADDIN-CONTEXT-FROM-EMAIL-R2.md) · [`DEDUP-AND-SAVE-BACK-IDENTITY.md`](DEDUP-AND-SAVE-BACK-IDENTITY.md) — handoffs from `email-communication-intelligence-r2`

## Overview

This project turns the existing save-only Spaarke Office add-in into a genuinely useful Spaarke surface inside Word and Outlook. Users open documents from **any source** — desktop, OneDrive, a DMS, Harvey, Claude — and draft however they like; Spaarke does not participate in the drafting. Spaarke participates at the two moments that matter: **filing the work product correctly**, and **surfacing the matter context** (related record, AI profile, similar documents, to-dos) that makes filing and drafting sensible.

This is a UX and productivity project, not an AI project. It fixes UAT-reported defects in the save flow, adds a tabbed pane, and closes gaps in document identity.

## Problem Statement

The add-in saves a document to Spaarke, and that is all it does. Concretely, as verified on 2026-09-04:

- **The pane cannot tell which document is open.** `word/WordHostAdapter.getItemId()` returns a `Date.now()`-suffixed string that changes on every call. Nothing can be built on top of it — not version-save, not profile display, not a record card, not open-record.
- **Saving an already-filed document creates a second row** rather than a new version. The server hook exists (`SaveRequest.DocumentMetadata.ExistingDocumentId`) but is inert on both sides.
- **Records created from the pane arrive incomplete** — no `sprk_matternumber`, no owner-derived defaults, no mapped fields — because number generation lives client-side inside the Create wizard, not on the server.
- **Word has no tabs.** `App.tsx` gates navigation to Outlook only; Share / Search / Recent are placeholder handlers.
- **Two Word adapters exist** and the untested one is the one that ships; `HostAdapterFactory` has zero call sites.
- **Typecheck is not a usable gate** — a large pre-existing `exactOptionalPropertyTypes` backlog masks new errors.

## Proposed Solution

Six threads, sequenced so each unblocks the next:

1. **De-risk first.** Four Phase-0 spikes gate downstream scope: the `document.url` shape on Word desktop (the keystone for identity), the Office Dialog API for opening records, whether a task pane can reach the Copilot pane, and whether the shipped upload-collision handling is reachable from the add-in's save path at all.
2. **Conditional document identity.** Resolve the open document to a `sprk_document` via `Office.context.document.url` → Graph `/shares/u!{enc}/driveItem` → the `sprk_graphitemid_uk` alternate key, plus a server-side custom-XML-part GUID stamp so a document that leaves Spaarke and returns still self-identifies.
3. **Fix the save flow** per UAT: name defaulting, a Profile section, Generate Profile, version-save with an explicit override, and a related-record card.
4. **Complete record creation server-side** — move numbering, owner assignment and Field Mapping Framework population into a shared service that `QuickCreateAsync` calls. Matter and Project only. Existing wizards untouched.
5. **Surface Spaarke** — a Find tab over the existing content-similarity engine (with the per-row authorization it currently lacks), Add To Do, Send Email, and the Word ribbon commands.
6. **Outlook parity throughout** — capabilities land in `shared/taskpane/` and are gated by host capability, never by host-type branching in views.

## Scope

### In scope

- Conditional document identity + server-side GUID stamp (forward-only)
- Save flow fixes: name defaulting, Profile section, Generate Profile, version-save with override, related-record card, open-record
- Tabbed pane (Save | Find) and a launch affordance for the existing Copilot agent if a mechanism exists
- Find: content-similarity search, gated on index state, **with per-row authorization added**
- Server-side record-creation completeness (Matter + Project)
- Add To Do; Send Email via Outlook
- Outlook parity for all shared-tier capabilities
- Housekeeping: Word unified JSON manifest, adapter consolidation, ribbon commands, typecheck debt

### Out of scope

- Competing with legal drafting tools — no tracked-change authoring, redlining, or drafting agent
- MCP / external-tool interop — owned by `spaarkeai-word-native-r1`
- Extending Spaarke AI capability — surfacing only
- Building duplicate detection — already shipped; r1 consumes it
- Requiring Spaarke as the document source
- Retroactive stamping of existing documents (**owner decision: forward-only**)
- Migrating the `Create*Wizard` components to the new creation service — evaluated after r1
- Deferred: Send Message modal · Send Email via Spaarke email client · Event task · "+More" fields on create · Tier-2 semantic near-duplicate detection

## Graduation Criteria

- [ ] A Spaarke-sourced document opened in Word desktop resolves to the correct `sprk_document` and matter
- [ ] A desktop-sourced document claims no identity and saves cleanly as new
- [ ] A stamped document, downloaded and re-opened from disk, self-identifies
- [ ] Saving an identified document produces a **version**, not a second `sprk_document` row (asserted by integration test)
- [ ] The "Save as new document" override creates a new record via the **link/graduate** path, asserting `sprk_canonicaldocument` linkage
- [ ] Filename collision behaviour is correct and provably non-destructive, using the mechanism Spike-4 identifies; no new collision logic is introduced
- [ ] Profile fields display for a completed profile and show state for a non-complete one; Generate Profile re-runs and completes
- [ ] A Matter created from the pane has `sprk_matternumber`, owner, and mapped fields populated; Project equivalent per its documented semantics
- [ ] Find returns content-similar results that are **permission-trimmed**, verified by a negative test (a user denied access to a matter sees none of its documents)
- [ ] A To Do created from Word carries document **and** related record as regarding
- [ ] Every shared capability works in both hosts or is explicitly capability-gated
- [ ] `npm run typecheck` is clean in `src/client/office-addins`
- [ ] `POST /api/office/save` has executing contract coverage (currently every test is skipped)
- [ ] BFF publish-size delta measured against a fresh build of master on every BFF-touching task; ≤60 MB compressed

## Coordination

| Project | Overlap | Action |
|---|---|---|
| `spaarkeai-compose-r8` | The other `.docx` write path; `Services/Compose/**` is `parallel-safe:false` | Run `/conflict-check` before any BFF PR. Never delete `docxBridge.ts`. ADR-049 governs. |
| `unified-access-control-r2` | Task 094 (collision pre-flight) · task 095 (two-slot association) · `/api/documents` authorization | Do not duplicate. Sequence `/api/documents` changes with them. |
| `email-communication-intelligence-r2` | Shipped the add-in's current state and the content-dedup layer | Consume; both handoff docs are in this folder. |
| `spaarkeai-word-native-r1` | Owns MCP + the declarative agent | FR-20 only *launches* their agent. |
