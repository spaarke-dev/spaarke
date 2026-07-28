# Task 022 — Bindings + "Review an NDA" card + NDA classification/routing

**Status**: complete · **Rigor**: FULL · Base HEAD: `95d391a41` (task 023)

## Design decision: fully data-driven classification, zero new BFF code

`chat-classify` (Binding `chat-classify/default`, Action `CLS-CHAT@v1`) already runs as the
`document_uploaded` event rule's ONLY member (`sprk_oneventbindings = [{"event":"document_uploaded",
"order":1}]`) and its own `toolDescription` already documents "nda" as a supported `docType` label
("Classify an uploaded document into a document-type label (nda, contract, invoice, ...)"). No new
classifier, no second intent-detection mechanism (ADR-039), no BFF code change — the classification
layer was already correct for this task; the work is entirely in what the CLIENT does with the
already-produced `docType`.

### Card resolution: reuse the capability-discovery seam, not a hardcoded GUID

`GET /api/ai/capabilities?surface=assistant` (`CapabilityDiscoveryEndpoints.cs`, pre-existing) already
projects the closed catalog's text-projectable Bindings (non-empty `toolDescription`) into
`{bindingId, consumerType, ...}`. `ConversationPane.tsx` already uses this via `useCapabilityDiscovery`
to resolve `compose-revise-document` / `compose-draft-document` / `compose-summarize` bindingIds
client-side (deferred fetch, enabled on-demand). Task 022 extends the SAME hook call's `enabled` flag
and derives `ndaReviewBindingId` the identical way — zero hardcoded GUID, zero new BFF endpoint,
portable across environments (dev/test/prod each seed their own GUID via
`Seed-PlaybookConsumers.ps1`).

### Classification → card: a UI-presentation branch, not a second classifier

`useEventBatch.ts` gained one new optional dep, `onClassified?: (data: EventClassificationData) =>
void`, invoked alongside the existing "File classified" transcript line whenever `event_classification`
arrives. `ConversationPane.handleNdaClassified` checks `docType.trim().toLowerCase() === "nda"` and, if
so, sets `ndaReviewFile` (triggering the card) and flips `ndaReviewCapabilityNeeded` (triggering the
discovery fetch). A non-"nda" (or unclassified) result leaves `ndaReviewFile` untouched → no card
(negative case, acceptance criterion 3). This is a render-time branch on data the ONE classifier
already produced — the same class of thing `isComposeDraftDocument` / `isCorrespondenceDraft` already
do for other payload shapes in this file; it is not a new classification mechanism.

### Card component: reuse `SuggestionCard`, not a new component (CLAUDE.md §11)

`SuggestionCard.tsx` (shipped by the notification-spine project) is a stateless presentational
component (`{suggestionId, title, snippet?, actionHint}` + `onAction`/`onDismiss`) with the exact shape
ASSISTANT-UI-ELEMENT-CRITERIA specifies for a Card (clickable region + dismiss 'x', hover only on the
clickable region, Fluent v9 tokens). Task 022 renders it directly with a locally-constructed
`suggestion` object — no coupling to the notification-spine's outbox/dismiss-endpoint machinery (that
stays for the proactive `useSuggestionCards` path only). Rendered at the top of the pane, alongside
`{suggestions.suggestionSlot}` (same region — both are "persistent act-on item" cards per the criteria
doc, distinct from the turn-scoped `ConsumerChips` strip).

### Click handler: reuse `mountFileInCompose` + `chips.dispatchBinding` — no new dispatch path

Clicking the card does two things, both via EXISTING mechanisms:
1. `mountFileInCompose(fileId, fileName)` — the SAME dynamic per-file `widget_load` seed the "Revise
   document" local chip already uses — opens the classified file in the Compose tab.
2. `chips.dispatchBinding(ndaReviewBindingId, { slots: { fileIds: [fileId] } })` — the SAME shared
   Click-path dispatch seam every other consumer chip uses (`useConsumerChips`'s documented public
   reuse point), scoped to just the classified file via `slots.fileIds` — the identical wire shape
   `EventRulesService.cs`'s own chip transitions already use (`new { fileIds = allFileIds }`).

### surfaceLaunchRegistry entry — added per the task's explicit instruction, not consulted by the click path

`nda-review` disposition is `Informational` (task 023's recommendation — read-only advisory, single
run → two client views), not `SurfaceLaunch`, so `ConversationPane.handleSurfaceLaunch` /
`resolveSurfaceLaunch` never fires for this Binding today. A registry entry (`kind: 'workspace-tab',
surface: 'compose'`) was added anyway, per the task's explicit step 4 instruction and the
`create-matter`/`create-task` precedent ("the registry entry is ready either way — it activates the
moment a Binding routes SurfaceLaunch for that consumerType") — this keeps the "compose" surface
identity for `nda-review` centralized in ONE place (ADR-039 §2) for a possible future TEXT-path
promotion. The CLICK path (this task) calls `mountFileInCompose` directly instead of routing through
`resolveSurfaceLaunch`/`handleSurfaceLaunch`, because the registry's `workspace-tab` kind assumes a
STATIC, self-sourcing `widgetData` (e.g. `list-tasks`'s fixed grid config) — it has no way to carry a
per-click DYNAMIC file id, which `mountFileInCompose` already handles correctly.

## Binding rows added (`infra/dataverse/sprk_playbookconsumer-rows.json`)

| consumerType | consumerCode | actionCode | disposition | risk | surfaces |
|---|---|---|---|---|---|
| `nda-review` | `default` | `nda-review` | Informational (100000000) | None (100000000) | `assistant,compose` |
| `nda-standard-summary` | `default` | `nda-standard-summary` | Informational (100000000) | None (100000000) | `assistant` |

Both carry non-empty `toolDescription` (required for the capability-discovery/text-path projection —
`ListTextProjectableBindingsAsync` filters on it), which also makes both genuine TEXT-path capabilities
("review this NDA" / "what does our NDA standard say about X") alongside the classification-triggered
Click-path card. **Live seeding via `Seed-PlaybookConsumers.ps1` is env-blocked in this session** (no
live Dataverse credentials) — the data-file changes are ready to seed on the next live-connected run;
GUIDs resolve automatically at seed time (portable mirror, per the file's own `$comment`).

## §10 BFF Hygiene

- **Placement Justification**: N/A — zero BFF (`Sprk.Bff.Api`) files touched. Classification reuses
  the existing `chat-classify` Action + `EventRulesService`; card resolution reuses the existing
  `GET /api/ai/capabilities` endpoint; dispatch reuses the existing Click-path dispatch endpoint. No
  new service, endpoint, DI registration, or package.
- **Publish size**: unchanged (no BFF `.cs` files modified).
- **Hot-path**: BFF touched = **NO**. SpaarkeAi = **YES** (`ConversationPane.tsx`, `useEventBatch.ts`)
  + shared lib (`Spaarke.UI.Components/services/surfaceHandoff/surfaceLaunchRegistry.ts` + its tests).

## Files changed

- `infra/dataverse/sprk_playbookconsumer-rows.json` — 2 new binding rows (data).
- `src/solutions/SpaarkeAi/src/components/conversation/useEventBatch.ts` — additive `onClassified` dep.
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — NDA card state,
  classification handler, capability-discovery wire-up, click handler, session reset, JSX render.
- `src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/surfaceLaunchRegistry.ts` — one
  new `nda-review` entry.
- `src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/__tests__/surfaceLaunchRegistry.test.ts`
  — one new resolution test.
- `src/client/shared/Spaarke.UI.Components/src/services/__tests__/surfaceLaunchRegistry.test.ts` — fixed
  a pre-existing stale exhaustive-key assertion (it was already missing `list-tasks` from an unrelated,
  already-landed task; added `nda-review` alongside it). A SEPARATE pre-existing failure in this same
  legacy/duplicate test file (`create-task` preset missing `sprk_eventtype_ref_name`) is unrelated to
  this task and was left as-is — flagged for whichever task owns that drift.
- `projects/ai-advanced-capabilities-nda-r1/tasks/022-bindings-review-card-classification.poml` —
  status → completed, notes appended.
- `projects/ai-advanced-capabilities-nda-r1/tasks/TASK-INDEX.md` — 022 → ✅.

## Negative case (acceptance criterion 3)

A non-NDA upload still runs `chat-classify` (order-1 event-rule member, unconditional) and still emits
`event_classification` with whatever `docType` it produced — `handleNdaClassified` only sets
`ndaReviewFile` when `docType.trim().toLowerCase() === "nda"`. Any other label (or an absent/malformed
one) leaves the state untouched, so `{ndaReviewFile && <SuggestionCard .../>}` renders nothing. No
server-side gating was needed for this — the negative case falls out of the client's simple equality
check on data the server was already producing.

## Live/env-blocked steps

- `Seed-PlaybookConsumers.ps1` against a live Dataverse environment (no credentials in this session) —
  data changes are seed-ready.
- The manual UI verification (upload NDA → card → Compose tab → NDA-REVIEW runs; upload non-NDA → no
  card) requires a live org + deployed client — flagged blocked-pending-environment per project
  convention, NOT faked. Automated coverage instead: 19 SpaarkeAi Jest suites (104 tests, incl.
  `ConversationPane.*` and event-path suites) pass unchanged; `surfaceLaunchRegistry` resolution test
  added and green; `npm run typecheck` reports 0 surface-owned errors.

## Follow-ons for dependent tasks

- **033** (Draft Alternative + trace activation, deps: 022) can rely on `ndaReviewBindingId` /
  `nda-review`'s Binding row being live.
- **051** (Golden-utterance dispatch eval, deps: 022) should add eval cases for the NEW TEXT-path
  phrasing surfaced by `nda-review`'s + `nda-standard-summary`'s `toolDescription` ("review this NDA",
  "what does our NDA standard say about confidentiality") alongside the Click-path card.
