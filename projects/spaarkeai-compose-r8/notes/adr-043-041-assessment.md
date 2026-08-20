# ADR-043 + ADR-041 Assessment — Track C (AI Edit Placement)

> **Task**: `050-adr-043-041-assessment.poml` · **Rigor**: FULL · **Date**: 2026-08-20
> **Status**: **COMPLETE — GO for Track C** (tasks 051–053), subject to the seven binding constraints in §6.
> **Nature**: READ-ONLY assessment. No `src/` file was modified. No `.claude/` file was modified.
> **Blocks**: all Track C code until this document is accepted.

---

## 0. Executive summary

| Question | Verdict | Path |
|---|---|---|
| **ADR-043** — does the anchor change touch `ActionKind` admission or the supersession-write leg? | **NO — orthogonal, proven in code.** Admission reads four *Binding-row* fields and never opens the payload; the supersession leg is pure ledger-key algebra and never reads the edit body. | **C — comply (declared Path C stands)** |
| **ADR-041** — must FR-C05's "apply anyway?" be a Gate? | **NO — it is structurally not an ADR-041 Gate.** But the *no-second-ask* property it protects **is** live here, and code proves the re-ask is reachable today. The obligation is ADR-040 ledger-durable resolution, not a `PendingPlanManager` gate. | **C — comply, with a stated obligation (§4.4)** |
| **ADR-041** — is FR-A07's "Edit a copy" in scope? | **NO — out of scope.** User-initiated, explicit origin, no capability invocation, no Binding, no `side_effect_class`. ADR-050 governs it, not ADR-041. | **C — comply** |
| **ADR-013** — new CRUD→AI dependency from the envelope addition? | **NO — confirmed by grep, not assumed.** `ProposedEdit` has zero references from `Services/Ai/**`. | **C — comply** |

**No §6.5 escalation is required.** §5 records what *would* have triggered one, and one standing
(pre-existing, not-created-by-R8) question that a reviewer must not mistake for R8 endorsement.

---

## 1. Method

The POML forbids an assessment from ADR text alone. Every finding below is anchored to a file and line
in the worktree at `c:\code_files\spaarke-wt-spaarkeai-compose-r8` as of 2026-08-20. The controlling
distinction the task names — *where an edit APPLIES* vs *how a capability is ADMITTED* — was tested by
locating both mechanisms in code and enumerating every field each one reads.

---

## 2. ADR-043 — the admission path, located in code

### 2.1 The `ActionKind` admission point

`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionDispatchOrchestrator.cs` — `DispatchAsync`
is the single Click-path entry. Admission is four sequential gates, **lines 248–291**:

| Line | Gate | Input it reads |
|---|---|---|
| 248–256 | Action-target present | `binding.ActionId` |
| **258–273** | **`ActionKind` gate** | `binding.ActionKind` — rejects anything not `Prompted`/`Coded` with `dispatch.action-kind-unsupported` (422) |
| 275–291 | Disposition admission | `binding.Disposition` → `DispositionRoutability.IsAdmissible` |
| 325–379 | Confirmation gate | `binding.Risk`, `request.DispatchUncertain`, `args.confirmGateId` |

The class comment states the axis separation explicitly at **line 56**:

> "The ActionKind gate (non-prompted kinds) is a SEPARATE, orthogonal axis owned by E-30."

and at **lines 258–265**:

> "the ActionKind decision is a deterministic total function over the closed kind vocabulary … The kind
> decision lives ONLY here."

**The complete input set of admission is: `binding.ActionId`, `binding.ActionKind`,
`binding.Disposition`, `binding.Risk`** — four fields of the `sprk_playbookconsumer` /
`sprk_analysisaction` catalog rows (`Services/Ai/PublicContracts/Binding.cs:121–124, 226`), plus
`request.DispatchUncertain` and a gate token. **Admission never touches the request payload and never
touches the response payload.** The only request-body reads in the whole admission block are
`TryReadFileIds` (line 212) and `TryReadConfirmGateId` (line 325).

Corroborating: the disposition registry is likewise catalog-keyed —
`Services/Ai/DispositionRoutability.cs:78` marks `BindingDisposition.Compose` routable, and
`OutputRouter.cs:280–281` routes it as a **pass-through that explicitly never parses the payload**:

> "The router stores + returns; it NEVER parses the opaque payload (Compose owns it)."

### 2.2 The supersession-write leg

`src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs`:

- **1539–1600** — `SupersedeComposeOutputAsync`, the endpoint
  (`POST /api/ai/chat/sessions/{id}/compose-outputs/supersede`).
- **1628–1685** — `SupersedeComposeOutput`, the pure computation.
- **1691–1694** — `IsComposeRetraction`.

Every field this leg reads is ledger-key algebra: `SessionOutput.Key`, `.BindingId`, `.Turn`,
`.Disposition`, and the single boolean sentinel `retracted`. The retraction it *writes* (1665–1681) is
a two-member payload — `{"retracted":true,"supersedes_ref":"…"}` — with `SourceRefs` provenance. It
constructs no edit body, and **it reads no edit body**. ADR-043's parenthetical ("retraction =
superseding empty output, no LLM") is satisfied by this code exactly as written.

### 2.3 Where a compose edit APPLIES — a different mechanism entirely

Placement lives in two places, neither of which is on the dispatch spine:

- **Server-side validator (retiring under FR-C04)** — `IComposeEditValidator` is registered in
  `Infrastructure/DI/ComposeModule.cs:28` and consumed at exactly **one** call site:
  `Api/ComposeEndpoints.cs:366` (`POST /api/compose/edit-batch/validate`, registered at 184–185).
  A repo-wide grep for `IComposeEditValidator` / `ComposeEditValidator` / `edit-batch` returns **zero
  hits under `Services/Ai/**` or `Api/Ai/**`**. Retiring it cannot reach the spine because the spine
  has never referenced it.
- **Client-side placement** —
  `Spaarke.Compose.Components/src/widgets/hooks/usePendingRedline.ts:339–388`
  (`findTargetMatches` / `resolveTargetSpans`) resolves `target_text` against the live TipTap document
  at materialize time, i.e. **after** the ledger write, in the browser.

### 2.4 The proof of orthogonality

Track C's envelope change adds `(paraId, span)` to the compose payload — the body inside
`SessionOutput.Payload` (`Services/Compose/ComposeEditModels.cs:75–80`, `ProposedEdit`). For that
change to touch admission, a field of the payload would have to appear in admission's input set.

**It cannot, and the code says so in three independent places:**

1. Admission's input set is enumerated in §2.1 and contains only catalog-row fields.
2. `OutputRouter.cs:275` — the router that carries the payload past admission "NEVER parses the opaque
   payload".
3. `Services/Ai/PublicContracts/ComposeDisposition.cs:28–34` — the published contract draws the line
   as an explicit ownership boundary: the platform owns the *envelope*
   (`ledger_ref` / `disposition` / provenance / supersession keying); the structured-edit schema
   "lives INSIDE the opaque `SessionOutput.Payload`. This contract deliberately bakes ZERO editor
   semantics into the platform surface".

The one place the AI spine looks inside a compose payload is
`Services/Ai/Handlers/SendWorkspaceArtifactHandler.cs:899–909`, and it reads exactly one field —
`body_html` — as a full-document discriminator, explicitly to **exclude** edit-shaped payloads
(comment at 846–851). Adding `paraId`/`span` does not affect that predicate. (It does produce a
constraint — see §6, C-3.)

### 2.5 Verdict

> **ADR-043 is ORTHOGONAL to Track C's anchor change. The declared Path C ("comply") stands.**
> Settling citation: `SessionDispatchOrchestrator.cs:258–291` (admission reads `binding.*` catalog
> fields only) read together with `OutputRouter.cs:275` and `ChatEndpoints.cs:1628–1685` (both the
> carry leg and the supersession leg are payload-agnostic).

### 2.6 What ADR-043 DOES bind on Track C

Orthogonal on the admission axis is **not** "ADR-043 has nothing to say". Three of its MUSTs are live
for tasks 051–053 and are Path-C compliance obligations, not exceptions. They are stated as binding
constraints in §6 (C-1, C-2, C-7).

---

## 3. ADR-041 — what a Gate actually is, in code

Before judging FR-C05, the reference implementation:

- **The decision** — `Services/Ai/Chat/PendingPlanManager.cs:172–188`,
  `RequiresConfirmation(ToolSideEffectClass? sideEffectClass, BindingRisk risk, bool dispatchUncertain)`.
  A **pure static function over three catalog/dispatch inputs**: the tool's declared
  `sprk_sideeffectclass`, the Binding's declared `sprk_risk`, and the dispatcher's own uncertainty
  signal. Doc comment at 154–159: "Driven exclusively by DECLARED catalog metadata … Tool-name lists
  are FORBIDDEN as gating inputs."
- **The suspend** — `SuspendInvocationAsync(PendingInvocation)`. `PendingInvocation` carries
  `GateId`, `SessionId`, `TenantId`, `ToolId`, `BindingId`, `SideEffectClass`, `Risk`, **`ArgsJson`**
  and `Title`. `ArgsJson` exists so the confirmed re-dispatch can **replay the suspended invocation
  verbatim**.
- **The ledger property** — `Models/Ai/Chat/SessionLedgerEntries.cs:298–333`, `SessionGate`:
  `GateId`, `Kind`, `Status` (`pending|confirmed|rejected|expired|superseded|…`), `Turn`, `BindingId`,
  `SideEffectClass`, `OutputKey`, `CreatedAt`, `ResolvedAt`.
- **The live wiring** — `SessionDispatchOrchestrator.cs:325–379`. Note lines 313–318: on
  `AlwaysConfirm` the orchestrator **suspends instead of executing** — "the Action never runs, so there
  is NO LLM spend and NO side effect until the user explicitly confirms."

**A Gate is therefore, structurally: a PRE-execution suspension of a catalog-declared capability
invocation, whose fire/no-fire decision is a pure function of catalog data, and whose resumable
payload is the invocation's own args.**

---

## 4. ADR-041 vs FR-C05 — "this clause changed since the suggestion — apply anyway?"

### 4.1 Where FR-C05 fires

At **materialize/apply** time in the browser: `usePendingRedline.ts:663–691` today, when
`resolveTargetSpans` returns `not_found`/`ambiguous`. Under Track C the same decision point becomes
"the `paraId` resolves but its content changed since the proposal" — the stale-target branch of
FR-C05.

### 4.2 Why it is NOT a Gate — four structural reasons, each code-anchored

1. **No invocation exists to suspend.** A Gate suspends *before* execution. At FR-C05's decision point
   the Action has already run, the LLM spend has already happened, and the output is already a stored
   `SessionOutput` (`OutputRouter.cs:217–224` writes before any render). There is nothing left to
   suspend and no `ArgsJson` whose replay would produce the user's intent.
2. **No catalog datum can decide it.** `RequiresConfirmation` (`PendingPlanManager.cs:172–188`) takes
   `sideEffectClass`, `risk`, `dispatchUncertain`. FR-C05's trigger is *"the document drifted between
   proposal and apply"* — a runtime fact about the live editor document, knowable only client-side and
   only at apply time. It is not a declared property of the capability and it is **not a risk tier**.
   Modelling it as a Gate would force one of two ADR-041 violations: either invent a synthetic
   `sprk_risk` value that fires on every edit regardless of drift (a gate that fires when nothing is
   stale — friction ADR-041's D-F1 exists to remove), or compute gate-fire from runtime document state,
   which is precisely the "runtime judgement" the MUST-NOT at ADR-041 line 60 forbids.
3. **The apply is not a gated side effect under D-F0(b).** The read/write asymmetry attaches to
   declared surfaces: `ToolSideEffectClass` on `sprk_analysistool` rows (agent-loop tool calls) and
   `BindingRisk` on `sprk_playbookconsumer` rows (dispatched capabilities) — the two inputs of
   `RequiresConfirmation`. Accepting a pending redline is neither: it mutates **unsaved local TipTap
   document state** via `usePendingRedline.accept` (`usePendingRedline.ts:854–868`). The durable side
   effect is the **Save**, which is a separate, user-initiated action on the Compose endpoints
   (`Api/ComposeEndpoints.cs`), entirely off the AI dispatch spine, and is governed by Tracks S/A.
4. **Origin is already explicit.** ADR-041 requires deterministic fail-closed origin classification,
   "Click ⇒ explicit". FR-C05's prompt is raised in direct response to a user click on a rendered
   affordance. Under the tier row, an explicit-origin, complete, non-catalog-risk action is not gated.

> **Verdict: FR-C05 is NOT an ADR-041 Gate. It MUST NOT be built as a `PendingPlanManager`
> suspend/resume.** Building it as one would put a *post*-execution dialog on a *pre*-execution
> mechanism, and would require either a fabricated risk tier or a runtime risk judgement — an ADR-041
> violation in the act of "complying" with it.

### 4.3 The part of ADR-041 that IS live — and the code proving the hazard is real

The rule the "apply anyway?" prompt genuinely engages is the *no-second-ask* invariant:

> "**MUST NOT** re-ask for a confirmation whose state is already recorded on the gate ledger."
> "**MUST** treat confirmation state as a Gate-ledger property (ADR-040) — a second ask for the same
> request is structurally impossible."

**This re-ask is reachable today.** Three code facts compose into it:

1. `ComposeWorkspace.tsx:2969–2974` — the untargeted reopen / refresh-durability pass picks the
   highest-turn edit-shaped compose output and re-materializes it, guarded only by
   `latestEdit.key !== lastMaterializedKey`.
2. `lastMaterializedKey` is React state — **it does not survive a page refresh**.
3. `useEditSupersession.tsx` (`clearTrackedEdit`, DEF-12) — an **ACCEPT performs no ledger write**:
   "clear the tracked edit WITHOUT a ledger write … there is nothing to retract". Only *undo* and
   *try-another* write a supersession.

Consequence: a user answers "apply anyway — yes", refreshes, and the same compose output
re-materializes against a document that has now definitively changed → the same question is asked a
second time, about a decision already made. That is the exact failure shape ADR-041's ledger-residency
MUST exists to make impossible, arriving through ADR-040 rather than through the gate engine.

### 4.4 The obligation on task 052 — precise enough to implement without re-assessing

FR-C05's confirmation MUST be built so that:

- **O-1 — Not a Gate.** No `PendingPlanManager.SuspendInvocationAsync`, no `SessionGate` entry, no
  `gateId` on the compose path. Do not add a `BindingRisk` value or a `side_effect_class` to make it
  fire. Do not route it through `RequiresConfirmation`.
- **O-2 — Resolution is ledger-durable, not component-local.** The user's answer MUST survive a page
  refresh and a re-materialize. `React.useState` / a ref / `sessionStorage` alone do **not** satisfy
  this; `lastMaterializedKey` is the demonstrated counter-example (§4.3.2).
- **O-3 — Reuse the existing seam; create no new one (root §11).** Two shipped carriers exist and are
  sufficient; pick one, do not build a third:
  - the **FR-17 supersession leg** (`ChatEndpoints.SupersedeComposeOutputAsync`) — an accepted or
    dismissed suggestion appends a superseding entry so it is no longer the head and is not replayed;
    or
  - an **ADR-040 `WidgetEvent` entry** (`SessionLedgerEntries.cs:269–288` — "A widget user-action
    (selection, highlight, **edit**, …) recorded as a consumable session event"), keyed by the edit's
    `{bindingId}@t{n}` (or `…#{i}` sub-key), which is exactly what this entry type is for.

  Recommendation: the supersession leg for accept/dismiss (it already exists, already writes, and
  already makes the re-materialize a no-op); a `WidgetEvent` only if the resolution must be recorded
  without removing the suggestion from the head.
- **O-4 — Append-only, keyed by the edit's ledger key.** No mutation of the original compose entry
  (ADR-040 MUST NOT: "mutate or delete ledger entries within a session"). The resolution references the
  superseded key, as the existing retraction does (`ChatEndpoints.cs:1679`).
- **O-5 — Idempotent re-materialize.** After the resolution is written, the reopen pass at
  `ComposeWorkspace.tsx:2969–2974` MUST NOT re-raise the question for that key. This is the acceptance
  test: *apply-anyway → refresh → the prompt does not reappear.*
- **O-6 — UI shell.** ADR-050 / `SprkModal`+`ConfirmModal` from `@spaarke/ui-components`; semantic
  tokens only (ADR-021). No bespoke chrome — same rule FR-A07 carries.

Scope note for the owner: O-2/O-3/O-5 are slightly beyond FR-C05's literal text ("deterministic and
explainable"), because the literal text is satisfiable by a component-local dialog that re-asks after a
refresh. They are stated as binding because that outcome would land a fresh instance of the exact
failure mode ADR-041 was written to close, and the fix is one call to a leg that already ships. If the
owner judges this out of Track C's budget, it must be filed as a tracked deferral, not dropped
silently.

---

## 5. ADR-041 vs FR-A07 — "Edit a copy" (reasoning task 043 can cite)

**Verdict: OUT of ADR-041 scope.** Reasoning, in the order ADR-041's overlay precedence evaluates:

1. **Injection-suspect** — no. The trigger is a deterministic server-side capability-gate evaluation
   over document constructs, not model output and not document-derived instruction text.
2. **Safety-perimeter degradation** — no. The gate is the *conservative* branch already; FR-A07 does
   not weaken it. Note the direction: FR-A07 makes the outcome *safer* than the plain read-only gate it
   supersedes, because "the original is never written to" (spec FR-A07 / Owner Clarifications).
3. **Incomplete args** — no. The single decision ("fork or don't") is complete at the click.
4. **Origin** — **explicit.** ADR-041's rule is "Click ⇒ explicit". The user clicks "Edit a copy" on a
   read-only banner they are already looking at. There is no AI origin anywhere in the path — no
   Binding, no Action, no `bindingId`, no dispatch, no LLM.
5. **Tier row** — not reached; and there is no catalog row to carry a tier. `RequiresConfirmation`
   requires a `ToolSideEffectClass` (from `sprk_analysistool`) or a `BindingRisk` (from
   `sprk_playbookconsumer`). FR-A07's fork has **neither**, because it is not a capability invocation.

FR-A07 *does* perform a real write (a new SPE item). ADR-041 does not gate every write in the product —
it gates **AI-invoked** side effects, which is why both of its inputs are AI-catalog columns. A user
clicking a button to create their own copy of their own document is ordinary product UX. Its
confirmation ("you are told what the copy will drop and you confirm") is required by **FR-A07 itself**
and shaped by **ADR-050** (`ConfirmModal`/`SprkModal`) — not by ADR-041.

**Cite as**: *FR-A07 is user-initiated with explicit origin and no catalog-declared capability;
`PendingPlanManager.RequiresConfirmation` has no input that could evaluate it. ADR-041 does not apply.
The confirmation is an ADR-050 modal.*

---

## 6. ADR-013 — no new CRUD→AI dependency (confirmed, not assumed)

Verification performed:

- `grep -rn "ProposedEdit" src --include=*.cs` → **11 hits, all inside
  `src/server/api/Sprk.Bff.Api/Services/Compose/`** (`ComposeEditModels.cs`, `ComposeEditBatch.cs`,
  `ComposeEditTransaction.cs`, `ComposeEditValidator.cs`, `IComposeEditValidator.cs`). **Zero
  producers or consumers under `Services/Ai/**`.**
- `grep -rn "target_text|match_mode|new_text" src/server/api/Sprk.Bff.Api/Services/Ai
  src/server/api/Sprk.Bff.Api/Api/Ai` → **2 hits, both doc-comments**
  (`ComposeDisposition.cs:30` describing the opaque payload; `SendWorkspaceArtifactHandler.cs:849`
  explaining what its discriminator *excludes*). **Zero code reads.**
- Direction of the one real dependency: `Services/Compose/ComposeDraftDisposition.cs` consumes
  `Services/Ai/PublicContracts/ComposeDisposition` — i.e. Compose→AI **through the PublicContracts
  facade**, which is exactly the ADR-013 MUST at line 44. Adding fields to a Compose-owned payload does
  not add a dependency edge in either direction.

**Verdict: no new CRUD→AI dependency. ADR-013 Path C confirmed.**

---

## 7. Binding constraints for Track C tasks 051–053

Seven, all Path-C compliance (no exception, no amendment):

- **C-1 (ADR-043) — Do NOT extend the operand vocabulary.**
  `Services/Ai/Context/ContextBinder.cs:59–64` holds a **closed, hardcoded** three-name operand
  vocabulary — `selectionText` / `changesText` / `documentText` — and
  `TryFindDeclaredOperandField` (356–385) matches **top-level** args properties from that list only,
  resolving to a single `{field: value}` operand. Track C's `paraId`, span, and FR-C03's enumerated
  closed paraId set MUST ride as `args.slots.*` members and/or inside the Action's
  `sprk_inputschema`-declared operand value. **Adding a fourth `OperandVocabulary` entry is a
  spine change** and would convert this Path C into a Path B. Good news: the client already sends
  `selectionAnchorStart`/`selectionAnchorEnd`/`targetParaId` in `args.slots`
  (`ComposeAiToolbar.tsx:781–796`), so FR-C01's request half is already on the wire — FR-C01 is a
  response+apply change, not a new request channel.
- **C-2 (ADR-043) — Seam test is the definition of done.** ADR-043 line 25: a dispatch/execution change
  is gated on a `tests/integration/seam/**` vertical-slice test (consumer → dispatch → stored
  `SessionOutput` → render/frame). "A green contract-shape test is NOT sufficient." Track C changes the
  payload shape crossing that slice; land the seam test alongside. `tests/integration/seam/Compose/`
  already hosts 30+ such tests (`ComposeParaOffsetAnchorSeamTests.cs`,
  `ComposeReferenceMapSessionLedgerSeamTests.cs` are the nearest neighbours).
- **C-3 (ADR-040) — Do not add `body_html` to an edit payload.**
  `SendWorkspaceArtifactHandler.cs:899–909` uses non-empty `body_html` as its full-document
  discriminator and comments (846–851) that it deliberately excludes edit payloads. An edit payload
  that grows a `body_html` member would silently seed a whole workspace tab from an edit.
- **C-4 (ADR-040) — Watch the 128 KB inline payload cap.** `SessionLedger.CapInlinePayload` caps
  inline payloads at 128 KB; over-cap entries become a truncation marker, and
  `ChatEndpoints.ProjectComposeOutputs` (1504–1526) **skips truncated entries entirely** — a truncated
  whole-document change list vanishes from the read projection. Adding `paraId` + span to every entry
  of an N-edit change list grows the payload. Measure the worst realistic case (whole-document revise)
  and confirm headroom, or state the degradation.
- **C-5 (ADR-041, from §4.4) — FR-C05 obligations O-1…O-6.** Not a Gate; ledger-durable resolution;
  reuse the FR-17 supersession leg or an ADR-040 `WidgetEvent`; append-only; idempotent
  re-materialize; ADR-050 modal.
- **C-6 (ADR-043) — Do not touch the supersession leg.** `ChatEndpoints.cs:1628–1685` is the
  ADR-043-sanctioned supersession-write leg. It is payload-agnostic by construction. Track C has no
  reason to open it, and doing so would move this assessment's verdict.
- **C-7 (ADR-043 / ADR-039) — No new dispatch protocol, no new catalog kind.** FR-C03's closed-set
  validation belongs in the Compose-owned validation path (server-side, per FR-C03 "rejected loudly"),
  not as a new admission gate in `SessionDispatchOrchestrator`. The compose Action stays
  `ActionKind.Prompted` + `disposition = compose`; changing its `sprk_inputschema`/output schema is a
  **catalog DATA** change, which is what ADR-039 wants.

---

## 8. §6.5 disposition

**No ADR conflict is being surfaced. No escalation block is required.** Both declared Path Cs stand,
and the ADR-013 question is clean.

For the reviewer's benefit, the two things that *would* have forced a §6.5 block, and why neither
fired:

- **Would have fired**: an admission gate reading a payload field, or the supersession leg parsing an
  edit body. Neither exists — admission's input set is enumerated at §2.1 and the supersession leg's at
  §2.2.
- **Would have fired**: FR-C05 requiring a runtime-computed risk classification to gate. It does not,
  because it is not a gate at all (§4.2).

### Standing question — NOT created by R8, flagged so Track C's landing is not read as endorsement

ADR-043 line 23 says the system "MUST admit deterministic/interactive capabilities (**compose edit**,
retraction) through the declarative spine via a deterministic `ActionKind` + a sanctioned
supersession-write leg." Today:

- the **retraction** half is satisfied — `ChatEndpoints.SupersedeComposeOutputAsync` is the sanctioned
  leg (§2.2);
- the **deterministic `ActionKind`** half is realized in the spine (`ActionKind.Coded` +
  `ICodedWorkflowRegistry`, `SessionDispatchOrchestrator.cs:387–412`) but **compose's deterministic
  apply leg does not currently run on it** — it runs on Compose endpoints plus client apply. The
  *proposal* half is genuinely LLM-driven, so `Prompted` is correct for it; the open question is only
  about the apply leg.

This predates R8 by two releases and Track C neither creates nor worsens it. If anything Track C moves
in the ADR's direction: it replaces client fuzzy text search with a deterministic, server-supplied and
server-validated anchor (FR-C02 `CitationResolver`, FR-C03 closed-set validation), which is the
prerequisite any future coded-apply leg would need. Recorded here so a reviewer does not read "Track C
is ADR-043-orthogonal" as "compose edit is fully ADR-043-conformant". Recommended disposition: file as
a follow-on for the AI-platform owner, not a Track C blocker.

---

## 9. Decision

> ## **GO for Track C code (tasks 051–053)**, subject to constraints **C-1 … C-7** in §7.

Acceptance-criteria trace:

| Criterion | Where satisfied |
|---|---|
| ADR-043 finding definitive, cites admission point + supersession leg by file/line | §2.1, §2.2, §2.5 |
| §6.5 block if touching / Track C BLOCKED | Not triggered — §8 records why |
| ADR-041 FR-C05 (AI-origin) definitive in/out + reasoning | §4.2 (out, as a Gate), §4.3–4.4 (the live obligation) |
| ADR-041 FR-A07 (user-initiated) reasoning task 043 can cite | §5 |
| If a Gate: obligation stated precisely for task 052 | Not a Gate; the substitute obligation is O-1…O-6, §4.4 |
| ADR-013 confirmed, not assumed | §6 (two greps, results quoted) |
| GO/BLOCK stated explicitly | §9 |
| NEGATIVE: no `src/` file modified | Confirmed — read-only session |
| NEGATIVE: no finding rests on ADR text alone | Every verdict carries a file:line citation |
