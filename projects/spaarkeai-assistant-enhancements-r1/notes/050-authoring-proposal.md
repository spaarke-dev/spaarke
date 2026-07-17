# Task 050 (FR-J1) — Authoring Proposal: richer `sprk_tooldescription` + chips + narrow ambiguity set + Q&A→list

> **Status**: PROPOSAL FOR OWNER REVIEW (Ralph) — **not yet applied to Dataverse**. Date: 2026-07-16.
> **Task**: `050-authoring-tooldescription-chips-ambiguity.poml` (Phase 6; blocks 051). Owner review = the completion gate.
> **Grounded against (live, spaarkedev1, read-only MCP, 2026-07-16)**: `sprk_playbookconsumer` rows for the R1 capabilities (create-matter/task/todo, chat-summarize, chat-classify, draft-correspondence, daily-briefing-narrate, compose-*), including their current `sprk_tooldescription` + `sprk_chiptransitions`.
> **Constraint anchor**: ADR-039 — ambiguity is authored **into tool descriptions**, never a classifier or a second decider. The create flows draft fields → launch a pre-seeded surface; the LLM never calls `create_record` / resolves GUIDs (`sprk_allowstools=false`).

## Summary

The R1 create trio (`create-matter`, `create-task`, `create-todo`) was already repointed to Surface Launch and given the **To Do vs Event-Task** disambiguation by task 002 — that half of the narrow ambiguity set is live. This proposal adds the **still-missing half of the §5 narrow set — the "file / open / close / matter" cluster** — as reciprocal cues between `create-matter` and `chat-summarize` (the "open a file" = create-a-matter-RECORD vs read-an-existing-document collision), authors the one genuinely-new item — a **`list-tasks`** capability that "what are my tasks?" / "show my open tasks" (FR-G2) **dispatches** (design §464 option (a): no new bare-Q&A machinery — a real capability whose chips fire), and adds successor chips. Every change owes eval cases (listed for task 051). Nothing here is applied yet — it is a before→after for the owner to approve.

---

## 1. Live before-state (verbatim, for reference)

| `sprk_consumertype` | Binding id | Disposition | Has tool-desc? | Chips today |
|---|---|---|---|---|
| `create-matter` | `89cd91f6-767d-f111-ab0e-70a8a590c51c` | Surface Launch | yes (enriched, task 002) | `Add a related task`→create-task, `Add a to-do`→create-todo |
| `create-task` | `3d9724e5-8279-f111-ab0e-7ced8ddc4cc6` | Surface Launch | yes (enriched, task 002) | `Make it a To Do instead`→create-todo |
| `create-todo` | `b78b1cf5-3381-f111-ab0f-7ced8ddc4cc6` | Surface Launch | yes (enriched, task 002) | `Make it an Event-Task instead`→create-task |
| `chat-summarize` | `651194cd-3670-f111-ab0e-70a8a590c51c` | Informational | yes | `Summarize again`→self |
| `chat-classify` | `5f3898d8-db78-f111-ab0e-7ced8ddc4cc6` | Informational | yes (event-rule member) | `Summarize this document`→chat-summarize |
| `draft-correspondence` | `f7dc4a00-6b79-f111-ab0e-7ced8ddc4cc6` | Informational | yes (email-only guard) | `[]` |
| `daily-briefing-narrate` | `b4503359-1771-f111-ab0e-7ced8ddc4a05` | Informational | yes | `[]` |
| `compose-draft-alternative` / `compose-revise-document` / `compose-explain-clause` / `compose-summarize-word-changes` | (compose family) | Compose / Informational | yes (intra-family disambiguation already authored) | mostly null |
| **`list-tasks`** | **— (NEW, does not exist)** | **— (proposed §4)** | **no** | **— (proposed §4)** |

**Key finding for §4**: the catalog has **no generic record-list / "my tasks" capability**. `insights-ask` targets matter-health scoring; `insights-search` is a RAG-catalog registration stub with a **null `sprk_tooldescription`** ("no engine target") — neither is a dispatchable home for "what are my tasks?". So `list-tasks` is genuinely-new surface (§4 justification below).

**Chip JSON shape (confirmed from live rows)**: `[{"target_binding_id":"<guid>","chip_label":"<text>","requires_attachments"?:true,"bulk_chip_label"?:"<text>"}]`.

---

## 2. Tool-description enrichments (before → after)

> Presented per-capability (a "row" each) rather than one wide table because the verbatim descriptions are long; each block is the FR-J1 before→after the owner reviews. Only capabilities with a proposed change are shown in full; unchanged capabilities are noted at the end.

### 2.1 `create-matter` — ADD the file/open cluster cue (the missing §5 ambiguity half)

**Current (verbatim)**:
> Create a new matter (a Spaarke sprk_matter record) from this conversation. Use when the user asks to create, open, start, or intake a matter (e.g. 'create a matter from this file', 'open a new matter for the Acme deal'). This capability DRAFTS the proposed matter (name, description, practice-area and matter-type LABEL suggestions, source citations) grounded in the session material — it does NOT create the record and does NOT ask follow-up questions in chat. After drafting, the proposal is handed to the Create Matter wizard, opened PRE-SEEDED: the drafted name and description fill the wizard fields, and the practice-area / matter-type are pre-selected from the deterministic constrained-field resolver's matches. The LLM NEVER resolves those closed-set lookups to GUIDs — it only suggests the LABEL; the resolver turns labels into the wizard's pre-selected dropdown values. The Create Matter wizard owns the gated write, the source-file attach, assignment, and review — producing the real record. Do NOT call dataverse.create_record; do NOT resolve practice-area / matter-type GUIDs via read_query; do NOT elicit fields in chat. Present the drafted matter proposal and let the pre-seeded wizard take over.

**Proposed** — insert this sentence-group immediately after the first sentence (after "…'open a new matter for the Acme deal').") and leave the rest verbatim:
> DISAMBIGUATION (file / open — the authored §5 ambiguity): here 'open' means opening a NEW matter (intake). If the user says 'open this file', 'view this document', or 'read this file' meaning they want to READ or summarize an already-uploaded document, that is the summarize capability (chat-summarize), NOT this one — use this only when the user wants to create/intake a matter RECORD. Legal users often say 'file' to mean the matter record; when 'file' clearly refers to the uploaded document as a thing to read, prefer chat-summarize. There is no 'close a matter/file' capability in R1 — a close request is not this capability. If the user only wants to SEE or LIST their existing tasks, that is list-tasks, not a create.

**Rationale**: §5's narrow set is "file, open, close, matter." Task 002 authored To Do/Event but left the file/open collision unaddressed, and `create-matter` already claims "open" as a trigger synonym — the exact word that collides with "open a file" = open a document. This puts the tie-break in front of the one decider (ADR-039), and adds a negative cue so `list-tasks` (§4) is not stolen by a matter-create trigger.

### 2.2 `chat-summarize` — ADD the reciprocal file/open cue

**Current (verbatim)**:
> Summarize the file(s) the user uploaded into this chat session. Produces a structured summary (TL;DR bullets, narrative summary, keywords, named entities) rendered in the Assistant Workspace pane. Use when the user asks to summarize, recap, or get a TL;DR of uploaded/attached documents. Optional args: fileIds (subset of session files; defaults to all, max 20), styleHint (e.g. executive, detailed, bullet-points). Informational output only — no side effects.

**Proposed** — append one sentence-group before the "Optional args" sentence:
> DISAMBIGUATION (file / open — the authored §5 ambiguity): use this when the user wants to READ, view, or 'open' an existing uploaded document to understand it ('open this file', 'what's in this document', 'give me a TL;DR'). If the user instead wants to CREATE or intake a new matter RECORD (legal 'file' = record), that is create-matter, not this. 'Close a file/matter' has no R1 capability and correctly falls through to the honest-refusal handler.

**Rationale**: makes the file/open cue reciprocal so the one decider sees the boundary from both competing descriptions — the ADR-039-safe way to enumerate the ambiguity without a lexicon/resolver.

### 2.3 `create-task` — ADD a list-vs-create negative cue (keep the shipped To Do/Event disambiguation)

**Current (verbatim)**: *(unchanged shipped text — the full Event-Task-vs-To-Do disambiguation authored in task 002; retained verbatim)* — begins "Create an Event-Task (a Spaarke sprk_event with subtype Task)…" through "…let the pre-seeded Event wizard take over."

**Proposed** — insert one sentence into the existing DISAMBIGUATION block (after the "Event-Task vs To Do" guidance):
> If the user asks to SEE, LIST, or SHOW existing tasks ('what are my tasks?', 'show my open tasks', 'what's on my plate?'), that is the list-tasks capability — NOT a create. This capability only CREATES a new Event-Task.

**Rationale**: `list-tasks` (§4) is new; `create-task` currently owns "task" language broadly and would over-trigger on "show my tasks." One negative cue keeps create out of the list lane (ADR-039 authored-boundary, not a classifier).

### 2.4 `create-todo` — same list-vs-create negative cue

**Current (verbatim)**: *(unchanged shipped text — the full To-Do-vs-Event-Task disambiguation from task 002; retained verbatim)* — begins "Create a personal To Do (a Spaarke sprk_todo action item)…".

**Proposed** — insert into its DISAMBIGUATION block:
> If the user asks to SEE, LIST, or SHOW existing to-dos/tasks ('what are my tasks?', 'list my to-dos'), that is the list-tasks capability — NOT a create. This capability only CREATES a new To Do.

**Rationale**: symmetric with 2.3 — protects the new list lane from both create capabilities.

### 2.5 Capabilities with NO change proposed (and why)

- **`chat-classify`** — runs automatically as the first member of the `document_uploaded` event rule (confidence gates M4). It is not user-uttered, so it needs no ambiguity cue. Leave verbatim.
- **`draft-correspondence`** — already tightly guarded ("Use ONLY when the user explicitly says EMAIL … Do NOT use for letters, memos, or any document … those go to compose-draft-document"). Its ambiguity (email vs compose) is authored. Leave verbatim.
- **`compose-*` family** — `compose-revise-document` already explicitly disambiguates against `compose-draft-alternative` (single clause) and `compose-draft-document` (brand-new doc); `compose-explain-clause`/`compose-summarize-word-changes` are read-only and unambiguous. The compose family is shipped and self-disambiguating. **No change** — flagged per the task's "only note if a description needs an ambiguity cue."
- **`daily-briefing-narrate`** — single clear trigger; no competing capability in the file/open/matter cluster. No cue needed (but it gains a chip as a *successor target* from `list-tasks`, §3).

---

## 3. Chip transitions (`sprk_chiptransitions`) — before → after

> Reuses the symmetric create-task⇄create-todo disambiguation chips already authored in task 002 (unchanged) and adds successors. **Ordering dependency**: the `list-tasks` Binding (§4) must be created FIRST and its `sprk_playbookconsumerid` captured before the `<list-tasks id>` placeholders below can be filled — chips with a dangling `target_binding_id` are a named failure mode (design §15.3 item 18).

| Capability | Current chips | Proposed chips |
|---|---|---|
| `create-matter` | `[{Add a related task→create-task},{Add a to-do→create-todo}]` | **unchanged** (keep the two; a "show my tasks" chip after a *matter create* is a weak successor — see Open Question 5) |
| `create-task` | `[{Make it a To Do instead→create-todo}]` | `[{Make it a To Do instead→create-todo},{Show my open tasks→list-tasks}]` |
| `create-todo` | `[{Make it an Event-Task instead→create-task}]` | `[{Make it an Event-Task instead→create-task},{Show my open tasks→list-tasks}]` |
| `list-tasks` (NEW) | — | `[{Create a To Do→create-todo},{Create an Event-Task→create-task},{What changed today?→daily-briefing-narrate}]` |

**Proposed JSON (fill `<list-tasks id>` after the Binding is created):**

`create-task`:
```json
[{"target_binding_id":"b78b1cf5-3381-f111-ab0f-7ced8ddc4cc6","chip_label":"Make it a To Do instead"},{"target_binding_id":"<list-tasks id>","chip_label":"Show my open tasks"}]
```
`create-todo`:
```json
[{"target_binding_id":"3d9724e5-8279-f111-ab0e-7ced8ddc4cc6","chip_label":"Make it an Event-Task instead"},{"target_binding_id":"<list-tasks id>","chip_label":"Show my open tasks"}]
```
`list-tasks`:
```json
[{"target_binding_id":"b78b1cf5-3381-f111-ab0f-7ced8ddc4cc6","chip_label":"Create a To Do"},{"target_binding_id":"3d9724e5-8279-f111-ab0e-7ced8ddc4cc6","chip_label":"Create an Event-Task"},{"target_binding_id":"b4503359-1771-f111-ab0e-7ced8ddc4a05","chip_label":"What changed today?"}]
```

**Rationale**: satisfies design §464 — the dispatched `list-tasks` answer "whose chips fire" feeds the reactive SNS (task 043). The create→list chips close the loop ("I made a task → show me my list"); the list→create chips are the grounded next-best-actions after seeing an empty/short list.

---

## 4. The `list-tasks` capability (the one genuinely-new item)

**What "what are my tasks?" / "show my open tasks" must dispatch** (design §464 option (a) + FR-G2). This is the operational home of the FR-G2 promise "show my open tasks → filtered Task grid."

### 4.1 CLAUDE.md §11 three-question justification (NEW surface)

1. **Existing** — What does this overlap with? Verified by live query: **nothing usable**. `insights-ask` = matter-health scoring; `insights-search` = a RAG catalog registration with a **null tool-description** (not chat-dispatchable, "no engine target"); `create-*` draft records, they do not read them; `daily-briefing-narrate` narrates *changes/due-soon* across matters, not an on-demand "my open tasks" list. No generic record-list capability exists.
2. **Extension** — Can I extend an existing one instead? **No, cleanly.** Extending `insights-search` would conflate document-RAG with a structured "my action items" record query and it has no dispatchable description to extend. The honest lean alternative is not "extend an existing capability" but "**author `list-tasks` as the first instance of a small `list-*` family**" (list-tasks now; list-matters/list-documents later by the same authoring pattern) rather than a one-off — see Open Question 2.
3. **Cost-of-doing-nothing** — Concrete failure without it: "what are my tasks?" / "show my open tasks" dispatches nothing → either `no_match_handler` refuses ("I can't do that") or, worse, the LLM free-forms an **ungrounded, hallucinated task list** — the exact ungrounded-suggestion path design §464 rejects. The §1.5 "Suggested Next Steps after an answer" promise and FR-G2 both break; bare conversational Q&A has no grounded home and the SNS (task 043) has nothing to fire chips from.

**Verdict**: justified. Keep it lean — one well-scoped list capability, not a bag of query handlers.

### 4.2 Proposed authoring spec

| Field | Proposed value | Notes |
|---|---|---|
| `sprk_consumertype` | `list-tasks` | new |
| `sprk_name` | `List Tasks (my open action items)` | — |
| UCID | **UC-G-3** (proposed — new, sits with the FR-G2 "show my open tasks" scope) | confirm numbering with the UC index at spec/eval time |
| `sprk_disposition` | **Informational (100000000)** — RECOMMENDED for R1 (see reasoning) | alt: Surface Launch (100000007) — Open Question 1 |
| Action | **NEW deterministic list Action** (see 4.3) | LLM never authors the query |
| `sprk_allowstools` | on the Action: the read is **deterministic/server-parameterized**, NOT LLM-authored FetchXML | mirrors the P1 "LLM never resolves a system-owned set" invariant |
| "my" scoping | filter to caller = **owner OR assignee**, `statecode = Open/Active` | see 4.4 |
| `sprk_chiptransitions` | see §3 (`Create a To Do` / `Create an Event-Task` / `What changed today?`) | chips fire → feeds task 043 |

### 4.3 Disposition + Action reasoning (RECOMMENDATION, with an honest flag)

- **Disposition — recommend Informational for R1.** design §464 frames the fix as "a `list-*` capability … **whose `sprk_chiptransitions` then fire**" — i.e. a grounded *answer* rendered in the Assistant pane (a compact list/card of the user's open tasks) with SNS chips. Informational delivers exactly that with **zero new client surface** and stays fully grounded. **The tension**: FR-G2 literally says "→ filtered **Task grid**," which reads as **Surface Launch** to a workspace Task view. But R1's launch registry (task 012) currently handles only create wizards/forms; a grid/`workspace-tab` launch is explicitly a **future `target.kind`** (surface-launch-mechanism.md §3 / 012 §4.5), i.e. **new client surface work beyond R1**. **Recommendation**: ship `list-tasks` as **Informational** in R1 (in-pane grounded list + chips), with a documented forward path to flip to Surface Launch (`workspace-tab`/grid) once 012's registry gains the layout kind. **Flagged as Open Question 1** — this is a real FR-G2-wording vs R1-scope judgment call for the owner.
- **Action — needs a NEW (small) Action; no reusable list Action exists.** A list capability inherently READS live data (the caller's open tasks), which the create Actions (`allowstools=false`, draft-only) do not do, and no query/list Action exists. Per ADR-039 + memory ("LLM never authors FetchXML"), the read must be a **deterministic, server-owned, parameterized query** ("my open action items": owner/assignee = caller, open state), returning a structured list the capability renders. The LLM's only job is to **select the capability** and optionally pass simple, closed filter args (e.g. `entity: todo|event|both`, `dueWindow: overdue|today|week`) — never author the query. This mirrors the §10 constrained-field-resolver pattern (deterministic, no LLM set-resolution). Recommend the Action live in BFF `Services/Ai` alongside the resolver (verify a task/todo read helper does not already exist before building — the honest "extend before add" check for the Action, distinct from the capability).

### 4.4 "my" scoping + entity span

- **Scope**: caller = **owner OR assignee** AND **open/active** state. Caller identity comes from the existing `CallerSystemUserResolver` (AAD `oid` → `systemuserid`) — the same seam the profile producer uses; do not re-invent.
- **Entity span**: "what are my tasks?" colloquially means both **To Dos (`sprk_todo`)** and **Event-Tasks (`sprk_event` subtype Task)**. **Recommend R1 `list-tasks` spans BOTH**, presented as one "my open action items" list with the entity indicated per row — consistent with the create-side ambiguity (users say "task" loosely). Alternative: task-specific = `sprk_event(Task)` only. **Open Question 2.**

### 4.5 Proposed `sprk_tooldescription` for `list-tasks`

> List the user's own open tasks — both their To Dos (sprk_todo) and their Event-Tasks (sprk_event, subtype Task) that are open and assigned to or owned by them. Use when the user asks to SEE, LIST, SHOW, or REVIEW their existing tasks/to-dos, or asks an open-ended 'what do I need to do / what's on my plate' question — e.g. 'what are my tasks?', 'show my open tasks', 'list my to-dos', 'what's on my plate today?', 'what do I still need to do?'. This capability READS and lists existing records only — it does NOT create anything: if the user wants to CREATE a new task/to-do, use create-task (Event-Task) or create-todo (To Do), not this one. The task list is scoped to the current user (owner or assignee) and open state; the underlying query is performed deterministically by the system — do NOT author a query or resolve filters yourself. Optional args: entity (todo | event | both; default both), dueWindow (overdue | today | week; omit for all open). Informational output — a grounded list rendered in the Assistant pane with suggested next-step chips; no side effects, no record writes.

---

## 5. Owed eval cases for task 051 (append to `owed-eval-cases.md`; E-050-* numbering)

> Format matches E-002-*: utterance → expected dispatch selection (+ output assertion). Surface-open is verified in 012/013, not here.

### Positive dispatch — file/open/close/matter cluster
| # | Utterance | Expected capability | Notes |
|---|---|---|---|
| E-050-01 | "open a new matter for the Acme deal" | `create-matter` | 'open' = matter intake (unchanged from E-002-02; re-assert after cue add) |
| E-050-02 | "open this file" / "open the uploaded document" | `chat-summarize` (NOT create-matter) | file/open cue: 'file' = the document to read |
| E-050-03 | "give me a TL;DR of this file" | `chat-summarize` | 'file' as document |
| E-050-04 | "pull up the Acme file" (legal 'file' = matter record, genuinely ambiguous) | `create-matter` OR elicitation gate | assertion: NOT a hallucinated answer; ambiguous → authored tie-break or gate, never multi-turn interrogation |
| E-050-05 | "close the Acme matter" | `no_match_handler` (honest refusal) | 'close' has no R1 capability — must NOT dispatch a create |

### Positive dispatch — Q&A → list-tasks (design §464 / FR-G2)
| # | Utterance | Expected capability | Notes |
|---|---|---|---|
| E-050-06 | "what are my tasks?" | `list-tasks` | the canonical §464 case |
| E-050-07 | "show my open tasks" | `list-tasks` | FR-G2 literal |
| E-050-08 | "what's on my plate today?" | `list-tasks` (dueWindow=today) | open-ended Q&A → grounded list |
| E-050-09 | "list my to-dos" | `list-tasks` (NOT create-todo) | list verb over create |
| E-050-10 | "what do I still need to do?" | `list-tasks` | open-ended |

### Disambiguation — list vs create (protects the new lane)
| # | Utterance | Expected | Assertion |
|---|---|---|---|
| E-050-11 | "add a task to follow up" | `create-task` (NOT list-tasks) | create verb wins; list-tasks must not over-trigger |
| E-050-12 | "add a to-do to review the NDA" | `create-todo` (NOT list-tasks) | symmetric |
| E-050-13 | "what are my tasks?" | `list-tasks` (NOT `insights-ask` / `insights-search`) | list-tasks is the correct home, not the insights family |

### Chip transitions (one-tap successors fire)
| # | Sequence | Expected | Assertion |
|---|---|---|---|
| E-050-14 | after `list-tasks` renders, user taps **"Create a To Do"** | `create-todo` re-dispatch | chip `target_binding_id` resolves (no dangling target) |
| E-050-15 | after `list-tasks` renders, user taps **"Create an Event-Task"** | `create-task` re-dispatch | resolves |
| E-050-16 | after `create-task` drafts, user taps **"Show my open tasks"** | `list-tasks` dispatch | new create→list chip resolves |

### Output shape (list-tasks reads, never writes; grounded + deterministic)
| # | Assertion |
|---|---|
| E-050-17 | The `list-tasks` turn returns a grounded list scoped to the caller (owner/assignee) + open state, makes **no `dataverse.create_record`** call, and the LLM makes **no LLM-authored query** — the read is deterministic/server-parameterized (mirrors the P1 "LLM never resolves a system-owned set" invariant). |
| E-050-18 | With no open tasks, `list-tasks` returns an honest empty-state (not a fabricated list) and still emits its create chips. |

### To-Do-vs-Event reinforcement (carry-over)
E-002-07..10 remain the authoritative Event-Task-vs-To-Do disambiguation cases; E-050 adds only the **list-vs-create** boundary (E-050-11..13) that the new `list-tasks` capability introduces.

---

## 6. Open questions for the owner (Ralph)

1. **`list-tasks` disposition — Informational vs Surface Launch.** Recommend **Informational** for R1 (in-pane grounded list + chips, zero new client surface, matches §464 "whose chips fire"). FR-G2 literally says "filtered **Task grid**," which is Surface Launch to a workspace Task view — but that grid-launch `target.kind` is a **future** registry capability (012 §4.5), i.e. out of R1's create-only launch scope. Ship Informational now with a documented flip-to-Surface-Launch path? Or pull the grid surface into R1?
2. **`list-tasks` scope — span both `sprk_todo` + `sprk_event(Task)`, or task-specific, or the first of a general `list-*` family?** Recommend "both, as one 'my action items' list." Confirm.
3. **Ambiguous-create default — still `create-task` (Event-Task) as the tie-break?** Task 002 authored Event-Task as the default when the noun is bare "task." Confirm the owner still wants Event-Task (not To Do) as the default draft, with the one-tap "Make it a To Do instead" chip as the correction.
4. **Chip wording** — "Show my open tasks" vs "My tasks" vs "Review my tasks"; "What changed today?" vs "My daily briefing." Owner preference?
5. **Should `create-matter` also emit a "Show my open tasks" chip?** After creating a *matter*, a task-list successor is a weaker fit than after create-task/todo. Proposed: leave create-matter's two chips unchanged. Confirm.
