# Remediation plan — every Fable-review finding, owned in this project

> **Owner decision 2026-09-21**: *"we need to address both of these issues fully — not only to overcome the
> limited blockers. There are a number of issues raised in your Synthesis, all of which appear important and
> that MUST be addressed in this project, not deferred or only added to GitHub issues."*
>
> Source: [`notes/fable-review-2026-09-21.md`](fable-review-2026-09-21.md). Nothing below is deferred to
> another project, to an issue, or to the wrap-up. Every finding gets a task that lands on this branch.

---

## 0. Scope change this represents

The project was 55 ✅ / 1 🔄 / 5 🔲 = 61 tasks, with 4 codeable tasks left. This plan adds **19 tasks
(061–079)** and **re-scopes 3 existing ones (058, 059, 060)**, taking the project to **80 rows**. Three of the
new tasks are security fixes for defects that pre-date this project but are reachable from the surface this
project built or extended — we own them because we are the ones who found them and the pane is now a caller.

**Two findings cannot be fully closed by a file change in this repo, and the plan says so explicitly rather
than pretending otherwise** — see §5 (the repo ruleset) and §6 (live-host verification).

---

## 1. Wave A — authorization (must land first, in this order)

The census task is **first** because without it every fix below can silently regress, and because it is the
mechanism that would have caught all of them.

| Task | Title | Why it is where it is |
|---|---|---|
| **061** | **Census: govern the Office + Rag route surface** — add `Api/Office/OfficeEndpoints.cs`, `Api/Office/CommunicationsEndpoints.cs`, `Api/Ai/RagEndpoints.cs` to `RouteAuthorizationGuardTests.GovernedFiles`; **in the same change** widen `FilterMarker` (`:1408-1411`) to recognise `AddEntityAccessFilter`, `AddJobOwnershipFilter`, `AddQuickCreateSourceAccessFilter`, `AddOfficeAuthFilter`, or rename those four to match the existing regex. Seed-test both directions: a governed route with its filter removed must go red. | Governs the surface **before** we change it, so 062–067 are enforced by a test rather than by review. Doing it after would let each fix land unguarded. |
| **062** | **F1 — trim `/office/search/entities` per caller.** Run the entity query as the caller (OBO / `MSCRMCallerID`) or post-trim each result through `AuthorizationService`. Negative test: a user with no rights on a matter must not see it in results. | **The only HIGH with a live consequence.** Keystone — F2/F3/F4 all need GUIDs and this hands them out. |
| **063** | **F2 — `send-to-index` tenant binding + per-row write authz.** Add `SendToIndexRequest` to `TenantAuthorizationFilter.ExtractTenantId` (or ignore the body and use `tid`); authorize each `DocumentId` for `write` before the app-only `UpdateDocumentAsync`. | The pane became a caller in this project (`FindView.tsx:313-316`), so the cross-tenant index write is now reachable from our surface. |
| **064** | **F3 — gate `/office/todo` source GUIDs.** Read-check `RegardingRecordId`, `DocumentId`, `CommunicationId`, `AssignedToContactId` in a `QuickCreateSourceAccessFilter`-shaped filter. Close the 403-vs-201 existence oracle. | The `DocumentId`/`CommunicationId` carriers were **added by this project** (task 035). Ours outright. |
| **065** | **F4 — require `TargetEntity` on `/office/save`.** Make it required (the code comment at `OfficeService.cs:143-145` already claims no shipped path omits it — that premise is false since task 037), **and** fix the Word ribbon quick-save to resolve a container the way the pane does. | Today every Word ribbon quick-save bypasses `EntityAccessFilter` and lands in the shared default container, then gets profiled and RAG-indexed. |
| **066** | **F9 — per-resource filter on `/api/office/communications/*`** (three routes, communication Read). | The pane calls all three on this branch. |
| **067** | **F5 — job-status ownership.** Persist creator OID on `sprk_processingjob`; make `JobOwnershipFilter.cs:157` **fail closed** on missing `CreatedBy`; delete the hard-coded test job `0000…0001` (`OfficeService.cs:1511-1541`). | Shares its schema change with 060 — sequence 067 → 060 so the column lands once. |

---

## 2. Wave B — durability (060 re-scoped, + its siblings)

| Task | Title |
|---|---|
| **060 (RE-SCOPED)** | Durable job store **plus** the two fields the original scope missed: persist `Result.Artifact` (`OfficeDocumentPersistence.cs:846-855`) and the creator OID from 067. **Client half is in scope**: `useSaveFlow.ts:755-776` must treat `Completed`-without-artifact as a **terminal error**, not fall through to a silent stall; same for the SSE path (`useSaveFlow.ts:622`). Acceptance criterion is behavioural (restart mid-save, pane reports an outcome), not "the dictionary is gone." |
| **068** | **Durability siblings.** (a) `OfficeProfileDispatcher.cs:125` `_ = Task.Run(...)` → enqueue via the existing job path using **task 029's per-save discriminator** (`UploadFinalizationWorker.cs:790-792`), which already solved the idempotency-key collision the dispatcher's comment cites as its reason to exist; delete the dispatcher and the 3 ctor params it needs. (b) `JobStatusService.cs:46` `_jobSequences` — per-instance SSE sequence numbers break `Last-Event-ID` reconnect across instances. |

---

## 3. Wave C — verification (make the controls actually bind)

| Task | Title |
|---|---|
| **069** | **Make the office-addins gate block.** Remove `"Reports, does not block"` (`office-addins-tests.yml:275`, `:427`) so the jobs genuinely fail the workflow. **Pin the test-debt total** — 111 becomes an asserted baseline, not an unpinned observation (`:400`, `:423`, `:437`), so 111 → 112 fails. **Fix the false trailing-newline claim** at `:252-254` (both sides of the assertion derive from the same array, so it protects nothing) — either make the check real or delete the claim. |
| **070** | **Give this project's C# tests a completing PR-time runner.** `ci-router/tier1/tier2` are **frozen** (shadow-comparison window, 2026-08-27) — so add a job to **`office-addins-tests.yml`, which this project owns**, that runs the Office contract + data-mutation + NFR-02 authorization suites on PR. This closes the gap without touching a frozen file. Residual that a file change cannot close: the repo **ruleset** still requires only `Router` — §5. |
| **071** | **Repair the 10 red jest suites (84 tests).** All test-side or harness-side; none is a shipped code bug. Add `ResizeObserver` to `jest.setup.js` (10 suites polyfill per-file today; `ShareView` does not and dies inside Fluent `MessageBar`); fix `mockReadItem` `itemType`; fix stale label strings from tasks 015/020/024/038; fix the fetch mocks missing `text()`/`headers`; fix `useEntitySearch` awaiting under fake timers. **Then add all 10 to `ci-gated-suites.txt`** — four of them were modified by this project and left ungated. |
| **072** | **Fix `npm run lint` and wire it.** `package.json:13` is `eslint src` and there is no `src/`; no workflow calls it. Point it at the real directories and add it to the gate. |
| **073** | **Align Node.** Gate 20 · nightly 20 · **deploy 18** (`deploy-office-addins.yml:34`) · desktop 22 · `package.json:68` `>=18` · no `.nvmrc`. Add `.nvmrc`, raise the deploy job to 20, tighten the engines range. **What ships must be built on the version the gate tested.** |
| **074** | **Run `tests/e2e`.** `word-addins/save-flow.spec.ts` was modified by this project and no workflow runs it. |

---

## 4. Wave D — code quality

| Task | Title |
|---|---|
| **058 (RE-SCOPED)** | Delete the four stub routes + 14 members, **plus** `useShareFlow.ts` (dead hook calling two non-existent `ApiClient` methods) and `tests/e2e/pages/addins/OutlookTaskPanePage.ts:305-331` — both missing from the current POML inventory. Reproduce-first criterion changed to a **random GUID**, demonstrating the routes read nothing (the original criterion documented a 200 that proves less than it appears to). |
| **059 (RE-SCOPED)** | **Drop the LOC acceptance criterion** (the metric CLAUDE.md §11.5 forbids). Remove the **six optional ctor params** (`:89-94`) justified by *"bare test constructions"* that do not exist — `grep -rn "new OfficeService(" tests/` returns nothing — and their unreachable null branches (`:1638-1648`, `:1881-1884`, `:2345-2349`, `:2494-2498`, `:2619-2622`), including the ADR-032-forbidden empty-list-200 at `:1881`. With 068 removing 3 more, the ctor goes **18 → ~10 with no new service class**. Leave the save spine intact — it is cohesive. |
| **075** | **Dead code + stale premises.** Delete `outlook/OutlookHostAdapter.ts` (502 lines, self-described dead, edited 5× on this branch just to keep `tsc` quiet) and its doc-comment reference; de-duplicate `mintDocumentShareLink` (`shareLinkService.ts:50-69` ≡ `sendEmailService.ts:98-119`); remove `HostAdapterFactory`'s uncalled API; correct the false comment at `OfficeService.cs:143-145`/`:341-343`; fix `OFFICE_INTERNAL` → **HTTP 400** (wrong status class for a server fault, `OfficeEndpoints.cs:654-663`); remove the hard-coded `spaarkedev1.crm.dynamics.com` (`OfficeDocumentPersistence.cs:937-942`). |

---

## 5. Wave E — spec and record integrity

| Task | Title |
|---|---|
| **076** | **FR-13 — stop shipping blank primary names.** `sprk_matternumber` IS `sprk_matter`'s primary name and `RecordCreationService` never writes it, so a pane-created Matter has an empty display name everywhere. Implement server-side numbering (Matter and Project) so no record is created nameless. **This is the UAT complaint the spec was written to fix**; it currently lives in a note with no owning project. Then fix SC-8, whose only "evidence" today is `AssertNoMatterNumberSent` — the opposite assertion. |
| **077** | **FR-16 — close the three gaps.** Records bridge (documents-and-records, as the spec says); Outlook's Find tab, which can never leave state 1 (`FindView.tsx:101`); and the post-save transition, where a desktop-sourced document saved this session never becomes findable (`App.tsx:712` vs `:748`). |
| **078** | **FR-05 — resolve the manifest contradiction.** Word runs from `dist/word/manifest.xml`, not the unified JSON the FR requires. Either sideload and verify the JSON (task 011's unverified ACs 6/7) or amend FR-05 with the evidence. **Decide, don't leave both on the record.** |
| **079** | **Record integrity.** Amend FR-12 (+ add its missing ADR-Tensions row) and fix SC-6, which cites a test asserting "no new collision logic" that returns 0 grep hits against logic that demonstrably exists; fix the 042 tally; sync the **7 POMLs** reading `not-started` while ✅; repair the **10 malformed POMLs** (5 XML errors, 5 missing `<steps>`, all already-completed tasks); re-verify or reopen the **14 ✅ tasks** whose completion claims the review found unsupported. |

Existing **057** (ADR-038 A2) keeps its sequencing constraint: it must precede `/test-diet`, and `.claude/` makes it **main-session only**. **090** closes the project.

---

## 6. What this plan does NOT claim to close

Stated up front so no one later reads silence as success:

1. **The repo ruleset.** 069 + 070 make the checks real and make them run. Whether a red check **blocks a
   merge** is a GitHub ruleset setting, not a file in this repo, and `ci-router.yml` is frozen. After 070,
   adding the office-addins workflow to the required set is a one-click owner action — the plan surfaces it
   at wrap-up rather than pretending a file change did it.
2. **Live-host verification (042).** 13 acceptance criteria and 14 parity rows need a real Office host. No
   task can substitute for that.
3. **Cross-project ISS-001** (`sprk_event` written to a column that does not exist) stays with
   `unified-access-control-r2`, which owns that file. We keep the note.

---

## 7. Execution order

```
061 ──► 062 ──┐
              ├──► 063, 064, 065, 066   (parallel — disjoint files)
067 ──► 060 ──┘
068 ──► 059
069 ──► 070 ──► 071 ──► 072, 073, 074   (072/073/074 parallel)
058, 075                                 (parallel with anything)
076, 077, 078                            (parallel)
079 ──► 057 ──► 042 ──► 090
```

Every task touching `Sprk.Bff.Api` carries the CLAUDE.md §10 obligations: Placement Justification, publish
size measured **against a fresh build of master** (never the recorded number), CVE check, and the test-update
obligation. `061` and `057` touch `.claude/` or `tests/Spaarke.ArchTests` governance and are
**`parallel-safe: false`**.
