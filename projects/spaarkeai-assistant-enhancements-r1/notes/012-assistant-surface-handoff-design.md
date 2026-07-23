# Assistant → Surface Hand-off — Design Note (informs tasks 002 + 012)

> **Status**: DESIGN (pre-code). No code changes here — this is the contract to sanity-check before task 012 builds it and task 002 authors the routing flag.
> **Date**: 2026-07-16. **Owner review**: Ralph Schroeder.
> **Grounded against**: `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts` (the shipped launchers), the live `create-matter`/`create-task` Binding rows, `Binding.cs` (`Compose` disposition precedent), and the existing `sessionStorage` usage across SpaarkeAi/LegalWorkspace (30+ files incl. `WorkspaceGrid.tsx`).

---

## 0. Scope first — three classes of "the Assistant makes something happen"

This matters because it tells us what this hand-off does and does **not** solve (owner's question, 2026-07-16). There are three distinct mechanisms; conflating them is the trap.

| Class | "The Assistant…" | Examples | Transport | Built? |
|---|---|---|---|---|
| **1. Dispatch (server-side effect, no surface)** | …*performs a function* | send an email, write a record server-side, run a coded workflow, summarize | the **session ledger** + dispatch (ADR-039/040); disposition = `Record`/`Email`/`Compose`/`Notification` or a coded Action | ✅ shipped |
| **2a. In-app surface (mount/arrange a surface in THIS SPA)** | …*shows me the right view in-app* | open a Workspace **tab** with a **widget** (Event/Calendar/Grid) filtered, apply a **workspace layout**, open Compose | **in-app event bus / direct mount with a config object** (`PaneEventBus`, `WorkspaceTabManager`, `useWorkspaceLayouts`) — **no id-rendezvous** (same memory space) | ◑ substrate shipped; Assistant-trigger + config-builder = **task 012 scope** |
| **2b. Launched Code Page surface (separate iframe/session)** | …*hands me the right UI, pre-seeded* | Create Matter wizard, Event wizard, OOB To-Do form | **correlation id + payload in the browser session** (§1–§3) | ⛳ **this note / task 012** |
| **3. External / cross-origin launch (open a system we don't own)** | …*hands off to another app* | DocuSign, a third-party SaaS, a Teams/Outlook deep link | server-brokered deep-link + a **callback/webhook → notification** back; **no shared browser session, no synchronous return** | ❌ not in scope (thinner, looser pattern if ever needed) |

**Answer to "does this solve launching other processes?"**
- **"Perform a function of some sort"** → that's **Class 1 (dispatch)** — already solved; you do NOT need the hand-off for it. A Binding with an execution disposition (or a coded workflow) does it; the ledger is the transport.
- **"Open another form / app / pane inside Spaarke"** → that's **Class 2** — and yes, this hand-off generalizes across *all* of them (wizard, OOB form, future panes/viewers/schedulers). That's the reusable win. The only thing that varies per target is a thin **launch adapter** (how you open it) and, for OOB forms, how much pre-seed the target accepts.
- **"Open some other external app"** → that's **Class 3** — genuinely different: cross-origin means no shared `sessionStorage` and no readable close-promise. You can *open* it (encoded/brokered URL) but any outcome must return via a server-side callback → notification. **Deliberately out of scope** here; flagged so we don't over-generalize Class 2's browser-session transport into something it can't be.

**So this note is scoped to Class 2**, and within Class 2 it is fully general. It is *not* a universal "Assistant → anything" bus, and that's the correct boundary.

---

## 1. The core problem: how does the Assistant "know" the specific launched instance?

It doesn't *discover* the instance — it **mints the identity and stamps it onto the target at launch.** There is no pre-existing wizard session to look up; the Assistant creates the shared name (a **handoff id**) and injects it.

```
Assistant (session A)                         Wizard Code Page (fresh instance W)
──────────────────────                        ────────────────────────────────────
1. mint handoffId = "h-<guid>"
2. store payload  ──► sessionStorage["sprk.handoff.h-<guid>"] = { …envelope… }
3. navigateTo(sprk_creatematterwizard,
      data:"handoffId=h-<guid>&bffBaseUrl=…") ──►  4. boot; read handoffId from OWN launch URL
                                                    5. read sessionStorage["sprk.handoff.h-<guid>"]
                                                    6. pre-seed fields + resolve fileIds
   (Assistant awaits the navigateTo promise)        7. user commits/cancels
8. promise resolves on modal close  ◄────────────   write sessionStorage["sprk.handoff.h-<guid>.result"]
9. read  …h-<guid>.result → honest claim               = { committed:true, recordId } (or cancelled)
10. cleanup: delete both keys
```

The **handoff id is the whole correlation.** Payload-out and outcome-back are both keyed by it. The URL carries only the *key* (small); the payload lives in the browser session (big enough for JSON refs).

---

## 2. The transport — browser session, keyed by the id

**Why the browser session works**: the Assistant host (SpaarkeAi code page) and the wizard (`sprk_creatematterwizard` web resource) are **same-origin** (both org web resources) and open in the **same browser tab** (`navigateTo target:2` is an in-page modal iframe). `wizardLaunchers.ts` already opens them this way; the codebase already leans on `sessionStorage` across these exact surfaces (`WorkspaceGrid.tsx`, workspace layouts, pinned workspaces, pane state — 30+ files). Paved road.

**Retrieval adapters (one envelope, per-boundary retrieval):**

| Destination | Boundary | Retrieval |
|---|---|---|
| Wizard / OOB form | same-origin, same tab (modal iframe) | **`sessionStorage` by id** (primary) |
| Compose widget | in the SpaarkeAi SPA (same session) | **ledger** `SessionOutput` (already shipped) |
| (future) different tab / window | same-origin, other tab | `localStorage` by id (cross-tab) + cleanup, or BFF fetch-by-id |
| (future, Class 3) external app | cross-origin | server-brokered; **not** browser session |

**⚠️ Build-time verifications (do NOT assume):**
1. Confirm the wizard modal iframe is **same-origin** with the launcher AND shares `sessionStorage` (same-origin nested iframes in one tab should — but Power Apps web-resource hosting can sandbox/subdomain; **if not shared, fall back to `localStorage` (cross-context, same-origin) with read-and-delete, or a BFF fetch-by-id**).
2. Confirm `navigateTo(...)`'s returned promise **resolves on modal close** (the launchers already `.catch()` it, so it is a promise — confirm the resolve timing). Do **not** rely on it carrying structured return data; the outcome comes from `sessionStorage[….result]`, the promise is just the "wizard is done" signal.
3. Align the key naming with the existing **sessionStorage sentinel convention** in the LegalWorkspace embedded-mode contract (don't invent a parallel scheme).

---

## 3. The entry-payload envelope (the shared contract)

What rides in `sessionStorage["sprk.handoff.<id>"]` — a JSON envelope, **files by reference, never by value**:

```jsonc
{
  "handoffId": "h-<guid>",
  "source": { "sessionId": "<chat session id>", "bindingId": "<capability>", "turn": 3 },
  "target": { "surface": "sprk_creatematterwizard", "kind": "wizard" },  // or "sprk_todo" / kind:"oob-form"
  "fileIds": ["<spe-file-id>", …],            // session-held SPE references; wizard fetches on load
  "draftValues": {                             // what the Assistant drafted in chat
    "sprk_mattername": "Acme NDA intake",
    "sprk_matterdescription": "…\nProvenance: source document nda-acme.pdf"
  },
  "resolvedLookups": {                         // constrained-field resolver (P1/task 010) output
    "sprk_practicearea": { "recordId": "<guid>", "confidence": "high" },
    "sprk_mattertype":   { "recordId": "<guid>", "confidence": "low", "candidates": [ … ] }
  },
  "provenance": { "sourceFiles": ["nda-acme.pdf"], "ledgerKeys": ["<binding>@t3"] },
  "createdAt": "<iso>", "ttlSeconds": 900
}
```

- **`fileIds`** answers "how does the chat file get into the wizard": by reference. The wizard authenticates via `@spaarke/auth` and resolves/attaches them on load. The binary never rides the URL or storage.
- **`resolvedLookups`** carries the constrained-field resolver's GUIDs so the wizard pre-selects dropdowns (`high` → pre-select; `low`/`none` → picker defaulted to the top candidate). This is where P1 ("LLM never resolves a closed set") pays off end-to-end.
- **`draftValues`** are the free-text fields the LLM is *good* at (name, description + provenance line).
- The **result** envelope (`…<id>.result`) mirrors this: `{ committed, recordId?, cancelled?, error? }`.

The **same envelope shape** serves wizard and OOB form; only the launch adapter + the pre-seed fidelity differ (OOB forms accept only `createFromEntity` / default-value params — a documented thinner adapter).

---

## 4. The routing flag (task 002) vs the transport (task 012)

- **Task 002 (catalog, small)**: add `SurfaceLaunch` as an 8th `sprk_disposition` value — the same move `Compose` (100000006) already made for a new destination. It means "this capability drafted an output; route it to a launched surface." Repoint `create-matter`/`create-task` off `Informational`+`create_record` onto `SurfaceLaunch`. The **which-surface** mapping (consumer-type → web resource) is tiny and co-designed here. One option-set value on the `sprk` publisher (maker portal) = the owner sign-off.
- **Task 012 (client, the substance)**: the id lifecycle + envelope + `sessionStorage` transport + the wizard's read/pre-seed + the return-path. This note is its contract.

`Compose` is the precedent for **both**: a disposition value for the routing, and store-then-destination-re-materializes for the transport. Class 2 wizard/OOB reuse the idea with a browser-session adapter instead of the in-SPA ledger read.

---

## 4.5 The in-app sub-class (2a) — opening a filtered view, and the grounding landmine

Owner example (2026-07-16): *"list of open tasks" → open a Workspace tab with the Event widget, passing the request as a filter.* This is **Class 2a**, and it is **simpler** than the wizard (2b), because the widget mounts in the **same React app/session** — there is no separate process, so **no correlation id and no browser-session rendezvous are needed.** The Assistant emits an in-app intent and the workspace pane mounts the widget with a **config object handed directly** (props/event-bus), exactly how Compose already re-materializes in-SPA.

The substrate already exists — `PaneEventBus`, `WorkspaceTabManagerComponent`, `useWorkspaceLayouts`, `register-workspace-widgets`, and widgets that already consume **structured config** (DataGrid `configId` → `sprk_gridconfiguration`; Calendar From/To/date-field). So a "list of open tasks" view-open is: dispatch a capability → hand the workspace pane `{ widget: "EventWidget", config: { filter } }` → it opens the tab. No new query engine.

**The translator is NOT a FetchXML builder** (owner clarification, 2026-07-16 — confirmed against `CalendarFilterPane.tsx`). The Event/Calendar widget already consumes a **structured date-filter object**, not a query language:

```ts
// CalendarFilterPaneOutput (the widget's actual filter contract)
{ type: 'range', start: '2026-07-16', end: '2026-07-23', dateFields: ['sprk_duedate'] }
```

So the "builder in between" maps NL → **that existing filter shape**. It stays inside ADR-039 / P1 by construction: **the LLM never authors the filter object or any query** — it fills a **closed set of filter dimensions**, and deterministic code emits the widget's config:

1. LLM fills closed-vocabulary dimensions: `{ dateRange: "this-week" | {from,to}, dateField: "sprk_duedate", status: "open", assignee: "me" }`.
2. Deterministic builder → the target widget's config (for Calendar: `{ type:'range', start, end, dateFields }`; relative dates resolved against the `## Current Date` block, same rule the create-task capability already uses).
3. Widget renders.

**⚠️ Honest gap — "my open tasks" is not purely a date filter.** `CalendarFilterPane` filters on **date range + date field only**. "**open**" (status = not-completed) and "**my**" (assignee/owner = caller) are **not** dimensions the calendar widget expresses today. So the target matters:

| NL request | Natural 2a target | Why |
|---|---|---|
| "what's due **this week**" | **Calendar/Event widget** | pure date-range on `sprk_duedate` — maps 1:1 to the existing filter |
| "**my open** tasks" | **filtered Task grid** (DataGrid `configId` → `sprk_gridconfiguration` + runtime status/owner filter) | the grid config vocabulary covers status + owner; the calendar does not |

So the builder targets a **closed dimension set** `{ dateRange, dateField, status, assignee }`, and each target widget **honors the subset it supports** (calendar = date; grid = status/owner/fields). For **"show my open tasks"** specifically, the honest target is a **filtered Task grid tab**, not the calendar. *(Build-verify: confirm `DataGrid`/`sprk_gridconfiguration` accepts a runtime status/owner filter override, not only a static `configId`; if not, that thin override is the small net-new piece — still deterministic, still no LLM query.)*

So a "view-open" hand-off reuses the **same envelope idea** (`target` + structured `config`), payload = a **filter-dimension set**, transport = **in-app event bus** (not sessionStorage). No LLM-authored query anywhere.

**Routing**: share the `SurfaceLaunch` disposition, discriminated by `target.kind = "workspace-tab" | "layout"` — one option-set value, one router branch.

**R1 scope addition (owner-approved 2026-07-16)**: a first 2a instance — **"show my open tasks"** — is added to R1 (was a documented fast-follow). It needs a new small task (NL → closed-dimension builder + the Task-grid target); to be created at execution time. The create flows (2b) remain the primary R1 create-flow work.

## 5. Why this also makes the Assistant honest (ties to task 020 / P5)

The Assistant claims "✅ Created the matter" **only** after it reads `…<id>.result.committed === true` (written by the wizard, read when the navigateTo promise resolves). It structurally cannot fabricate success, because the claim is gated on an outcome the *wizard* wrote under the id the *Assistant* minted. FR-C1/P5 (task 020) falls out of the same correlation id — no separate ack channel needed for the launch path.

---

## 6. Open questions for the owner

1. **`SurfaceLaunch` disposition** — approve adding the option-set value (the `Compose` move), or carry the routing on a column instead? (Recommend: disposition value — it's the platform's single routing signal per `Binding.cs`.)
2. **Result-write trust** — the wizard writing `committed:true` to browser storage is client-attested. For high-stakes "created" claims, do we want the Assistant to *verify* via a BFF read of the created record id, not just trust the storage flag? (Recommend: yes for creates — cheap, and it hardens P5.)
3. **Class 3 (external apps)** — confirm it stays out of R1 (recommend: yes; revisit only when a concrete external target exists).

---

## 7. Non-goals (explicit)

- NOT a universal "Assistant → anything" bus. Class 1 (dispatch) and Class 3 (external) are different mechanisms.
- NOT a new session store or a parallel ledger — the durable copy of drafted output stays the `SessionOutput` (ADR-040); `sessionStorage` is the *transient* client rendezvous, discarded on read.
- NOT a new dispatch endpoint (compose-r2 invariant) — the launch is a client action on a dispatched capability's stored output.
