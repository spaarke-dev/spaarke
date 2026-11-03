# Task 019 — W2 gate: notes

> Rigor FULL. Tags: deploy, bff-api, security, review. Started 2026-11-02.
> **Deviation from the POML (standing rule):** step 4's deploy does NOT run from this session. The dev BFF is deployed
> by R3 in a batched candidate, with the owner's go in R3's session. This session runs steps 1–3, then the live checks
> (5–8) once dev carries the W2 commits, with the owner's OK for the test-row creates.

## W2 change set reviewed

Every W2 merge on master, up to `origin/master` 1c919560c3:
- #2282, #2287, #2288 (1fa0cf5cb8..1d8cf7b794);
- #2366 073, #2367 070, #2370 037, #2376 038;
- #2392 14b9335a6c, #2435 6b0c1e2fed;
- #2442 071, #2459 072, #2383 074;
- #2486 017 b083d62183;
- #2504 039 0e3962069b.

## Adversarial pass 1 (Opus, fresh agent, 2026-11-02, over 1c919560c3)

The reviewer got the NFR-01 focus list and the ADR-008 path-A note.

**Result: 0 BLOCKING and no authorization bypass or existence oracle.** It ran 2509 tests (2509 passed, 3 skipped) and an ArchTests subset (75 passed).

| # | Finding | Where | Class | Disposition |
|---|---|---|---|---|
| P1-1 | The chat create drops the index columns with the UPDATE helper. It logs `client_search_index_dropped` at once, with no record id and the applied value "unchanged", even when the dropped value equals the one step 8 applies. That breaks FR-38's rule to log only when the values differ, and the log has the wrong shape. | `Services/Ai/Handlers/DataverseCreateRecordHandler.cs:394` | F2 (NIT) | **Fixed:** PR #2514, merged as a50ada7330. The handler drops the columns without logging and passes the dropped values to the service's step-12 log (`RecordCreationService.cs:1462`). |
| P1-2 | A system principal's step 2 checks only the document reason code of `RefuseAppOnlyWrite`. | `RecordCreationService.cs:398` | K2 | Dormant: no app-only caller reaches the service with another table today. Revisit when 033/034 move the system creators onto the service. |
| P1-3 | Repair has no go-live cutoff, so FR-25's "never write a row created before go-live" is not enforced in code yet. | `RecordCreationService.Repair.cs:74` | K2 | Owned by task 052 (the scheduled pass that drives Repair). Repair is not wired to any caller today. **052's brief must carry this item.** |
| P1-4 | An unreadable intermediate parent gives 409 `ParentUndetermined`, not 503. | `RecordCreationService.cs:1051-1055` | K1 | Pre-existing (the deleted core behaved the same). Fails closed; the status code is a known limit. |
| P1-5 | 3 skipped tests: `OfficeEndpointsContractTests.Post_OfficeQuickCreate_*`. | `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs:253,276,298` | K4 | Pre-existing skips from 2026-01 and 2026-03, reason "Requires fully mocked Dataverse services for quick create". The W2 quick-create path is covered by `OfficeRecordOwnershipTests`, `CrossSurfaceOwnerTests` and `SecureRootCreateRouteTests.Office*`. |

## Adversarial pass 2 (Opus, fresh agent, 2026-11-02, over 33daa35141 = #2514 plus all of W2)

**Result: 0 BLOCKING.** No authorization bypass or existence oracle, and every NFR-01 hunt item was checked and found clean:
- caller-check exception → 503 `check_failed`;
- WhoAmI → 403;
- the mapping-source probe is uniform and timing-equalised;
- a mapped filing lookup is refused;
- an unregistered chat table → `table_unsupported`;
- owner → typed 409 or 503;
- NFR-07 order matches pre-W2 `c4a2a7b812`;
- step-12 helpers never throw;
- the autonumber retry has no side effects;
- route auth and the ledger are intact.

Tests: 1750 passed; ArchTests subset 316 passed.

| # | Finding | Where | Class | Disposition |
|---|---|---|---|---|
| P2-1 | A failed create logs every field value (`Payload: {Payload}` plus Dataverse's error body, at Error). Through step 11 this reaches the email.draft recipients and body, and the Office name and description. | `Spaarke.Dataverse/DataverseWebApiService.cs:2126-2142` (from `RecordCreationService.cs:220`) | F4 (privacy) | **Fixed:** PR #2520. All three PATCH-failure logs carry the field names, the status and a strict `0x…` error code only. The main session re-checked the fix diff: clean. |
| P2-2 | The Office autonumber retry now fires only on a 412 from the PATCH upsert. The SDK `0x80060892` classifier no longer sees a body. Task 013's note "live check needed" (task-013-notes.md:105) is still open. | `RecordCreationService.cs:223`, `:1657-1659` | K (unverified) | **Live check:** add it to live check B. Provoke or observe a seed collision, or read the status Dataverse returns. If the status differs, fix the classifier before W3. |
| P2-3 | A search-index bind under a different navigation-property name would survive the drop. It is not live: all six tables use `sprk_AI_Search_Index`. | `RecordTableRules.cs:566-590` | K (hardening) | **Fixed:** PR #2520. A new ArchTest checks each table's navigation property, with must-fire and must-not-fire controls. |
| P2-4 | #2514's run-as-user drop log fires before the secure-filing refusal and the POST. No current table reaches it. | `DataverseCreateRecordHandler.cs:448-454` | NIT | **Fixed:** PR #2520. The log is written after a successful POST, with 3 tests. |
| P2-5 | When step 8 faults, the applied value is (null, null), so a malformed dropped value compares equal and is not logged. | `RecordCreationService.cs:1408-1419` | K4 | Moot once task 041 makes step 8 a no-op. No fix. |

A third full pass is not needed: P2-1 is a logging-only change. A scoped Sonnet re-check covers the fix diff and its direct callers (CLAUDE.md §8.5).

## Step 1: publish size and CVE

Measured against a fresh master on each fix branch:

| PR | Change | Publish delta | Total | CVE |
|---|---|---|---|---|
| #2514 | P1-1 | (the implementer measured none; very small) | — | none |
| #2520 | P2-1, P2-3, P2-4 | +23 B vs a50ada7330 | 39.29 MB, 192 files | none |
| #2519 | #2515, event number | +735 B vs 0fc87effd4 | 39.29 MB | none |

The W2 total stays far below the 55 MB review line. 039 measured 39.10 MB at f56cf8f724.

**Review status:** two full passes, with no F-class finding open once #2520 merges. Still open: P2-2 (live check B) and the live checks themselves.

## Live checks A–C and App Insights (dev BFF 3740e1a105, live 18:52Z on 2026-11-02)

Full detail is in `notes/task-019-live-checks.md`. The parent record is MAT-000032 (ordinary), owned by the BU default team "Spaarke Business Unit 1".

| Check | Result |
|---|---|
| A: child-records route, Event | **PASS.** The owner is the BU default team. The I-1 stamp is set, `EVT-000001` was assigned, and the search index is the BU's. **Deviation:** the "mapped fields" clause does not apply. The route deliberately passes no mapping until W3, where task 020 adds mapping to it, so that clause moves to 020. |
| B: Office quick-create, Project | **PASS.** Same owner as A, the stamp is set, 6 fields were mapped, and `PRJ-000044` was assigned. The response shape is unchanged. |
| P2-2 | Remains K, unverified. The alternate key on `sprk_projectnumber` exists and is Active. A collision was not provoked. |
| C: chat create | **PASS (intent), 2026-11-03.** The owner created a To Do through the Console Assistant: event `6d1563bb-829d-49da-9d29-92ea695ebba6`, `EVT-000003`. The owner is the BU default team "Spaarke Business Unit 1". The I-1 stamp `sprk_regardingmatter` was set at create, the search index is `spaarke-files-index`, and it was created by `# mi-bff-api-dev` (the service path). **Caveat:** the Assistant chose its own name ("Contact Outside Counsel") and parent (PAT-792345), not the prompt's "FMSWP-019 C" under MAT-000032. This is LLM behaviour, not a write-path defect. Afterwards, the chat tool description on dev was re-seeded (`Seed-TypedHandlers.ps1 -OnlyHandler DATAVERSE-CREATE-RECORD`, row `18b3531f-…`). It now tells the model that `sprk_eventnumber` is set by the server. |
| Negative check (AppendTo) | **Owner action.** It needs a narrower user; `# UAC Child BU Test User` fits. |
| App Insights | No 5xx after the deploy. The `sprk_userentityassociation` "does not exist" exception (×10 at 19:00:52Z) is **not new**. It is the known #2292 (UAC-r2): the nightly reconciler logs about 1,000 of them a day. Check A triggered one first-time membership event, which is 10 log lines. The fault is fail-closed, so it does not affect A or B. **Correction:** the "24 h before" baseline was wrong, because `az monitor app-insights query` defaults to `--offset 1h`. Future baselines must pass `--offset` or a `timestamp` filter. Check A produced no telemetry: #2522. |

Also found: the Office Project create took about 18 s, about 11 s of it on six sequential assigned-access grants (#2523).

**Rows to delete later:**
- event `fcdd4724-e243-4d37-9583-d694a8644318`;
- project `1cccf07d-0e0a-43f9-8ea0-f40533d5d691`;
- 6 `sprk_assignedaccess` rows, with their ids in the live-checks note;
- event `6d1563bb-829d-49da-9d29-92ea695ebba6` (check C). The owner created this row, so delete it only with the owner's OK.
