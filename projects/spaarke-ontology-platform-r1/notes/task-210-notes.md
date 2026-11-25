# Task 210 — column display names and choice labels (#3040 DI-169, #3020 DI-165)

Branch `work/ontology-210-column-display-names` from origin/master `e70ff38894`, worktree `C:\wt210`. Rigor FULL; Opus @ high.

## What changed (shown text only)

| Surface | Before | After |
|---|---|---|
| Rule wizard, Condition → column lists (subject and related-table rows) | `createdon`, `sprk_openeddate`, `statecode` | `Created On`, `Opened Date`, `Status`; option value still the logical name |
| Rule wizard, Threshold "Measure" list | `sprk_totalbudget` | `Total Budget`; value unchanged |
| Rule wizard, Wording → "Values the message can quote" | button text `zz_state` | display name; the inserted token is still `{{logicalName}}`; test ids unchanged |
| Decision wizard "How this was determined" (plain line) | "Each matter where createdon is on or before 5 days ago." | "Each matter where created on is on or before 5 days ago." |
| Same, `statuscode` / choice equality | "status is 659490001", "review outcome is not 100000003" | "status is Open", "review outcome is not Dismiss" (label known); number when not |
| Formal line, stored rule bodies, API field values, PredicateCompiler allow-list, TemplateEligibleFields | — | unchanged |

## Label-source decision (step 1; root CLAUDE.md §11)

Inventory of existing sources:
- `RuleBodyDescriber.ColumnLabels` (static, column-keyed, 12 entries, sentence wording) and `EntityLabels` (task 203, guarded).
- `PolicyVersionValidator.AuthorableColumns` — closed list of 50 (entity, column) pairs; the catalog offers exactly these.
- Client `utils/tableLabels.ts` (tables only; task 203).
- `MetadataService` (`Services/Dataverse`, task 012/072) — projects `RetrieveEntityRequest(Attributes)` to a slim DTO including every state/status/picklist option label; cached in Redis (system key `sdap:dv:entitymetadata:v2:{entity}`, already allow-listed, 6 h absolute TTL), org-wide, app-only. Scoped registration; `UpdateRecordActionCore` already reaches it from a singleton via `IServiceScopeFactory`.
- `DecisionParameterSources` — per-caller picklist read, tenant cache, wizard options only (the issue's reason it is not the source).
- Record-type catalog memo in `TodoRegardingBuilder` — in-process, unbounded lifetime, for `sprk_recordtype_ref` rows (not metadata).

Decision:
1. **Column names: static.** `RuleBodyDescriber.ColumnDisplayNames`, keyed by (entity, column), holds the Dataverse display name of every `AuthorableColumns` entry. Guards: every authorable column has one, it lists nothing else, and each equals `SpaarkeMaster/Entities/*/Entity.xml` (schema pairing). The catalog serves them (`FieldLabels` on `OntologyCatalogSubject` and `OntologyCatalogJoin`, additive JSON), so the client keeps no second list. Sentences use the existing column-keyed `ColumnLabels` wording first (unchanged: `statuscode` "status", `statecode` "state", `sprk_respondedon` "response recorded", budget utilization percent, plus eight entries equal to the lower-cased display names, kept so a column of a body written outside the BFF on a table where it is not allow-listed reads as before), then the display name lower-cased, then the logical name.
2. **Choice labels: reuse `MetadataService`.** Option labels differ per table and per environment, so they come from entity metadata. The describer resolves the scoped `MetadataService` through `IServiceScopeFactory` (new optional constructor; DI picks it; the 3-argument constructor remains for the 25 test constructions and yields numbers). No new cache, service, interface, endpoint or package.
   - Read only when the body tests a choice value: a non-`statecode`, non-lookup column compared for equality (`=`, `<>`, bare value, list) with a whole number. Most bodies (dates, statecode, lookups, Threshold quantities) read nothing (test `ABodyThatTestsNoChoiceValue_ReadsNoMetadataAtAll`).
   - One read per table per description, from the shared Redis copy; Dataverse is called only on a cache miss, once per table org-wide per 6 h. No per-request Dataverse metadata read on the decision path (test `AStatusReasonTest_…`: warm cache, zero `IDataverseService` invocations).
   - Fails soft: any fault (Redis, Dataverse, missing attribute) shows the number; the description is not refused; one Warning per table (`RuleDescriptionLabelsUnavailable`, 50372) with entity + exception type only. Caller cancellation propagates.
   - A comparison (`>=` etc.) on a choice column keeps the number ("is at least Open" would compare words).
   - Why not an in-process memo: ADR-009 forbids an L1 over Redis without profiling proof and needs a documented `IMemoryCache` exception; the Redis copy already is the app-wide bounded cache the POML asks for.

§11 three answers (PR):
- **Existing**: `MetadataService` (cached option labels), `RuleBodyDescriber.ColumnLabels`/`EntityLabels` (static labels), `AuthorableColumns` (closed list).
- **Extension**: extended all three in place — `ColumnLabels` became `ColumnDisplayNames` + `ColumnPhrases` in the same class, the describer consumes `MetadataService` as is, the catalog records gained one additive field. New surface: one optional constructor parameter, one EventId, one catalog field; no new service, registration, endpoint, cache or package.
- **Cost of doing nothing**: decisions read "status is 659490001" and "where createdon is …"; admins pick columns by schema names (#3020, #3040).

## Callers of the describer (6) — behaviour

`DecisionCapability` (decision plan), `OntologyAdminRulesService`, `BillingGuidelinesService`, `RecordProcedureReadService`, `RuleSetService` (also reached by the billing draft job). Signature unchanged (`DescribeAsync(string?, CancellationToken)`); only the plain-language text changes. Billing bodies return from `BillingRuleCatalogue.Describe` before the label scan, so Finance text is unchanged.

## Tests (ADR-038)

Failing on origin/master (master sources, new tests):
- Server: `MasterProof` copy of the column theory + created-on test against master `RuleBodyDescriber`: **25 failed / 26 passed** (every non-`ColumnLabels` column, e.g. `createdon`, `sprk_openeddate`, `sprk_triagecategory` null test). The statuscode-label, guard and catalog tests use new members, so on master they do not compile.
- Client: master `RuleWizard` sources with the new tests: **3 failed / 62 passed** (column texts, measure text, Wording button text).

New / changed:
- `tests/unit/domain/Signals/RuleBodyDescriberLabelTests.cs` (new, 61 cases): display-name guard vs Entity.xml and allow-list; theory over all 50 authorable columns; created on; statuscode / list / `<>` / picklist in a related clause by label (labels read from Entity.xml, served through the real `MetadataService` over an in-memory distributed cache); unknown value → number; metadata failure → number, not refused, logged by type; cancellation; no read for non-choice bodies; comparison keeps number; DI resolves the scope-factory constructor (`ValidateScopes`, `ValidateOnBuild`).
- `tests/integration/auth/Ontology/OntologyAdminAuthoringEndpointTests.cs`: `Catalog_LabelsEveryColumnItOffers_ByItsDisplayName_AndKeepsTheLogicalNameAsTheValue`.
- Client `RuleWizard.test.tsx` + `fixtures.ts`: display names with logical values (when rows, related rows, preview body), fallback to logical name, measure text, Wording button text with logical token.

Contract baselines: `tests/integration/contract/Api/Signals/Baselines/plan.200.*.json` carry `"ruleDescription":null`, so no describer text is pinned there; the Signals filter run confirms no baseline drift (none updated).

## Results

- BFF build: 0 warnings, 0 errors.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Signals|FullyQualifiedName~OntologyAdmin|FullyQualifiedName~RuleBodyDescriber"`: **5938 passed, 0 failed, 6 skipped** (includes `SignalRoutesContractBaselineTests`; no baseline changed).
- `tests/Spaarke.ArchTests` (built explicitly, full): **2793 passed, 0 failed**.
- Client `@spaarke/ui-components`: `npx jest src/components/OntologyAdmin src/components/DecisionWizard` **536 passed / 31 suites**; `tsc --noEmit` clean; `client-lint.mjs check --package ui-components` ok (the only changed client package).
- Publish size: see the PR (fresh short-path worktrees, Compress-Archive Optimal).

## Self-review (Step 9.5)

- Code review, own diff. One F2 found and fixed before commit: folding the old column-keyed `ColumnLabels` into the (entity, column) display-name table would have turned e.g. "due date" back into `sprk_duedate` for a body written outside the BFF on a table where the column is not allow-listed. The original `ColumnLabels` entries are kept and take precedence; display names fill the rest.
- K4: when Redis is unreachable at runtime, `MetadataService` falls back to a Dataverse read on every call, so a description of a body that tests a choice value would read metadata per request in that degraded state. Deployed BFFs fail fast at startup without Redis (ADR-009); this is `MetadataService`'s existing behaviour for all its callers.
- K4: lower-casing a display name can read oddly for an acronym; no authorable column has one today ("to do due date" is the oddest).
- ADR check: ADR-009 (no new cache; `MetadataService`'s system key is already allow-listed), ADR-010 (no new registration; one constructor parameter), ADR-021 (no new styling), ADR-038 (tests in KEEP paths `tests/unit/domain`, `tests/integration/auth`), ADR-028 (metadata read app-only through the existing service; reference data, not matter data). Clean.

rework: none task 210
