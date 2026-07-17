# Surface-Launch Mechanism — the capability→surface hand-off contract (task 002 → consumed by 012/013)

> **Status**: AUTHORED 2026-07-16 (task 002). **Owner review**: Ralph Schroeder (content = FR-J1).
> **Companion**: [`012-assistant-surface-handoff-design.md`](012-assistant-surface-handoff-design.md) (the client transport + envelope). This note is the **catalog-side contract**: *which* capability routes to *which* surface, and the exact routing signal. 012 builds the client registry + transport from this.
> **Grounded against (live, spaarkedev1, verified task 002)**: the `create-matter`/`create-task` Binding + Action rows, `DispositionRoutability.cs` / `OutputRouter.cs` / `Binding.cs` (SurfaceLaunch already coded + merged), `wizardLaunchers.ts`, `wizardRegistry.ts`.

---

## 1. The mechanism, in one paragraph

A create capability no longer writes the record. Its **Action drafts structured fields** (already does — JSON output), and its **Binding disposition = `Surface Launch` (100000007)**. On dispatch, the shipped `OutputRouter` stores the drafted JSON as a `SessionOutput` (ledger value `surface_launch`) and **performs NO server-side write** — a pass-through leg, exactly like `Compose`. The **client** reads that stored output, looks up the capability's `sprk_consumertype` in a small **client-side launch registry** (task 012), and opens the entity's real create surface **pre-seeded** with the drafted values. The surface (wizard / OOB form) owns the gated write → a real record. No dead-end, no LLM-authored record write.

**Routing signal = the disposition. Surface binding = the consumertype → client registry. Payload = the Action's drafted JSON + resolver GUIDs (envelope per 012 §3).**

---

## 2. Why disposition (not a new Binding column)

`Binding.cs` treats `sprk_disposition` as **the single routing signal** for every output destination (informational / work_product / overlay / email / record / notification / compose). `Compose` (100000006) is the precedent: it added a *new destination* as a disposition value + a store-then-client-re-materializes transport. `SurfaceLaunch` (100000007) is the same move for a *launched surface*. Adding a parallel "surface-target" column would be a **second routing mechanism** — forbidden by ADR-039 (one decider) and by CLAUDE.md §11 (default-to-reuse). The option-set value was the only schema add; the owner approved + deployed it (100000007).

**The concrete web-resource name (`sprk_creatematterwizard`, …) stays CLIENT-SIDE** (BFF hygiene §10: the server catalog must not carry client deployment/web-resource names). The catalog names the *capability* (`sprk_consumertype`); the client owns the *surface*. `wizardLaunchers.ts` is already exactly this registry.

---

## 3. The routing table (the contract 012 consumes)

Keyed by the Binding's `sprk_consumertype` (already carried through the ledger/SessionOutput, so the client can map it):

| `sprk_consumertype` | `target.kind` | Client surface (012 registry) | Pre-seed / preset | Launch adapter |
|---|---|---|---|---|
| `create-matter` | `wizard` | `sprk_creatematterwizard` (registry key `matter`; `launchCreateMatterWizard`) | `draftValues` → matter name/description; `resolvedLookups` → practice-area / matter-type GUIDs (resolver, task 010) | `Xrm.Navigation.navigateTo` (existing launcher) + handoff-id/sessionStorage envelope |
| `create-task` | `wizard` | `sprk_createeventwizard` (registry key `event`; `CreateEventWizard`) | **subtype preset `sprk_eventtype_ref = "Task"`** (GUID `124f5fc9-98ff-f011-8406-7c1e525abd8b`); `draftValues` → title/description; priority hint | `navigateTo` (⚠️ build-verify: add an `sprk_createeventwizard` launcher — `wizardLaunchers.ts` hoisted matter/project/summarize/find-similar/work-assignment/playbook, NOT event; the shared `CreateEventWizard` component + `src/solutions/CreateEventWizard` Code Page exist) |
| `create-todo` | `oob-form` | OOB `sprk_todo` create form (no custom wizard) | `draftValues` → `sprk_name`/`sprk_description`; `sprk_priority` from priority hint; regarding (ADR-024) owned by the form | `Xrm.Navigation.navigateTo({pageType:'entityrecord', entityName:'sprk_todo', ...})` per [`MODAL-DECISION-CRITERIA`](../../../docs/standards/MODAL-DECISION-CRITERIA.md) — thinner pre-seed (OOB accepts `createFromEntity`/default-value params only) |

**`target.kind`** is the discriminator the client branches on (`wizard` | `oob-form` | — future — `workspace-tab`/`layout` for the 2a "show my open tasks" case, per 012 §4.5). For R1's three create intents, `kind` is a **constant per consumertype** and lives in the client registry (not LLM-chosen, not server-authored — a client launch concern). It may alternatively be echoed in the Action output if 012 finds that cleaner; the registry is the source of truth either way.

---

## 4. What the Action produces (verified — no output redesign needed)

Both shipped Actions **already emit structured drafted-fields JSON** (this was the pleasant surprise of task 002 — the "redesign the output" work was already done by ai-architecture-redesign-r1):

- **CREATE-MATTER@v1** (`63f086d3-767d-f111-ab0e-70a8a590c51c`): `{ matter_name, matter_description, practice_area_suggestion (LABEL), matter_type_suggestion (LABEL), cited_refs[] }`. Labels not GUIDs → already P1-clean; the resolver (010) turns labels→GUIDs into `resolvedLookups`.
- **CREATE-TASK@v1** (`b66c8dda-8279-f111-ab0e-7ced8ddc4cc6`): `{ title, description, priority_suggestion (enum), cited_refs[] }`. Deliberately no due-date/assignee — user-supplied in the surface.
- **CREATE-TODO@v1** (NEW, task 002): mirrors CREATE-TASK@v1 → `{ title, description, priority_suggestion, cited_refs[] }` drafting `sprk_todo` fields.

**The repoint is therefore three small moves, not an output rewrite:**
1. Binding `sprk_disposition`: Informational (100000000) → **Surface Launch (100000007)**.
2. Rewrite `sprk_tooldescription`: remove all "call `dataverse.create_record`" / GUID-resolution / POST-CONFIRMATION-write instructions; describe drafting + hand-off to the pre-seeded surface (which owns the write). Add the Event-Task-vs-To-Do disambiguation (P4).
3. Action `sprk_allowstools`: true → **false** — the drafting turn produces ONLY the JSON draft, no tool calls (structurally enforces P1: the LLM cannot resolve a closed set or write a record). The GUID resolution moves to the deterministic resolver (010); the write moves to the surface.

---

## 5. Server does NO write (already coded + merged)

`OutputRouter` (case `BindingDisposition.SurfaceLaunch`) is a **pass-through**: the `surface_launch` SessionOutput is stored (ADR-040 store-before-render), and the router returns without any Dataverse write. `DispositionRoutability` registers `SurfaceLaunch` as `Routable=true`, ledger value `surface_launch`; the seam test `DispatchAsync_SurfaceLaunchDisposition_Admits_Routes_Stores_AndRenders` pins admit⇔route⇔store⇔render (ADR-043 DoD). The **client owns the launch**; the surface owns the write. Nothing on the server changes when these rows flip — the code path already exists.

---

## 6. P4 disambiguation — one-tap, never text negotiation (chip transitions)

The closed intent set is **Event-Task vs To Do** (plus matter). Resolution is authored **into the tool descriptions** (inference: explicit "to do" → To Do; "task"/"event"/time-blocked language → Event-Task) — **no classifier** (ADR-039). The **one-tap correction** when inference guesses wrong is a **chip transition**, not a chat question:

- `create-task` (Event-Task) → chip **"Make it a To Do instead"** → `create-todo`.
- `create-todo` → chip **"Make it an Event-Task instead"** → `create-task`.
- `create-matter` → chips **"Add a related task"** → `create-task`, **"Add a to-do"** → `create-todo` (natural next steps after a matter).

This satisfies the acceptance criterion "resolves by inference or one-tap pick, never multi-turn text negotiation" structurally: the chip re-dispatches the *other* capability, re-drafting + re-launching the correct surface. (`target_binding_id` = the sibling Binding's `sprk_playbookconsumerid`.)

---

## 7. Interim state on spaarkedev1 (honest note)

Flipping these rows to `SurfaceLaunch` **before** task 012's client hand-off ships means, in the interim on dev, a "create a matter" turn produces a stored draft with disposition `surface_launch` but **no client consumer launches the wizard yet** — it degrades to "draft shown, nothing opens" (a pass-through, NOT a hard error). This is expected mid-vertical: 002 is the foundation 010/012/013 build on, and the full flow is proven before the deploy gate (task 054). Not a regression to escalate — the create flows were already broken (the UAT dead-end); this replaces one broken behavior with an inert-until-wired one.

---

## 8. Consumed-by / build-verify checklist for task 012

- [ ] Build the client launch registry keyed by `sprk_consumertype` (table §3). Reuse/extend `wizardLaunchers.ts` — do NOT fork.
- [ ] **Add an `sprk_createeventwizard` launcher** (the one missing surface) + confirm its exact web-resource name against `src/solutions/CreateEventWizard`.
- [ ] Confirm the wizard modal iframe shares `sessionStorage` with the launcher (012 §2 build-verify #1) — else fall back to `localStorage`/BFF-fetch-by-id.
- [ ] Carry the Event subtype preset (`sprk_eventtype_ref = Task`) in the envelope for `create-task`.
- [ ] Wire the return path (`…<id>.result.committed`) → P5 honest ack (task 020).
