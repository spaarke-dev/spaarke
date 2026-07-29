# Email Workspace (Outlook-style) — `email-communication-solution-r5`

> **Status**: Initialized 2026-07-27 via `/project-pipeline` · **Round**: r5 (Communication surface line; successor to `email-communication-solution-r4`)
> **Owner**: ralph.schroeder · **Branch**: `work/email-communication-solution-r5`
> **Type**: UI/surface (dual-use Pattern D) + 1 new BFF endpoint + 1 config change · **Hot-path**: BFF=Y · SpaarkeAi=Y · CI=N · Skills=N

## What this is

A dedicated Outlook-style **Email** surface inside Spaarke: a flat card list of Email-type `sprk_communication` records on the left (driven by Dataverse saved views) and a reading pane on the right that shows the selected email **as the sender sent it** — full quoted reply/forward history and inline images — with reply/reply-all/forward/compose via the canonical composer, plus inline attachment and record-association review.

Ships in two mounts from one shared React 19 component (dual-use **Pattern D**): a SpaarkeAi **workspace widget** (`email`) and a standalone **code page**. The OOB `sprk_communication` model-driven form and its PCFs are kept; r5 extracts the controls' **React-agnostic logic** into shared cores and adds a reusable `.eml`→HTML render capability.

## Graduation Criteria

The project is **Complete** when all 10 success criteria in [`spec.md`](spec.md#success-criteria) pass, in particular:
1. Surface opens as widget AND code page, rendering identically (dual-mount parity).
2. Left list shows only Email-type records for the selected view; view switch re-populates.
3. Email with quoted history renders the full chain **as sent** (inline images) from the `.eml`.
4. Archive-less email degrades to `sprk_body` + note (no error).
5. Reply/Reply All/Forward/New open the canonical composer with correct prefill; send via existing path.
6. Association review interactive + additive; reply shows inherited parent regarding.
7. Malicious HTML executes no script; `.eml` in sandboxed iframe.
8. OOB form + 4 PCFs regression-free after Layer-1 extraction.
9. BFF publish ≤60 MB, delta reported; `eml-render` endpoint tested.
10. No React-version cast on new code-page work; Layer-1 cores React-agnostic.

## Documents

| File | Purpose |
|---|---|
| [`spec.md`](spec.md) | AI-optimized implementation spec (19 FR / 7 NFR) — permanent reference |
| [`design.md`](design.md) | Use-case → design narrative (6 lenses, 7 Explore audits) |
| [`plan.md`](plan.md) | Phases, WBS, critical path, parallel groups, discovered resources |
| [`CLAUDE.md`](CLAUDE.md) | AI context — load first for any task |
| [`current-task.md`](current-task.md) | Active task state (context recovery) |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | Task tracker + dependency/parallel groups |

## Scope at a glance

**In**: dual-use `email` surface; flat card list + view selector; reading pane rendering `.eml` as sent in a sandboxed iframe (degrade to `sprk_body`); `GET /api/documents/{id}/eml-render` (MimeKit + sanitize); full-width toolbar; canonical compose reuse; reused header/attachments/associations/tracking; shared hardened `sanitizeEmailHtml` + retrofit; two-layer control extraction; archiving default-on.

**Out**: replacing the OOB form/PCFs; chat/Teams/SMS/ACS; Spaarke-side thread reconstruction; thread-entity association inheritance (deferred server round); `.eml` backfill; new send/archive/attachment endpoints; new AI Actions/Bindings; remote-image privacy gate (fast-follow); bespoke server `.eml` render cache (fast-follow).

## Coordination (hot-path)

This project is in the **most-contested BFF cluster**. Before any BFF or shared-lib PR, run `/conflict-check`. Key overlaps:
- `Services/Communication/IncomingCommunicationProcessor.cs` (FR-17) — shared with `spaarke-notification-spine-r1`, `messaging-communication-app-r1/r2/r3`, `email-communication-solution-r4`.
- `@spaarke/communication-components` / `@spaarke/ui-components` — shared with `messaging-r2/r3`.
- Predecessor `email-communication-solution-r4` (EmailComposer/send engine/PCF surface) should merge before r5 lands.

See [`../INDEX.md`](../INDEX.md) for the full registry.

## How to execute

Say **"continue"** or **"work on task 001"** — this invokes the `task-execute` skill (see [`CLAUDE.md`](CLAUDE.md) §Task Execution Protocol). Never implement POML tasks manually.
