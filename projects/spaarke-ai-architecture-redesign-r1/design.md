# Spaarke AI Architecture Redesign R1 — Design Document

> **Status**: DRAFT v1.0 — 2026-07-05, for operator review → `/design-to-spec`
> **Authors**: Operator + Claude Fable 5 (converged over the 2026-07-04/05
> strategic-pivot sessions: canonical doc v0.1→v0.4, code audit, greenfield
> design, overlay convergence, ADR review — all decisions operator-ratified)
> **Parent epic**: #421 SPAARKE AI
> **Authoritative companions** (this design summarizes; they govern detail):
> - **Target**: [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) **v0.4** (converged; ratified decision register §7.7)
> - **Sequencing**: [`notes/audit-inputs/SPAARKE-AI-MIGRATION-MAP.md`](notes/audit-inputs/SPAARKE-AI-MIGRATION-MAP.md) (P0-P4)
> - **Per-component verdicts**: [`notes/audit-inputs/OVERLAY-MATRIX.md`](notes/audit-inputs/OVERLAY-MATRIX.md) (approved, E-1..E-5 ruled)
> - **Evidence + rationale**: [`notes/audit-inputs/`](notes/audit-inputs/README.md)

---

## 1. Problem statement

Spaarke has executed 30+ AI-related projects and produced useful artifacts but
**not a working AI system the end user can rely on**. The 2026-07-05 full-code
audit quantified why: each project built a capability *plus its own private
plumbing* — the estate now carries **ten coexisting intent-detection
mechanisms** (several firing sequentially inside one chat turn), **four
parallel routing configuration surfaces**, ~24 duplicate/overlapping
implementations (three client summarize stacks, two orchestration engines, two
gate stores), a substantial dead-code register, and **no carrier for
cross-capability composition** — capability outputs are streamed to the screen
and forgotten, so the platform's differentiating bet (capabilities composing in
one session) has no mechanism at all. The most recent symptom: document
summarize — the single "working" capability — broke on a user typo because
dispatch depended on a hardcoded regex.

The governance root cause is also known: the domains that drifted worst
(dispatch, session semantics, capability model) had **no ADR**, while two
recent ADRs actively codified the mechanisms now being retired. Both fixed
(amendments applied; ADR-039/040 authored).

## 2. The delivered product (end-user terms — this section IS the acceptance backbone)

**The Spaarke Assistant becomes a working legal-ops copilot**: a user drops in
a document or types a request in plain language and reliably gets analysis,
answers with citations, records created, and drafts written — each step
flowing into the next in one conversation.

Every phase gates on a **user-verifiable UAT script**, not artifact
completion. A phase that ships its components but fails its script is not done.

**The browser rule (binding — added at operator review)**: every G-gate UAT
script is executed by a **user in the Spaarke UI**, end-to-end, with the
result *rendered on screen* — a passing curl, a green test, or a log line does
NOT satisfy a gate. "We didn't build an output for that" is a gate failure by
definition: if the user can't see it and act on it, it doesn't exist. The only
exception is **P0**, which is deliberately dark (foundations) and gates on
engineering evidence — it is the single phase with no user-visible gate, and
it says so.

| Gate | The user can now… (UAT script summary) |
|---|---|
| **G-P1** | Upload a file to the Assistant and — typing nothing — see what the document is and get a summary with working next-step chips. Any phrasing or typo of "summarize" also works. (Kills: inert uploads, the regex.) |
| **G-P2** | Type anything and get one of exactly four outcomes: it does it; it asks a clarifying question and proceeds ("what's the due date, and assign to you?"); it answers ad-hoc questions over documents AND Dataverse records with citations ("show me all open Acme matters over $100k" — never pre-built); or it says honestly what it can't do. It remembers the session ("email that summary to John" works — the summary is addressable). Record writes always confirm before executing. |
| **G-P3** | The flagship journey runs as ONE conversation: upload contract → auto-summary → chat about a clause → "create matter from this" → pre-filled wizard → confirm → "draft the client letter" (references the real summary + real matter) → "create a follow-up task" → done. Plus: the daily briefing email arrives each morning; the assistant behaves identically on record forms, workspace, and SPA. |
| **G-P4** | Nothing new — everything above is *boring*: reliable, fast, telemetered, on a codebase smaller than today's. |
| **G-M** (maker gate, at P4 — added at operator review) | **A business analyst — not an engineer — authors a brand-new small capability entirely as data** (JPS prompt + output schema + Binding with chips + eval case, via PlaybookBuilder/ScopeConfigEditor, ZERO deploys), and a user then invokes it in the UI and gets a rendered result. This gate tests the "second product" claim directly; if it fails, the platform claim fails. |

**The second product**: after G-P3, adding legal capability #N+1 (NDA review,
invoice validation, obligation extraction…) is **a catalog row a business
analyst authors** (prompt + output schema + chips + eval case, no deploy) —
not another engineering project. That is the structural fix for the
30-projects-no-product pattern.

## 3. Target architecture (summary — canonical doc v0.4 governs)

**Three entry paths, one brain, one ledger, two closed catalogs:**

- **Event path** — manifest rules fire capabilities on events (upload →
  auto-classify + summarize; schedule → daily briefing; inbound email →
  triage). Deterministic; bounded (cost cap, opt-out, bulk rules).
- **Click path** — chips/ribbons/wizards carry a `binding_id`; invocation is
  deterministic, zero LLM.
- **Text path** — ONE bounded function-calling agent turn (budgeted tool
  calls, cited reads, gated writes, chain persisted). The only probabilistic
  decider on the platform. **No classifier stack, no thresholds, no trigger-
  phrase index** — capability tool descriptions are the intent surface;
  regressions are caught by a golden-utterance eval suite in CI.
- **Session Ledger** (ADR-040) — append-only, addressable, typed
  (Doc/Output/ToolChain/Turn/WidgetEvent/Gate) over the existing 3-tier store.
  Storage precedes rendering; disposition (informational / work_product /
  overlay / email / record / notification) is the only rendering contract.
  This is the composition carrier the estate never had.
- **Execution** — `prompted` Actions (JPS render → one structured LLM call;
  the existing LinearConsumers stack) and `coded` Actions (registered C#
  workflows; Daily Briefing is the first). The node-graph engine is **frozen**
  (Insights pipelines only; retired by attrition). No maker-authored control
  flow, ever — makers own prompts/schemas/scopes/bindings/chips (data), never
  branches (code).
- **Manifest** — **no new tables**: `sprk_analysisaction` (Action — execution
  unit) + `sprk_playbookconsumer` (Binding — invocation unit) extended;
  `sprk_analysistool` extended to the 8-field tool contract. The Binding table
  is the ONLY routing surface. "Capability" and "playbook" are vocabulary, not
  schema.
- **Tools** — the existing typed-handler framework, extended; new `dataverse.*`
  handlers mirror the GA Dataverse MCP tool contracts over BFF-OBO (per the
  July-2026 research: delegated-only auth = user-context parity; Copilot-credit
  metering avoided; swap-ready if the P0 OBO spike passes).
- **One Confirmation Gate**, one pending store, `side_effect_class`-driven.
- **Client** — kept: SprkChat, PaneEventBus, widget registries,
  StructuredOutputStreamWidget. ONE `dispatchConsumer(bindingId, args)` helper
  replaces the three per-surface dispatch stacks; chips carry binding ids.

All architecture decisions are RATIFIED (v0.4 §7.7: D1-D12 as amended;
OQ-1..OQ-4 resolved; overlay exceptions E-1..E-5 ruled). This project
implements; it does not re-open design.

## 4. Scope

### 4.1 In scope (the five phases — migration map v1.0 governs detail)

- **P0 Foundations**: ledger model + persistence (dark), catalog column
  extensions, `ICodedWorkflow` convention, `dataverse.*` handlers + 8-field
  tool rows, boot reconciliation health checks, registration-hygiene fixes,
  eval-suite scaffold, **OBO-for-`mcp.tools` spike**, user-OBO verification.
- **P1 First capability**: chat-summarize as Action+Binding; Event path live
  (upload composite); Click path (binding-id chips + the one client helper);
  E-2 Insights-output→ledger adapter; close the r7 tactical branch keeping the
  4 sound fixes and dropping the 3 dispatch patches. **ADR-039 → Accepted.**
- **P2 Text-path hard cutover**: loop contract (budget/cites/chain);
  gate unification (D12); loop-native elicitation; refusal binding +
  telemetry; chat NL cutover; **dispatcher stack deleted same phase**
  (PlaybookDispatcher, IntentReranker, CandidateSelector, CompoundIntentDetector);
  legacy Chat/Tools deletion.
- **P3 Consumer + client consolidation**: all remaining consumers as Bindings
  (document-profile, matter/project pre-fill, workspace summarize,
  email-analysis, Insights ask/search); Daily Briefing as first coded
  composite (narrate flag deleted); **plus two NEW proving capabilities the
  G-P3 script depends on** (gap closed at operator review — the script
  promised outputs the scope didn't build): **`draft-correspondence`**
  (prompted Action → `email.draft` write-shape over the existing Graph draft
  path, gated, rendered as reviewable draft — the §3.9 terminal hub, ~22
  inbound edges) and **`create-task`** (prompted Action → `dataverse.create`
  of `sprk_event(type=task)` under the conversational-confirm gate — the
  walkthrough's steps 10-14); the three routing-appsettings blocks
  deleted; engine-shell deletions; ConversationPane decomposition; LegalWorkspace
  + Compose summarize onto the shared helper; FieldDelta path deleted at cutover.
- **P4 Sweep + hardening + graduation**: Track-B completion (grep-verified),
  catalog governance, new data-model docs, ADR A-3 refreshes, PlaybookBuilder
  canvas de-scope → BA scope/prompt/binding editor, publish-size verification,
  `/test-diet`, Action Engine R1 re-based spec filed.
- **Track B deadwood sweep runs continuously from P0** (the ~30
  dependency-free deletes start immediately). **Scope restated per operator
  direction**: the sweep covers ALL dead technical debt in the inventory's §9
  register — including code with NO relationship to the target design
  ("not in the way" is not a reason to keep dead code); every entry ends
  grep-verified-deleted or carries a written keep-with-reason.
- **P0 also carries portfolio reconciliation** (gap closed at operator
  review): formally re-scope/close `spaarke-ai-platform-unification-r7`
  (this project absorbs its remaining waves), re-point the R4
  daily-update-service graduation gate and the Action Engine R1 /
  insights-engine-r3 resumption triggers from "R7 ships" to this project's
  phases, and file the Action Engine re-based spec stub.

### 4.2 Explicit non-goals

- Deep legal capabilities beyond the proving set (contract full-review, NDA
  clause review, redlining, invoice validation) — authored as catalog rows
  AFTER this platform ships; write-shape tools for redlining are follow-on.
- Runtime Dataverse MCP transport (pending the P0 spike; contracts are
  swap-ready regardless).
- Relocating the agent loop into Azure AI Foundry Agent Service (wrong
  user-context model for a headless multi-tenant BFF — researched, rejected).
- Re-migrating the frozen Insights pipelines; any maker-facing graph authoring.
- New Dataverse tables for the manifest.

### 4.3 Cutover doctrine (binding)

Existing-customer continuity is NOT a constraint (operator, 2026-07-05):
**hard cutover per surface**, no parallel-run, no compat shims retained.

## 5. Constraints, hot paths, placement

- **Hot-path declaration**: <hot-path-declaration> BFF=**Y** ·
  SpaarkeAi=**Y** · ci-workflows=**N** · skill-directives=**Y** (P4 updates
  jps-* skills + PlaybookBuilder-related guidance after canvas de-scope) ·
  root-CLAUDE.md=**N** </hot-path-declaration>
- **Placement justification** (CLAUDE.md §10): every server component stays in
  `Sprk.Bff.Api` per ADR-013's four extraction criteria (latency +
  transactional coupling with session/SSE state). No new deployables. Publish
  size verified per task; expected **net reduction** (the project deletes more
  than it adds — the v0.4 rewrite itself was net −111 lines of design).
- **Component justification** (CLAUDE.md §11): pre-answered for every new
  component in the overlay matrix (3.5 build-fresh items; everything else
  extends existing code with grep-cited evidence).
- **Binding ADRs**: ADR-039 + ADR-040 (this project promotes them to
  Accepted at P1/P0); amended ADR-013 (capability invocation canon) + ADR-037
  (section streaming only; engine-steering rescinded); plus the standing set
  (008, 009/014, 010, 015, 016, 018, 019, 028, 029, 030, 031, 032, 036, 038).
- **Umbrella commitments** (canonical §5.10): InsightArtifact envelope,
  IInsightsAi facade + honesty primitives, Assistant contract v1.1,
  widgets-r1 topic-registry pattern — all preserved; Action Engine R1 re-bases
  on this design.
- **Testing**: ADR-038 pyramid; the golden-utterance eval suite is a KEEP-class
  `tests/integration/contract/**` asset; every P0-P3 code task runs FULL rigor.

### ADR Tensions

None open. The two conflicts this design would have raised (ADR-013 playbook
canon; ADR-037 engine-steering) were resolved via approved Path-B amendments
on 2026-07-05 *before* this project starts — see
[`notes/audit-inputs/ADR-REVIEW-VS-GREENFIELD.md`](notes/audit-inputs/ADR-REVIEW-VS-GREENFIELD.md).
ADR-039/040 are Proposed with promotion gates inside this project's phases.

## 6. Risks (top 5)

| Risk | Mitigation |
|---|---|
| Loop dispatch accuracy below expectation at launch | Eval suite from P0; deterministic context pre-filter; ADR-039-documented pre-filter re-entry at ~100+ catalog entries |
| Ledger model change destabilizes live sessions | P0 ships dark (no readers) → P1 writes → readers follow; session TTLs bound blast radius |
| P2 hard cutover regresses chat UX | G-P2 UAT script is the gate; Event/Click paths structurally unaffected |
| Scope creep back into mechanism-building | ADR-039 MUST NOTs + adr-check at Step 9.5; the spec's FRs derive from the migration map only |
| Frozen-engine drift | ADR-039 + amended ADR-037; code review flags any new engine-bound capability |

## 6.5 Execution tooling: `/goal` (evaluated 2026-07-05 — adopt at wave level)

Claude Code's `/goal` (GA v2.1.154) sets a completion condition an independent
evaluator checks per turn until met. **Adopted for this project at the
wave/work-batch level only** — its evaluator judges observable evidence in the
transcript, which matches the migration map's grep-verified acceptance style.
It is **prohibited at phase-gate level**: G-P1..G-P4 are human UAT gates by
design. Composition rules (goal conditions include shown evidence + scope bind
+ turn cap + "Step 9.5 gates passed"; conditions authored into wave
definitions at task-create time) are in
[`notes/goal-feature-evaluation.md`](notes/goal-feature-evaluation.md) and
should land in this project's generated CLAUDE.md.

## 7. What /design-to-spec should produce

- FRs grouped by phase P0-P4, each phase carrying its **G-gate UAT script as
  acceptance criteria** (§2, incl. the browser rule and the G-M maker gate) —
  user-verifiable, not component-complete.
- NFRs: publish-size ceiling (ADR-029); eval-suite green as merge gate from
  P1; telemetry (`dispatch_refused`, tool budgets); ADR-015 tier compliance
  for ledger entries; hard-cutover verification (grep-zero for retired
  mechanisms/config keys per phase); **plus the four added at operator review**:
  - **Prompt-injection threat model (security NFR)**: uploaded-document text
    and tool results are UNTRUSTED input to the text-path loop; verify
    `AgentContentSafetyMiddleware` coverage on the loop path; injection
    attack cases (hostile document instructing exfiltration/side effects) in
    the eval suite; the confirmation gate is the last line, not the only one.
  - **Latency targets per gate**: upload→first-summary-token and
    text-turn TTFB budgets (capability-tool schemas in context → prompt
    caching required as an implementation note).
  - **Per-tenant AI usage metering**: ADR-016 budgets surfaced as a
    billing-grade per-tenant meter (product/pricing requirement in this
    market) — at minimum the counter lands; the admin/pricing surface may be
    a named follow-on.
  - **Output-quality checks in the eval suite** beyond dispatch: per-capability
    schema-conformance + citation-integrity assertions, so a prompt edit that
    degrades output fails CI, not UAT.
- Wave structure = P0-P4 + continuous Track-B stream; dependencies per the
  migration map's spine. **Every generated wave definition (plan.md wave
  section + TASK-INDEX wave header) includes a pre-authored `/goal` condition
  per §6.5's composition rules** (shown evidence + scope bind + turn cap +
  "Step 9.5 gates passed") — ready to paste at wave start. This is a
  project-scoped pilot: NO changes to the `/project-pipeline` or
  `/task-create` skills; if the pattern proves out by P1, file a `/defer` to
  promote it into the skills via `ai-procedure-maintenance`.
- The Track-B delete register as enumerated cleanup tasks (grep-verified
  acceptance).
- **Named deferrals (explicit, not silent)**: admin observability dashboards
  (audit-trail UI over ledger/Tier-2, cost dashboards, refusal-backlog view)
  → follow-on project, filed via `/defer` at P4; deep legal capabilities
  beyond the proving set per §4.2.
