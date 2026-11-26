# Current Task State — specialist-legal-agents-r1 (umbrella program)

> **Last Updated**: 2026-11-25 late (housekeeping: G8 open tests closed out, coordination + accelerator told; design v0.8)
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1` · **Board**: Issue #3002 under Epic #421
> **Phase**: Phase 0 spec written 2026-11-25 (`spec.md`); no POML tasks yet. Run `/project-pipeline` only on the owner's go.
>
> **2026-11-25 (owner):** OD-2 amended (patent attorney first; NDA stays a function, intake harness in `spaarke-intake-pipeline-r1` #3128); OD-4 Cowork + area agent for drafting; OD-5 Dataverse profile + Code Page (§3.14); OD-6 MCP shape and `docs/architecture/SPAARKE-AI-SURFACES-AND-PACKAGING.md` (PR #3147); MCP parity is `spaarkeai-mcp-bff-api-r2` (#3150). D-A8 decided (no supervision in Phase 1; chaining designed for, §3.15). Fable reuse review (`notes/profile-reuse-review-2026-11-25.md`): profile anchors on the existing `sprk_userprofile`; only new surface is a server-written `sprk_agentprofile`/`-version` pair; owner questions are D-A18. The Spaarke AI agent is removed now (mcp-r2 §4.5; tenant steps E1/E2 are the owner's). Phase 1 child slug now `specialist-legal-agents-patents-r1`.

## OPEN NOW (checkpoint 2026-11-25 evening, read first)
- **Design v0.8:** D-A18 (2)–(8) accepted; T1 specialty = `sprk_technologyarea` (DEV-only table, needs columns + solution export); taxonomy tables are keys for L2, not storage (§3.14).
- **D-A18 all decided (owner 2026-11-25):** (1) new `sprk_agentprofile` pair; keep the LIVE `sprk_aipersona` (chat default prompt), template row points at it.
- **DEV facts (2026-11-25):** `sprk_playbookrun`/`noderun` 0 rows (BFF keeps runs in memory); `sprk_precedent` 0 rows; `sprk_technologyarea` 0 rows, 2 columns.
- **mcp-r2** (separate worktree) design v0.2 holds the MCP-side answers (incl. Dataverse MCP position §4.7).
- **Owner tenant action pending:** remove Spaarke AI (coordination runbook E1/E2).

## Quick Recovery

| Field | Value |
|---|---|
| **Active work** | **Phase 0 spec WRITTEN** (`spec.md`, commit a84c2762b5; reuse scan `notes/reuse-scan.md`). Owner go 2026-11-25. Interview: G1 = NDA then patents (both); spike tools on a never-merged branch (`IActionRunner` over `agreement-review`; Phase 1 uses playbook orchestration); scope G1/G2-rest/G3/G5/G6/patent spike/I-1..I-4, OUT G4 Outlook/G10/G11; staged small test runs. **Next:** owner reviews spec.md, then `/project-pipeline` only on the owner's go. Board #3002 Status Reason = In progress |
| **Step** | G8 work is closed: G8-P2 ✅, V1 ⚠️ (BR-3), V2 ✅ (BR-5 dynamic discovery default). V1a DROPPED, V3 DEFERRED to provisioning D3, G11 optional/not planned, V5 dropped (Spaarke AI removed). Coordination [0afb62] told the V2 result and test status; accelerator-r1 [d8204a] told D-A11/Q7/D-A16 + OD-2 (both 2026-11-25 late, no reply yet) |
| **Design version** | design.md v0.8 (`325edca96c`, D-A18 (1) `d93babf97e`) |
| **Uncommitted** | none after the housekeeping commit |

## Next action (exact)
**IN FLIGHT (2026-11-25 late): docs/ADR PR "playbook architecture"** (owner: "update our documentation and ADR so that it reflects our playbook architecture … so we do not recurringly re-assess 'the playbooks are not used'"). Worktree `C:code_filesspaarke-wt-docs-playbook-architecture`, branch `docs/playbook-architecture` off master 3c8778b74e, nothing written yet. Plan:
- (a) rewrite `docs/architecture/playbook-architecture.md` (now a SUPERSEDED redirect, the root of the confusion) as the canonical "what playbook means" doc (docs-architecture structure): concept D-209 + 3 formats; frozen node engine; live composite = prompted Action / coded workflow + Binding; run record = `sprk_playbookrun` "AI Run";
- (b) ADR-039 Path B amendment, concise `.claude/adr` and full `docs/adr`: "Playbook: concept live, engine frozen" + MUST record every AI run on `sprk_playbookrun`;
- (c) canonical AI doc `SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`: §4.2 pointer, a run-record subsection in §6, a revision-log row;
- (d) `CAPABILITY-INDEX.md` row 16 is MISLEADING (it points "run an AI analysis or add a playbook" at the frozen `AnalysisOrchestrationService` / `INodeExecutor`). Split it into: run an AI capability (Action + Binding: `LinearConsumers/ActionRunner.cs`, `IActionResolver.cs`); composite (`Services/Ai/ICodedWorkflow.cs`, `Narrators/DailyBriefingNarrator.cs`); record an AI run (`sprk_playbookrun/Entity.xml`); frozen engine node types (Insights only);
- (e) frozen banners: `ai-architecture-playbook-runtime.md` and `.claude/patterns/ai/node-executor-authoring.md` point to (a);
- (f) a FAILURE-MODES entry; INDEX rows;
- then the PR, and send the link to mcp-r2 (D-run-record-hold released; schema split proposed: its task 005 builds the shared columns, our Phase 1 adds the profile-version lookup).

The decision itself is recorded (fbf1bfb9a4). mcp-r2 was told.

0. **Run path reviewed (Fable, 2026-11-25, `notes/run-path-review-2026-11-25.md`):** Phase 1 uses the **action-based model** (Action via `IActionResolver` + `IActionRunner` behind a `PublicContracts` facade; `ICodedWorkflow` for multi-step), NOT playbook orchestration. The node engine is frozen by ADR-039, and ADR-013 was amended 2026-07-05. The spec is corrected. **Stale in design.md** (owner's doc, not edited yet):
   - §3.14/§12 D-A18 (7) (lookups on `sprk_playbookrun`, which requires a playbook);
   - §3.15 (`PriorOutputReference` is documentation only, not chaining).

   **Owner decision open:** where Action runs are recorded (new capability-run table / relax `sprk_playbookrun` / ledger + Decision Record); agree it with mcp-r2 (its FR-13 persists playbook runs only). Then `/project-pipeline` on the owner's go.
1. **Owner, by 2026-11-30 (admin center):** remove G8-a agent + 2 skills, G8-p2 `802fd9cc`, G8-p2-sl `d38f8df8`, G8-p2-org-learn `0421c980`, G8-p2-dd `cb5afd1c`. When confirmed, tell COPILOT-PLATFORM-COORDINATION so it closes the inventory rows.
2. **Owner:** Spaarke AI tenant removal (coordination runbook E1/E2); mcp-r2 Phase 0 removal deploy waits on E1.
3. **NDA owner questions** (`notes/nda-current-state-2026-11-24.md` §7): **SUPERSEDED by OD-2 (owner, 2026-11-25)**: NDA is a function; the harness is in intake-pipeline-r1. They no longer block the Phase 0 spec.
4. **On the owner's go:** Phase 0 spec from §7–§8: G1 go/no-go, G5 routing, G6 invoke-vs-recommend, G7 (in G2), Cowork patent-drafting spike, tool-returned-instructions test, code-inventory desk work. Every test agent gets its R4 row to coordination before upload (R7: owner + test user only).
5. **In parallel:** patent practice content (topic catalogue, baseline standards, references, schemas); golden-set cases need a patent attorney reviewer.
6. **Then:** Phase 1 child `specialist-legal-agents-patents-r1`, after G1 and mcp-r2's tool-host conventions + durable `sprk_playbookrun`.
- Build rules BR-1–BR-7: `phase0/g8/README.md` and the `declarative-agent` skill Step 5.5. App Insights: BFF requests sampled ~25–33%, no caller identity; use several turns per check.

## G8 result (recorded in phase0/g8/README.md, commit 9c557d7e95)
- An Agent Builder agent cannot use the tenant federated connector (A1 ❌, A2 "No connectors available" ❌, skills inside the agent ❌). Plain Chat works (`spike_whoami` ✅).
- App Insights shows only `tools/list` from Sydney, with no `tools/call`.
- Agents reach MCP only through their **own** tool. The owner's Copilot synopsis agrees: one MCP server, tenant connector = the horizontal path, agents = direct MCP clients.

## Open owner decisions
- **Gate A0 CLEAR (2026-11-24):** D-A11 split by layer, D-A16 one roadmap, Q7 shared catalogue (accelerator refreshes first); Q2 resolved from the ontology model: the positions master is a **Playbook** (D-209), not "Policy versions". Accelerator r1 told about D-A11/Q7/D-A16 (and OD-2) on 2026-11-25.
- **Phase 0 runs HERE** (OD-3 amended 2026-11-24, no child project). **NDA current state reviewed 2026-11-24:** `notes/nda-current-state-2026-11-24.md`. There are three NDA review routes already: platform agreement-review, Word plus our playbook, and Cowork nda-review. There is no NDA drafting. Submit-and-return flow: the pieces exist, but nothing connects them. Two NDA standards (KNW-011 v0.1 vs accelerator v1.2) need to become one. Owner questions in §7 of that note (one standard; the submit-and-return flow as Phase 1; drafting). ~~Do NOT start the Phase 0 spec until they are answered.~~ **Superseded by OD-2 (owner, 2026-11-25); no longer a blocker.** **2026-11-24 owner:** `sprk_servicerequest` is the request entity (front-door-r1 not going forward; no `sprk_legalrequest`); aim for ONE intake process model; a separate processing record is likely. **2026-11-25: the submit-and-return pipeline is its OWN project, CREATED:** `spaarke-intake-pipeline-r1` (#3128, Epic #421), worktree `C:/code_files/spaarke-wt-spaarke-intake-pipeline-r1`, design v0.1 there; discussion continues in that project; run record = extend `sprk_processingjob` (owner agreed). Earlier: code review DONE (3 Fable reviews, key claims re-checked); proposal written: `notes/intake-process-model-proposal.md` (four records, ten stages, run record = extend `sprk_processingjob` recommended; invoice machinery = pattern not code). Owner decisions in its §7. Read-only master worktree removed 2026-11-25.
- **Still open, not A0 blockers:** D-A12 framing; D-A13 Microsoft-first; D-A1 front door (G8-P2 now done); D-A7 (at G1); D-A5 pricing; BR-5 (dynamic discovery as default); the R1 change (with coordination).

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model; sub-agents can't write `.claude/`.
- Don't edit the accelerator or mcp-r1 worktrees; coordinate by message only.
- Copilot tenant artifacts: before uploading ANY agent, plugin, package or connector, send its R4 inventory row to COPILOT-PLATFORM-COORDINATION (Issue #3006). Install for the owner and test user only (R7). Tenant clicks are the owner's.
- App Insights query: use the PowerShell form in `phase0/g8/README.md`. `az` rejects KQL with `name=` aliases.
- Accelerator constraints: never put `answer-key/` or the real NDAs (Corteva, RPX/Helios) anywhere an agent can read; reuse `FORBIDDEN_IN_PACKAGE`.
