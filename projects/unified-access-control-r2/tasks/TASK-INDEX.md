# TASK-INDEX — `unified-access-control-r2`

> **58 tasks** across 6 phases · generated 2026-08-21 · `scripts/Validate-TaskPoml.ps1`: **PASS** (57 clean, 0 errors, 1 benign WARN on 090)
> Source: [`spec.md`](../spec.md) 32 FRs / 7 NFRs · [`plan.md`](../plan.md) · [`CLAUDE.md`](../CLAUDE.md)
> Status legend: 🔲 pending · 🔄 needs retry · ✅ complete

Number gaps (020–029, 045–049, 059, 070–079, 084–089) are intentional insertion room.

---

## ⚠️ Read before executing anything

| Rule | Why |
|---|---|
| **001 blocks every Phase 0 code task** | NFR-07 — the access-path test baseline is near-zero. Characterize before changing behaviour |
| **034 is a blocking merge gate for 036** | NFR-04 — if impersonation is inert the query silently returns org-wide rows. Equality between impersonated and app-only = fail |
| **008 (delegation) must ship before 063/065** | Otherwise the PCF "+ User" button is one-click privilege escalation on a confidential matter |
| **030 before 032 · 031 before 035/036 · 040 before 041** | ADR amendments sanction the shape; path B means the amendment merges before or alongside dependent code |
| **030 / 031 / 040 are main-session-only** | They edit `.claude/**`. Sub-agents cannot write there (root CLAUDE.md §3) — "Edit denied" is the boundary working |
| **`/conflict-check` before EVERY BFF PR** | Surface shared with shipped `SPA-external-access-platform-r1/r2` + `teams-app-r1`; draft `SPA-r3` must be notified |

---

## Phase 0 — Enforcement remediation (20 tasks)

| # | Task | FR / finding | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| ✅ 001 | Access-path characterization + negative suite | NFR-07 | — | **P0-W0** | ✅ | sonnet | high |
| ✅ 002 | Authorize document download | FR-01 / A-1 | 001 | — | ❌ | sonnet | high |
| ✅ 003 | `OperationAccessPolicy` keys + completeness test | FR-03 / A-3,A-20 | 001 | — | ❌ | sonnet | high |
| ✅ 004 | `AuthorizationService` caller-scoped | FR-02 / A-2 | 001,003,014 | — | ❌ | **opus** | **xhigh** |
| ✅ 005 | Lift the Read ceiling | FR-04 / A-20 | 001,004 | — | ❌ | sonnet | high |
| ✅ 006 | Caller-scoped `PermissionsEndpoints` | FR-05 / A-4 | 001,004 | — | ✅ | sonnet | high |
| ✅ 007 | Enforce grant expiry in the read filter | FR-06 / A-5 | 001 | — | ❌ | sonnet | high |
| ✅ 008 | Delegation rule — Write-on-target | FR-07 / A-6 | 001 | — | ❌ | sonnet | high |
| ✅ 009 | Scope-check external To Do PATCH (+H-8a) | FR-08 / A-7 | 001 | — | ❌ | sonnet | high |
| ✅ 010 | Idempotent grant + revoke-all | FR-09 / A-11 | 001 | — | ❌ | **opus** | **xhigh** |
| ✅ 011 | Reject same-entity self-join | FR-10 / A-17 | 001 | **Wave A** | ❌ | sonnet | high |
| 🔲 012 | Track or disable anonymous share links | FR-11 / A-14 | 002 | — | ❌ | sonnet | high |
| ✅ 013 | Workforce email `oid` no-hijack | FR-12 / A-18 | 001 | **Wave A** | ❌ | sonnet | high |
| ✅ 014 | Cache key includes auth mode | FR-13 / A-19 | 001 | **P0-B** | ✅ | sonnet | high |
| ✅ 015 | Deterministic + complete membership paging | FR-14 / A-10 | 001 | **Wave A** | ❌ | sonnet | high |
| ✅ 016 | Close-project cascade (contact + org) | FR-15 / A-12 | 001 | — | ❌ | sonnet | high |
| ✅ 017 | SPE revoke matcher + H-8b relic | FR-16 / A-13 | 001,010 | — | ❌ | sonnet | high |
| ✅ 018 | Remove dead filter + bound `in`-clause | FR-17 / A-15,A-16 | 001 | **Wave A** | ❌ | sonnet | high |
| ✅ 019 | Fix `LookupUserMembership` `["*"]` | FR-17 / A-22 | 001 | **P0-B** | ✅ | sonnet | high |
| ✅ **020** | **Org-grant SPE member cleanup** | **FR-16b** / 017 §6 | 017 | **Wave A** | ❌ | sonnet | high |

**Critical path**: 001 → {003, 014} → 004 → {005, 006} · plus 001 → 010 → 017 → **020** · plus 002 → 012

---

## Phase 0b — Review remediation (9 tasks, filed 2026-08-24)

Filed by owner decision from the multi-agent re-review of the 13 completed Phase 0 tasks.
Findings: [`notes/review-2026-08-24-findings.md`](../notes/review-2026-08-24-findings.md).
**021–027** are the review's §6 proposals; **028** and **029** were both found while resolving
task 009 — 028 from its escalation, 029 from the read/write asymmetry its fix created.

| # | Task | Finding | Deps | Safe | Tier | Effort | Why |
|---|---|---|---|---|---|---|---|
| ✅ **022** | Document-surface authorization sweep | **C1,C2,C3**,H2,H3,H5 | 002 | ❌ | **opus** | **xhigh** | **DONE 2026-08-24** — 19 of 22 routes gated; 3 remaining are collection-shaped (Phase 1). See outcome below |
| ✅ **021** | **RE-SCOPED + DONE**: matched design 5.1 - dropped BU + account creation, BU resolved by name, owned via its default team, container-only stamp, fails loudly | Critical (C4/C5) | 008 | ❌ | **opus** | high | **COMPLETE 2026-08-25.** Live 409 regression CLOSED. 9/9 perturbations bit; the sweep found **2 coverage holes in the FAKE** (it ignored `$top` and the team-filter predicates - third instance of task 016's class). **2 owner findings**: the BU is `Secure Project` **SINGULAR** (docs said plural - 8th docs-vs-metadata instance), and the owner team holds **System Administrator** (review §D says it must not). Neither document isolation nor FR-28's share is achieved by this task. See [`notes/task-021-provisioning-stamping.md`](../notes/task-021-provisioning-stamping.md) |
| 🔲 **025** | Test-integrity: 4 untested seams + gate mechanism + false-claim tests | H6,M3,M7 | 003,007,017 | ❌ | **opus** | **xhigh** | This is WHY the rest could hide — the central gate can return blanket rights with the suite green |
| 🔲 **023** | Grant upsert must write `sprk_expiresdate` (+ FR-09 acceptance) | H1 | 007,010 | ❌ | sonnet | high | A-5's shape resurrected on the write path by the two tasks that closed it on the read path |
| 🔲 **029** | External To Do read + create parity (matter + WA) | (task 009 asymmetry) | 009 | ❌ | sonnet | high | Task 009 widened PATCH but not list/create — **the write plane is now wider than the read plane**. Owner intent: parent flows from creation context |
| 🔲 **028** | Service request: the missing 4th core accessible set | (task 009 escalation) | 009 | ❌ | sonnet | high | Model names 4 core types; `CallerPrincipal` carries 3. Completes the owner parity decision |
| 🔲 **024** | SPE Graph paging + `/revoke` status parity | M1,M2 | 016,017 | ❌ | sonnet | high | `container_not_cleared` currently gives FALSE assurance on a multi-page container |
| 🔲 **026** | Schema-truth doc repair | M4,M5 | — | ✅ | sonnet | medium | Cheapest, and the only one attacking the CAUSE of five stale-column recurrences. **⚠️ SCOPE ADDED 2026-08-24**: `src/solutions/SpaarkeCore/entities/sprk_project/secure-project-fields-schema.md` is the **root cause of Critical C4/C5** — it documents `sprk_securitybuid`/`sprk_externalaccountid`, live metadata has `sprk_securitybu`/`sprk_externalaccount`. The provisioning code was implemented faithfully FROM this doc. Its relationship names and its `pac data export` example are wrong too. **Fix it even if 021 lands first** |
| 🔲 **027** | e2e tier reconciliation — or deliberate retirement | M6 | — | ✅ | sonnet | high | A suite that reads as coverage, pins 4 nonexistent contracts, and runs in no workflow |

> **Task 022 outcome (2026-08-24)** — the class was **22 routes across 4 files**, not the "~15" the
> review estimated. **19 are now gated**; the 3 remaining have no caller-supplied document id and need
> collection-level scoping (Phase 1 evaluator work, tasks 032/054). Full inventory + reasoning:
> [`notes/task-022-document-surface-inventory.md`](../notes/task-022-document-surface-inventory.md).
>
> - **Two operation keys added** (`write`, `delete`) — `AddDocumentAuthorizationFilter`'s own `<param>`
>   doc had always advertised `"read", "write", "delete"` while two thirds of that contract could not
>   be honoured, and honouring it would have produced an unconditional 403 rather than a compile error.
>   Both verified reachable in the snapshot before being added.
> - **C2 + C3** (destroy), **H2** (tamper + pointer disclosure), **H3** (checkout family + `analyze`),
>   **C1** (bulk download), **5 URL-minting reads**. **H5** fixed (sixth stale-column instance).
> - **C1 needed both halves**: per-document authorization AND collapsing the `_FAILED.txt` denial
>   reason into the not-found reason. Fixing only the first would have created a 500×-amplified
>   enumeration oracle that did not exist before.
> - **New defect found by writing the first ever test for bulk download**: `ZipArchive` is synchronous
>   and Kestrel disallows sync IO, so the endpoint threw on its happy path. Fixed per-request via
>   `IHttpBodyControlFeature`. Honest reading: C1 was a *working enumeration* primitive and a *broken
>   exfiltration* one.
> - **Three doc comments asserting "enforcement happens elsewhere" were false** (bulk-download twice,
>   `/checkout` once). One — `share-link`'s — was **true** on inspection. Verify the mechanism a
>   comment names; do not treat the comment as evidence.
> - **Perturbations: 21 run, 20 bite.** The one zero was confirmed by a two-factor experiment to be
>   *unreachable* code rather than missing coverage (14 → 17 failure delta proves the guard is
>   covered once reachable).


### 🔴 Phase 0b — two tasks added 2026-08-25 by owner decision

| Status | Task | Why | Sev | Deps | ∥-safe | Tier | Effort | Notes |
|---|---|---|---|---|---|---|---|---|
| ✅ **045** | **auth-v4 integration / CI unblock — DONE** — merge master, migrate `CallerRecordAccessProbe` off its client secret, repair 5 Moq ctor sites | **BLOCKER** | 021 | ❌ | **opus** | **xhigh** | **COMPLETE 2026-08-25. `Router = SUCCESS` at `c5edf2448` — the FIRST CI-adjudicated state of this branch since `ffc2cb1de`; PR #812 is MERGEABLE.** Found TWO unpredicted causes beyond the two diagnosed: the probe had **ZERO test coverage** (every fixture substitutes it), and **master shipped 6 stale tests** asserting endpoints it had deleted — invisible to master's own gate because tier1 filters that project by changed surface and tier2 is advisory. PR #812 is `CONFLICTING`, and a conflicted PR dispatches **NO** workflows (not a red gate — *no* gate). Trial merge produced 22 failures: master's auth-v4 forcing functions FR-F1/FR-F2 fail on `CallerRecordAccessProbe.cs:134,137` (**D1 with its premise expired** — auth-v4 closed E-3 on 2026-08-24 without seeing an unmerged site), and master widened `DataverseWebApiClient`'s ctor so Moq class proxies throw. Fix for the former is a **faithful port of master's `DataverseUserClient`**, not new auth design. See [`notes/ci-dark-and-authv4-integration-2026-08-25.md`](../notes/ci-dark-and-authv4-integration-2026-08-25.md) |
| ✅ **046** | **`Secure Project Owner` role — DONE** — created, assigned to the owner team, **System Administrator REMOVED** | High | 021 | ❌ | **opus** | **xhigh** | **COMPLETE 2026-08-25.** Role `e4ebabd9-…` holds **exactly ONE privilege — `prvReadsprk_Project` @ User (Basic) depth**; on that one team only (0 users, 0 other teams); team still has **0 members**; assignment re-proven **after** the SysAdmin removal. The hypothesis was wrong in every dimension — **7 privileges @ BU depth → 1 @ User depth**; Dataverse named the one it needed (*"is missing prvReadsprk_Project"*), and `Write` proved unnecessary (the team is an ownership anchor, never an actor). 🔴 **BUT: confirmed empirically that secure projects are NOT isolated** — `Test User 1`, an ordinary non-admin, **read a real secure project** owned by the secure team, because `Spaarke Basic User` holds `prvReadsprk_Project` at **`Deep`** and `Deep` at root reaches every descendant BU. That is **§5.2's unremediated prerequisite**, now proven end-to-end rather than inferred from a depth census. Negative control passed (a `Basic`-depth principal WAS denied), so BU containment works once depth is fixed. **Owner decision needed**: BU restructure (§5.2's decided fix) vs. narrow `Deep`→`Local` (zero measured blast radius today). Child-entity question answered: **18 entities / 19 lookups** (POML said 3), `sprk_document` has **two** — filed as its own task, not implemented. See [`notes/task-046-secure-project-owner-role.md`](../notes/task-046-secure-project-owner-role.md) |
| 🔲 **047** | **Validate provisioning END-TO-END against live dev** — deploy, create a real secure project, prove it gets its OWN container | High | **045, 046** | ❌ | **opus** | high | Provisioning has **never once run successfully in any environment** — dev holds **ZERO** secure projects. ⚠️ **Assert INEQUALITY, not presence**: three existing projects already carry `sprk_containerid = b!vzGDfDpd7km…`, identical to the ROOT BU's container, so "a container id is set" is precisely the false positive. Also verifies both DENY paths (absent BU → fail closed; re-post → 409) and the live proof the 409 regression is closed. Deploy is **operator-driven** (`Deploy BFF API` is `disabled_manually`). Does NOT prove document isolation — nothing reads the container yet |

### 🔴 Phase 0c — Secure Documents (added 2026-08-25; owner decision: **broker-only for BOTH workforce and external contacts**)

> **Read [`../SECURE-DOCUMENTS-BUILD-PLAN.md`](../SECURE-DOCUMENTS-BUILD-PLAN.md) before executing any of these.** It is the coordination contract: the decision, the three invariants, what each component is *for*, and the honest claim at the end of Wave 2.
>
> **Why now**: **zero secure projects exist in any environment.** Build it before the first one and there is never a migration.
>
> **The decision in one line**: the BFF is the single access-decision point for every document and every byte, for every principal kind. No user is ever granted an SPE container permission — `GrantMembershipAsync` stays at zero callers. The per-project container is **blast-radius containment**, not the live ACL.

| Status | Task | Wave | Why | Deps | ∥-safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| ✅ **070** | **Gate `POST /api/ai/search`** — authorize the parent for `scope=entity`, refuse `scope=all`, stop emitting `driveId`/`speFileId` | 1 | **Exploitable NOW.** The filter returns allow for *every* scope incl. `default`. Any authenticated non-admin gets tenant-wide document names, AI summaries, TL;DRs and SPE pointers. Never touches SPE, so container ACLs are irrelevant | 046 | ❌ | **opus** | **xhigh** |
| ✅ **071** | **Retire the drive-keyed OBO routes** — delete preferred, gate as fallback | 1 | `AddDocumentAuthorizationFilter` appears **zero** times in `OBOEndpoints.cs`; read, PATCH, **DELETE**, enumerate. Under broker-only they have no legitimate purpose | 046 | ✅ | **opus** | high |
| ✅ **072** | **Gate `share-link`** + bounded expiry + anonymous no longer the default | 1 | The one route on the group with no filter; minted a **non-expiring, anyone-with-the-link** URL. Task 002 closed its eight siblings and missed it. Also fixed a pre-existing defect masking **every** handler exception on all 9 routes as `500 "Authorization Error"` | 046 | ❌ | **opus** | high |
| ✅ **073** | **Container upload RETIRED, not gated** — `Api/UploadEndpoints.cs` deleted (218 lines, 0 additions) | 1 | Was **exploitable at HEAD**: `PUT /api/containers/{containerId}/files/{*path}` took the container id off the route and wrote **app-only (MI)**, so no container ACL was needed to abuse it. Closed by retirement rather than a gate — all three routes had no legitimate caller, and retirement removes the wrong-resource-domain **shape** along with the defect. Regression guard: `tests/integration/regression/MiContainerKeyedWriteRouteRetirementTests.cs`. **Merged 2026-08-27** (`904051d29`) — one modify/delete conflict, deletion taken | 046 | ✅ | **opus** | high |
| ✅ **074** | **ArchTest forcing function** — an ungated document/Dataverse route fails the build | 1 | ⭐ **Highest-value task in both waves.** Enforcement is by enumeration and the count has been wrong *every* time: ~15 estimated → 22 found → then `/api/ai/search` + `share-link` found *after* that sweep → then 5 more in `OBOEndpoints`. Precedent: CORS-drift gate `34ef54542`. **Has now produced 3 holes: 077, 078, 081** | 046 | ✅ | **opus** | **xhigh** |
| 🔄 **075** | **Record-aware container resolver** — secure record → its OWN `sprk_containerid`; else BU cascade; **absent secure container FAILS CLOSED** | 2 | Provisioning stamps the container (021) and **nothing reads it**, so secure content lands in shared containers. SPE is **additive-only** — *"you can't break inheritance on arbitrary files"* — so no per-item permission can ever retract that. This seam IS the document guarantee | 046 | ❌ | **opus** | **xhigh** |
| 🔲 **083** | **Container-selection authorization sweep** — every SPE write path must have the SERVER choose the container from a record it authorized | 9 | 🔴 **OWNER-DIRECTED 2026-08-27.** One defect class, ≥5 instances, **2 LIVE** (`PUT /api/drives/{driveId}/upload` is **app-only MI**, `DELETE /api/drives/{driveId}/items/{itemId}` is a **destroy**) + 2 **UNTRACED** + Compose (#858, behind PR #806). Owner rejected offloading: acceptance criteria FORBID handing any row to another project | **075, 076** | ❌ | **fable** | **xhigh** |
| 🔄 **076** | **Record-keyed upload contract** — routes take `(entity, recordId)`, the server resolves the container, the client stops deciding | 2 | **REWRITTEN 2026-08-27 to option (C)** (see note below). Under (A) the authorization key and the container are **two keys for one decision** and F-9 proves they already diverge in shipped code; under (C) they are the same value by construction. Also still owns the 7 server-side `ArchiveContainerId` sites (no client, no wizard — the easiest to miss) and the two harmful `sprk_containerid` writes | **073, 075** | ❌ | **opus** | high |

| ✅ **077** | **Authorize `POST /api/ai/search/records`** — the twin of 070's defect, on the same route group | 1 | **Exploitable NOW.** `RecordSearchAuthorizationFilter` reads `tid`, writes an audit log, calls `next()`. Authorizes nothing. Leaks record **names** tenant-wide — for a secure matter the name is often the sensitive fact | **070** | ❌ | **opus** | **xhigh** |
| 🔲 **078** | **Authorize `GET /api/v1/containers/{containerId}/documents`** | 1 | Lists **any** container's documents behind `RequireAuthorization()` alone — the read-side twin of 073. Needs 075's container→record mapping | **075** | ✅ | **opus** | high |
| ✅ **079** | **Version routes RE-KEYED onto the document row + gated** (was: "gate the two drive-keyed OBO version routes") | 1 | Two MORE routes of 071's shape, incl. **prior-version BYTES**. Unlike 071's four these had a **live caller** (`versionHistory.ts:81`), so they had to be GATED not deleted. Found by a **caller** inventory, not a route inventory. Went further than gating in place: drive-keyed → `GET /api/documents/{documentId}/versions[/{versionId}/content]`, each `AddDocumentAuthorizationFilter("read")` (`DocumentVersionEndpoints.cs:133,182`), so the SPE pointer is now read off the row the caller was authorized against instead of supplied by the caller. Perturbation-proved the **gates** keep 074 Rule A green, not the waivers. **Merged 2026-08-27** (`229c4f849`) — zero conflicts, zero overlap with 073 | 071 | ✅ | **opus** | high |
| ✅ **080** | **Restore cross-record search** — `scope=all` FILTERED per row, not refused | 1 | 070 refused `scope=all` on the premise "no caller needs it". **That premise was FALSE** — the code page emits it for the "All" row, for blank-label rows, and for *every* search after the user types a query. Authorizes the PAGE, not the corpus, so no dependency on 031 | **070** | ❌ | **opus** | **xhigh** |
| 🔲 **082** | **Caller-identity census** — a downward ratchet on direct claim reads + the §11 four-primitive decision | 1 | **Filed 2026-08-27 by owner directive.** After 081 + PR #832 there are **four** caller-identity primitives, and **71 files read identity claims directly** while #832 covers **30** `src/server` files — leaving ~40 unaudited, incl. ~10 further `*AuthorizationFilter.cs`. The sibling project proved the two MOST plausible-looking idioms (`oid ?? NameIdentifier`, `FindFirst("oid")`) are the broken ones, so an unaudited population is a live risk. Instrument = the proven `CredentialGuardTests` ratchet shape: a NEW direct read fails the build; existing sites grandfathered with a reason + classification. ⛔ **Seed the count only AFTER #832 and the ten worktrees merge** | **073,079,075,081,PR-832** | ❌ | **opus** | **xhigh** |
| ✅ **081** | **Classify the caller**, then scope the tenant-container-resolver diagnostic | 1 | **Live on master.** Takes `tenantId` from the QUERY STRING and treats the caller's JWT `tid` as a mere *fallback* → tenant A resolves tenant B's SPE container id. The 400-vs-200 "not served by this stamp" split is also a **tenant-enumeration oracle**. Found by 074's census forcing a new-file classification | — | ❌ | **opus** | **xhigh** |

**Wave 1 (070–074) is parallel-capable** — 071/073/074 are `parallel-safe: true`; 070 and 072 touch shared authorization surface and serialize. **Wave 2**: 075 → 076 strictly, and since the 2026-08-27 rewrite **073 must also be merged before 076** (it deletes `UploadEndpoints.cs`, removing the app-only twin of the surface 076 reshapes). Wave 1 and Wave 2 can otherwise run concurrently.

**077 and 078 were added mid-wave on 2026-08-25**, both surfaced by task 074's forcing function on its **first run** — on a surface this project had already enumerated by hand four times (~15 estimated → 22 found → +2 → +5). That is the argument for 074 demonstrated rather than asserted. Note 077 in particular: a filter *was* attached to that route, so it looked gated to every prior review and to 074's own first rule; only the second rule — does the filter actually consult an authorization service? — catches it.

> ✅ **081 unblocked 2026-08-26 — owner chose option B (classify the caller).** Its escalation trigger fired legitimately: the prescribed fix (`tid` must match) denies the shipped L2 H13 I4 probe 100% of the time, because that probe is *by design* one tenant's Managed Identity asking about another tenant's resolution. Option C (move the probe to a named inbound API key) was recommended, then **withdrawn** — it fragments the L2 probe fleet off MI onto a static key and destroys attribution. B places a **caller-kind primitive in `Spaarke.Core.Auth`** (binding — `Spaarke.Core` cannot reference BFF `Infrastructure/**`, so a BFF-side primitive is unreachable by the evaluator and gets rebuilt), which the unified evaluator needs anyway for the ADR-034-derivation decision. Re-tiered `sonnet/high` → **`opus`/`xhigh`** and `∥-safe` → **❌** (touches `Spaarke.Core/Auth/**`). Decision record: [`../notes/task-081-tenant-diagnostic-BLOCKED.md`](../notes/task-081-tenant-diagnostic-BLOCKED.md).
>
> ✅ **074 is BLOCKING as of 2026-08-26** (was advisory). Four facts appended to `ci-tier1-blocking.yml`'s `arch-tests` `--filter`: `EveryGovernedRouteCarriesPerResourceAuthorizationOrANamedWaiver` · `NoAuthorizationFilterIsDecorative` · `ScannerAccountsForEveryRegistrationInTheGovernedFiles` · `TheEndpointFileCensusIsPinned`. The ownership block is gone — `ci-cd-unit-test-remediation-r1` is not active (owner decision). `sdap-ci.yml` deliberately **not** touched: it carries `continue-on-error` at both job *and* step level so it can never fail a build, and it is open in PR #806.
>
> ⚠️ **Do NOT "simplify" the four facts to one.** Rule B (`NoAuthorizationFilterIsDecorative`) is not redundant with Rule A: the route that leaked the tenant's documents *had* a filter attached, so Rule A classified it gated and four separate human sweeps agreed. Only Rule B catches a filter that authorizes nothing. And the census is a *drifting count on purpose* — without it the other three simply would not govern a newly-added endpoint file. It has since fired twice and been right both times (071's deletions; master's 2 new files → finding 081).
>
> ⚖️ **076 rewritten 2026-08-27 — owner chose option (C), the record-keyed upload contract.** Its escalation fired legitimately: 12 client sites resolve their container *before the owning record exists*, so the resolver cannot be asked where they sit. The note recommended **(A)** (client keeps resolving a BU container as a *fallback*; add a resolver call at each upload point) and called **(C)** *"not deliverable inside 076 as scoped."* **That scope framing was stale when written** — it predates 073 shipping its deletion. Re-measured on-branch 2026-08-27: `UploadEndpoints.cs`'s 3 container-keyed routes are **deleted by 073**; `GET /api/v1/containers/{containerId}/documents` is **already 078**; the 12 `SpeAdmin` routes administer the container itself (no owning record). What is genuinely left is **one route converted** (`OBOEndpoints.cs:51`, the only live client upload route) and **two deleted** (`:102/:137` — their client first calls `GET /api/obo/containers/{id}/drive`, which is **mapped nowhere**, so the chunked path is dead by 404). **Scope was never the argument anyway**: under (A) the authorization key is `(entity, recordId)` while the container stays client-supplied — two keys for one decision, and F-9 proves they already diverge in shipped code; under (C) they are the same value by construction. (C) also deletes F-5 (`AssociateToStep.tsx:154-160`, record-aware and **fails OPEN**) rather than making it a call site to keep correct forever, and removes the *shape* that made 073's vulnerability expressible. Re-tiered `sonnet` → **`opus`**, deps `075` → **`073, 075`**. **Creates a ship-together obligation** (client + BFF) and deletes 074's three `Pending` OBO waivers. Decision record: [`../notes/coordination-compose-r8-2026-08-27.md`](../notes/coordination-compose-r8-2026-08-27.md) AMENDMENT 2; escalation: [`../notes/task-076-callsite-inventory-and-ESCALATION.md`](../notes/task-076-callsite-inventory-and-ESCALATION.md).

**Live end-to-end validation is task 047**, which must assert **INEQUALITY** against every BU container — three existing projects already carry the root BU's container id, so "a container id is set" is precisely the false positive.

**Not in these waves** (Wave 3+/operator): computed 1-hop inheritance (document access from parent) · the `sprk_issecure` veto, which exists in **no** authorization path today · FR-28's share half + the Manage Access 403-swallowing UI bug · BU migration (operator) · the standing empirical canary · verifying the SPE container type is `restrictive` not the default `open` (operator; needs the owning app's token) · FR-31's "secure is reversible" wizard copy, which is false without retro-securing migration (owner decision).

---

**Recommended order**: ~~022~~ ✅ → ~~021~~ ✅ → ~~045~~ ✅ → ~~046~~ ✅ → **Phase 0c Wave 1 (070–074) ∥ Wave 2 (075→076)** → **047 (live validation)** → 025 → 023 → **029** → 028 → 024, with 026 and 027 runnable
in parallel at any time (both `parallel-safe: true`, no deps, no contended code).

**Phase 0c goes first** because 070 and 073 are exploitable at HEAD, and because zero secure projects exist today — the window to build this without a migration is open now and closes the moment a real secure project is created.

**029 before 028**: 029 closes a live incoherence on the shipped surface (writable-but-not-listable
records) using two patterns that already exist — the `documents` module's OR'd `ScopeDimension` list
and the already-entity-generic `ApplyResolverFieldsAsync`. 028 adds a root that nothing yet consumes.
Running 029 first also means 028 has exactly one place to add its fourth root on each path.

**Not filed as tasks** (recorded as constraints on existing tasks): H4 `/share-link` missing
authorization → task 012 · M8 `AccessGrantModal.postJson` never checks `res.ok` → task 065.

> **Task 001 outcome (2026-08-21)**: 62 tests green at `tests/integration/auth/UnifiedAccessControl/`
> (the ADR-038 §2 security-auth KEEP path — **first backfill**; it had zero compiled files and was
> globbed by no csproj). **9 of 20 Phase 0 findings pinned, 1 partial, 10 not reachable offline.**
> Tasks 002/003/004/005/006/008/010/011/014 have their baseline and are unblocked. Tasks
> **007, 012, 013, 015, 016, 017, 018, 019** must supply their own coverage — see
> [`notes/task-001-untestable-findings.md`](../notes/task-001-untestable-findings.md) §2–3 for why and
> the recommended approach (extract a query-builder seam inside each fix task).
> ⚠️ Any task testing `/api/v1/external` MUST use `ExternalCollaborationTestFixture` — the shared
> fixtures make that group return 500, which silently turns "not 403" assertions into vacuous passes.

> **Wave P0-B outcome (2026-08-21)** — 014 + 019 executed **in parallel** (the only file-disjoint pair in
> Phase 0). No file overlap; post-wave build + full suite verified by the orchestrator.
> **014**: key is now `sdap:auth:access:{authMode}:{userId}:{resourceId}` (`sp`/`obo`), never the raw token.
> Escalation evaluated and correctly did NOT fire — `userId` IS the caller's `oid`, so two OBO callers
> already differ (verified independently). 3 characterizations flipped + 1 new test.
> **019**: `IncludeRelated` is now always `null`; `includeRelated: true` is a **logged-warning no-op**, not a
> silent one. Escalation FIRED and is resolved-but-open: the flag is visible in the Playbook Builder canvas
> and does nothing. **No playbook sets it today** (verified), so this is latent. 019 also corrected a
> pre-existing test that had pinned the buggy `Contain("*")` behaviour.
> Follow-ups filed: register **I-4** (no tenant segment in `sdap:auth:*` keys → design task 035's per-user
> cache tenant-aware from the start); stale "task 054 implements" comments remain in `MembershipEndpoints.cs`
> + `IMembershipResolverService.cs` → **task 015** owns that directory.

> **Task 003 outcome (2026-08-21)**: 4 keys registered; 15-test source-scanning completeness gate added
> (`OperationAccessPolicyCompletenessTests`). 8 task-001 characterizations flipped. Sweep **confirmed
> A-20's list complete** (22 `Add*Filter` extensions exist; only 7 consult the policy) and filed **A-23**
> (a second orphaned filter → task 018). Two new obligations recorded as POML constraints:
> **task 005 MUST map Dataverse `AppendToAccess`** (else `POST /api/office/save` is permanently 403 while
> *looking* fixed), and **task 018 deletes `AddOfficeDocumentAccessFilter`** alongside A-15. Rationale:
> [`notes/task-003-operation-rights-decisions.md`](../notes/task-003-operation-rights-decisions.md).

> **Task 004 outcome (2026-08-21)**: token rides on `AuthorizationContext.UserAccessToken`
> (**`required string?`** — forces every construction site to declare intent, so app-only is a visible
> `= null`, never a default; produced 7 compile errors across 11 sites). Missing token → DENY with
> `sdap.access.deny.no_caller_token`, data source **never consulted**. `IHttpContextAccessor` was rejected
> — `Spaarke.Core` has no ASP.NET Core dep and `LayerDependencyTests` guards that. POML **Step 3 was
> vacuous** (zero app-only consumers), not skipped.
> ⚠️ **FR-02's criterion is NOT closed by 004 alone** — `PermissionsEndpoints.cs:76,:159` still pass
> `userAccessToken: null` because they call `IAccessDataSource` **directly**, bypassing
> `AuthorizationService`. That is A-4 → **task 006**, which should route them THROUGH the service rather
> than re-plumb the token. Rationale: [`notes/task-004-caller-scoped-design.md`](../notes/task-004-caller-scoped-design.md).

> **Task 006 outcome (2026-08-21)**: ✅ **FR-02's criterion is now CLOSED** alongside FR-05 — a repo grep
> for `userAccessToken: null` returns **zero** production call-sites. `AuthorizationService` gained
> `GetCallerAccessAsync(userId, resourceId, userAccessToken, ct)` — **no default** on the token param,
> because A-4's root cause was the `= null` *default* on `IAccessDataSource.GetUserAccessAsync`, not a
> missing null check. `AuthorizeAsync` routes through it, so the service now has **exactly one** member
> touching `_accessDataSource` (verified by grep + a test pinning that both paths present identical
> arguments) — acceptance criterion 5 is structural, not asserted. Fourteen capabilities project from ONE
> snapshot rather than fourteen `AuthorizeAsync` calls (the batch route would otherwise be 1,400
> rule-chain evaluations per 100-doc request). No-access shape = **200 + all-false**, not 403.
> **Second disclosure found + closed**: the batch handler honoured a `UserId` from the request BODY.
> `DataverseAccessDataSource.cs:184-199` treats `userId` and `userAccessToken` as INDEPENDENT, so that
> would have queried a different principal under the caller's OBO token and written task 014's cache key
> under the **victim's** oid. `BatchPermissionsRequest.UserId` is removed (wire-compatible).
> Escalation trigger evaluated and correctly did NOT fire — **zero clients** call either route (two
> independent greps); the endpoint has been shipping a disclosure nothing consumed.
> ⚠️ Until **task 005** lifts the Read ceiling, eleven of the fourteen capabilities are false for
> everyone in production — the honest interim state, not a regression.
> Rationale: [`notes/task-006-capability-rights-mapping.md`](../notes/task-006-capability-rights-mapping.md).

> **Task 005 outcome (2026-08-21)**: the A-20 Read ceiling is gone. **Key discovery: the fix was mostly
> RECONNECTION, not new code** — `MapDataverseAccessRights` (all seven flags, `AppendToAccess` included)
> and `PrincipalAccessResponse` already existed in `DataverseAccessDataSource.cs` as **dead code**: the
> orphaned wiring of a `RetrievePrincipalAccess` implementation replaced long ago by a
> "can-I-retrieve-the-record → therefore Read" probe. Confirmed repo-wide: **`RetrievePrincipalAccess`
> had ZERO live call sites** (every reference was a doc comment).
> `RetrievePrincipalAccess` is now called first; **the old probe survives as a fallback** because the
> deleted comment's claim that it "may not be available" under OBO is unverified and unverifiable
> offline. The composition cannot regress — worst case is exactly today's behaviour — and failures log
> the **`RPA-FALLBACK`** marker so a silent re-cap at Read is visible.
> **Both escalation triggers evaluated, neither fired**: RPA *replaces* the probe (1 call, no extra round
> trip), and all six consumers of `AccessSnapshot.AccessRights` were enumerated — `AiAuthorizationService`
> checks Read only (unaffected), two are the intended beneficiaries, two read a different rights model
> entirely.
> ⚠️ **Corrected a mis-framed task-001 test**: `Characterization_WritePlusOperation_DeniedUnderReadCeiling`
> was doc-commented "FLIPPED BY: task 005" — **following that would have been a security regression**. It
> gives the rule a Read-only snapshot; denying Write+ there is permanently correct. The ceiling was never
> observable at the rule layer. Renamed + re-documented; real coverage moved to the endpoint suite.
> Task 003's `AppendTo` obligation and task 006's capability obligation are both **discharged**.
> ⚠️ **Untested boundary (deliberate, not hidden)**: no test exercises the `RetrievePrincipalAccess` HTTP
> call itself — mocking that transport is ADR-038 ban B1. Its URL form and OBO availability need a live
> tenant → folded into **task 034** / Phase 4 acceptance.
> Rationale: [`notes/task-005-rights-mapping.md`](../notes/task-005-rights-mapping.md).

> **Task 002 outcome (2026-08-22)**: **R1's January-2026 attack scenario is closed.**
> `GET /api/documents/{id}/download` now carries `AddDocumentAuthorizationFilter("read")`. The app-only
> SPE stream is deliberately UNCHANGED — files written by the MI are only readable by it (Writer-Identity
> Matching, auth constraints Pattern 4); the defect was the missing Dataverse-level gate, not the stream.
> ⚠️ **SCOPE WIDENED BY ONE ROUTE, deliberately**: `GET /api/documents/{id}/content` streams the SAME
> bytes from the SAME app-only path with the SAME missing gate. Closing `/download` alone would have left
> the attack fully intact behind a different URL, so both were closed together.
> **Operation key `"read"`, not `download_file` (Write)** — the sibling route
> `DataverseDocumentsEndpoints.cs` `GET /api/v1/documents/{id}/download` ALREADY uses `"read"`, as does
> eml-render; task 001's characterization pinned exactly that these routes *disagree*. Write would have
> recreated the inconsistency and newly denied download to every Read-only user on a live UI path.
> ⚠️ **OPEN FOR OWNER**: enforcement now says Read while task 006's `CanDownload` capability says Write.
> Benign in effect (UI hides a button that would work) but it is the divergence FR-05 criterion 5 exists
> to prevent. Two options documented — a product decision, not an implementation one.
> ⚠️ **FILED, NOT FIXED**: four more routes on this group (`preview-url`, `view-url`, `office`, `preview`)
> have no per-document filter. They mint URLs rather than stream bytes — a different blast radius and a
> separate decision. **Should be assessed as its own task.**
> Rationale: [`notes/task-002-download-authorization.md`](../notes/task-002-download-authorization.md).

> **Task 010 outcome (2026-08-22)**: A-11 (**ranked #1 of 13**) closed. `/grant` UPSERTS against a logical
> key; `/revoke` sweeps EVERY active row on that key. Grant→grant→revoke leaves zero.
> **Logical key = `(root) × (Contact XOR Organization)`**, and two details are load-bearing: (a) a row may
> carry BOTH a contact and an org — the org is the contact's **firm**, association metadata, NOT identity;
> contact wins, or a person grant and an org grant on the same root would collide and could revoke each
> other. (b) `_sprk_contact_value eq null` is what makes an org grant an org grant — drop it and revoking
> one firm's grant sweeps every member's personal grant. The key mirrors the READ side
> (`ExternalParticipationService.cs:511`) term for term: **write/read disagreement about "the same grant"
> IS A-11.**
> **Concurrent-grant race**: both racers MUST elect the same survivor or they deactivate each other and
> the grant vanishes — worse than the bug. Election is `OrderBy(id).First()`, stable and clock-independent
> (`createdon` can tie).
> **Underivable key → FAIL LOUDLY, deactivate nothing.** The POML flags this as an escalation, but the
> task's own ADR-003 constraint answers it: siblings that cannot be queried cannot be guaranteed absent,
> so reporting success is forbidden. All three escalation triggers evaluated; **none fired**.
> ⚠️ **A REAL DEFECT was caught by the full suite** (`ExternalAccessContractTests.InviteAndGrant_…`): the
> upsert adopted an unaddressable row (`Id == Guid.Empty`) as "the existing grant", aimed an update at
> `Guid.Empty`, and returned an empty id — a silent no-op reported as success. Fixed in **production**
> (discard rows with no usable id), not by adjusting the stub. Task 005's TRX-capture technique named it
> immediately; under `-v q` it would have been indistinguishable from the pre-existing flake.
> ⚠️ **Duplicates remain INVISIBLE** to the participation surface until Phase 1 replaces the read-side
> `GroupBy` collapse (scoped out by constraint). Task **017** edits the same file next and MUST NOT reduce
> the sweep back to a single row.
> Rationale: [`notes/task-010-grant-lifecycle.md`](../notes/task-010-grant-lifecycle.md).

> **Task 008 outcome (2026-08-22)**: A-6 closed — **FR-07's delegation gate is in place, so the PCF
> "+ User" button (task 065) is unblocked.** `AddDelegationRuleFilter()` on the `/api/v1/external-access`
> group enforces B-14 (Write on the target record, evaluated as the caller) across all SIX mutations.
> **Group-level, dispatching on the bound request TYPE with a default that DENIES** — a seventh route
> added later is gated from its first request rather than inheriting the hole. Route→target: grant /
> invite / invite-and-grant → the grant root; **revoke → the ROW's root, not the body's `projectId`**
> (checking the body would let a caller with Write on any project revoke grants on a matter they cannot
> touch); close/provision → the project.
> **The existing authorization path could NOT serve this.** `DataverseAccessDataSource` hard-codes
> `sprk_documents({id})` in BOTH its RPA target and its fallback probe, so it answers `None` for a
> project for EVERY caller — the filter would have denied universally. `IDataverseUserClient` is the
> right shape but is twice-gated (compound AI gate + `ToolFramework:Enabled`) → depending on it from
> six unconditional routes would be the §10 F.1 asymmetric-registration anti-pattern. Hence
> `CallerRecordAccessProbe`: OBO `WhoAmI()` → `RetrievePrincipalAccess`, entity-generic, fail-closed.
> **`WhoAmI` is not incidental** — RPA takes the principal as an ARGUMENT, so an app-only version would
> carry the caller's identity as *data* (a wrong id silently answers about the wrong person: the A-2
> shape). Under OBO the identity is the *credential*.
> **No read-probe fallback, deliberately**: a read proves Read, and Read is not licence to grant. So an
> RPA outage denies all six mutations rather than widening them — logged as `DELEGATION-RPA-UNAVAILABLE`,
> and **task 034 now owns live RPA verification for six mutation endpoints, not just the document read.**
> Both escalation triggers evaluated; **neither fired** — `provision-project`'s premise was false (the
> handler already requires the project to exist), and revoke's extra read duplicates one the handler
> already performs. ⚠️ **Residual owner decision**: provisioning creates a **business unit**; is
> Write-on-project the right gate, or should it need a privileged role?
> ⚠️ `/invite` now REQUIRES a resolvable root (it provisions a CIAM identity). The only first-party
> caller already sends `projectId` as required, so nothing breaks — but it is a contract narrowing.
> ⚠️ Two `ExternalAccessContractTests` needed an entitled caller in their fixture; the production rule
> was **not** weakened for them. ⚠️ Filed for triage: `EntityAccessFilter` passes
> `"{entityType}:{entityId}"` into that same document-only data source and can therefore only resolve
> `None` — the Office save path's entity check may be inert (same class as A-20, no Phase 0 owner).
> 🔔 **ADR-028 A4 needs an owner ruling**: a NEW `.WithClientSecret(...)` site (the 8th). E-3 covers
> transitional sites and does not license expansion; there is no `WithClientAssertion` anywhere in the
> repo, so complying would mean inventing MI-FIC plumbing inside an authorization filter. **Path A
> proposed.** ADR-003's "rules only / no new auth service layers" tension is the SAME one already routed
> to task 030's path-B amendment — cited, not re-escalated.
> Rationale: [`notes/task-008-delegation-rule.md`](../notes/task-008-delegation-rule.md).

> **Task 008 follow-ups (2026-08-23, owner-authorised)** — all three review items closed; details in
> [`notes/task-008-delegation-rule.md` §10](../notes/task-008-delegation-rule.md).
> **(a) `EntityAccessFilter` was inert — CONFIRMED, then FIXED.** The characterization suite was written
> and run FIRST against unchanged code: 7 of 9 failed, `ProbedTargets` empty, and a caller holding
> `AppendToAccess` on the target matter got **403**. `POST /api/office/save` carrying a `targetEntity`
> was refused for every caller — filing an email/document to a matter from the Outlook/Word add-in could
> not succeed, while filing WITHOUT a matter worked (the filter short-circuits on a null target), which
> is why it read as flakiness. Fixed by resolving the target's own collection and asking
> `CallerRecordAccessProbe`; `OperationAccessPolicy` still decides WHICH right the operation costs, so
> there is one policy and only the rights SOURCE changed. Perturbation: point it back at
> `sprk_documents` → 6 of 9 fail. Should fold back into `AuthorizationService` when **task 032**
> generalizes the seam (noted in code).
> **(b) `provision-project`: Write is correct; the real exposure was idempotency.** `Create` cannot be
> used — `CreateAccess` is an entity-level privilege, not a right held ON an existing record, so
> `RetrievePrincipalAccess` does not report it and requiring it would deny everyone. Write is also
> exactly what the endpoint needs: its final act is an `UpdateAsync` stamping three fields on the
> project. (The BU is created by the APP-ONLY identity, so the caller's own BU-create privilege is never
> consulted regardless.) The damage the question was worried about is now removed by a **409
> idempotency guard**: nothing on the path deduped — not the client, not the route (no
> `IdempotencyFilter`, unlike `/office/save`), not Dataverse — so a retry after a timeout-that-actually-
> succeeded created a second BU + container + account and repointed the project at empty infrastructure.
> EITHER stamped reference triggers the refusal, because half-provisioned is where a blind re-run does
> the most damage. Perturbation: disable the guard → 4 of 5 fail.
> **(c) Replication-lag 403 fixed.** The wizard provisions seconds after creating the project, so the
> delegation check could ask about an unreplicated record. `NotFoundRetryDelay` retries 400 ms then
> 1200 ms on not-found. ⚠️ Documented tradeoff: under OBO, Dataverse cannot distinguish "not replicated"
> from "you cannot see it", so this also slows genuine denials by ~1.6 s — acceptable only because these
> are low-volume admin mutations and a caller who CAN see the record gets a 200 (no retry). Pure
> `internal static` so it is testable without a transport mock (ban B1).
> **(d) `tests/integration/data-mutation/**` BACKFILLED** — it was the last of the seven ADR-038 KEEP
> paths with no csproj glob, so a write-path test placed there compiled nowhere and ran never. **All
> seven KEEP paths now compile.**
> **(e) ADR-028 A4 exception ACCEPTED** by the owner → recorded in [`design.md` §9](../design.md).

> **Task 007 outcome (2026-08-23)**: A-5 closed — `sprk_expiresdate` was written at grant time and read
> NOWHERE (no `$filter`, no `$select`, no sweep job), so an expired grant conferred full access forever
> while the UI presented expiry as a working control. Now enforced server-side on every conferring read.
> **Closed read-path list (acceptance criterion 5)** — 3 paths gained the predicate
> (`QueryGrantSetAsync`, `QueryOrganizationGrantRowsAsync`, and the `GetProjectContactIdsAsync` display
> list whose contract says "active access"); **2 paths deliberately did NOT** —
> `ExternalGrantLifecycle` (grant upsert + revoke sweep) and `ProjectClosureEndpoint`'s cascade. Adding
> expiry there would make **expired grants unrevokable**, skipping exactly the rows an operator is
> cleaning up. "Add it everywhere" was the obvious reading and would have introduced a new defect.
> ⚠️ **`sprk_expiresdate` is DATE ONLY** — verified against LIVE Dataverse metadata (the escalation
> trigger required checking, not trusting docs; the name matched so the trigger did not fire, but the
> TYPE was new information). Two consequences: the comparison must be a bare `yyyy-MM-dd` (a datetime
> literal risks a 400, and a 400 here returns an EMPTY grant set — a silent total access outage), and
> **`ge` not `gt`**, deviating from the POML's prescribed `gt {utcNow}`. `gt` against a date-only column
> kills a grant at 00:00 ON its own expiry date, silently shortening every dated grant in the system by
> a day; "access until 30 June" means 30 June works. FR-06's acceptance is about an expiry IN THE PAST,
> which `ge` satisfies.
> ⚠️ **The `eq null` branch is load-bearing**: OData `ge` excludes nulls, and most grants have no expiry
> — without it the predicate would revoke every open-ended grant, an outage rather than an expiry bug.
> Per task-001's obligation the query builders were EXTRACTED as pure `internal static` members first
> (task 001 could not pin A-5 because the queries were inline before `SendAsync`, and mocking transport
> is ban B1). Perturbations: drop the predicate → 2/11 fail; drop `eq null` → 1/11; `ge`→`gt` → 1/11;
> ungroup the org disjunction → 1/11 (without brackets the AND terms bind only to the LAST org and every
> other org's grants leak through). ⚠️ **Honest limit**: these assert the QUERY, not Dataverse's
> evaluation of it — end-to-end needs the tenant, filed on **task 034**.
> Rationale: [`notes/task-007-grant-expiry.md`](../notes/task-007-grant-expiry.md).

> **⚠️ CI repair (2026-08-23, commit `3e5b9d373`) — and a process failure worth keeping.**
> **There are SEVEN test projects; tasks 002–008 were verified against THREE.** CI went red on task
> 008's commit with 9 failures in `Spe.Integration.Tests`, which no local run had touched. Two causes,
> two different correct responses: **five were fixture** (contract tests with no substituted
> `CallerRecordAccessProbe`, so the real probe correctly denied offline — fixed by entitling the
> fixture's caller, NOT by weakening the rule), and **four were a real contract change** (an empty
> identifier names no resolvable target, so the delegation rule denies 403 before the handler's 400 —
> task 008's ADR-003 constraint verbatim). Those four tests were flipped with rationale.
> **The local gate is `dotnet test` at the repo root PLUS** `Spaarke.ArchTests`, `Spaarke.Core.Tests`
> and `RecordSyncJob.IsolatedTests`, which the root run does not pick up. Running one project and
> reporting "full suite green" is how this survived six tasks.
> Verified after repair: **11,338 passed / 0 failed** across all seven.
> 🔔 **Client-visible**: `/grant`, `/revoke`, `/close-project` now answer **403**
> (`sdap.access.deny.delegation_target_unresolved`) instead of 400 for a body with an empty identifier.

> **Task 016 outcome (2026-08-23)**: A-12 closed — closing a Secure Project now actually revokes access.
> **Two independent defects, either sufficient alone.** (1) The cascade `$select`ed
> `_sprk_contactid_value`, an attribute that **does not exist** — live metadata declares the lookup
> `sprk_contact` → `_sprk_contact_value`, and there is no `sprk_contactid` on the table at all. A
> `$select` on a nonexistent column is a 400, the helper rethrew, `Handle` had no `try` → **every**
> closure 500'd having revoked nothing and never reached SPE removal. Task 070 had already fixed the
> *sibling* project lookup in this same file and left the contact one stale — same typo class, twice.
> (2) The projection required a non-null contact, and **a null contact IS the organization-grant
> discriminator** — so even with the column fixed, every org grant would have survived closure with the
> whole firm's access intact. ⚠️ The solution's `views-schema.md` still says `sprk_contactid` and is
> **wrong**; live metadata + `ExternalParticipationService` + `ExternalGrantKey` all agree on
> `_sprk_contact_value`.
> **Why no test caught it**: `ExternalAccessRow` was `private`, so no test could name
> `QueryAsync<ExternalAccessRow>` — the pre-existing `CloseProject_DataverseQueryThrows_PropagatesException`
> said exactly that in its own comments and then asserted `Guid.Empty == Guid.Empty`. Now `internal`
> (ADR-038 §4 seam, ban B8 via `InternalsVisibleTo`), and **the fake table rejects unknown `$select`
> columns the way Dataverse does** — a fake that ignored the projection would have gone green on the
> shipped bug.
> **In-scope extension**: `DeactivateAccessRecordsAsync` swallowed per-row failures and returned only the
> success count, so a closure that revoked 2 of 5 answered `200 OK` — the same false-success shape the
> ADR-003 constraint forbids for enumeration, and the precedent is one directory over in
> `ExternalGrantLifecycle.DeactivateAsync` (task 010). Continue-on-error is kept (aborting leaves MORE
> access standing); the failures are now counted and reported. Three reason codes, all carrying
> `accessRecordsRevoked`. Perturbations: stale `$select` → **14/20** fail; restore the null-contact
> exclusion → **6/20**; rethrow instead of typed response → **2/20**; ignore `failedCount` → **2/20**;
> drop the unaddressable-row guard → **1/20**.
> 🔔 **Client-visible**: `/close-project` can now answer **500 + RFC 7807** with `reasonCode ∈
> {sdap.closure.incomplete.enumeration_failed, …partial_revocation, …container_not_cleared}` where it
> previously answered 200 (partial revocation) or an untyped 500. Retry is safe and intended.
> 🔔 **NEW FINDING → filed on task 017**: `SpeContainerMembershipService.ListExternalMembersAsync`
> catches BOTH `ServiceException` and `Exception` and returns `[]`, so `RemoveAllExternalMembersAsync`
> reports "0 removed" whether the container was empty **or Graph was unreachable**. Close-project then
> reports 200 while external users may still hold file permission — FR-15's own acceptance failing on
> the SPE half. Task 016 built the receiving end (`container_not_cleared`); the guard is **unreachable
> until 017 makes that service report honestly**, and is documented as untestable-today rather than
> covered by a fake exception the service cannot throw.
> Verified: **11,357 passed / 0 failed** across all seven projects; publish **43.69 MB** compressed
> (unchanged — no packages added). Rationale:
> [`notes/task-016-close-project-cascade.md`](../notes/task-016-close-project-cascade.md).

> **Task 017 outcome (2026-08-24)**: A-13 closed, H-8b cleaned up, and the task-016 constraint discharged.
> **Escalation checked first and did NOT fire**: nothing in this codebase adds an SPE container permission
> — `GrantMembershipAsync` has **zero callers**, `/grant` reports `SpeContainerMembershipGranted: false`
> ("broker-only"), and neither invite endpoint touches SPE. The SPE removal path is therefore a **cleanup
> path for ACLs this product did not create** (legacy / admin-added), which is why `NoPermissionFound` is
> the ordinary healthy answer and why the path is worth keeping rather than deleting.
> **The fix was DELETION, not repair.** The endpoint carried its own private copy of the SPE revoke which
> set `contactIdStr = contactId.ToString()` and searched for that GUID *inside* `userPrincipalName` — but
> membership is written with the contact's **email**, so it matched nothing, ever. It then returned `true`
> on no-match ("may have already been removed"), claiming SPE success on every revoke while the ACL entry
> sat untouched. Meanwhile `SpeContainerMembershipService.RevokeMembershipAsync` already matched on email
> correctly (`FindPermissionByEmail`) and had **zero callers** — the endpoint had forked a working
> implementation and broken it. Fork deleted; the endpoint calls the service (CLAUDE.md §11).
> `IGraphClientFactory` left the handler signature with it — this endpoint no longer talks to Graph at all.
> **A bool could not carry the answer** (ADR-003: distinguish "confirmed absent" from "match failed"), so
> the response gained `SpeContainerOutcome` in {`NotAttempted`, `PermissionRemoved`, `NoPermissionFound`,
> `Failed`}. Note that "contact has no email" maps to **`Failed`**, not `NoPermissionFound`: without the
> key an existing permission is unfindable, which is unknown, not absent.
> **task-016 constraint DISCHARGED**: `ListExternalMembersAsync` now propagates (an empty list means one
> thing only) and `RemoveAllExternalMembersAsync` returns `SpeBulkRemovalResult(Removed, Failed)` instead
> of a bare `int`. `ProjectClosureEndpoint`'s `container_not_cleared` guard is now **reachable and tested**
> — including the subtler *partial*-clear case, which under the old `int` contract looked like success.
> **H-8b**: `WebRoleRemoved` deleted (hard-coded `false` at every call site; Spaarke manages no Power Pages
> web roles). `GrantMembershipAsync` was **NOT** deleted — flagged for the owner instead, carrying an
> explicit "no callers by design / broker-only" header, because it defines the identity key the revoke
> matcher must match.
> **task-010 "assess and file"**: org-grant SPE cleanup is a KNOWN GAP, filed not fixed. An org revoke has
> no single grantee, hence no email; cleanup needs an organization to members expansion this path lacks
> (the same one declined in task 016 for cache invalidation). It reports `NotAttempted`. Bounded because
> broker-only creates no member ACLs in the first place.
> **⚠️ The perturbation run caught a hole in my own tests**: re-swallowing listing failures initially passed
> EVERYTHING, because the closure tests substitute `RemoveAllExternalMembersAsync` at its seam and so never
> exercise `ListExternalMembersAsync` at all — the very fix the constraint asked for was untested. Four
> service-level tests were added. **Lesson worth keeping: mocking at a seam proves the CALLER handles a
> failure, never that the CALLEE reports one.** Perturbations after the fix: GUID matcher **2**,
> false-success-on-no-match **3**, Graph-error-as-absent **2**, re-swallow listing **2**, ignore per-member
> failures **1**.
> **The e2e spec was pinning the bug** — `revocation.spec.ts` asserted
> `speContainerMembershipRevoked === true`, which passed *because* the broken matcher always returned true.
> Flipped to assert an honest outcome.
> 🔔 **Client-visible**: `/revoke` — `webRoleRemoved` **removed**; `speContainerOutcome` **added**;
> `speContainerMembershipRevoked` is now true ONLY when a permission was actually deleted (it was
> effectively a constant `true`). Shipped-client impact nil — `AccessGrantModal` awaits the call and ignores
> the body; its stub used field names that never matched the DTO.
> Verified: **11,372 passed / 0 failed** across all seven .NET projects, plus **26** frontend tests (needed
> `npm install --legacy-peer-deps` first — `node_modules` is absent in a fresh worktree). Publish
> **43.69 MB** compressed, unchanged. Rationale:
> [`notes/task-017-spe-revoke-matcher.md`](../notes/task-017-spe-revoke-matcher.md).

> **Task 020 ADDED 2026-08-24 by owner decision.** Task 017 assessed the organization-grant SPE cleanup
> gap and FILED it (task 010's constraint permitted "assess and either fix or file"). The owner ruled it must
> be **scoped into this project, not deferred**, so it is now Phase 0 task **020** rather than a note.
> **The gap**: an org-grant revoke passes `ContactId = Guid.Empty`, so there is no single grantee email to
> match a container permission on — Dataverse rows are swept for every member but SPE cleanup reports
> `NotAttempted`. **What bounds it**: broker-only creates no member ACLs, so the exposure is LEGACY ACLs
> (pre-broker versions, or admin-added) — which is also the only population nothing else will ever clean up.
> **Scoping work done up front so execution does not re-derive it**: the membership junction is
> `sprk_contactorganization` — lookups `sprk_contact` / `sprk_organization`, i.e. `_sprk_contact_value` /
> `_sprk_organization_value`, plus `statecode` — **verified live 2026-08-24**, which also resolves the
> standing "confirm against the created junction schema" caveat in
> `ExternalParticipationService.QueryActiveOrgIdsAsync` (the assumption there is CORRECT; 020 deletes the
> caveat). The reference impl to mirror is that same method, inverted.
> ⚠️ **Also found while scoping**: the junction carries `sprk_enddate`, and the READ path
> (`QueryActiveOrgIdsAsync`) considers `statecode` only — so a membership ended by date but never deactivated
> still confers inherited access today. 020 deliberately does NOT change read behaviour (that would alter who
> has access, beyond its scope); it sweeps by `statecode` alone so revoke stays at least as aggressive as the
> read path, and files the asymmetry onto **task 043** (FR-24/FR-25 org expansion).

## Phase 1 — One evaluator (10 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 030 | ADR-003 amendment — two-surface authorization | FR-19 sanction | — | — | ❌ *main-session* | opus | high |
| 🔲 031 | ADR-028 A2 amendment — impersonated derivation | FR-20 sanction | — | — | ❌ *main-session* | opus | high |
| 🔲 032 | Evaluator spine — `(recordId→rights)` + max + veto seams | FR-19 | 030 | — | ❌ | **opus** | **xhigh** |
| 🔲 033 | Consumer propagation · **delete the `Collaborate` stamp** | FR-19 | 032 (+009 soft) | — | ❌ | opus | high |
| 🔲 034 | **Negative canary — NFR-04 merge gate** | FR-20 | — | **P1-A** | ✅ | sonnet | **xhigh** |
| 🔲 035 | `ImpersonatedRootSetSource` + per-user cache | FR-20 | 031 | — | ❌ | opus | high |
| 🔲 036 | Flag-gated swap + truncation + runbook | FR-20, NFR-02/03/04 | 032, **034**, 035 | — | ❌ | **opus** | **xhigh** |
| 🔲 037 | Restricted veto + Secure pre-max suppression | FR-21, FR-22 | 032 | — | ❌ | sonnet | high |
| 🔲 038 | Deny-list store — schema + fail-closed reader | FR-23 | — | **P1-A** | ✅ | sonnet | high |
| 🔲 039 | Deny veto wiring + ordered-pipeline tests | FR-23, FR-19 | 032,037,038 | — | ❌ | sonnet | high |

## Phase 2 — One definition of member (5 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 040 | ADR-034 amendment — registry first-class | FR-24 sanction | — | — | ❌ *main-session* | opus | high |
| 🔲 041 | Access-conferring column registry (contact **+ org**) | FR-24 | 040 | **P2-A** | ✅ | sonnet | high |
| 🔲 042 | Standing-grant baseline levels (contact + org) | FR-25 | 032 | — | ❌ | sonnet | high |
| 🔲 043 | Org-expansion term + fallback registry filter | FR-24/25/22 | 037,041,042 | — | ❌ | sonnet | high |
| 🔲 044 | Unified-evaluator seam suite (Phase 1–2 contract) | FR-19…25 | 039,041–043 | **P2-A** | ✅ | sonnet | high |

## Phase 3 — Child inheritance (9 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 050 | Core-ancestor derivation in the shared resolver | FR-26 | — | **P3-W1** | ✅ | **opus** | **xhigh** |
| 🔲 051 | `RegardingResolver` re-stamp on set/reparent/clear | FR-26 | 050 | **P3-W2** | ✅ | opus | high |
| 🔲 052 | Server-writer audit + C# `CoreAncestorResolver` | FR-26 | 050 soft | **P3-W1** | ✅ | sonnet | high |
| 🔲 053 | Ancestor-stamp backfill script | FR-26 | 050,052 | **P3-W2** | ✅ | sonnet | medium |
| 🔲 054 | Root-set generalization (`sprk_servicerequest` 4th root) | FR-27 | 032,035,036 | — | ❌ | sonnet | high |
| 🔲 055 | Evaluator child-inheritance term | FR-27 | 054,032,037,038 | — | ❌ | sonnet | high |
| 🔲 056 | Child-module registration (todo/event/communication) | FR-27 | 055,**009**,**018** | — | ❌ | sonnet | high |
| 🔲 057 | Phase-3 seam tests | FR-26/27 | 052,055,056 | **P3-W6** | ✅ | sonnet | high |
| 🔲 058 | Taxonomy + inheritance docs (**Matter ≠ Project**) | FR-26/27 | 056 | **P3-W6** | ✅ | sonnet | medium |

## Phase 4 — Secure Project · Manage Access · wizard (10 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 060 | POA seam consolidation (2→1, +revoke) | FR-28/29 pre | **010** | — | ❌ | **opus** | **xhigh** |
| 🔲 061 | Secure provisioning rework — svc-acct owner, share-only | FR-28 | 060,**008** | **P4-W2** | ❌ | sonnet | high |
| 🔲 062 | **NFR-05 role-depth standing assertion** | FR-28 | 034 | **P4-W2** | ✅ | sonnet | high |
| 🔲 063 | Internal system-user share endpoints (delegation-gated) | FR-29 | 060,**008**,010 | **P4-W3** | ❌ | sonnet | high |
| 🔲 064 | Provenance read + deny-list endpoints | FR-30, FR-23 | 063,060,032,038,041,042 | **P4-W4** | ❌ | sonnet | high |
| 🔲 065 | `AccessGrantModal` "+ User" picker | FR-29 | **063** | **P4-W4** | ✅ | sonnet | high |
| 🔲 066 | Modal provenance rows + suppressed rendering | FR-30 | 064,065 | — | ❌ | sonnet | high |
| 🔲 067 | Modal deny-list UI + standing-grant levels | FR-23/25 UI | 064,066 | — | ❌ | sonnet | high |
| 🔲 068 | Wizard Secure step + copy fixes (Power Pages) | FR-31 | 061 | **P4-W3** | ✅ | sonnet | medium |
| 🔲 069 | Phase-4 seam tests | FR-28/29/30 | 061,063,064 | — | ✅ | sonnet | high |

## Phase 5 — Attestation (4 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
> **Renumbered 080–083 → 086–089 on 2026-08-26.** The Phase 0c security insertions filed on 2026-08-25/26
> took 080 and 081, which were already occupied by this block — the documented insertion room was
> "070–079 and 084–089" and 080–083 were not in it. That was a filing error in Phase 0c, not here.
>
> This block moved rather than the insertions because **this block is entirely unstarted** (no code, no
> commits, no source comments), whereas Phase 0c task 080 is shipped and referenced by commit
> `d6d156ac1`, by `notes/task-080-cross-record-search.md`, and by comments in five source files.
> Renumbering the shipped one would have made the git history point at a task file that no longer exists.
> Internal deps were rewritten with it (087→086, 088→087, 089→087+088).

| 🔲 086 | `sprk_accessevent` schema + data-model doc | FR-32 | 032,038 | — | ✅ | sonnet | medium |
| 🔲 087 | Append hooks at every grant/deny choke point | FR-32 | 086,060,063,064 | — | ❌ | sonnet | high |
| 🔲 088 | Evaluator versioning + point-in-time replay | FR-32 | 087,032 | — | ❌ | sonnet | **xhigh** |
| 🔲 089 | Attestation seam tests + docs | FR-32 | 087,088 | — | ✅ | sonnet | medium |

## Wrap-up

| # | Task | Deps | Safe |
|---|---|---|---|
| 🔲 090 | `/test-diet` · H-8a/H-8b closeout · lessons-learned · README → Complete | all | ❌ |

---

## Parallel execution groups

| Group | Tasks | Prerequisite | File-disjointness |
|---|---|---|---|
| **P0-W0** | 001 | — | Tests only (`tests/AccessControl/**`, `Spaarke.Core.Tests/Auth/**`). Blocks all Phase 0 code work |
| **P0-B** | 014, 019 | 001 | `Infrastructure/Caching/CachedAccessDataSource.cs` vs `Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` — the only two Phase 0 code tasks outside all four contended directories |
| **P1-A** | 034, 038 | — | `tests/integration/auth/**` (new) vs new deny-list schema/reader + an append-only DI block |
| **P2-A** | 041, 044 | 040 / 039 | `Services/Ai/Membership/**` vs `tests/integration/seam` (new files only) |
| **P3-W1** | 050, 052 | — | TS shared lib (`Spaarke.UI.Components/src/services/`) vs C# `Services/Communication/**` |
| **P3-W2** | 051, 053 | 050, 052 | `RegardingResolver` PCF vs `scripts/` |
| **P3-W6** | 057, 058 | 055, 056 | `tests/integration/seam` vs `docs/architecture` |
| **P4-W2** | 061, 062 | 060, 008, 034 | `src/**` provisioning vs `tests/integration` |
| **P4-W3** | 063, 068 | 060, 008, 061 | `Api/ExternalAccess/**` vs `CreateProjectWizard/**` |
| **P4-W4** | 064, 065 | 063 | `Api/ExternalAccess/**` + Infrastructure vs `AccessGrantModal` + `TrackingFieldTrio` |

**Max concurrency 6 agents/wave.** Build verification between waves is mandatory: `dotnet build src/server/api/Sprk.Bff.Api/` after any `.cs` change; `npm run build:prod` for PCF (**never** `npm run build` — root CLAUDE.md §12).

### Honest note on parallelism

**Phase 0 barely parallelizes**, and that is a property of the codebase, not a planning shortcut. Seventeen of its nineteen tasks cluster in four contended directories — `Api/ExternalAccess/**`, `Infrastructure/ExternalAccess/**`, `Spaarke.Core/Auth/**`, `Spaarke.Dataverse/DataverseWebApiService.cs`. Only `{014, 019}` are genuinely file-disjoint. Task 006 is disjoint and safe but has no co-schedulable partner in its wave.

Two agents editing an authorization path concurrently produces a silent merge mess, so these run in dependency-ordered waves and merge serially with `/conflict-check`. Phases 3 and 4 parallelize better because PCF, scripts, docs and BFF work are genuinely separable.

### Cross-phase collision audit (verified 2026-08-21, not assumed)

| Potential collision | Verdict |
|---|---|
| 044 / 057 / 069 / **089** (was 083) all write `tests/integration/seam` and are all `safe:true` | **Benign** — serialized by phase dependencies, and each creates distinct files. Preserve this ordering if phases are ever resequenced |
| 015 (P0) and 041 (P2) both touch `MembershipResolverService.cs` | **Safe** — 015 is `parallel-safe:false`, so it never co-runs |
| 038 (`safe:true`) shares `ExternalAccessModule.cs` with 035/036/042/056 | **Safe** — those are all `safe:false`; 038's only partner is 034 (tests-only) |
| 065 touches `TrackingFieldTrio`; 050 touches `Spaarke.UI.Components` | **Disjoint** — `components/AccessGrantModal/` vs `src/services/`; no Phase 0 task touches either |
| 065 / 066 / 067 all edit `AccessGrantModal.tsx` | **Serialized** by the 065→066→067 dependency chain |

## Escalation triggers (legitimate stops, not failures)

Tasks carrying `<escalation><trigger>` for genuine judgment boundaries — a task that stops here is behaving correctly (root CLAUDE.md §6 / §6.5):

- **018** — spec FR-17's "bound the in-clause per FR-25" cross-reference is ambiguous (FR-25 is Phase 2); does not guess
- **042** — default when a subject's `sprk_accesspermissiongrant` baseline is empty but the standing flag is set
- **043** — level for a non-standing derived term ("default-on" names no level source)
- **037** — matter / work-assignment lack `sprk_issecure` / `sprk_accesspermission` columns
- **038** — deny-list subgrid storage shape

## Deferred / out of scope

**FR-18 (BU restructure) has no tasks** — reclassified to UAT/environment work. Tasks 061/062 fail closed when the topology is unconfigured and loud-skip pre-UAT; live-dev acceptance is recorded in `notes/phase4-uat-acceptance.md`. Also out: AI-search trimming for contacts (A-21), field-level visibility, break-glass, organization-hierarchy cascade, GDPR erasure.
