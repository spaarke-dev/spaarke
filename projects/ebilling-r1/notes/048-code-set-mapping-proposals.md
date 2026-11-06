# Task 048 - AI code-set mapping proposals (admin-confirmed)

Status: code complete and committed. Spec: FR-22 (admin action sentence, step 4), D-52, ADR-008/013/019/028. **No live change was made** and none is needed (section 4). One open owner question on proposal quality (section 3).

## 1. What shipped

| Area | Files |
|---|---|
| Service | `Services/Finance/Coding/CodeSetProposalService.cs` (also holds the models and `CodeSetReferenceTables`): `ProposeAsync`, `ConfirmAsync`, and the `ICodeSetSuggestionProvider` hook |
| Endpoints | `Api/Finance/CodeSetMappingEndpoints.cs`: `GET /api/finance/code-set-mappings/proposals`, `POST /api/finance/code-set-mappings/{table}/{id}/confirm` (`{table}` = `mattertype` or `practicearea`, body `{ "codeSetId": "<guid>" }`), plus `CodeSetForbiddenCodeFilter` |
| DI / mapping | `FinanceModule.cs`: the one `NoCodeSetSuggestionProvider` registration is replaced by `AddScoped<CodeSetProposalService>()` and a forwarding `AddScoped<ICodeSetSuggestionProvider>(sp => sp.GetRequiredService<CodeSetProposalService>())`. `EndpointMappingExtensions.cs`: one `app.MapCodeSetMappingEndpoints()` line |
| Tests | seam `tests/integration/seam/Finance/CodeSetProposalServiceSeamTests.cs` (17), contract `tests/integration/contract/Api/Finance/CodeSetMappingEndpointsContractTests.cs` (24 cases); `FakeCodingReferenceData` extended (Null/NotNull conditions, `TopCount`, names/state/body on rows, `UpdateAsync` applied to the stored row) |
| Arch tests | `RouteAuthorizationGuardTests.Ledger.cs`: governed file added (`RouteLevelGate`), `ExpectedEndpointFileCount` 126 -> 127, the stamping filter classified in `NotAuthorizationForms`. `ADR010_DITests.cs`: ceiling 166 -> **165** |
| Comment drift | `ICodeSetSuggestionProvider.cs` doc comments (no behaviour change) |

### Placement and justification (CLAUDE.md section 10 and 11)

- **Placement (bff-extensions A.1):** in the BFF, `Services/Finance/Coding/` and `Api/Finance/`, beside 047. Synchronous reads, embeddings and one Dataverse update per confirm; no background work (ADR-052 has nothing to place). No Action, no Binding, no `ConsumerTypes` entry, no prompt or schema in C# (D-52, ADR-013): the only AI call is `IInvoiceAi.GenerateEmbeddingAsync`.
- **Existing / Extension / Cost (section 11):** (1) overlaps `CodeSetResolver` (reads the same mappings) and `TimekeeperMatcher` (embedding + cosine); both reused (`TimekeeperMatcher.Cosine` is called, not copied), neither modified. (2) 047's seam is extended by supplying its second implementation; no resolver edit. (3) Without it FR-22 fails: unmapped matter types fall back to the Litigation default with no flagged suggestion and no admin action to fix the mapping.
- **New surface:** 2 routes (own endpoint file per the brief), 1 service class registered concrete, 1 endpoint filter class. **No new interface.**
- **ADR-010:** `ICodeSetSuggestionProvider` now has two implementations (`NoCodeSetSuggestionProvider`, `CodeSetProposalService`), so it stops being a 1:1 mapping. Measured with the ceiling at 0: 165 mappings, that interface not among them. Ceiling lowered 166 -> 165 (delta -1; this task adds +0). 047's entry promised "gains the second in 048".
- **ADRs:** 001 (minimal API), 002 (one writer: `ConfirmAsync`; check as the caller, write as the app, owner decision G5), 008 (per-route filters: stamping filter, then `AddFinanceAuthorizationFilter`, then `RequireEbillingEnabled`), 010, 013 (facade only), 015 (ids only in logs, never request text), 018 (flag per route), 019 (ProblemDetails with `errorCode`), 028 / D-76 (caller's own table privilege, no role or app-role check), 032 (`NullInvoiceAi` throws the feature-disabled exception; the route answers 503).

## 2. Behaviour

**Proposals (GET).** For every ACTIVE `sprk_mattertype_ref` / `sprk_practicearea_ref` row with a null `sprk_defaultbillingcodeset` (matter types first, each by name): the best usable code set, its cosine score (0-1, 3 decimals), the runner-up, and a one-sentence rationale. Mapped and inactive rows are excluded. Nothing is written. A row with no usable set at all comes back with `proposed: null` and a rationale saying so.

**Usable set** = active, with at least one active Task code (the same test `CodeSetResolver` applies on load), so the shared "UTBMS Activity" / "UTBMS Expense" sets and inactive sets are never proposed or confirmable. Sets are embedded as `"{name} ({body})"` (the table has no description column, task 018); reference rows as their name (they have no description either). Set embeddings are loaded once per service instance (per request or job scope).

**Confirm (POST).** Re-reads the row (a row that is gone or inactive: 404 `ebilling.codeset.not_found`), refuses with 409 `ebilling.codeset.already_mapped` when it has a mapping (nothing written), refuses with 404 `ebilling.codeset.not_found` when the code set is unknown, inactive, has no active Task code, or the id is empty; otherwise sets `sprk_defaultbillingcodeset` on exactly that row. `ConfirmAsync` is the only writer of the column in `src` (grep of `sprk_defaultbillingcodeset` / `DefaultCodeSetColumn`: reads in `CodeSetResolver` and `CodeSetProposalService`, one `UpdateAsync` in `ConfirmAsync`; the rest are the Dataverse schema definitions and the 018 script).

**Authorization (D-76, fail closed, as the caller).** List: Write on BOTH tables. Confirm: Write on the one table named in `{table}`. An unknown `{table}` is a 404 before any rights query. 403 body carries `reasonCode` (from the shared filter) and `errorCode: ebilling.codeset.forbidden` (stamped by `CodeSetForbiddenCodeFilter`, chained before the authorization filter, which only reads its 403 result). Order: 401 (group `RequireAuthorization`), then authorization, then the flag (503 `ebilling.disabled`), so the flag never replaces authorization. AI off: 503 with the facade's `ai.invoice.disabled` code.

**Hook (FR-22 step 4).** `SuggestCodeSetAsync` runs only when the resolver's steps 1-3 gave no set. It returns a set only when NEITHER the record's matter type NOR its practice area has a mapping: it re-reads the rows, and if either is mapped (even to a set the resolver skipped as stale) it returns null so a suggestion never second-guesses an admin's choice. A lookup that is not set counts as unmapped; with both unset there is nothing to rank and it returns null. With both set and unmapped it proposes for the matter type. The resolver validates the id and flags `CodeSetSource.AiSuggestion` / `NeedsReview`; the suggestion is never persisted (seam test: resolver + real provider, no `UpdateAsync`, mapping still empty).

### Deviations and interpretations

1. **Privilege names are mixed case:** `prvWritesprk_MatterType_Ref` and `prvWritesprk_PracticeArea_Ref` (live `privilege` rows, spaarkedev1, 2026-10-16). The POML's example `prvWritesprk_mattertype_ref` would be "not held" for every caller. Pinned by literals in the contract tests.
2. **Test placement:** the POML names `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Coding/CodeSetProposalServiceTests.cs`; as in 047 the service tests are on the KEEP seam path (they reuse `FakeCodingReferenceData`), the endpoint tests on the contract path. Same assembly.
3. **No concrete-service interface and no UI** (brief: smallest surface). The admin lists proposals and confirms through the two routes; no form, view or page (SpaarkeMaster source untouched).
4. **`ebilling.codeset.not_found` is used also for an unusable code set** (inactive, no Task codes, empty id) and for an unknown `{table}` segment. The ADR-019 set in the brief is closed at three codes; a fourth for "not usable" was not added.
5. **GET needs Write on both tables** (the brief: "the same check"), so an admin who can write only one table cannot list. Confirm needs only the named table.
6. The stamping filter (a public class) is an addition beyond the brief: the shared `FinanceAuthorizationFilter` emits only `reasonCode`, and the acceptance criteria name `ebilling.codeset.forbidden`. Editing the shared security filter was the alternative and was rejected as a larger blast radius.
7. TASK-INDEX.md and current-task.md were not edited (launch brief).

## 3. Escalation to the owner: proposal quality on the seeded data (POML trigger 2)

Live probe, 2026-10-16: `text-embedding-3-large` at 3072 dimensions on `spaarke-openai-dev` (read-only; embeddings of the six seeded sets "name (body)" against the 11 dev reference rows, same text shapes the service uses; throwaway script, not committed):

| Row | Best set (score) | Runner-up | Reading |
|---|---|---|---|
| Matter type Litigation | UTBMS Litigation (0.519) | IP Patent (0.357) | right |
| Matter type Patent | IP Patent Prosecution (0.404) | Trademark (0.352) | right |
| Matter type Trademark | Trademark (0.525) | IP Patent (0.331) | right |
| Matter type Employment | Counseling (0.290) | IP Patent (0.265) | plausible, weak |
| **Matter type Commercial** | **Trademark (0.310)** | IP Patent (0.261) | **wrong** (no set fits; Counseling or Project would be closer) |
| Practice area Appellate | Litigation (0.249) | IP Patent (0.236) | right, weak |
| Practice area IP Patents | IP Patent (0.491) | Trademark (0.348) | right |
| Practice area IP Trademarks | Trademark (0.516) | IP Patent (0.390) | right |
| Practice area Banking & Finance | Bankruptcy (0.379) | Litigation (0.268) | plausible |
| **Practice area Mergers & Acquisitions** | **Bankruptcy (0.288)** | Litigation (0.272) | **wrong** |
| **Practice area Commercial Transactions** | **Trademark (0.347)** | Bankruptcy (0.317) | **wrong** |

The clear-cut rows (Litigation, Patent, Trademark, IP, Appellate) rank correctly; the three rows with no good set in the seeded eight (Commercial, M&A, Commercial Transactions) get an obviously wrong proposal at low scores (0.29-0.35 against 0.40-0.53 for the right ones, but Employment at 0.29 is plausible, so no safe cut-off separates them). The POML says to STOP and propose a prompted Action when embeddings propose an obviously wrong set for the seeded data. **The coordinator directed completion, so the task is delivered as specified (embeddings, nothing applied unconfirmed, the score and runner-up shown to the admin), and no Action was added.** The owner decides one of:
- (a) accept as is: the admin sees the score and runner-up and confirms each row, so a bad proposal costs a rejection, not a wrong mapping;
- (b) add a minimum score in settings (below it a row gets `proposed: null`); the data above does not give a clean threshold;
- (c) a prompted Action (`invoice-code-set-propose`, own Binding and eval family) that sees the set names and each set's sample code descriptions and can answer "none fits"; this is a new task.
The hook is lower risk than the list: it is flagged for review per line and never persisted.

## 4. Live changes and seeding

None made, none required: no Action row, no Binding row, no `ConsumerTypes` entry, no schema change, no solution change. The deploy (task 053) ships the build as is. After deploy a smoke check is `GET /api/finance/code-set-mappings/proposals` as an admin with `Ebilling:Enabled=true`; expect 11 rows on dev today (5 matter types, 6 practice areas) and the table in section 3.

## 5. Verification

- Build: `dotnet build` of `Sprk.Bff.Api` and the test project, 0 warnings, 0 errors.
- New tests: 41 (17 seam + 24 contract cases), all pass. With the 047 resolver and coding seams: 90 pass / 0 fail.
- Mutation checks (3 mutations, 6 tests failed, then restored): confirm without the already-mapped check, the hook ignoring a mapped lookup, and the 403 `errorCode` not stamped. Each is caught by the named tests (`Confirm_AfterTheRowGainedAMappingSinceTheProposal...`, `Confirm_AnAlreadyMappedRow...`, `Suggest_WhenEitherRecordLookupIsMapped...`, `EveryRoute_CallerWithoutThePrivilege...` x3). The tests were written with the code, so the "fails before" proof is the mutation run rather than a revert.
- Finance / eBilling / coding / consumer-type / SPE container subset of `tests/unit/Sprk.Bff.Api.Tests` (after a clean rebuild of the test project; an interrupted build had left the first bin incomplete): 922 passed, 0 failed, 2 skipped (live).
- `tests/Spaarke.ArchTests`: 1052 passed, 0 failed (route ledger at 127 files, ADR-010 ceiling 165, gate-combination constructibility).
- Whole `tests/unit/Sprk.Bff.Api.Tests` project (final code, `--no-build`, 54 min on a loaded machine): 23936 passed, 89 failed, 25 skipped, 24050 total. The 89 failures were all in unrelated areas (Office, Compose, Communication, External access, Insights and similar; none in Finance, CodeSet or Coding) and looked load-induced. Every failing class (66 classes, 844 tests) was re-run on its own: 844 passed, 0 failed. Two samples also passed alone before that re-run.
- Publish size (BFF hygiene section 10; `dotnet publish -c Release -r linux-x64 --self-contained false`, `Compress-Archive` Optimal, PDBs included; each tree from a `git archive` into a short path, not iterated in): fresh `master` 37.15 MB; this branch before the task (`9db5d265e`) 37.29 MB; with this task 37.30 MB. **Task delta +0.01 MB** (branch vs master +0.15 MB). 192 files in each. No package added; `dotnet list package --vulnerable --include-transitive`: none.

## 6. Review results (Step 9.5)

Rigor STANDARD; code-review and adr-check run because `tests/**` is touched.

- **adr-check:** compliant with ADR-001, 002, 008, 010, 013, 015, 018, 019, 028, 032 and bff-extensions A-F (reasons in section 1). Violations: none. ADR tensions: none.
- **code-review (self):** cohesive files (service 380 lines is one flow: list, rank, confirm, hook; endpoints file is two routes and one filter). No swallowed failure: the AI exception and Dataverse faults propagate (503 / 500). Logs carry ids only. Duplicated: a 15-line paging helper (`RetrieveAllAsync`), kept private rather than editing 047's resolver.
- **Adversarial pass:** the one `adversarial-reviewer` pass could not be launched (the session's concurrent sub-agent limit was reached); not run. The suggested read-only brief: diff `9db5d265e..dabb1ea20` plus this notes commit, focus on authorization ordering, the hook against resolver steps 2-3, and the shared fake edits.

## 7. Known limits (K-class)

- ~~L1 (K3)~~ **Reclassified and fixed in task 054 (2026-10-16).** This was a confirmed lost update on a real path, so it was never a valid known limit (a K-class never holds a confirmed defect). The original claim that "the generic Dataverse seam has no such update" was wrong: `IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync` (If-Match on `versionnumber`, `DBConcurrencyException` on 412) already existed. Scenario as found: two admins confirm different sets for the same row at the same moment; both pass the "unmapped" re-read, the later unconditional write silently replaces the first admin's mapping, and both are told 200. Fix: the confirm re-read now reads `versionnumber`, the write is conditional on it, and a concurrent change re-reads (up to 3 attempts) and answers `AlreadyMapped` (409) without overwriting. Test: `CodeSetProposalServiceSeamTests.Confirm_WhenAnotherAdminMapsTheRowBetweenTheReReadAndTheWrite_Returns409Outcome_AndKeepsTheOtherMapping` (fails with the old unconditional write, passes with the fix). See notes/054-code-set-matching.md section 6. Since 054 fix-round-1, a row that keeps changing through all 3 attempts but is still unmapped answers `ConcurrentChange` (409 `ebilling.codeset.concurrent_change`), not `AlreadyMapped`.
- L2 (K3): the hook costs two reference reads plus one embedding per set and per row for every invoice that reaches step 4 (set embeddings are cached per scope, so once per job scope). A job coding many invoices in one scope pays the set embeddings once.
- L3 (K3): proposals embed names only; quality is bounded by section 3.
- L4 (K4): the list is not paged (reference tables hold tens of rows); one embedding call per row, sequential.
- L5 (K4): a mapped-but-stale lookup (set inactive or without Task codes) makes the hook return null, so such an invoice falls to the settings default and the resolver logs the stale-mapping warning; the admin fixes the mapping by hand.
