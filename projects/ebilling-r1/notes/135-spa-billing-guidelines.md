# Task 135: SPA Billing Guidelines view (FR-49, D-66)

Scope: a firm user's read-only view of the published billing rules in force for their firm(s): `BillingGuidelinesService`, `GET /api/v1/external/ebilling/billing-guidelines`, the `BillingGuidelinesWidget` and its client, one registry descriptor, tests, the ArchTest ledger entries. Rigor STANDARD, raised to FULL (a BFF endpoint is added).

## Route and widget

- Route: `GET /api/v1/external/ebilling/billing-guidelines`, optional `?coreRecordId={guid}` for the per-record view. Mounted on the shared external collaboration group in `Api/ExternalAccess/BillingGuidelinesEndpoints.cs` (the file sits beside `FirmOutcomeEndpoints.cs`: the ArchTest ledgers and the invoice-surface guard are keyed on that namespace; a first version in `Api/Finance/` failed the `/api/finance` step-0 guard and was moved).
- Chain: group `RequireAuthorization(ExternalCollaboration)` + caller-principal filter, then `.AddBillingGuidelinesAuthorizationFilter()`, `.ExemptFromWorkforceCeiling(...)`, `.RequireEbillingEnabled()` (flag after authorization, ADR-018).
- Widget id: `billing-guidelines` (`planes: ['ciam']`, no `requiredEntitlement`, `defaultForRoles: []`: openable from the library, not a default tab). Files: `src/widgets/BillingGuidelinesWidget.tsx`, `src/api/billing-guidelines-client.ts`, descriptor in `src/registry/widgetRegistry.ts`. No new npm dependency.

## Placement justification (CLAUDE.md §10, §11)

1. **Existing** — it overlaps `RecordProcedureReadService` (the internal per-record Procedure read, 084) and `FirmFindingsProjection` (the firm-visible outcome, 130). It reuses 082's `ProcedureResolver`/`ProcedureResolution.RulesOn`, 083's `RuleSetClauseVisibility.ForFirm` and `FirmFacingLabelOf` (extracted from `RuleSetService.FirmFacingNameOfAsync`, one definition), `RuleBodyDescriber.DescribeAsync`, 130's firm read (`ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync`) and the SPA header's firm-name read (`InvoiceSubmissionService.ReadActiveFirmNamesAsync`), and `IAccessibleRecordSetService.ComposeForCiamContactAsync` + `AccessibleRecordSet.Contains` for the per-record view.
2. **Extension** — `RecordProcedureReadService` is internal-facing: it returns assignment reasons, Procedure ids and names, override history and write permissions, none of which a firm may see, and its caller gate is a workforce rights probe. A firm-safe allow-list DTO and a CIAM firm gate are a different contract, so a separate small service reusing the shared reads is smaller than adding a firm mode to it.
3. **Cost of doing nothing** — FR-49: an outside-counsel user has no way to see which billing rules apply to their firm; the SPA has no read of the rules at all.

BFF hygiene (§10): placement = `Api/ExternalAccess` route + `Services/Finance` service, DI in `FinanceModule` (one scoped registration). No AI facade, no background work, no new package.

## What the view shows (and does not)

- A rule set is listed only when its rule set is Published (or Retired, as 083), the Procedure/record rule is in force today (the resolver), and `visible to firms` is true. The flag decision is `RuleSetClauseVisibility.IsVisibleToFirms`, the same one `ForFirm` uses (extracted, not copied).
- The DTO is an allow-list built field by field: set (firm name, name, scope label, version, dates, rules), rule (name, description-available, description, parameters, clause quote, section reference, in-force-from), parameter (name, value, unit). No document link, container id, drive item id, Procedure id, record id, override reason or internal Procedure name when a firm-facing name is set.
- A rule the describer refuses is listed with its name and clause and `descriptionAvailable: false`: no description, no parameters, the refusal text is never shown (ids are logged).
- A record rule shows under a neutral label (`Record-specific rule`); a rule from a parent shows `From the parent record` and never names the parent.
- Parameters come from the rule body's scalar and list members with units from a closed table (`BillingGuidelinesService.ParameterTable`); `BillingGuidelinesServiceTests.EveryBillingSchemaMember_HasATableEntry` reads every billing schema and fails when a member is in neither the table nor `NotShown`.

## Owner question

A caller who belongs to several firms sees every firm's sets, each labelled with its firm (the default the brief named). Alternative: pick one firm (a selector) per session. Not decided here.

## Deviations

1. Workforce ceiling: the route is declared `ExemptFromWorkforceCeiling`, because its own filter refuses every workforce principal with the stable 403 before any read. The ceiling gate has no table list to declare for a route no workforce principal can reach.
2. The per-record check: the POML names `ComposeForCiamContactAsync` + `Contains` but not the table; the filter composes the set for the three core tables (matter, project, work assignment) and the first that contains the id decides the kind. Up to three composes per record request.
3. Endpoint file placed in `Api/ExternalAccess` (see above), not `Api/Finance`.

## Known limits

- K1: legacy Procedure-less rule sets are not shown (the resolver only returns Procedure and record rules).
- K2: the widget is not hidden from the widget library when the flag is off; only its body renders nothing (503 `ebilling.disabled`). Hiding the card needs a flag in `/me`, which is outside this task.
- K3: one resolver read per firm (N firms = N reads); callers have few firms.
- K4: the parameter table is closed; a new rule-body member needs a table row (ratchet test).
- K5: the unit gate has no ui-test run (the brief excluded it): dark mode is by construction (Fluent v9 tokens only, no hex), 360 px is covered by a style-guard unit test, not a browser check.

## Tests

- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/BillingGuidelinesServiceTests.cs` (17): shows, other firm, flag off or unset, Draft/not in force/Retired, assigned-only, parent label, record rule label, refusal marker, allow-list members, several firms, parameter table, parameter shaping.
- `tests/integration/auth/Finance/BillingGuidelinesScopeTests.cs` (16): 200, 401, 403 (workforce, systemuser, no firm: same body), 503 (memberships, compose fault), 503 `ebilling.disabled`, per-record 200 and the uniform 404.
- `src/client/external-spa/tests/BillingGuidelinesWidget.test.tsx` (13): grouping, firm labels, marker, empty, hidden when disabled, outage with Retry, 403/404, 360 px, descriptor.
- ArchTests ledgers: `RouteAuthorizationGuardTests.Ledger.cs` (GovernedFile, CreditedForm, file count 154 to 155), `AuthenticationSchemeCensusTests.cs` (pinned route), `ExternalInvoiceVisibilityGuardTests.cs` (NotInvoiceSurfaces: reads firm names and rules, no invoice-family row).

### Fail-before (neutralised, then restored)

| Guarded behaviour neutralised | Test that failed |
|---|---|
| the `visible to firms` check in the service | "a rule set with 'visible to firms' off (or never set) never appears" |
| the firm passed to the resolver (first firm for all) | "a caller of several firms sees each firm's sets" |
| the refused rule drops its parameters | "a rule the describer refuses is listed ... with no parameters" |
| the CIAM-plane/systemuser check, the no-firm check and the record `Contains` in the filter | workforce, systemuser, no-firm, kill-switch-with-workforce and the per-record 404 cases (8 failed) |
| the widget's disabled branch | "is hidden when the eBilling flag is off" |

The service's own firm filter is belt and braces beside the resolver's firm condition; neutralising it alone is not observable because the resolver already excludes another firm's Procedure (that exclusion is 082's, tested there).

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | 0 warnings, 0 errors |
| Unit (`BillingGuidelines\|RuleSet\|ProcedureResolver\|ExternalInvoice\|FirmFindings`) | 1473 passed, 0 failed (includes the 17 service tests and the 16 auth tests) |
| ArchTests (`Route\|External\|Finance\|Redaction\|Census`) | 680 passed, 0 failed (8 failed before the ledger entries and the file move) |
| external-spa `npm test` | 61 files, 1728 tests passed |
| external-spa `npm run typecheck`, `npm run lint`, `npm run build` | clean, clean, built |

### Publish size and CVEs (§10, NFR-01)

Command `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o out` from two scratch trees at short paths; framework-dependent linux-x64, Release; compression `Compress-Archive -CompressionLevel Optimal` over `out/*`; PDBs included.

| Tree | Compressed |
|---|---|
| Base (`29c0739f3a`, the commit this branch started from) | 40.428 MB (42,391,404 bytes) |
| This branch | 40.442 MB (42,406,317 bytes) |
| Delta | +0.014 MB (+14,913 bytes) |

Both are far below the 60 MB ceiling and the +5 MB trigger. The base is the branch's own starting commit (master plus the ebilling work already merged), so the delta isolates this task; no recorded baseline was used. No package was added: `dotnet list package --vulnerable --include-transitive` reports none vulnerable.

BFF Hygiene §10 + NFR-01 verified: delta +0.014 MB, no new HIGH CVE.
