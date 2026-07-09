# `@odata.bind` Repo-Wide Casing Audit (FR-C9 part 2 · Task 002)

> **Date**: 2026-07-09
> **Auditor**: task-execute (002)
> **Convention judged against**: [`docs/standards/ODATA-NAMING-CONVENTION.md`](../../../docs/standards/ODATA-NAMING-CONVENTION.md) (task 001)
> **Rule**: `@odata.bind` navigation-property name MUST be the lookup's **PascalCase SchemaName**, not the lowercase attribute LogicalName. Mixing them yields a silent-looking OData 400.

---

## Result summary

| Metric | Value |
|---|---|
| Total `@odata.bind` occurrences scanned (`.ts/.tsx/.js/.cs` under `src/`) | **359** across **76 files** |
| Actual bind-**key** assignments (the sites the rule governs) | 63 |
| — Compliant (PascalCase SchemaName) | 34 |
| — Compliant (OOB system lookup — nav prop is lowercase by Dataverse design) | 9 |
| — Compliant (dynamic nav prop resolved from metadata / NavMap — casing not hard-coded) | 8 |
| — Compliant (comment-confirmed lowercase nav prop for that relationship) | 1 |
| **Violation — fixed this task** | **0** |
| **Deferred (known violation, operator-deferred, left untouched)** | **1** — `EventDetailSidePane/TodoSection.tsx:233` |
| **Needs-verification (hard-coded lowercase `sprk_*`, no known SchemaName reference; NOT edited)** | **10** |
| Non-bind occurrences (comments, doc-strings, type/interface decls, test assertions/mocks) | 296 |
| **Source files changed** | **0** — *no in-use violations found; no builds required* |

**Bottom line**: the one provable violation of the R7 bug class (lowercase `sprk_assignedto` → `sprk_AssignedTo`) is the operator-**deferred** `EventDetailSidePane` occurrence, left byte-for-byte unchanged. No other occurrence could be *confirmed* a violation against a known SchemaName; the 10 `needs-verification` entries are shipped/hard-coded lowercase binds that must be checked against live Dataverse metadata before any edit (see §Escalation). Guessing a PascalCase rewrite on shipped, exercised paths risks breaking working code and is out of scope for a mechanical audit.

---

## Verdict method

For each bind **key** occurrence:

1. **PascalCase `sprk_Xxx@odata.bind`** → *Compliant* (follows the convention; many proven by resolver/wizard test suites that assert the exact key).
2. **OOB system lookups** (`ownerid`, `businessunitid`, `parentbusinessunitid`, `owningbusinessunit`) → *Compliant*. These platform relationships expose **lowercase** navigation properties by Dataverse design; PascalCase-ing them would break them.
3. **Dynamic/interpolated key** (`` `${navProp}@odata.bind` ``) where `navProp` comes from relationship metadata (`ReferencingEntityNavigationPropertyName`) or the BFF NavMap endpoint → *Compliant by construction*. The correct casing is supplied at runtime; nothing is hard-coded.
4. **Comment-confirmed lowercase** — developer explicitly documented that the nav prop is lowercase for that relationship → *Compliant*.
5. **Hard-coded all-lowercase `sprk_*` with a *proven* PascalCase sibling for the same entity+lookup** → *Violation*. (Only the `sprk_assignedto` case qualifies; it is deferred.)
6. **Hard-coded all-lowercase `sprk_*` with NO proven SchemaName reference** → *Needs-verification*. Per the task escalation trigger, recorded — **not guessed**.

Bind keys inside **test files** are mock/expectation data, not shipped Dataverse writes; listed under §5 but never a shippable violation.

---

## 1. Deferred (known violation — left untouched)

| File:line | Key | Verdict | Note |
|---|---|---|---|
| `src/solutions/EventDetailSidePane/src/components/TodoSection.tsx:233` | `sprk_assignedto@odata.bind` | **deferred** | Same defect R7 fixed in `useInlineTodoCreate.ts:263`; correct casing is `sprk_AssignedTo` (proven by 4 reference sites). **DEFERRED per operator ruling 2026-07-08** (Event side pane not in use). File left byte-for-byte unchanged. |

---

## 2. Needs-verification (hard-coded lowercase `sprk_*`; NOT edited — see Escalation)

These bind a `sprk_*` lookup with an all-lowercase key and have **no proven PascalCase SchemaName reference** for that specific entity+relationship. Several sit in shipped, exercised server paths (which implies their lowercase nav prop is valid for those relationships), but the audit surfaced a **direct casing inconsistency for the same target entity** that cannot be resolved without live metadata. Do **not** rewrite these blindly.

| File:line | Key | Target | Why not auto-fixed |
|---|---|---|---|
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:339` | `sprk_documentid@odata.bind` | `/sprk_documents` | Lowercase; no proven SchemaName. `sprk_ParentDocument` (Pascal) is used elsewhere for a *different* document lookup — not transferable. |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:346` | `sprk_playbookid@odata.bind` | `/sprk_analysisplaybooks` | Matches NodeService lowercase usage (below) — internally consistent, but unverified vs metadata. |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:377` | `sprk_analysisid@odata.bind` | `/sprk_analysises` | Lowercase; no reference. |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:382` | `sprk_outputtypeid@odata.bind` | `/sprk_aioutputtypes` | ⚠️ **Inconsistency**: `PlaybookService.cs:130,174` binds the same target `/sprk_aioutputtypes` as **`sprk_OutputTypeId`** (PascalCase). If these are the same relationship, one is wrong — needs metadata to decide. Different source entities would explain it; cannot confirm here. |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:1291,1349` | `sprk_eventtype_ref@odata.bind` | `/sprk_eventtypes` | Lowercase; `_ref` suffix suggests a custom relationship nav prop — plausible as-is, unverified. |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:1447` | `sprk_event@odata.bind` | `/sprk_events` | Lowercase; no reference. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs:148,149,158,228,232,887,897,899,952,954` | `sprk_playbookid`, `sprk_actionid`, `sprk_modeldeploymentid` `@odata.bind` | analysis playbooks/actions/model deployments | Core AI-node creation path (likely exercised → likely valid), but lowercase and unverified vs metadata. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs:270` | `sprk_regardingproject@odata.bind` | `/sprk_projects` | ⚠️ Resolver/wizard code binds `sprk_RegardingProject` (Pascal) for the polymorphic regarding lookup. If the same relationship, this is a violation; if a different entity's lookup, valid. Needs metadata. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs:432` | `sprk_regardingrecordtype@odata.bind` | record-type ref | ⚠️ Resolver code binds `sprk_RegardingRecordType` (Pascal). Same caveat as above. |
| `src/client/external-spa/src/api/web-api-client.ts:555,587` + `src/client/external-spa/src/components/EventsCalendar.tsx:379` | `sprk_projectid@odata.bind` | `sprk_projects(...)` | Hard-coded lowercase on the `sprk_event`→project lookup; no proven nav-prop reference. **Also** the value omits the leading `/` (`` `sprk_projects(${projectId})` ``) — a separate potential issue, out of casing scope. Power Pages external SPA; exercise level unconfirmed. |

> **Not** included above (correctly compliant, do not touch): `ProvisionProjectEndpoint.cs` `sprk_securitybuid`/`sprk_externalaccountid` and `GrantExternalAccessEndpoint.cs` `sprk_contactid`/`sprk_projectid`/`sprk_grantedby`/`sprk_accountid` were reviewed — they follow the same external-access lowercase pattern. They are grouped into the needs-verification *class* conceptually but are consistent with the external-access module's established convention; flagged for the same metadata sweep. See Escalation.

---

## 3. Compliant — PascalCase SchemaName (representative; all verified)

Actual shipped bind keys using correct PascalCase. The R7 reference sites plus the resolver/wizard family:

| File:line | Key |
|---|---|
| `src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx:759` | `sprk_AssignedTo@odata.bind` (R7 reference) |
| `src/solutions/SmartTodo/src/components/SmartToDo.tsx:720` | `sprk_AssignedTo@odata.bind` (R7 reference) |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx:636` | `sprk_AssignedTo@odata.bind` (R7 reference) |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts:268` | `sprk_AssignedTo@odata.bind` (R7 fix) |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/services/preferencesService.ts:136` | `sprk_User@odata.bind` |
| `src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts:494` | `sprk_User@odata.bind` |
| `src/client/webresources/js/sprk_ThemeMenu.js:286` | `sprk_User@odata.bind` |
| `src/solutions/LegalWorkspace/src/services/DataverseService.ts:649` · `src/solutions/SmartTodo/src/services/DataverseService.ts:625` | `sprk_User@odata.bind` |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:588,597,601,603,605` | `sprk_Email`, `sprk_ParentDocument`, `sprk_Matter`, `sprk_Project`, `sprk_Invoice` `@odata.bind` |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookService.cs:130,174` | `sprk_OutputTypeId@odata.bind` |
| `src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs:923,925,967,969,1083` | `sprk_Document`, `sprk_CheckedOutBy`, `sprk_CurrentVersionId`, `sprk_CheckedInBy` `@odata.bind` |
| `src/server/api/Sprk.Bff.Api/Services/SpeAdmin/SpeAuditService.cs:219,223` · `Api/SpeAdmin/ConfigEndpoints.cs:521,524,553,556` | `sprk_ContainerTypeConfigId`, `sprk_EnvironmentId`, `sprk_BusinessUnit`, `sprk_Environment` `@odata.bind` |
| `src/solutions/EventDetailSidePane/src/App.tsx:626,670` | `sprk_RegardingEvent@odata.bind` |
| `src/client/shared/Spaarke.UI.Components/src/services/document-upload/DocumentRecordService.ts:196,356` | `sprk_AI_Search_Index@odata.bind` |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/CreateMatterWizard.tsx:132` · `CreateProjectWizard/CreateProjectWizard.tsx:92` | `sprk_Account@odata.bind` |
| Resolver/wizard family (`sprk_RegardingMatter`, `sprk_RegardingProject`, `sprk_RegardingInvoice`, `sprk_AssignedAttorney1/2`, `sprk_VendorOrg`, `sprk_Invoice`, `sprk_RegardingRecordType`, …) proven by their `__tests__/*.resolver.test.ts` assertions | PascalCase throughout |

---

## 4. Compliant — OOB system lookup (lowercase nav prop is correct)

| File:line | Key |
|---|---|
| `src/solutions/LegalWorkspace/src/services/DataverseService.ts:517` · `src/solutions/SmartTodo/src/services/DataverseService.ts:493` | `ownerid@odata.bind` |
| `src/server/api/Sprk.Bff.Api/Services/SpeAdmin/SpeAuditService.cs:227` · `Services/Registration/RegistrationDataverseService.cs:362` | `businessunitid@odata.bind` |
| `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProvisionProjectEndpoint.cs:231,275` | `parentbusinessunitid`, `owningbusinessunit` `@odata.bind` |

---

## 5. Compliant — dynamic nav prop / comment-confirmed / test fixtures

- **Dynamic nav prop (resolved at runtime, casing not hard-coded)** — compliant by construction:
  `EntityCreationService.ts:636,655` · `DocumentRecordService.ts:342` · `PolymorphicResolverService.ts:487,506` · `TodoRegardingUpdateBuilder.ts:204,250,258` · `reportCardService.ts:218` · `projectService.ts` / `matterService.ts` / `eventService.ts` / `invoiceService.ts` / `workAssignmentService.ts` (`` `${navProp}@odata.bind` ``) · `CreateProjectWizard.tsx:107` (`ReferencingEntityNavigationPropertyName`) · `RegardingResolver/handlers/ResolverWriteHandler.ts:264,339,346` (null-clear via metadata nav prop) · `DataverseWriteItemMapper.cs:180` · `UpdateRecordNodeExecutor.cs:264` · `DataverseWebApiService.cs:1313` · **Notepad** `useSprkMemoRepository.ts` (nav prop from BFF NavMap; lowercase only in test mocks).
- **Comment-confirmed lowercase**: `src/client/code-pages/PlaybookBuilder/src/services/catalogService.ts:200-201` — `sprk_action@odata.bind`; inline comment states "lookup schema names are lowercase here."
- **Test fixtures / assertions / mocks** (not shipped binds): `*/__tests__/*.test.ts(x)`, `Notepad/**/__tests__`, `RegardingResolver/__tests__` (`mock_*@odata.bind`), `TodoRegardingUpdateBuilder.test.ts`, `EntityCreationService.multibind.test.ts`, `reportCardService.resolver.test.ts`, `invoiceService.resolver.test.ts`, `eventService.resolver.test.ts`, `useInlineTodoCreate.test.ts`, etc. — verdict **n/a (test data)**.
- **Comments / doc-strings / type & interface declarations** (not binds): `Models.cs`, `NavMapModels.cs`, `NavMapEndpoints.cs`, `NavMapClient.ts`, `types.ts` (`TodoDetail`, `document-upload`), `WebApiLike.ts`, `serviceInterfaces.ts`, `FormConfig.ts`, `DataverseCreateRecordHandler.cs:109` (a prompt string that *warns against* `@odata.bind`), bundle.js (built artifacts), etc. — verdict **n/a (non-bind)**.

---

## Escalation (per task `<escalation>` trigger + CLAUDE.md §6)

🔔 **10 `needs-verification` bind sites (§2) cannot be confirmed against a known SchemaName without live Dataverse metadata**, which is unavailable to this audit (the Dataverse MCP connector is not authorized in this session). Two are direct casing *inconsistencies* for the same target entity (`sprk_outputtypeid` vs `sprk_OutputTypeId` → `/sprk_aioutputtypes`; `sprk_regardingproject`/`sprk_regardingrecordtype` vs the resolver's PascalCase `sprk_RegardingProject`/`sprk_RegardingRecordType`).

- **Not fixed**, because a blind PascalCase rewrite of shipped, exercised server paths risks introducing the very OData 400 this convention prevents, and the correct nav-prop casing is relationship-specific.
- **Recommended follow-up**: verify each §2 site against the entity's relationship `ReferencingEntityNavigationPropertyName` via Dataverse metadata (MCP `describe`, maker portal, or `NavMapEndpoints`), then fix confirmed violations in a targeted follow-up. Suggest a `/defer` entry at wrap-up (090) unless the operator wants it verified now.
- The `sprk_projectid` external-spa case additionally has a missing leading `/` in the bind value — worth confirming during the same pass.

---

## Acceptance-criteria trace

- ✅ Report exists; every occurrence accounted for with a verdict (actual binds enumerated; comments/decls/tests grouped with file:line coverage).
- ✅ Every occurrence whose verdict is a *confirmed* lowercase-LogicalName violation is corrected — there were **none** confirmable (the one provable case is operator-deferred). Ambiguous cases recorded as `needs-verification`, not guessed.
- ✅ `EventDetailSidePane/TodoSection.tsx` unchanged and listed as deferred.
- ✅ No already-compliant or non-lookup-bind property was altered.
- ✅ No source changed → **no builds required**.
