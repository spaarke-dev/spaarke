# Assistant Surface-Launch Mechanism

> **Status**: Canonical (2026-07-22). Source of truth is the code; this doc points to it.
> **Scope**: How the Assistant, having selected a capability, **deterministically opens a follow-on surface** (a wizard, an OOB form, a workspace grid/widget tab, …). This is the spine between "the agent picked a capability" and "a surface appears."
> **Audience**: Anyone extending the Assistant with a new capability that opens a surface.
> **Companion design notes** (original authorship): [`projects/spaarkeai-assistant-enhancements-r1/notes/surface-launch-mechanism.md`](../../projects/spaarkeai-assistant-enhancements-r1/notes/surface-launch-mechanism.md) (catalog-side contract, task 002) · [`.../notes/012-assistant-surface-handoff-design.md`](../../projects/spaarkeai-assistant-enhancements-r1/notes/012-assistant-surface-handoff-design.md) (the hand-off envelope).

---

## 1. The one idea

The Assistant does **zero intent detection** to decide what surface to open. The decision is made in exactly one place — the **Binding the agent selected** (`sprk_playbookconsumer`, disposition = `SurfaceLaunch`) — and the Binding carries a **`consumerType`** string. The client then performs a **static lookup** of that string in the **`surfaceLaunchRegistry`** to find the concrete surface, and opens it. That's it.

> **Determinism invariant (ADR-039):** `consumerType` IS the routing decision. The client never guesses, classifies, keyword-matches, or re-derives which surface to open — it only does `registry[consumerType]`. One decider (the grounded catalog), one lookup (the registry).

This is why surface routing is reliable: there is no second intent mechanism, no branching by capability name in the BFF, and no per-consumerType `if` ladders in the client.

### 1.1 Scope — this is the REACTIVE path (vs the proactive spine)

This document covers the **reactive** path: the user asks, the agent selects a capability, and a surface opens *in response*. There is a separate, **proactive** path — the Assistant surfacing something worth attention *without* being asked — and it does **NOT** go through this registry. Do not build a second push channel; reuse the spine.

| | **Reactive surface launch** (this doc) | **Proactive push** (the notification spine) |
|---|---|---|
| Trigger | user message → agent selects a Binding | server-initiated typed signal (e.g. Daily Briefing) |
| Path | `consumerType` → `surfaceLaunchRegistry` → open surface | outbox row → SignalR ping → client handler → (⚠️ renderer removed — see note) |
| "Open a record/thing" | wizard / form / grid via registry `kind` | `openRecordModal` on the regarding record (former card; planned OOB-bell `navigationTarget:"dialog"`) |
| Mechanism owner | `surfaceLaunchRegistry` (code) | `@spaarke/notifications` client + `OutboxService` (one spine) |
| Doc | *(this doc)* | [SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md](SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md) |

> **⚠️ 2026-08-20**: the in-Assistant suggestion renderer (`useSuggestionCards.tsx`) was **removed** by `spaarkeai-assistant-enhancements-r2` (FR-E1). The proactive spine's `suggestion` rows are produced-but-unrendered today; the live UI consumer is `communication-arrived`. The proactive record-open surface is being rebuilt as OOB Dataverse bell notifications (`spaarke-notification-spine-r2`). The mechanism distinction below still holds — only the specific renderer changed.

They are complementary and must stay distinct: proactively acting on a suggestion opens a **record modal** (`openRecordModal` / the OOB `navigationTarget:"dialog"` equivalent), which is the standard record-open — *not* a surface-launch-registry entry. If you need the Assistant to *proactively* surface something, add a **producer** to the spine (ADR-047), never a new channel here.

## 2. End-to-end flow

```
User message
  │
  ▼  agent turn — grounded tool selection (ADR-039)
Binding selected (sprk_playbookconsumer), disposition = SurfaceLaunch (100000007)
  │
  ├── TEXT / agent path ──────────────────────────────────────────────┐
  │     BindingCapabilityTool emits SSE "surface_launch"               │
  │     = ChatSseSurfaceLaunchData { BindingId, ConsumerType, Payload }│
  │                                                                    │
  ├── CLICK / chip path ──────────────────────────────────────────────┤
  │     terminal AnalysisChunk carries { disposition:"surface_launch", │
  │       consumerType } (SessionDispatchOrchestrator)                 │
  │                                                                    ▼
  ▼                                          client parses (useSseStream / chip dispatch)
ConversationPane.handleSurfaceLaunch({ consumerType, payload })
  │
  ▼  resolveSurfaceLaunch(consumerType)  → SurfaceLaunchRegistryEntry (STATIC LOOKUP)
  │
  ├─ kind = workspace-tab | layout  →  PaneEventBus dispatch("workspace", widget_load)
  │                                     → in-app grid/widget tab (self-sourcing)
  │
  └─ kind = wizard | oob-form       →  launchSurface(...)
                                        → sessionStorage hand-off envelope
                                        → Xrm.Navigation.navigateTo(web-resource / entityrecord)
                                        → surface reads the envelope on boot, pre-seeds, returns an outcome
```

**Two transport families** (the registry's `kind` selects which):

| Family | Kinds | Channel | Used for | Seeding |
|---|---|---|---|---|
| **Event-bus** | `workspace-tab`, `layout` | `PaneEventBus` `widget_load` on the `"workspace"` channel (in-app, no server round-trip) | grids, widgets, workspace layouts | via `widgetData` on the registry entry (widget self-sources its data) |
| **Hand-off** | `wizard`, `oob-form` | `sessionStorage` envelope + `Xrm.Navigation.navigateTo` | create wizards, OOB entity forms | via the `SurfaceHandoffEnvelope` (draft values + `resolvedLookups` + `fileIds`) |

**Two entry paths, one router.** A surface can be triggered by the **text/agent path** (an SSE `surface_launch` event) or the **click/chip path** (a terminal chunk carrying `disposition:"surface_launch"`). Both converge on the same `handleSurfaceLaunch` → `resolveSurfaceLaunch` router — the routing is identical regardless of how the capability was invoked.

## 3. The registry contract

**File**: [`src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/surfaceLaunchRegistry.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/surfaceLaunchRegistry.ts)

```ts
interface SurfaceLaunchRegistryEntry {
  kind: SurfaceKind;              // 'wizard' | 'oob-form' | 'workspace-tab' | 'layout'
  surface: string;               // interpreted per kind (see below)
  title: string;                 // dialog/form title, or the workspace-tab display name
  preset?: Record<string, unknown>;      // hand-off kinds: authoritative draftValues merge
  widgetData?: Record<string, unknown>;  // event-bus kinds: widget_load payload (e.g. a configId)
}

// The whole table, keyed by sprk_consumertype:
const SURFACE_LAUNCH_REGISTRY: Record<string, SurfaceLaunchRegistryEntry>;

// The only accessor — undefined for unmapped consumerTypes (graceful, never throws):
function resolveSurfaceLaunch(consumerType): SurfaceLaunchRegistryEntry | undefined;
```

**What `surface` means per `kind`** (see [`surfaceHandoff/types.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/types.ts) `SurfaceKind`):

| `kind` | `surface` is… | Opened via |
|---|---|---|
| `wizard` | a web-resource name (`sprk_creatematterwizard`) | `navigateTo({pageType:'webresource'})` + hand-off |
| `oob-form` | an entity logical name (`sprk_todo`) | `navigateTo({pageType:'entityrecord'})` + hand-off |
| `workspace-tab` | a **registered workspace widget type** (`my-tasks-list`) | `PaneEventBus` `widget_load` |
| `layout` | a workspace layout id | `PaneEventBus` `widget_load` |

**Why `surface` identity lives here, in code (not in Dataverse)** — deliberate, per [BFF hygiene §10](../../CLAUDE.md) + ADR-039: the server catalog names the **capability** (`consumerType`); the concrete web-resource / entity / widget-type is a **client deployment concern**. Business analysts enhance Actions and tool/bundle descriptions **in data**; they never author surfaces, so a surface descriptor in data buys nothing and adds a failure surface. (This is the reasoned conclusion after evaluating and rejecting a data-side `surfaceTarget` field — see the abandoned r2 exploration.)

### Current registry inventory (code-side, stable)

| `consumerType` | `kind` | `surface` |
|---|---|---|
| `create-matter` | wizard | `sprk_creatematterwizard` |
| `create-project` | wizard | `sprk_createprojectwizard` |
| `create-task` | wizard | `sprk_createeventwizard` (preset: Event Task-subtype) |
| `create-todo` | oob-form | `sprk_todo` |
| `create-work-assignment` | wizard | `sprk_createworkassignmentwizard` |
| `summarize-files` | wizard | `sprk_summarizefileswizard` |
| `find-similar` | wizard | `sprk_findsimilar` |
| `list-tasks` | **workspace-tab** | `my-tasks-list` (My Tasks grid) |

> **Catalog-state note (changes over time — verify in Dataverse):** the *disposition wiring* is catalog data, not code. As of 2026-07-22 only `list-tasks` carries `disposition = SurfaceLaunch` live; `create-matter`/`create-task` are currently `Informational` and rely on the agent calling `create_record`. The registry entry is ready either way — it activates the moment a Binding routes `SurfaceLaunch` for that `consumerType`.

## 4. The hand-off seam (wizard / oob-form seeding)

For the hand-off kinds, the surface is opened in a separate navigation context, so pre-seed data can't ride the call directly. It rides a **`sessionStorage` envelope** keyed by a minted `handoffId`.

- **Envelope**: `SurfaceHandoffEnvelope` ([`surfaceHandoff/types.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/types.ts)) — `draftValues` (free-text the LLM drafted), `resolvedLookups` (closed-set GUIDs resolved server-side — the LLM never authors these, ADR-039), `fileIds` (SPE references **by reference only — never inline binary**), provenance, TTL.
- **Launcher**: `launchSurface(...)` ([`surfaceHandoff/launchSurface.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/launchSurface.ts)) — writes the envelope, mints `handoffId`, calls `navigateTo`, then reads the **outcome** (`SurfaceHandoffResult`) back from sessionStorage on modal close (this gates the honest "✅ Created …" claim, P5).
- **Read side**: the launched Code Page boots via `readHandoffFromUrl` / `handoffSeed` and pre-fills its form; per-wizard field mapping lives in each wizard.

**Shaping happens here, and only here.** Surfaces are otherwise **self-sourcing** — a grid queries Dataverse itself, a wizard has its own form. The action's output is *not* reshaped into the surface (for `list-tasks` it's just the chat acknowledgement). The only "shape the output to the surface" step is this optional seeding.

## 5. The BFF side (kept intentionally thin)

**File pointers**: `Services/Ai/PublicContracts/Binding.cs` (the `BindingDisposition` enum) · `Services/Ai/Chat/BindingCapabilityTool.cs` (emits the SSE) · `Api/Ai/ChatEndpoints.cs` (`ChatSseSurfaceLaunchData`) · `Services/Ai/OutputRouter.cs` (routing) · `Services/Ai/SurfaceLaunchEnricher.cs` (`resolvedLookups`).

- **`BindingDisposition`** (routing verbs): `Informational`(100000000), `WorkProduct`(100000001), `Overlay`(100000002), `Email`(100000003), `Record`(100000004), `Notification`(100000005), `Compose`(100000006), **`SurfaceLaunch`(100000007)**.
- **The SSE payload** carries only `{ BindingId, ConsumerType, Payload }` — **no** configId / widgetType / surface identity. Surface identity is client-resolved (§3). The BFF **never branches by consumerType** for routing (`OutputRouter`: *"No branching by capability name, consumer type, or any second routing surface"*).
- **`OutputRouter` is a pass-through** for `SurfaceLaunch` — it stores the ledger `SessionOutput` (ADR-040, store-before-render) and returns the payload verbatim; it never parses the launch payload (the client owns it). No per-disposition shaping.
- **`SurfaceLaunchEnricher`** is the one pre-router transform: it resolves label→GUID `resolvedLookups` for closed-set fields (currently `create-matter` only) and merges them into the payload. Everything else is a no-op.

## 6. How to extend — add a new surface-bearing capability

**The common case is small.** A new "open X" capability is:

1. **Catalog (data — no deploy):** author an **Action** (`sprk_analysisaction`) + a **Binding** (`sprk_playbookconsumer`) with `disposition = SurfaceLaunch` and a precise **`toolDescription`** (this is what makes the agent select it — the "trigger"). Pick a `consumerType`.
2. **Registry (code — one entry):** add a `SURFACE_LAUNCH_REGISTRY` entry keyed by that `consumerType`, choosing the `kind`:
   - **grid / widget** → `kind:'workspace-tab'`, `surface` = a registered workspace widget type. (Register the widget in `register-workspace-widgets.ts`; a grid also needs its `sprk_gridconfiguration` — see the [DataGrid framework doc](SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md).)
   - **create wizard** → `kind:'wizard'`, `surface` = the wizard web-resource. A genuinely new web-resource transport also needs a launcher in [`wizardLaunchers.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts); existing wizards reuse the shared one.
   - **OOB entity form** → `kind:'oob-form'`, `surface` = the entity logical name (+ any draft-key→column map).
3. **Test:** add a case to [`surfaceLaunchRegistry.test.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/__tests__/surfaceLaunchRegistry.test.ts).
4. **Deploy** the client (SpaarkeAi) — the catalog rows are live data.

> Because `handleSurfaceLaunch` branches purely on `entry.kind`, **no new `if consumerType === …` branch is ever needed** — a new surface of an existing kind is *one registry entry*.

**Adding a NEW kind (rare — e.g. `page`, `url`):** add the value to `SurfaceKind`, add one arm to the `handleSurfaceLaunch` router (event-bus vs a new transport), and document it here. Do this **demand-pulled** — only when a real capability needs it (don't pre-build dark kinds).

## 7. Key files (component map)

| Concern | File |
|---|---|
| **The registry** (consumerType → surface) | `.../services/surfaceHandoff/surfaceLaunchRegistry.ts` |
| Surface kinds + envelope types | `.../services/surfaceHandoff/types.ts` |
| Hand-off launcher (wizard/oob-form) | `.../services/surfaceHandoff/launchSurface.ts` |
| Hand-off storage (sessionStorage) | `.../services/surfaceHandoff/handoffStorage.ts` |
| Launched-surface read side | `.../services/surfaceHandoff/readHandoff.ts` |
| Wizard web-resource launchers | `.../components/WorkspaceShell/wizardLaunchers.ts` |
| **The router** (branches on `entry.kind`) | `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` → `handleSurfaceLaunch` |
| SSE parse of `surface_launch` | `.../SprkChat/…/useSseStream.ts` |
| Workspace widget registry (widget types) | `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` |
| BFF emit | `Services/Ai/Chat/BindingCapabilityTool.cs`, `Api/Ai/ChatEndpoints.cs` (`ChatSseSurfaceLaunchData`) |
| BFF disposition + routing | `Services/Ai/PublicContracts/Binding.cs`, `Services/Ai/OutputRouter.cs`, `Services/Ai/SurfaceLaunchEnricher.cs` |

## 8. Invariants (do not break)

1. **No client intent detection.** `consumerType` → `registry` lookup only. Never classify/keyword-match to pick a surface. (ADR-039)
2. **Surface identity stays in code** (the registry). Data carries the capability (`consumerType`) + analyst-enhanceable content (prompts, tool descriptions) — never a surface descriptor.
3. **Branch on `kind`, never on a `consumerType` literal** in `handleSurfaceLaunch`.
4. **Files by reference, never inline binary** through the hand-off envelope.
5. **The LLM never authors closed-set values** — those come as `resolvedLookups` from the server resolver.
6. **Graceful degrade** — an unmapped `consumerType` opens nothing (no throw); a malformed payload falls back, never crashes the pane.
7. **BFF carries no surface identity** and does not branch by consumerType for routing.

---

## Related
- **[Notification & Action Spine architecture](SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md)** — the **proactive** counterpart (§1.1). Server-initiated `kind`-typed signal → outbox → SignalR → suggestion card → `openRecordModal`. Reuse it for proactive push; never build a second channel ([ADR-047](../../.claude/adr/ADR-047-notification-action-spine.md)).
- [ADR-039 — grounded execution & closed catalogs](../../.claude/adr/ADR-039-grounded-execution-closed-catalogs.md) (the "one decider" principle)
- [ADR-040 — ledger store-before-render](../../.claude/adr/ADR-040-*.md) (the `OutputRouter` pass-through)
- [SpaarkeAi workspace architecture](SPAARKEAI-WORKSPACE-ARCHITECTURE.md) · [DataGrid framework](SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md) (grid surfaces)
- [Assistant surface hand-off architecture](../../projects/spaarkeai-assistant-enhancements-r1/notes/012-assistant-surface-handoff-design.md) (original design)
