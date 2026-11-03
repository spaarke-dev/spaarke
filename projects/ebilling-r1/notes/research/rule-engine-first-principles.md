# One rules engine or two? — a first-principles analysis for Ontology + eBilling

> **Status:** Research note · 2026-10-14 · feeds `design.md` §5.3 item 4 and owner decision §8.3 in `solution-overview.md`.
> **Question:** Should Spaarke have one rules engine serving the Ontology platform and the eBilling module, or two engines where one feeds the other?
> **Answer in one line:** **One rules *system* with two *evaluation strategies*.** Share the rule representation, the authoring/validation seam, the policy store with versioning and citation, the closed consequence catalogue and the Signal / Decision Record outputs. Keep fact assembly and the evaluation loop workload-specific: FetchXML push-down for the Ontology's set-based detection, an in-memory fact set for eBilling's transactional validation. eBilling co-builds; it does not wait, and it does not fork.

Terms used below: **engine** = the shared language, store, validator and outputs; **evaluator** = the code that assembles facts for one workload and runs rules over them.

---

## 1. The two workloads, verified against the documents

| | Ontology (`spaarke-ontology-platform-r1`) | eBilling (`ebilling-r1`) |
|---|---|---|
| Question asked | "Which records are now in state X?" — detection over the record graph, scheduled or event-driven (design.md §1.2: membership is "a deterministic predicate over Dataverse (FetchXML)") | "Is *this* invoice compliant, and what follows?" — validation of one submission inside the session (overview §4.4; queued per ADR-052, result shown in the SPA) |
| Unit | A subject record (matter, task, communication); set-based, cross-entity, time-windowed, re-raise / quiet windows (`PredicateCompiler` `now±Nd`, `quietWindowDays`) | One invoice header + N lines, plus reference data: approved timekeepers, rate cards with effective dates, UTBMS code sets, matter dates, prior invoices (OCG reference §4, rows 3–6, 10–15) |
| Rule shapes | Existence / absence over related entities; thresholds over materialised facts (rollup + calculated columns, CM-3) | Per-line predicates, effective-dated lookups, aggregates across lines (hours per timekeeper per day), cross-invoice checks (duplicates), computed consequences (adjust to approved rate, disallow, cap) |
| Consequence | Write a Signal → Work Item → human decides from a closed action catalogue → Decision Record | Reject at submission (deterministic only), auto-adjust with rationale, or flag → per-line human accept / reduce / reject → Decision Record; firm notified with rule + clause |
| LLM | Perception upstream (classification into a bounded set); prose downstream; **never the predicate** | Perception upstream (block billing, vague narrative, UTBMS suggestion) producing facts; rule drafting at author time from the OCG; never the predicate |
| Timing | Minutes–hours acceptable | Tens of seconds budget already spent on extraction; rules must add ~nothing |

The two agree on everything that is a *principle* — closed rule types, LLM at author time / deterministic at run time, versioned and citable rules, Signal and Decision Record as outputs — and differ on exactly two mechanical things: **where the facts come from** and **how the loop runs**. That is the whole decision.

---

## 2. Layer decomposition — what should be shared and what should not

| Layer | Ontology today (`origin/stream/c-036-026`) | eBilling need | Verdict |
|---|---|---|---|
| **1. Rule representation** | Closed `RuleType` enum mirroring `sprk_policyversion.sprk_ruletype` (Threshold, Switch, Existence); one JSON body per type, validated by a JSON Schema (`RuleBodySchemaValidator`, Json.Schema.Net 7.3.4, 32 KB cap, strict parse) | A closed catalogue of ~10 deterministic templates, each with parameters and a clause citation (overview §4.3, §7.2) | **Share the meta-model** — typed JSON bodies, schema per type, closed enum. The *types* are partly workload-specific (§5.1) |
| **2. Authoring** | LLM proposes a typed body; human approves; `PolicyVersionValidator.ValidateForSave` + evaluation-time re-check fails closed; `RuleBodyDescriber` renders plain-language clauses | OCG document → AI drafts bodies from the catalogue with clause quotes → legal-ops edits/publishes; firm-facing reason text | **Share** the validator seam and the describer. eBilling adds the document-to-rules playbook (perception, upstream) and clause provenance |
| **3. Versioning / effective dating / citation** | `sprk_policy` (stable code) + immutable `sprk_policyversion` (versionnumber, effectivefrom/to, approvedby/on, messagetemplate, decisionplan); immutability by security role | Same, plus "which version was in force on the *line date*" and a clause reference | **Share one store.** Add clause-ref columns and a first-class as-of lookup (the Ontology needs it anyway for "the exact rule text in force that day", component model §4.8) |
| **4. Fact assembly** | Query push-down: body → one FetchXML; facts are columns, rollups, calculated columns, `ILiveFactResolver` | In-memory fact set loaded once per submission: lines, firm timekeepers, rate-card lines effective in the period, code sets, matter header, prior-line hashes | **Workload-specific.** This is the genuine divergence; no single mechanism serves both without distorting one |
| **5. Evaluation runtime** | Dataverse executes the query; the evaluator (task 031, **not yet built**) pages ids and writes Signals | C# evaluates each template over the fact set per line and per group; latency inside a job | **Workload-specific**, same dispatch shape (closed enum → handler) |
| **6. Consequences / actions** | Closed `DecisionActionCatalog` in code; `sprk_decisionplan` on the version; human gate | flag / adjust (computed amount) / reject, plus the same human gate per line | **Share the pattern** (closed catalogue in code, plan on the version); add billing actions and an automatic consequence for submission time |
| **7. Explanation / audit** | `SignalWriter` (dedupe key, `sprk_factsnapshot`, `sprk_sentence` rendered only from fields the predicate read — §0.3, `sprk_evidencerefs`); append-only `sprk_decisionrecord` at the gate | Same outputs; both tables already carry `regardinginvoice`; needs a **line-level** subject and firm-visible reason | **Share.** Two schema gaps (§5.4) |

So five of seven layers are shared and two are not. "One engine" and "two engines" are both the wrong frame: the right one is **one engine, two evaluators**.

---

## 3. Technology survey (facts as of 2026-10-14; sources in §8)

Criteria: **D** deterministic and testable · **C** LLM-authorable into a closed, validatable form · **R** readable by legal ops · **E** per-line + aggregate + lookup + computed adjustment · **P** compiles / pushes down to Dataverse queries · **N** .NET 10 in-process · **S** no arbitrary code · **L** licence · **H** maintenance health.

| Candidate | Facts | D | C | R | E | P | N | S | L / H | Verdict |
|---|---|---|---|---|---|---|---|---|---|---|
| **Typed C# rule templates + JSON parameters** (the current Spaarke pattern: `RuleType` + schema per type; Finance `SignalEvaluationService`'s `ISignalRule` strategy is the hard-coded precursor) | No package. Each type = a schema, a C# handler, a describer entry | ✅ | ✅ parameters only; schema-validated | ✅ via `RuleBodyDescriber` | ✅ each template does exactly what it declares | ✅ per type (Existence already compiles) | ✅ | ✅ closed by construction | n/a | **Adopt for both workloads** |
| **FetchXML push-down** (`PredicateCompiler`) | One query per rule; 15 `link-entity` max; aggregate queries capped at 50,000 rows; `ne` includes nulls; no correlated variables by design (§8.0.1a); evaluates after persistence | ✅ | ✅ | via describer | ❌ no computed adjustment, no effective-dated per-line lookup, aggregates only within the 50k cap and not mixed with detail | ✅ it *is* push-down | ✅ | ✅ | n/a | **Keep for set-based detection only** |
| **Microsoft Power Fx** — `Microsoft.PowerFx.Interpreter` 1.8.1 (2026-02-09), MIT; the language of Dataverse formula columns (Learn page updated 2026-01-09) | Host supplies records/tables and enables functions; Excel-like; Copilot / "formula suggestions" already generate it | ✅ | ⚠️ open expressions; closedness must come from a typed symbol table and a read-set analyser, not a schema | ✅ best of the languages | ✅ in memory | ⚠️ only formula columns (same-record scalars, 1,000 chars, no currency output); the NuGet interpreter does not delegate to FetchXML | ✅ netstandard2.0 | ✅ if the host enables no I/O functions | MIT / Microsoft, active | **Reserve**: the right expression language *if* open customer formulas are ever required. Not needed for either workload now |
| **Google CEL for .NET** — Celly 1.2.0 (Apache-2.0, 2026-07-18, claims 100 % cel-spec conformance, `EvalLimits`, `EstimateCost`, 9.2K downloads); CelDotNet 0.2.1 (Apache-2.0, compiles to `Expression<Func<T,bool>>`/EF Core, 902 downloads); no first-party Google .NET implementation | Non-Turing-complete, cost-bounded by design | ✅ | ⚠️ same as Power Fx | ❌ engineer syntax | ✅ | ⚠️ CelDotNet → LINQ/SQL, not FetchXML | ✅ | ✅ | young, small communities | **No** — good language, wrong ecosystem and audience |
| **JSON Logic** — `JsonLogic` 6.1.0 (json-everything, MIT; same family as the `JsonSchema.Net` 7.3.4 already in the BFF); custom ops via `IRule`; Native AOT | Lisp-in-JSON; `var`, `if/and/or`, `map/filter/reduce/all/some/none`, `in`, `missing` | ✅ | ✅ JSON-native | ❌ | ✅ (clunky aggregates) | ❌ | ✅ | ✅ | ⚠️ repo README references an Open Source Maintenance Fee EULA on NuGet packages — **verify applicability for the JsonSchema.Net version already shipped** | **Reserve** as an internal representation for an open predicate *inside* a template, if ever needed |
| **Microsoft RulesEngine** 6.0.1 (2026-07-03) | Rules are lambda-expression **strings** evaluated via `System.Linq.Dynamic.Core` (≥ 1.6.2) + FastExpressionCompiler; JSON workflow schema | ✅ | ❌ open C#-like expressions; validation = compilation | ❌ developers only | ✅ | ❌ | ✅ net10 | ❌ Dynamic LINQ exposes member access on inputs | MIT / active (~4.3k ★) | **No** — it is the "declare code in JSON" playbook mistake with a reflection surface |
| **NRules** 1.0.4 (2026-02-03), MIT | Rete, forward chaining; rules written in C# DSL | ✅ | ❌ rules are code → deploy per rule | ❌ | ✅ | ❌ | ✅ | ✅ | MIT / steady | **No** — customers cannot author; chaining is a non-goal |
| **DMN / FEEL** — ScratchyDisk.DmnEngine 2.0.1 (MIT, 2026-03-03, ANTLR4 + NLog, ~1.1K downloads); FeelSharp 1.2.0 (F#/FParsec) | OMG standard; decision tables are exactly what legal ops read | ✅ | ✅ tables | ✅ | ✅ | ❌ | ✅ | ⚠️ FEEL has CLR interop | single-maintainer packages | **Adopt the table *shape*, not the engine** — `DecisionTable` is already one of the seven closed types |
| **OPA / Rego** — OpaDotNet (community, Wasm via wasmtime, needs the `opa` CLI to compile, preview versions) | Authorization-shaped | ✅ | ⚠️ | ❌ | ⚠️ | ❌ | ⚠️ Wasm | ✅ | community | **No** |
| **Cedar** — MonoCloud.Cedar 0.1.0 (native bindings, fork of cedar-java) | principal / action / resource | ✅ | ⚠️ | ❌ | ❌ | ❌ | ⚠️ native | ✅ | 0.x | **No** for rules; a candidate for Authority (§4.14) later, out of scope |
| **Dataverse business rules / Power Automate** | Form/entity-scoped UX; flows are async, per-tenant, unversioned | ⚠️ | ❌ | ⚠️ | ❌ | n/a | ❌ | ✅ | platform | **No** — ADR-002: "Entity-scope business rules — defaults/UX only; never an invariant owner"; plugins and low-code plugins are prohibited |

Two conclusions follow. First, no candidate dominates the closed-template pattern on the criteria that matter here (closedness, citability, readability, zero new attack surface, zero new packages). Second, the expression languages (Power Fx, CEL, JSON Logic) are not *bad* — they are the answer to a question neither workload asks. Both designs say so explicitly: "configurable *content*, not configurable *concepts*" (component model §4.12) and "AI proposes rules **from a closed catalogue of rule templates**" (overview §4.3). An open expression language is precisely the ratchet §4.11 warns about: once the LLM can write any formula, the human cannot approve the compiled form and the closed vocabulary becomes a type-checker problem instead of a schema.

---

## 4. Why not one evaluator?

It is worth stating why the obvious unifications fail, because the owner's guidance asks for the technically correct answer, not the tidy one.

**Push-down everything (make eBilling rules FetchXML).** Rate ceilings need "the approved rate for *this* timekeeper on *this* line's date" — a correlated lookup the compiler refuses by design (§8.0.1a) and FetchXML cannot express without one query per line. Daily-hours caps are aggregates grouped by (timekeeper, date) that must then be joined back to the offending lines; FetchXML aggregates cannot return detail rows and are capped at 50,000. Adjust-to-approved-rate is a computation, not a filter. And the invoice must be persisted and queryable before any of it runs, while the submission path wants to decide on extraction output. Push-down is the wrong tool for a transaction.

**In-memory everything (load candidates, evaluate in C#).** This works for the Ontology in principle, and the compiler already anticipates a per-item judgement step: `CompiledPredicate.DateOnlyWhenFields` tells the evaluator it "must widen the query … before judging per item". But making it the *primary* strategy would reverse CM-3 ("Dataverse is the evaluator"), discard the worklist-as-a-view property (§4.15.1), and move every matter in the tenant through the BFF on each run. That is the compromise the owner said not to make.

**One expression language over an in-memory fact set for both.** Technically coherent (Power Fx would be the pick), but it trades the schema-closed form for an open one, costs a read-set analyser to keep §0.3 honest, and buys expressiveness nobody has asked for. It becomes the right answer only under the conditions in §7.

Therefore: one engine, two evaluators.

---

## 5. Recommended architecture

### 5.1 Rule types — one closed enum, extended with review, each type declaring its strategy

`RuleType` stays a plain C# enum mirroring the Dataverse choice column; adding a member remains "a code change with review, never a configuration row" (`RuleType.cs` remarks). eBilling brings the *unbuilt* members of the original seven plus a few billing-specific ones. Do not let each OCG category become a type; parameterise.

| Rule type | Status | Covers (OCG reference §4) | Body parameters (sketch) | Strategy |
|---|---|---|---|---|
| `Existence` | built, compiles | detection: "approved invoice > $50k awaiting payment", "invoice with no rate card in force" (`sprk_invoice` is already in `EvaluatorGlobalReadableEntities`) | subject, when, all[] | push-down |
| `Threshold` | enum only, no schema | line-level expense caps (row 11), invoice-age (row 12) | field, op, value, scope | both |
| `Switch` | enum only | kill switches | flag | both |
| `AllowList` | original seven | approved timekeepers (row 5), classification allow-list, non-reimbursable expense codes (row 10), UTBMS code validity incl. activity-without-task (row 13) | source: literal list **or** reference table; mode allow/deny; field | in-memory (push-down later) |
| `RateCeiling` (or `DecisionTable`) | new | rate above approved (row 6) | lookup precedence (matter+timekeeper > matter+class > firm+timekeeper > firm+class), tolerance, consequence `adjust-to-approved` | in-memory |
| `Increment` | new | tenth-hour increments, minimum charges (row 3) | increment, applies-to fee lines | in-memory |
| `AggregateCap` (≈ `Envelope`) | original seven | daily hours per timekeeper (row 4), meals per day (row 11), task-code caps (row 16), attendees per event (row 5) | measure, group-by keys, window, cap, consequence | in-memory |
| `DateWindow` | new | late submission, stale lines, outside matter dates (rows 12, 15) | from/to field refs, offset days | in-memory (push-down-able) |
| `Duplicate` | new | duplicate invoice / line within and across invoices (row 14) | key fields, window | in-memory + one targeted query |
| `ObservationGate` | new | judgement checks — block billing, vague, clerical, internal conference (rows 1, 2, 7, 8, 17) — **flag only** | observation predicate, confidence floor | in-memory over AI-produced facts |
| `DelegationLimit` | original seven | approval routing (R2) | — | gate |

**Reference data is not rules.** Rate cards, approved-timekeeper lists and UTBMS code sets are effective-dated *tables* the rules look up; the rule is "bill at ≤ the approved rate, else adjust". This keeps the catalogue near ten types and keeps a rate change a data edit, not a policy version.

### 5.2 Two evaluation strategies behind one validator

`PolicyVersionValidator` remains the single fail-closed seam for save time and evaluation time. Today `TryPrepareForEvaluation` returns a `CompiledPredicate` (FetchXML). Generalise minimally: a compiled rule is either a **FetchXML predicate** (push-down) or an **in-memory rule** (a typed C# object built from the body). Both expose the **declared read-set** — the fields a template reads — so `TemplateEligibleFields`, `sprk_factsnapshot` and the §0.3 "the sentence may assert only what the predicate read" rule hold identically for both. For in-memory types the read-set is static per template (declared in code), which is simpler than the compiler's per-body derivation.

| | Ontology detection evaluator (task 031) | eBilling `InvoiceComplianceEvaluator` |
|---|---|---|
| Trigger | `IScheduledJob` (ADR-036) and per-matter events | `IJobHandler` on submission (ADR-004/052), result pushed to the SPA |
| Facts | Dataverse runs the query; page subject ids | One `InvoiceFactSet` load (~5 queries): lines, firm timekeepers, rate-card lines effective in the billing period, code sets, matter header, prior-line hashes in the window |
| Loop | per policy version → query → per subject → `SignalWriter` | per policy version in force **on the line date** → per line / per group → findings |
| Consequence | Signal + decision plan | finding → `sprk_autoconsequence` (Flag / Adjust / Reject): reject sets a terminal status and writes an auto Decision Record citing policy + version + clause; adjust proposes the computed amount for human confirmation; flag writes a Signal |
| Outputs | `sprk_signal`, `sprk_decisionrecord` | the same two tables, plus per-line status / adjustment columns |

The in-memory strategy is also the Ontology's own upgrade path: component model §4.15.1 says `ILiveFactResolver` "remains the path for facts that outgrow" rollups, and "push-down for candidate selection, in-memory for judgement" is exactly that path. The two strategies are complementary, not rival.

### 5.3 Storage — one store

`sprk_policy` / `sprk_policyversion`, no eBilling rules tables. Additions, all additive:

- `sprk_policy`: `Billing` in `sprk_category`; a lookup to the source guideline `sprk_document` (the OCG as a versioned SPE document — the provenance differentiator, overview §2.5).
- `sprk_policyversion`: `sprk_clauseref` (section id + quoted text, bounded), `sprk_autoconsequence` (choice: Flag / Adjust / Reject — a column, consistent with the branch's rule that the Signal's type, severity and template "live in their own columns" and that `then` is reserved and refused in the body), `sprk_firmreason` (firm-visible text template, same token rules as `sprk_messagetemplate`).
- Scope: `PolicyScopeResolver` already resolves tenant/matter applicability; per-firm overrides (R2) extend it rather than adding a table.
- Reference tables (new, eBilling-owned, data not rules): timekeeper, rate card + rate-card line (effective from/to, rack vs approved), UTBMS code set + version.

### 5.4 Outputs — two schema gaps to settle before the first row

1. **Line-level subject.** `SignalWriter.RegardingLookupByEntity` lists nine subject types; neither `sprk_billingevent` nor `sprk_invoicelineitem` is one. Either add a line lookup to `sprk_signal` (and to `sprk_decisionrecord`), or make the invoice the subject and carry the line in `sprk_evidencerefs`. The former matches "flags are Signals on the invoice **or line**" (overview §4.5) and keeps the dedupe key `{policycode}|{type}|{id}` meaningful per line. Decide jointly with the one-line-entity decision (overview §6).
2. **Grouping matter for an invoice.** `VerifiedMatterDerivation` covers only `sprk_matter` and `sprk_communication`; an invoice subject currently **escalates**. Add `sprk_invoice.sprk_matter` (and the line → invoice → matter hop) as verified derivations.

Both are the kind of change the Ontology design itself flags as "taken late, a data migration" (component model §11, item 3).

### 5.5 What this means for the three practical questions

**(a) eBilling R1 timing.** eBilling does not depend on the Ontology *evaluator* (task 031, unbuilt). It depends on the policy tables (already in master per `repo-state-data-ai.md` §3), and on four pure, tested classes on `stream/c-036-026`: `RuleType`, `RuleBodySchemaValidator`, `PolicyVersionValidator`, `SignalWriter` (+ `RuleBodyDescriber`). eBilling's first tasks — line-entity decision, timekeeper / rate card / code-set tables, submission operation, extraction fields — need none of them. Sequence: ask for `Services/Signals/**` + its tests to merge (or be cherry-picked) ahead of eBilling's first evaluator task, roughly two to three weeks out. If it has not merged by then, eBilling builds the in-memory strategy **in a worktree branched from the stream branch**, in the same namespace, against the same tables — co-building, not copying — and reconciles at merge. Under no scenario does eBilling wait months, and under no scenario does it ship a second store or a second validator.

**(b) The FetchXML compiler.** Keep it unchanged as the push-down strategy. Do not replace it and do not generalise it now; introduce the compiled-rule abstraction only when the second strategy lands, so the Ontology branch is not reworked for a consumer that does not exist yet. Its invariants (verified joins, global-read allow-list, no `$` references, `sprk_budget.modifiedon` refusal) are untouched.

**(c) Where rules live.** `sprk_policy` / `sprk_policyversion` for every rule in both workloads; reference data in its own effective-dated tables; no eBilling rule-body tables. "Not two engines that diverge" (overview §5.3 item 4) is met by sharing the store, the enum, the schemas and the validator — which is stronger than sharing a rule-body format alone.

---

## 6. Is the current code on this path?

Largely yes; the gaps are small and already anticipated by the code's own comments.

| Item | State on `stream/c-036-026` | Needed |
|---|---|---|
| Closed `RuleType` + schema per type | ✅ pattern in place; only `Existence` has a schema; `Threshold`/`Switch` fail closed "no schema authored yet" | Author schemas as types are seeded (the branch's own plan) |
| `PolicyVersionValidator` single seam | ✅ | Return an in-memory compiled rule as a second shape; keep `TryPrepareForEvaluation`'s contract |
| `SignalWriter` | ✅ dedupe, factsnapshot, §0.3 rendering | Line subject; invoice → matter derivation (§5.4) |
| `DecisionActionCatalog` / `DecisionPlanService` | ✅ closed catalogue, fail-closed plan | Add billing actions (accept / reduce / reject line; approve / reject invoice); auto-consequence column |
| Describer (`RuleBodyDescriber`) | ✅ Existence only | One describer entry per new type; reuse for firm-facing reasons |
| `PolicyScopeResolver` | ✅ tenant/matter | Firm scope later |
| In-memory evaluator | ❌ none; Finance `SignalEvaluationService` is the hard-coded precursor (`ISignalRule` over snapshots, thresholds from options) | Build `InvoiceComplianceEvaluator` as the first in-memory strategy; retire the Finance thresholds into policy rows when convenient (decision 22 already makes it "one producer among several") |
| Ontology evaluator (031) | ❌ unbuilt | Unchanged by this decision |
| Reconcile decision 21 ("no LEDES; invoice metrics arrive from the e-billing platform") | design.md §1.0 | Overview §5.3 item 1: Spaarke-billed customers are native; mirror mode remains for external e-billing. The rule engine decision does not depend on it |

---

## 7. Risks, decision criteria, and what would change the answer

**Risks**

| Risk | Mitigation |
|---|---|
| The stream branch merges late; eBilling is tempted to copy the classes | Co-build from the branch in a worktree; one namespace; a reconciliation task in `TASK-INDEX.md` |
| Generalising the validator/compiler into a "framework" | Two concrete strategies, one enum, no registry; CLAUDE.md §11 three questions on every new type |
| Ratchet toward an expression language ("just let the LLM write the formula") | Schema per type; `$`-reference and `then` refusals stay; a new type is a reviewed code change — the same guard §4.11 already relies on |
| §0.3 honesty for in-memory rules | Static read-set per template feeds template eligibility and `sprk_factsnapshot` exactly as `TemplateEligibleFields` does |
| Line-item subject decided late | Settle §5.4 with the one-line-entity decision before the first Signal row |
| json-everything maintenance-fee EULA | Independent of this decision but surfaced by it: verify for `JsonSchema.Net` 7.3.4 already in the BFF; file as an issue |
| Publish size / CVEs (CLAUDE.md §10) | Recommended path adds **no packages** |

**Decision criteria used:** determinism and testability; closed, schema-validatable form for LLM drafting with human approval of the compiled form; citability (policy + version + clause) and immutable versions; readability for legal ops via the describer; latency inside a submission job; fact locality (one invoice vs. the record graph); zero new runtime attack surface; one store; no change to the Ontology's binding invariants (§0.3, CM-3, closed types, no cross-clause variables).

**What would change the answer**

1. **Open customer-authored formulas become a requirement** (not a closed catalogue). Then adopt Power Fx (Dataverse-native, MIT, Microsoft-maintained, LLM-familiar, the most readable) over the in-memory fact set, with a typed symbol table as the closed vocabulary and a read-set analyser for §0.3; the Ontology keeps push-down. That is the "two engines, one feeds the other" world. Market evidence argues against needing it: incumbents configure by category, and the AI-on-guidelines leaders map to templates (overview §2.3).
2. **Push-down proves insufficient for most detection rules** (correlated variables needed often). Then in-memory becomes primary for both and FetchXML is demoted to candidate selection — a migration this design already permits without a redesign.
3. **The stream branch cannot merge for months.** eBilling co-builds in-place (§5.5a); still one store, one enum.
4. **Microsoft ships a GA, ALM-grade server-side Power Fx runtime in Dataverse** (ADR-002's own reconsideration clause). Revisit placement, not the layer split.

---

## 8. Sources

Repository (read on `origin/stream/c-036-026` unless noted): `src/server/api/Sprk.Bff.Api/Services/Signals/{RuleType,PredicateCompiler,RuleBodySchemaValidator,PolicyVersionValidator,SignalWriter}.cs`, `Services/Signals/Schemas/existence-rule.schema.json`, `Services/Signals/Actions/{DecisionActionCatalog,DecisionPlanService}.cs`, `tests/fixtures/signals/*`; master: `Services/Finance/SignalEvaluationService.cs`, `Services/Insights/LiveFacts/ILiveFactResolver.cs`, `Sprk.Bff.Api.csproj` (JsonSchema.Net 7.3.4), `.claude/adr/ADR-002-thin-plugins.md`, `.claude/adr/ADR-052-workload-placement.md`; `projects/spaarke-ontology-platform-r1/{design.md, notes/ontology-component-model.md, notes/mvp-technical-spec.md}`; `projects/ebilling-r1/notes/{solution-overview.md, owner-requirements-2026-10-14.md, research/ledes-utbms-ocg-reference.md, research/repo-state-data-ai.md}`.

External:
- Power Fx: https://github.com/microsoft/power-fx (MIT) · https://www.nuget.org/packages/Microsoft.PowerFx.Interpreter (1.8.1, 2026-02-09) · https://learn.microsoft.com/en-us/power-apps/maker/data-platform/formula-columns (formula columns use Power Fx; limits)
- CEL: https://www.nuget.org/packages/Celly (1.2.0, Apache-2.0) · https://www.nuget.org/packages/CelDotNet (0.2.1, Apache-2.0) · https://www.nuget.org/packages/Cel.Compiled
- JSON Logic: https://www.nuget.org/packages/JsonLogic (6.1.0) · https://docs.json-everything.net/logic/basics/ · https://github.com/json-everything/json-everything (MIT; maintenance-fee EULA note)
- Microsoft RulesEngine: https://www.nuget.org/packages/RulesEngine/6.0.1 (System.Linq.Dynamic.Core dependency) · https://gittrend.io/repo/microsoft/RulesEngine
- NRules: https://www.nuget.org/packages/NRules (1.0.4, MIT)
- DMN/FEEL: https://www.nuget.org/packages/ScratchyDisk.DmnEngine (2.0.1, MIT) · https://www.nuget.org/packages/FeelSharp
- OPA / Cedar: https://github.com/me-viper/OpaDotNet · https://www.nuget.org/packages/OpaDotNet.Wasm · https://www.nuget.org/packages/MonoCloud.Cedar
- Dataverse limits: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/aggregate-data (50,000-row aggregate cap) · https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/join-tables (15 `link-entity` maximum)
