# Spaarke Ontology Platform R1 — project operating manual

> Read with [`current-task.md`](current-task.md) (current state). Repo-wide rules are in root [`CLAUDE.md`](../../CLAUDE.md)
> (task execution via `task-execute`, escalation, §6.5 ADR protocol, checkpointing); BFF rules load from
> `.claude/rules/bff-hygiene.md`, credential rules from `.claude/rules/credentials.md`. This file holds only what is
> specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.
> Restructured 2026-10-07; the previous version is archived verbatim at `notes/handoff-history/CLAUDE-archive-2026-10-07.md`.

## 1. Scope and status

The project makes **one predicate** computable, recordable and re-tunable without a deployment — *a communication
classified **fee or scope change** in the window AND no **budget revision** in that window* — and the machinery that
records what was decided about it: Policy → **Signal** (`sprk_signal`) → **Work Item** in a worklist → **Decision
Record**. R1 also ships the Do lane (overdue/due-soon work as Work Items, D-16), Threshold rules, the decision commit
route, the Ontology admin tab, the canonical modal migration (D-26) and cleanup C-1..C-27.
**Out**: Connection Engine / connectors / LEDES, the Action **Engine**, Authority, MCP server, bitemporality,
per-entity fact tables, the inquiry SLA (D-20), Switch rules (D-16). Check `spec.md` §2 before adding scope.

Status: [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) (critical path to 031 at its top) and `current-task.md`.
Spec: `spec.md` · Design: `design.md` · Plan: `plan.md` · Decisions: `notes/decisions.md`.

## 2. Binding rules for this project

**Vocabulary** — `notes/ontology-component-model.md` §3 is authoritative. **Signal** = a condition that held
(engineering term); **Work Item** = the actionable unit it produces (user-facing). A worklist row is a **core record
grouping Work Items** (D-34/D-36) — "row" is layout, not vocabulary. **"Flag" and "typed outcome" are retired**
(use `disposition`). A Signal's *subject* is the target of a Work Item, never a Work Item itself.

**Truth and determinism**
- A capability must TEST what its message CLAIMS (spec §0.3): `sprk_sentence` and `sprk_messagetemplate` may assert
  only what the predicate read. A template naming a field the rule body does not read is a defect. Templates may name
  only TemplateEligibleFields (task 022); the UI composes row headlines from display columns (D-23).
- Membership, rank, predicate truth, authorize/deny and Decision Record content are **deterministic**. The LLM only
  classifies (bounded by `sprk_triagecategory`) and writes prose over facts it did not compute.
- Rule bodies are validated at **evaluation** and fail closed (task 022); the grammar is the closed set in
  `PredicateCompiler` (Existence + Threshold in R1, D-16; date fields take at most one lower + one upper bound, D-40).

**Data and security**
- One Decision Record per human resolution, written **last** by the single BFF commit route (FR-18, D-17); append-only
  **by privilege** — no human or Spaarke role may Write/Delete it (verify with the union-of-roles check, NFR-11).
- Signals and Decision Records group under the **core record** via `CoreAncestorResolver` — never a parallel
  derivation; the model stays **extensible** to new core types without code changes (D-34, D-36, D-37).
- Signals/Decision Records on **Secure** records are secure children (D-33, task 039); no-core items are owner-only
  (D-35, D-39); **no skips** (D-38). Signals are read only through the BFF read route (FR-24).
- Dedupe key is **episode-scoped**; re-raise rules per D-13/D-32; suppression 3 dismissals / 30 days per (policy,
  core record) — per (policy, item) when there is no core record (D-11 as amended).
- "Today" for any date comparison is the **item's assignee's → owner's → UTC** time zone (D-25); Date Only bounds
  are widened a day in the query and judged per item (task 024 note for 031).
- Every new BFF route passes the #1312 route-authorization census (NFR-10).

**UI**
- Reuse first, binding (FR-27, FR-28 as amended by D-24): extend `WorkspaceShell/MetricCard.tsx` + `MetricCardRow`
  (always cite the full path — `Spaarke.Visuals/src/components/MetricCard.tsx` is a different component),
  `DocumentRowMenu.tsx`, `SprkChat/OutcomeCard.tsx`; **one** row component with data-driven variants; anything new
  lands in `@spaarke/ui-components`, never `src/solutions/SpaarkeAi/`.
- Before any UI task: check it against prototype `HANDOFF.md` @ `ae1cc9f` §1 (binding behaviour), §3 (data needs),
  §4.1 (wizard host). **Do not port prototype code.** Where v4 shows what the solution cannot do, it is an owner
  decision (`notes/v4-prototype-vs-solution.md`).
- Modals: `SprkModal` + `WizardShell`, launched in-app (D-26; ADR-050 amendment = task 110).

**Testing (ADR-038, project additions)**
- A test asserting a Dataverse column list, entity name or option-set value must be paired with something touching
  the **real schema**.
- A path that throws **consistently** is a defect to diagnose, never noise.
- "Proven live" means the **real route** against spaarkedev1, not replayed payloads.

**Process**
- Deferred work and newly found issues: `/project-defer-issue-tracking` writes `notes/defer-issues.md` AND a GitHub
  issue — never one without the other.
- Done = POML acceptance criteria (closed set) pass · Step 9.5 gates for FULL rigor · POML `<status>` AND
  TASK-INDEX marker both updated (`scripts/check-task-status-drift.ps1 -Project spaarke-ontology-platform-r1`) ·
  deviations recorded in `notes/`.
- BFF-touching tasks measure publish size against a **fresh master build from short paths**, same zip tool, file
  counts compared (`.claude/rules/bff-hygiene.md`).

**ADR tensions approved for this project** (root CLAUDE.md §6.5; detail in `spec.md` §6)
- ADR-039 — path A — Policy decides what is *true*, Binding what *executes*; cite in PR descriptions.
- ADR-040 — path B — `SessionGate` and the Decision Record are siblings (`sprk_gatesessionid`); amended (task 003).
- ADR-028 — path A — the Signal writer authenticates as its own managed identity, never the sysadmin BFF identity.
- ADR-009 — path A — task 096's in-process verdict cache.
- ci-cd-unit-test-remediation-r1 FR-A02 — path A — blocking Tier 1 Xrm capability guard + router docs-only fix.
- ADR-050 — path B (pending, task 110) — canonical modal preset and in-app launch rule.

## 3. Owner directives and standing decisions

Full log with sources and superseded items: [`notes/decisions.md`](notes/decisions.md).

**How the owner wants work done**
- 2026-10-05 — **Fix found defects now; never defer.** Defects unrelated to the ontology ship as their **own PR** to master.
- 2026-10-05 — **Merging is the owner's call**, per PR ("merge when green" approvals are recorded in the task). Never merge without one.
- 2026-10-05 — Every rework round gets a **focused independent review** before merge; every review so far found something real.
- 2026-10-05 — Role edits, Azure changes and Dataverse schema changes need **explicit owner approval** (the approved set is in spec §9).
- 2026-10-05 — v4 is the UI baseline; **flag** inconsistencies with the real solution for the owner to resolve.
- 2026-10-07 — Restricted/Limited/Secure or record-access work **coordinates with uac-r2's latest code** (§4).

**Product decisions still binding** (one line each; `notes/decisions.md` has the rest)
- 2026-10-06 — Events: one open-work predicate `EventStatusCode.IsOpenWork` (Draft, Open, On Hold, Reassigned); Reassigned is completable; "my events" = owner OR assigned-to-my-contact OR created-by-me.
- 2026-10-07 — D-13 episode key + 14-day quiet window · D-14 admin stamps `sprk_inforceto` · D-16 Path B + Do lane + Threshold.
- 2026-10-07 — D-17 one commit route, record last · D-18 writer creates budget revisions and updates the amount · D-19 approve variance is record-only · D-21 server-side work-assignment create.
- 2026-10-07 — D-22 admin = gated tab writing as the caller · D-26 canonical modal + migrate wizards · D-27 `sprk_duedate` always · D-28 `statuscode` authoritative.
- 2026-10-07 — D-29 To Do score on calendar days · D-30 severity column · D-31/D-35 all To Dos, no-core owner-only · D-32 Off→On re-raises.
- 2026-10-07 — D-33 secure-child Signals/Decision Records · D-34/D-36/D-37 core-record grouping, extensible · D-38 no skips · D-39 per-item suppression · D-40 two-bound dates · D-41 To Do dates → Date Only (task 106).
- 2026-10-07 — D-42..D-56 close every open point: rank sev→highpriority→oldest→number · overdue 1 day · WA in both assigner's and assignee's Do lane · reassign/extend Routine · drop RowMenu/OutcomeCard reuse · DR tab in R1 · association-confirmed trigger · recall columns on triage category · Missing + freshness issue · Know rule offers Assign Work · Confirm = confirmation, no chat · templated drafts · WA response columns (task 047) · budget amount as the user · no inquiry due date.
- 2026-10-07 — D-57 Briefing uses IsOpenWork (no Draft data change) · D-58/D-59 work-assignment response columns + create via uac-r2's RecordCreationService · D-60 drop foreign tables from our solution · **D-61 never stamp unchanged Signals nightly** (uac-r2 100k-row limit) · D-62 users lose Create on Decision Records with 049.
- 2026-10-07 — D-63 final due date informational everywhere (task 068) · D-64 recall gate on a synthetic set the owner labels blind · **D-65 one canonical server create path per table** (uac-r2 picks for work assignments) · **D-66 no core code in `Services/Ai`** (task 048, not an ADR exception) · D-67/D-68 #1390 and #1391 merge after uac-r2 approves.
- 2026-10-08 — **No parking (owner):** never park or defer an issue that this project will not address. Every defect found (pre-existing or not, ours or another domain's code) and every loose end (an undeployed surface, a skipped live check) becomes a TASK here and gets done. A "next-round" issue or a hand-off to another project's next deploy does not count. A deferral is valid only by an explicit owner decision (e.g. D-50). Other projects' code is still coordinated for review (e.g. uac-r2 via #1355).
- 2026-10-08 — D-69..D-81: skipped marker opt-in · ADR-050 Path A for legacySize (closes with 111) · 111 dev deploy · notification keeps Open-only (D-72) · orders by due date (D-73) · **D-74 Briefing Open-only (supersedes D-57)** · D-75 master BFF + Console to dev · D-76/D-79 restore matter-membership scope on notifications · D-77 ISS-018 fix as one PR · D-78 scheduler fails loudly and retries · D-80 audit sprk_playbooknode in dev · D-81 LegalWorkspace + external SPA deploys are this project's tasks.
- 2026-10-05 — SmartTodo's palette is the one due-urgency scheme (overdue red · 0–3 dark orange · 4–7 yellow · 8–10 grey).
- 2026-10-07 — Writer credential vs tenant-isolation rule I5 (owner): tenant-pinned `DefaultAzureCredential` locked to the writer's own UAMI — every non-MI source excluded, `AZURE_TOKEN_CREDENTIALS` refused unless MI, both pinned by tests; I5 satisfied (spec §6 ADR-028 row).

## 4. Coordination

- **unified-access-control-r2 (uac-r2)** — owns `SecureChildLineage.cs`, `config/secure-record-owner-role.json`,
  `CoreAncestorResolver`, `RecordOwnershipResolver` (I-6), `RecordRouteAccessAuthorizationFilter`,
  `CallerRecordAccessProbe`, the route ledger (`RouteAuthorizationGuardTests.Ledger.cs`), ADR-034, ADR-003's
  `ExternalCallerContext`. Before an access-touching task: fetch master, re-read those files and name them in the
  completion record; check uac-r2's open PRs; reuse their mechanisms; their files change only through their review;
  if their code invalidates the plan, stop and escalate. **Channel: issue #1355** (their sessions hold cross-session messages; use the issue). Open with them (2026-10-07):
  review **PR #1390** (039 lineage + role config) and **PR #1391** (org-owned AppendTo fix in `OwnedChildWrite`); name
  **one canonical work-assignment create path** (`RecordCreationService` vs `OwnedChildWrite`, D-65 — 046 waits); agree
  the **move of the write core out of `Services/Ai`** (D-66, task 048). Their binding conditions: register only lookup
  columns that exist (one-pass reconciliation fails for every table otherwise); lineage + role config in ONE PR; deploy
  order schema → roles → BFF; never stamp unchanged rows (100k changed-rows/pass limit, D-61). Their master merges have broken our PRs mid-flight before (#1312).
- **spaarke-prototype** — owns `HANDOFF.md` (UI contract, pinned `ae1cc9f`). We consume; we do not edit.
- **ci-cd-unit-test-remediation-r1** — owns the CI tier workflows; our Tier 1 guard + router change is a recorded
  path-A exception to its FR-A02.
- **Shared UI packages** (`@spaarke/ui-components`, Daily Briefing, SmartTodo, Events) are edited by many projects —
  run `/conflict-check` before BFF or shared-UI PRs; hot-path registry `projects/INDEX.md`.

## 5. Environment and live actions

- **Environment**: Dataverse **spaarkedev1** only. Solution `OntologyPlatformSolution`, publisher Spaarke, prefix
  `sprk_`. Five tables: `sprk_signal`, `sprk_decisionrecord`, `sprk_policy`, `sprk_policyversion`,
  `sprk_budgetrevision` (all also in the *Spaarke Platform* app, read-only forms).
- **Schema changes**: Web API `POST EntityDefinitions` / `.../Attributes` with explicit PascalCase `SchemaName`, the
  `MSCRM.SolutionUniqueName: OntologyPlatformSolution` header (not on a solution-create POST), `DateTimeBehavior` on
  every datetime, local choice values from `100000000`. Recipe: `notes/schema-draft.md`. **Never MCP `create_table`**.
  Publish only the affected entities — **never tenant-wide `PublishAllXml`**.
- **Needs owner approval**: role/privilege edits, Azure changes, schema changes, merging PRs, deleting anything.
- **Test data**: prefix `zz-NNN-`, delete afterwards, confirm by query.
- **Roles**: Spaarke Console User · Spaarke Ontology Administrator (held by the owner) · Spaarke Ontology Service
  (the writer). Privileges live on the **root-BU copy**; per-BU copies with no privileges are normal. Matrix and
  evidence: `notes/security-roles.md` (§10 = 2026-10-07 edits).
- **Never**: commit secrets; create a client secret for the writer; recreate or reference `BFF-API-ClientSecret`;
  remove System Administrator from the existing BFF identities; strip `Service Writer` / `Service Deleter` (held only
  by Microsoft first-party app identities — benign, verified); add a field security profile to these tables.
- **Deploy order**: role edits land **before** any BFF build carrying new secure-record config, in every environment.
- **Writer identity**: config key `Ontology:Writer:ManagedIdentityClientId` (UAMI `mi-ontology-writer-dev`);
  live seam tests run with `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`.

## 6. Gotchas — do not re-learn

- 2026-10-02 — **MCP `create_table` can't set the publisher** (env default prefix `new`); logical names are immutable → delete-and-recreate. Use the Web API recipe.
- 2026-10-02 — Omitting `DateTimeBehavior` breaks filtered-view generation; every later relationship on the entity fails with an error naming the *datetime*.
- 2026-10-02 — Things that look generic are not: `ILiveFactResolver` predicates are a closed `switch`; `ISignalRule` (Finance `SignalEvaluationService`) is a private nested interface; `GenerateDeterministicId(matterId, type)` can't take a polymorphic subject. `notExists` now exists only in `PredicateCompiler` (task 021). "Already generic" ≠ "no new code".
- 2026-10-03 — Swallow-and-log paths are invisible: App Insights appId `6a76b012-46d9-412f-b4ab-4905658a9559` (`traces` for `[comms-policy]`/`[comms-ri]`, `exceptions` for swallowed throws).
- 2026-10-04 — `origin/master` moves fast (70 commits in hours); merge master before any deploy, and expect another project's merge to conflict an open PR mid-review (#1312 vs #1302).
- 2026-10-04 — BFF unit suite ~15 min: always `run_in_background`. Contended full suites give false failures — a failure counts only if it reproduces alone and not on master.
- 2026-10-04 — Delegated wide audits failed repeatedly; run targeted agents directly and re-verify load-bearing claims. An agent waiting on its own helpers can stall silently (2026-10-05): give agents "no sub-agents" when the output matters.
- 2026-10-05 — A one-way schema change breaks every reader and writer of the column: inventory them first (098, 106).
- 2026-10-05 — Fixing a broken route can expose a latent hole (097: no record-level authorization once routes stopped crashing).
- 2026-10-06 — `pcf-scripts` exits 0 on a failed build; judge PCF builds by output. PCF prod builds use `npm run build:prod`.
- 2026-10-06 — Jest: setting `process.env.TZ` inside a test file does not pin the zone; use a shared test environment (098).
- 2026-10-07 — Restricted/Limited only exclude **external** contacts (ADR-003); the staff wall is the **Secure** flag (`sprk_issecure`). Don't confuse them.
- 2026-10-07 — Adding privileges to Secure Record Owner can inject SharePoint privileges at Global (uac-r2 setup guide §5.4): remove exactly those. uac-r2's runbook strips privileges not in its config — our two Reads sit outside it until task 039 (comment on #1355).
- 2026-10-07 — `dotnet build Spaarke.sln` does not rebuild `tests/Spaarke.ArchTests`; build it explicitly before trusting an arch run (a stale build showed 16 false failures).
- 2026-10-07 — A POML quoting a bare element name in prose breaks its XML parse — escape angle brackets; `scripts/Validate-TaskPoml.ps1` catches it. Apostrophes break bash heredocs — write commit messages to a file.
- 2026-10-07 — A cross-session message to another Claude session can be held for approval and expire; use a GitHub issue as the durable channel.
- 2026-10-07 — In PowerShell, `[IO.File]` resolves relative paths against the PROCESS directory (this worktree), not `cd`'s location: an edit meant for another worktree silently landed here. Always pass absolute paths to .NET file APIs.
- 2026-10-07 — A PowerShell double-quoted here-string (`@"…"@`) treats the backtick as an escape: Markdown code spans lose their backticks and `` `t ``/`` `b `` become tab/backspace. Write Markdown with the Edit/Write tools or a single-quoted `@'…'@`, never `@"…"@`.
- 2026-10-08 — The repo's deploy skills (`pcf-deploy`, `dataverse-deploy`, `ribbon-edit`) import with `pac solution import --publish-changes`, which publishes ALL customizations in the environment — the tenant-wide publish this project forbids (task 111 ran it in spaarkedev1). In a task prompt, say: import WITHOUT `--publish-changes`, then publish only the imported components (`PublishXml` for that solution's entities and web resources).
- 2026-10-08 — Two PRs green on their own can break master together (#1408 changed a return type; #1359's test, merged after it, still used the old one → CS0411 on master, #1418). Before merging a PR whose CI ran on an older base, merge master into it (or check that nothing it calls changed on master since its last CI run).

## 7. Key documents

- `spec.md` (§6 ADR tensions, §9 decisions, §11.1 open points) · `design.md` (§8, §10 settled) · `plan.md` ·
  `notes/decisions.md` · `notes/defer-issues.md`
- Evidence: `notes/schema-draft.md` · `notes/security-roles.md` · `notes/v4-prototype-vs-solution.md` ·
  `notes/v4-reconciliation.md` · `notes/secure-signals-scoping.md` · `notes/modal-wizard-canonical-approach.md` ·
  `notes/079-uac-coordination.md` · `notes/ontology-component-model.md` (vocabulary)
- UI contract: prototype `HANDOFF.md` @ `ae1cc9f` — `spaarke-dev/spaarke-prototype`, branch
  `feature/2026-10-spaarke-console`, `projects/2026-10-spaarke-console/`; local
  `c:\code_files\spaarke-prototype-wt-spaarke-console\projects\2026-10-spaarke-console\`
- Applicable ADRs: 002 (no plugins; invariants have one BFF owner) · 003 (external access) · 004 (`IJobHandler`) ·
  009 · 013 (AI only via `PublicContracts`) · 015 (privilege flagged, never decided) · 021 (dark mode) · 024
  (polymorphic regarding) · 028 · 034 (access model) · 036 (`IScheduledJob`, no timer `BackgroundService`) · 038
  (testing) · 039 · 040 · 045 (measure the tokenizer's cost) · 050 · 052 (workload placement)
- Related projects: unified-access-control-r2 · spaarke-prototype (2026-10-spaarke-console) ·
  ci-cd-unit-test-remediation-r1 · Draft PR #1111 is this branch
