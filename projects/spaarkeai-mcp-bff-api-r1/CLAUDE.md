# Spaarke in Microsoft Copilot and Cowork: MCP Server in the BFF (r1) — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.

## 1. Scope and status

This project puts Spaarke's **Decide lane** and **Decision wizard** into Microsoft 365 Copilot Chat and Cowork through one stateless MCP server in the BFF at `/mcp/legal-operations` (ADR-028 A10 auth, OBO as the caller). It delivers four tools (`spaarke_show_decisions` model-visible; `spaarke_get_decision_plan`, `spaarke_search_decision_lookup`, `spaarke_complete_decision` app-only) plus an app-only paging tool; the BFF rework B-1–B-8 and B-10 that makes REST and MCP two thin adapters over one decision capability in `Services/Signals/`; the widget `ui://spaarke/decisions.html` (`src/client/widgets/spaarke-decisions/`, embedded in the BFF); dev-tenant registration (A10 connector client, federated connector "Spaarke", plugin package with skill `spaarke-decisions`, runbooks); Phase 0 tests T0–T14, E1–E5; docs and terminology. The Spaarke AI agent fallback (FR-17) is built only if T1 shows no widget in Chat. **Out of scope:** Do/Know lanes, Decided history, the full Console, inquiry outcomes, a model-callable complete tool, org prompts, Partner Center, customer stamps, Teams/mobile, recording the channel, switching the Console lookup picker, removing the April surface (#2575, #1523) or the DEBUG logger (#1574), and the billing review slice.

Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md`. Design: `design.md`. Plan: `plan.md`.

Tasks (2026-11-06, 28 POMLs): Phase 0 spike 001–003 · Phase A BFF rework 011–017 (run order 011 ✅ → 017 → 013 → 014 → 015; 016 alongside; 012 cancelled 2026-11-07, option C) · Phase B MCP host and tools 020–025, plus 010 (MCP caller-path verification, after 024 and before 025) · Phase C widget 030–032 · Phase D registration, deploy and live E2E 040–043 (041 deploy-gated; 043 conditional on T1) · Phase E docs 050–052 (050/051 main session only) · wrap-up 090. **Next action:** Wave 1 — 001 and 017 in parallel; 011 is done (`tasks/TASK-INDEX.md` "Parallel Execution Plan", re-planned 2026-11-07).

## 2. Binding rules for this project

**Auth and identity**
- MUST authenticate `/mcp/*` with OAuth at the Spaarke-tenant authority, scope `api://{bffAppId}/user_impersonation`, `aud` = the BFF app; explicit `.RequireAuthorization()`; `/mcp/*` under the FallbackPolicy (FR-01, FR-02).
- MUST run every tool as the caller (OBO), `systemuser` callers only; MUST NOT accept CIAM or A9 home-directory tokens — refused server-side **before any tool runs** (FR-02).
- MUST NOT use the connector's Entra SSO option; MUST NOT pass the caller's token downstream, log it, or log its claims (FR-02, NFR-05).
- A failed OBO never falls back to app-only; any app-only read applies the caller's own record-access check (FR-02).
- MCP tools take the caller from the request `HttpContext` through `IHttpContextAccessor`. A null context or an unauthenticated caller fails closed with `UserContextRequired`, never app-only. No `ICallerContext` in v1 (FR-10, owner option C 2026-11-07). Keep `/mcp/*` stateless with `PerSessionExecutionContext=false`: task 010 pins both, and decision capabilities run only inside an HTTP request (v1 limitation).

**Decision domain invariants**
- `DecisionCommitService` stays the single owner of completion; writes go only through it, need explicit Confirm, a prior plan read and an idempotency key; nothing relies on a host confirmation prompt (FR-08, NFR-06).
- MUST NOT change `SignalReadService` read rules or `DecisionCommitService` validation, idempotency or execution order (spec MUST NOT).
- Commit invariants move **byte-for-byte** through B-1 and each is stated in the B-1 PR: order (1a) caller readability → uniform 404; ledger replay; (1c) app-only Signal read; (1c-billing); `PrepareAsync`; (1c′) `CallerMayDecideAsync`; preflight; writes; locks released in `finally`. Replay decided server-side only. D-197 `CheckVersionOwnerAsync` stays put. `SignalWriter`, `DecisionRecordWriter`, `DecisionCallerRights` untouched.
- MUST NOT add a second billing-refusal check; one MCP test proves a billing Signal returns the mapped `ontology.decision.decided_in_invoice_review` refusal and writes nothing.
- REST responses of the signal, plan and decision routes stay byte-for-byte unchanged against recorded contract tests; one `reasonCode` vocabulary; transport errors never carry domain failures (FR-09).
- Decision-slice services keep their `HttpContext`/`ClaimsPrincipal` parameters. Capability methods take the request `HttpContext` (or `ClaimsPrincipal`) and pass it through unchanged (FR-10 as amended 2026-11-07, option C).

**Model-visible output**
- Privileged Work Items (`sprk_privilegeflagged` on Signal or subject) reach `content`/`structuredContent` of model-visible tools only as "N privileged items — open to review"; detail only via app-only tools (FR-04).
- `spaarke_show_decisions` input is a closed `category` enum mirroring `laneCountFilters.ts` (incl. `invoiceDecisions`), no other properties (FR-03). Every tool has an explicit `readOnlyHint`, a title, and a ≤ 1,024-char "use for / not for" description.
- Every tool result < 64 KiB at 200 Work Items; page 1 ≤ 50 slim rows; list row ≤ 600 bytes (FR-05, FR-11, NFR-02).

**Placement and platform**
- Register through `AddMcpModule`, map in `EndpointMappingExtensions` as `/mcp/{area}`; v1 maps only `legal-operations` (FR-01, ADR-010).
- `Mcp:Enabled=false` → 503 ProblemDetails via null object, no tool runs (FR-01, ADR-032).
- Extend `RouteAuthorizationGuardTests` to `MapMcp` with a must-fire test (B-8).
- MUST NOT attach the external-plane (`ExternalCollaboration`) policy to any MCP tool or the B-3 route (FR-07).
- MUST NOT call Dataverse's built-in MCP server or any route that bypasses the BFF's rules.
- MUST NOT add background work (ADR-052). Every tool finishes under 30 s.
- Widget: no network origins (`_meta.ui.csp` empty), no tokens, only `callServerTool`; theme from host context, none in storage; Fluent v9 semantic tokens only; aim < 1 MB, fail the build > 3 MB (FR-12, NFR-03, NFR-07).
- Every BFF task records publish-size delta vs a fresh master build and checks for new HIGH CVEs (NFR-04). The `Microsoft.Extensions.AI` bump passes AI unit + integration suites (NFR-08).
- Conformance suite (`--spec-version 2026-07-28`) green in CI (NFR-09).
- Deferred work and newly found issues: `/project-defer-issue-tracking` writes `notes/defer-issues.md` AND a GitHub issue — never one without the other.

ADR tensions approved for this project (root CLAUDE.md §6.5):
- ADR-028 (A8) — **Path B, done** — the connector client plane is ADR-028 **A10** (owner-approved 2026-11-05); a T5 failure requires a follow-up amendment. Spec "ADR Tensions"; design §6.2.
- ADR-013 / `bff-extensions.md` — **Path C** — MCP host stays in the BFF; extraction criteria not met. Spec "Placement Justification"; design §14.
- ADR-008 / ADR-010 / ADR-032 — **Path C** — per-tool policies (B-7), module DI (B-6), null-object kill switch. Design §8.
- ADR-002 — **Path C** — same single invariant owner; widget previews, server decides. Design §18.

## 3. Owner directives and standing decisions

Full log: `notes/decisions.md`. Owner decisions O-1–O-26: `design.md` §0.
- 2026-11-07 — **Option C:** `ICallerContext` (B-4) is deferred; spec FR-10 is amended and supersedes design §8 B-4. MCP tools use the request `HttpContext` and call the same decision services as REST. Task 010 is now the MCP caller-path verification (it depends on 020, 021, 023 and 024, and blocks 025); 012 is cancelled. — `notes/decisions.md` "Option C"
- 2026-11-06 — **Ontology condition 1:** no dev BFF deploy from this branch until ontology's #2580/#2579 pair has deployed (it waits for ebilling task 112). Phase 0 live tests run from a separate spike branch cut from master (`spike/spaarkeai-mcp-phase0`, never merged), deployed only with the owner's per-SHA go in uac-r2's session. If this branch must deploy sooner, back the merge out first. — `notes/decisions.md` "Merge of #2580 / #2579"
- 2026-11-06 — **Ontology condition 2:** D-207 order and replay guard unchanged; the MCP commit goes only through `DecisionCommitService`'s existing entry, never a second route that skips 1c-billing.
- 2026-11-06 — **Ontology condition 3:** their pair merges to master first; our master PR merges after. If their branches get new commits, re-merge — merge, never rebase.
- 2026-11-06 — #2580/#2579 are merged into this branch; plan and build against that code (`notes/pre-pipeline-context.md`).
- 2026-11-06 — **D-208:** "Finding" is the user word **only** for a billing Work Item in billing link-outs; generic rows stay "Work Item"; never an entity or code name.
- 2026-11-06 — **D-209:** qualify "playbook" by its format (JPS / legal / contract-review playbook) in user-facing Copilot text wherever the format matters.
- 2026-11-06 — **Naming** (O-25): follow `notes/coordination/2026-11-06-copilot-naming-and-ownership.md` §1 — tools `spaarke_{verb}_{object}`; skills `spaarke-{area}-{capability}` (core drops the area); widget `ui://spaarke/{widget}.html` in `src/client/widgets/{skill-name}/`; plugin slug `spaarke` with explicit display name; connector "Spaarke"; OAuth client "Spaarke Copilot Connector – dev"; "Spaarke AI" stays the agent name; "Spaarke Copilot" retired; never "Spaarke Connect" for a Copilot connector.
- 2026-11-05 — `Mcp:Enabled` defaults to `false`; turned on in dev only (spec Assumptions).
- 2026-11-05 — The owner runs every admin-center step as Global Admin (O-20); a second admin may be designated. Escalate, don't attempt.
- 2026-11-05 — Client-secret writes for the connector client need human confirmation (root CLAUDE.md §9; FR-16). Prefer a public client with PKCE.
- 2026-11-05 — No model-callable complete tool (O-14). April surface stays (O-15) except the OpenAPI action if FR-17 is built.
- 2026-11-06 — Portfolio: Epic #421 (SPAARKE AI AND AUTOMATION). Hot-path `root-CLAUDE-md` = YES (FR-18).

## 4. Coordination

- **spaarke-ontology-platform-r1 / r2** — owns `Services/Signals/**` and terms (`ontology-component-model.md` §1.1, §3). Reviews **every** PR touching `Services/Signals/**`, `Api/Signals/**` or the worklist route. The B-1 PR lists every commit invariant. Owns #2580/#2579 (merged here; deploy gate above).
- **unified-access-control-r2** — reviews every auth diff before merge (010, 021, any `AuthorizationModule.cs` change) against FR-02's four checks: FallbackPolicy covers `/mcp/*`; must-fire guard test on unauthenticated `/mcp/{area}`; CIAM and A9 refused server-side before any tool runs, tested; no app-only fallback on failed OBO. Phase 0 spike deploys run in their session with the owner's per-SHA go.
- **spaarke-legal-ai-accelerator-r1** — plugin/skill naming agreed (OD-20): one plugin per area, skills `spaarke-{area}-{capability}`, build gate `^spaarke(-[a-z0-9]+)+$` ≤ 64 chars; first project to start patents owns `spaarke-patents`.
- **ebilling-r1** — EB-D-194: invoice decisions happen only in Invoice Review. v1 shows billing Work Items only as a link out to `sprk_invoicereviewpage` (`invoiceId`, optional `lineId`, bare lower-case guids). A Copilot billing slice needs an owner decision plus uac-r2 and ebilling review.
- **spaarke-auth-system-of-record-r1** — its planned ADR-028 amendment must be A11 or higher (A10 is ours); tell at next contact.
- **agent-framework-fit-assessment** — a proposed `Sprk.Insights.Mcp` becomes a new `/mcp/{area}` on this host, not a second server.
- Peer sessions: `spaarke-wt-spaarke-ontology-platform-r1-38`, `spaarke-wt-ebilling-r1-c7`, `spaarke-wt-spaarke-legal-ai-accelerator-80`, `spaarke-wt-unified-access-control-r2-20`.
- Run `/conflict-check` before each BFF PR (`AuthorizationModule.cs`, `Services/Signals/**` are shared).

## 5. Environment and live actions

- Environments: **dev BFF and dev tenant only**. No other environment in v1.
- Phase 0 runs from `spike/spaarkeai-mcp-phase0` (cut from master, own worktree, never merged).
- Any deploy needs the owner's explicit go **per SHA**. Dev BFF deploy from this branch (task 041) additionally waits for ontology's pair to deploy (§3).
- Admin-center steps (connector creation, plugin deployment approval, Developer Portal OAuth) are run by the owner; sessions prepare runbooks and verify.
- Never store a client secret in the repo or Key Vault without the owner's confirmation; a confidential client's secret lives only in Microsoft's registration (A10).
- Tests use a licensed **non-admin** user for consent and approval behaviour.
- Do not touch the April surface (`src/solutions/CopilotAgent/`, `/api/agent/*`, `AgentToken:CopilotAudience`) unless FR-17 is adopted.

## 6. Gotchas — do not re-learn

- 2026-11-06 — `HttpContext` reaches beyond the spec's list: `DecisionCommitService.CommitAsync` (L439) and helpers (L622, L770, L927, L1578), `DecisionPlanReadStamp` L42/L86, `Actions/DecisionParameterSources` L109/L404, `Actions/DecisionActionExecutors` (record field `HttpContext Http` L73), `Actions/DecisionRouteCores` (7 methods), `AssignTaskNotifier` (5), `DecisionCommitFollowUps` L70, `Actions/BudgetInquiryExecutor` (`IHttpContextAccessor`). `SignalReadService` takes `ClaimsPrincipal` (no change). Fix (2026-11-07, option C): leave these parameters in place; capabilities and MCP tools pass the request `HttpContext` through; 012 is cancelled.
- 2026-11-06 — The worklist list projection does not select `sprk_privilegeflagged` (only `DecisionCommitService.cs:313` reads it). Fix: 015 adds it to the list projection only.
- 2026-11-06 — `WorklistItemSummary` does not exist; the row type is `WorklistItemRow` (with #2580's `InvoiceReview` member). B-5 creates the summary and must keep `invoiceReview`.
- 2026-11-06 — `WorklistLanePanel` is not exported from the Worklist barrel. Fix: deep import plus the exported `buildLaneView` (`worklistView.ts`); fallback a single-lane prop on `WorklistWidget`.
- 2026-11-06 — `openInvoiceReview.ts` calls Xrm `navigateTo` and cannot run in the widget. Reuse only its `data` rules (`buildInvoiceReviewLaunch(...).pageInput.data`, encoded once more inside a URL) with host `openLink`.
- 2026-11-06 — `createWorklistService({ fetch })` expects a `ResponseFetchFn`; MCP shims must return a real `Response` and pass `reasonCode` through so `decisionDisplay.ts` shows "Decide this in Invoice Review".
- 2026-11-06 — The live Console mounts the Worklist via `src/solutions/LegalWorkspace/src/sections/worklist.registration.ts`, not the direct widget; any shared-lib seam must keep that mount working.
- 2026-11-06 — #2580's tests assert exactly one batched `BillingEventRead` per worklist response; B-1/B-5 must keep that count.
- 2026-11-06 — No bundle-size check exists in CI yet. Fix: task 030 adds it; the widget build must run before `publish-bff-artifact.yml` embeds it.
- 2026-11-06 — The MCP transport returns 406 without `Accept: application/json, text/event-stream`; streamed responses need buffering disabled (`WorkspaceMatterEndpoints.cs:196-200`).
- 2026-11-06 — Agent-tool sub-agents cannot write `.claude/` (root CLAUDE.md §3). Fix: tasks 050 and 051 run in the main session.

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/decisions.md` · `notes/defer-issues.md`
- `notes/coordination/2026-11-06-copilot-naming-and-ownership.md` (naming §1, coordination §2) · `notes/pre-pipeline-context.md` (#2580/#2579 facts)
- Research: `notes/mcp-project-research-starter.md`, `notes/copilot-as-spaarke-surface-2026-11-05.md`
- Applicable ADRs: ADR-001, ADR-002, ADR-008, ADR-010, ADR-012, ADR-013, ADR-015, ADR-016, ADR-019, ADR-021, ADR-028 (A8, A10), ADR-029, ADR-032, ADR-038, ADR-050, ADR-052
- Constraints: `.claude/constraints/bff-extensions.md`, `.claude/constraints/auth.md`, `.claude/constraints/azure-deployment.md` (publish-size rule)
- Related projects: spaarke-ontology-platform-r1, unified-access-control-r2, spaarke-legal-ai-accelerator-r1, ebilling-r1, spaarke-auth-system-of-record-r1

---

## Deferrals & Issues — tracking obligation (read this)

This project tracks deferred work + newly-discovered issues in TWO places, kept in sync:

1. **`notes/defer-issues.md`** — source of truth (full context, links, traceability)
2. **GitHub Issues** on the portfolio board (visibility — others can see + claim)

### When to file

| Situation | Use |
|---|---|
| Spec scope item dropped to keep this project shippable | DEF-{NNN} |
| Refactor / cleanup > 2hr that's not in current spec | DEF-{NNN} |
| Production / dev bug uncovered outside this project's responsibility | ISS-{NNN} |
| Telemetry / monitoring gap requiring follow-up | ISS-{NNN} |
| Failure mode discovered + worked around (not fixed) | ISS-{NNN} |

### How to file

Invoke `/project-defer-issue-tracking` (alias `/defer`) — it writes to BOTH places in one step.

NEVER add an entry only to `notes/defer-issues.md` and skip the GitHub Issue. The whole point of this protocol is visibility. The `push-to-github` skill scans for entries without GitHub URLs and blocks push until they're filed.

### Status

See `notes/defer-issues.md` for the current rollup. Use `gh issue list --label spaarkeai-mcp-bff-api-r1` for the visible-to-team view.
