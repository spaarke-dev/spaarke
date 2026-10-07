# Task 100 — richer "+ New" create forms (UAT round 5 item 3, decisions B + C)

> **Task**: `tasks/100-create-record-fields.poml` · FULL · opus / high · directional · 2026-10-05
> **Record**: `notes/042-uat-round5-2026-10-05.md` §1 item 3, §3 decisions B and C. Builds on 099 (the "+ New" form
> is the only thing shown while creating — kept).

## 1. Live schema (spaarkedev1, 2026-10-05 — Dataverse MCP `describe` + `EntityDefinitions` metadata)

| Owner field | Matter | Project | Invoice |
|---|---|---|---|
| Name | `sprk_mattername` | `sprk_projectname` | `sprk_name` (primary name, ApplicationRequired) |
| Description | `sprk_matterdescription` (Memo) | `sprk_projectdescription` (Memo) | `sprk_description` (Memo, "Description") |
| Matter Type | `sprk_mattertype` → `sprk_mattertype_ref` | — | — |
| Practice Area | `sprk_practicearea` → `sprk_practicearea_ref` (name `sprk_practiceareaname`, code `sprk_practiceareacode`) | — | — |
| Project Type | — | `sprk_projecttype_ref` → `sprk_projecttype_ref` (name **`sprk_name`, no code column**) | — |
| Assigned To | `sprk_assignedtointernal` → **contact** ("Assigned To Internal") | `sprk_assignedtointernal` → **contact** | **`sprk_assignedto1` → contact ("Assigned To 1")** — the invoice's first Assigned To slot; it has no `sprk_assignedtointernal` |

- `sprk_assignedtointernal` targets **contact** on both tables, as the BFF assumed; `docs/data-model/sprk_matter-related-tables.md:23`
  ("systemuser") is stale. **No escalation trigger fired**: every listed field has a column; no schema was created.
- Live reference rows: 6 practice areas, 2 project types, all active.

## 2. API shape

**`POST /api/office/quickcreate/{type}`** — three optional request fields added (`QuickCreateRequest`), none validated as
required by the server:

| Field | Applies to | Written to | Unknown / empty |
|---|---|---|---|
| `description` (existing) | all three (Invoice newly) | see §1 | blank → not written |
| `practiceAreaId` | Matter | `sprk_practicearea` | verified to exist; unknown or unverifiable → dropped + warning (same posture as `matterTypeId`, owner decision 2026-09-11) |
| `projectTypeId` | Project | `sprk_projecttype_ref` | same |
| `assignedToContactId` | all three | Matter/Project `sprk_assignedtointernal`; Invoice `sprk_assignedto1` | absent / `Guid.Empty` = the server's default (§4) |

Ownership (I-6, BU default owner team), numbering (I-11, platform autonumber + key-collision retry), BU defaults and
field mapping are untouched; existing contract tests for them are unmodified and green.

**Reference lists — one parameterized route (CLAUDE.md §11).** Task 038's `GET /api/office/search/matter-types` became
`GET /api/office/search/{list}` with `{list}` ∈ `matter-types` | `practice-areas` | `project-types`, looked up in the
CLOSED table `OfficeSearchService.ReferenceLists` (any other name → 404, nothing read). The matter-types URL and the
wire shape (`results: [{id, name, code?}]`) are unchanged, so the deployed 1.1.1 pane keeps working against a new BFF.
`MatterTypeListResponse`/`MatterTypeOption` → `ReferenceListResponse`/`ReferenceListOption`;
`IOfficeService.GetMatterTypesAsync` → `GetReferenceListAsync(OfficeReferenceList)`. The literal `/search/entities`
keeps precedence (pinned by a test).

**`GET /api/office/quickcreate/defaults`** (new) → `{ assignedTo: { id, name, email? } | null }`: the caller's OWN linked
contact (task 141's link — never an email match), from `RecordCreationService.ResolveDefaultAssigneeAsync`, which shares
`ResolveMakerContactIdAsync` with the create — so the prefill and the server's default are one answer. Unresolved caller
→ 200 `assignedTo: null` (the create itself refuses such a caller with its own message).
§11: no existing Office route returns the caller's contact (`/api/external/me` is the external plane); the reference route
was not extended because the list is cached client-side for 24 h and is not per-user; without it the pane could not show
the prefill the owner decided (B) — it would show an empty field while the server assigns the user.

## 3. Placement (bff-extensions.md §A)

In the BFF: inline in the user's create request (latency, WP-3 write path, existing `RecordCreationService` + Office
module); nothing is background work (ADR-052 not engaged). No new service, interface, DI registration or package
(ADR-010) — `RecordCreationService` is injected into the new handler from its existing registration. No AI dependency.

## 4. Assigned To: default and clear (owner decision B)

| Type | Prefilled with | Cleared (field empty → omitted from the request) |
|---|---|---|
| Matter, Project | the user's linked contact | **the server's existing default = the maker's linked contact** (unified-access-control-r2 task 152); a field-mapping value, if a rule wrote one, is kept. No link → blank (`assigned_internal_unset` logged). The form says so when cleared: *"Left empty, it is assigned to you (Name)."* |
| Invoice | the user's linked contact | **no assignee** — invoices have no server default (none existed; adding one is not in the owner's ask) |

Precedence on Matter/Project (`ApplyAssignedInternalAsync`): request's contact → mapped value → maker. The request's
contact wins over a mapping rule because it is the user's explicit choice on the form (the pane sends no record context,
so mapping does not run from the pane today).

## 5. Access check on posted lookup ids

- **`assignedToContactId`**: `QuickCreateSourceAccessFilter` (the route's existing ADR-008 filter, already credited in
  the route census) now also requires **Read on the contact as the CALLER** (OBO `CallerRecordAccessProbe`, the shared
  entity-set table, the `read` policy key — the same mechanism `TodoSourceAccessFilter` uses for the To Do assignee). Every
  refusal is ONE constant 403 body (`OFFICE_009`, reasonCode `assignee_inaccessible`, fixed detail): unreadable,
  nonexistent and "probe threw" are indistinguishable (pinned), and nothing is created. Before the create would have
  written any posted GUID app-only.
- **`matterTypeId` / `practiceAreaId` / `projectTypeId`**: not gated per record — organization-owned reference rows the
  pane loads from the app-only reference route; each is existence-checked and an unknown one is dropped with a warning.

## 6. The pane

- **`CreateRecordForm.tsx`** (new; RelatedToPicker hosts it): Matter — Name*, Description, Matter Type*, Practice
  Area*, Assigned To Internal; Project — Name*, Project Type, Description, Assigned To Internal; Invoice — Name*,
  Description, Assigned To. One column (fits 320 px), Fluent tokens only, required markers + `aria-required`, per-field
  `role="alert"` messages on Enter with a required field missing; Create disabled until required fields are set.
  §11.5: split out because `RelatedToPicker` (≈660 lines) would otherwise own a second reason to change.
- **Assigned To = the shared `LookupField`** by exact alias `@spaarke/ui-components/lookup-field` (webpack + jest; types
  from `shared/types/spaarke-lookup-field.d.ts`, the `send-email-pane` mechanism — the shared source does not pass this
  package's stricter `tsc`), never the barrel. Its search is the pane's ONE contact search (`App.handleSearchContacts`,
  shared with the To Do and Email tabs), threaded App → SaveView → SaveFlow → RelatedToPicker. Queries under 2
  characters (LookupField's browse icon) are answered locally rather than sent to a route that 400s on them. Results show
  "Name (email)" to tell two contacts with one name apart (task 091).
- **Data** — `useCreateRecordFormData` (new hook): the three lists + the prefill, loaded **lazily on the first "+ New"**,
  then kept. `referenceListService.ts` replaces `matterTypeLookupService.ts` (one loader, one cache per list; the
  matter-types cache key is unchanged so cached lists survive; a "… not found" warning clears that list's cache). A
  failed list shows its message + Retry and is announced; a failed prefill is just an empty optional field.
- **POST body** (`SaveFlow.createRelatedRecord`): only the fields the form set, every GUID through the shared `cleanGuid`.
- 099's behaviour kept: while the form is open only the pills + form show (`onCreatingChange`).

## 7. Tests

| Where | What |
|---|---|
| `tests/integration/contract/Api/Office/OfficeQuickCreateFieldsContractTests.cs` (new, 15) | every field to its live column for all three types; unknown practice area → warning, no lookup; cleared assignee → maker default, no probe; invoice without assignee → unassigned; matter-only field ignored on a project; the contact gate per type (403, nothing created); unreadable ≡ nonexistent ≡ probe-threw bodies; `/quickcreate/defaults` (linked, no link, unresolved, 401) |
| `OfficeMatterTypeLookupContractTests.cs` (+6) | each list reads its own table/columns, active only; unoffered names 404 with no read; `/search/entities` still wins |
| `tests/unit/.../Services/Office/OfficeReferenceListMappingTests.cs` (renamed from `OfficeMatterTypeMappingTests`, +5) | per-list column mapping, project types have no code, closed table |
| `RecordCreationAssignedInternalTests.cs` (+5) | requested contact beats mapping and maker; empty → maker; prefill == the contact the create assigns; unreadable contact → no prefill |
| `Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` | waiver `GET /api/office/search/matter-types` → `/search/{list}`; new Permanent waiver `GET /api/office/quickcreate/defaults` (caller's own identity, takes no id) |
| `CreateRecordForm.test.tsx` (new, gated) | fields + order per type; LookupField resolves to the shared source; prefill, clear (+ note), pick another, late prefill fills / never overwrites a cleared field; server error shown; Cancel |
| `RelatedToPicker.matterType.test.tsx`, `SaveFlow.matterTypeQuickCreate.test.tsx`, `RelatedToPicker.errorSurfacing.test.tsx`, `referenceListService.cache.test.ts` (renamed with its service) | rewritten for the new contract: both Matter fields required; ids sent; per-list Retry; Invoice prefill → body (end to end) |

**Seeded faults** (each restored byte-identical): server — assignee gate disabled → **5 failed** (3 per-type 403s, the
every-field Matter probe assertion, indistinguishability); client — Practice Area not required → **1 failed**.

## 8. Gates

| Gate | Result |
|---|---|
| BFF build | **0 warnings / 0 errors** |
| BFF Office / quick-create / record-creation / ownership / idempotency scopes | **625 passed / 0 failed / 8 skipped** (633), final run after every edit |
| ArchTests | **349 / 349** |
| office-addins jest (all suites) | **77 / 77 suites, 1075 tests** (was 76 / 1057) |
| `npm run lint` | **0** (`--max-warnings 0`) |
| `npx tsc --noEmit --skipLibCheck` | **68** = baseline, all in test/mock files; **0 production** |
| Publish (fresh short-path worktrees, `Compress-Archive` Optimal, incl. PDBs) | master `origin/master` `b4b58a361` **47,869,190 B (45.652 MB)** → branch `HEAD f585023b6` + this task's 10 server files **47,884,824 B (45.667 MB)** = **+15,634 B**, **212 = 212** files. Note: HEAD predates master's newest server commits (e.g. `SpeAdminTokenProvider` −371 lines), so part of the +15 KB is master drift, not this task. Both worktrees removed |
| CVE | `dotnet list package --vulnerable --include-transitive`: **none**; no package or project file changed |

## 9. Quality gates (Step 9.5)

**Code review — 0 Critical.** Fixed in review: the contact search would have been called with an empty query by
LookupField's browse icon (a guaranteed 400) — now answered locally. `getAccessToken` is an inline arrow in `App`, so a
cleanup-scoped cancellation would have discarded the one prefill request — the hook guards by mount instead.
Accepted / noted (not fixed):
- S1: a field-mapping rule can still write a wrong-typed value into `sprk_practicearea`/`sprk_projecttype_ref` (only
  `sprk_mattertype` has a revert guard). Pre-existing for every mapped lookup; the pane sends no record context.
- S2: the reference existence check does not test `statecode` (an inactive row could be set by a crafted request) —
  same as the matter type since task 038; the pane only offers active rows.
- S3: the unknown-list 404 carries `errorCode OFFICE_VALIDATION` (the taxonomy has no generic not-found code).
- S4: a prefill arriving after the user typed (but did not pick) in Assigned To replaces the typed text.

**ADR check — 0 violations.** ADR-001/008 (Minimal API; the contact gate lives in the route's existing filter; two
read-only routes waived with stated reasons); ADR-010 (no registration); ADR-002 / write-path WP-1..3 (BFF write, I-6 +
I-11 unchanged, no plugin); ADR-012 as amended (exact alias, no barrel); ADR-013 (no AI); ADR-019 (Office problem
shape, `OFFICE_009`); ADR-021 (tokens only); ADR-028 (OBO probe as the caller; no secret); ADR-038 (contract + unit KEEP
paths, client `__tests__`, no banned patterns); ADR-044 (`Guid`-typed server, `cleanGuid` client). UAC-r2 boundaries:
`tests/integration/auth/UnifiedAccessControl/*` and `SecureBuRoleDepthAssertion*` untouched;
`CommunicationsEndpoints.cs` untouched.

## 10. Deviations

- The create form is a new component (`CreateRecordForm.tsx`) rather than more code in `RelatedToPicker.tsx` (§11.5).
- `matterTypeLookupService.ts` (+ its gated cache test) renamed to `referenceListService.ts` (+ test) — the gated-suite
  list is updated in the same change, every case kept.
- Lists load on the first "+ New", not on pane mount (matter types used to load on mount).
- Invoice "Assigned To" = `sprk_assignedto1` ("Assigned To 1"): sprk_invoice has no single "Assigned To" column.

## 11. Open — live (needs a BFF + add-in deploy)

Create one Matter, Project and Invoice from the pane with every field; open each in Spaarke and see the values; clear
Assigned To on a Matter (→ you) and on an Invoice (→ empty); pick a contact you cannot read (→ the 403 message).
`docs/data-model/sprk_matter-related-tables.md:23` still says systemuser — a doc fix for the main session.
