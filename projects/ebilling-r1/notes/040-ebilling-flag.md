# Task 040 - eBilling flag, options and null-object fallbacks

Spec FR-45, ADR-018, ADR-032, ADR-010. Rigor STANDARD (sonnet @ high).

## What was built

| File | Change |
|---|---|
| `Configuration/EbillingOptions.cs` | New. Section `Ebilling`; `Enabled=false`; `MaxSubmissionFileSizeMb=25`, `[Range(1,100)]`. |
| `Api/Finance/EbillingEnabledFilter.cs` | New. `IEndpointFilter` reading `IOptionsMonitor<EbillingOptions>` per request; off -> `FeatureDisabledException("ebilling.disabled", ...).AsFeatureDisabled503()`; plus `RequireEbillingEnabled()` extension. |
| `Infrastructure/DI/FinanceModule.cs` | `AddOptions<EbillingOptions>().Bind().ValidateDataAnnotations().ValidateOnStart()` and the flag-off registration-rule comment (append only). |
| `appsettings.template.json` | New `Ebilling` section with `_comment` (append only; `ContentUnderstanding` from task 003 untouched). |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Finance/EbillingEnabledFilterTests.cs` | 13 tests (below). |

## Escalation trigger - checked, did not fire

Trigger: an existing capability flag already covers Finance endpoints (ADR-018 rule 1). Checked:
`FinanceEndpoints` / `FinanceRollupEndpoints` groups carry no flag filter; `FinanceOptions` has no `Enabled`;
`DocumentIntelligence:Enabled` / `Analysis:Enabled` gate only individual AI services inside the module
(`IInvoiceAnalysisService`, `IInvoiceSearchService` Null peer) - they do not gate the Finance route family, and the
existing Finance routes (classification review, rollups) must keep working with eBilling off. eBilling is a new
standalone capability, so rule 2 applies (one new flag). No ADR conflict.

## Dependency on task 041

No `IInvoiceAi` null object was needed or touched. The registration rule in `FinanceModule` cites the existing
`NullInvoiceAi` (it is the AI facade's Null peer); task 041 reshapes `PublicContracts/*` and must keep that Null peer.

## Tests (all in `EbillingEnabledFilterTests`, real in-process pipeline via TestServer)

- flag off -> 503, type `https://errors.spaarke.com/feature-disabled`, title "Feature Disabled", `errorCode` `ebilling.disabled`, handler not invoked
- no `Ebilling` section -> 503 (default off)
- flag on -> handler invoked, body exactly the handler's, no extra headers
- authorization filter chained before the flag filter wins with flag on and off (flag never replaces authorization)
- config reload flips the response without restart
- through the REAL `AddFinanceModule`: `MaxSubmissionFileSizeMb` 0 / 101 / -5 fail `host.StartAsync()` with `OptionsValidationException`; 1 / 25 / 100 boot; no section boots with `Enabled=false`, 25

## Verification

- `dotnet build` BFF: 0 errors. `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter EbillingEnabledFilterTests`: 13/13.
- Finance/DiGraph/Startup/OptionsValidation/EndpointGrouping subset: 327/327. `tests/Spaarke.ArchTests`: 1045/1045.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.
- Publish size (BFF hygiene CLAUDE.md section 10): see the completion line below.

## For the main session (outside this task's write boundary)

1. `.claude/adr/ADR-018-feature-flags.md` "Current Flags" table: add row
   `| EbillingOptions | Enabled | false | Kill switch for eBilling (invoice submission, extraction, review, AP export); 503 ebilling.disabled |`.
   The same row belongs in `docs/adr/ADR-018-feature-flags-and-kill-switches.md` if that file keeps a flag table.
2. Task 053 sets `Ebilling:Enabled=true` in dev (deploy-time app setting `Ebilling__Enabled`).
3. Tasks 044 / 048 / WP3-WP6 routes chain `RequireEbillingEnabled()` after the authorization filter; task 049's job handler
   reads `IOptionsMonitor<EbillingOptions>.CurrentValue.Enabled` itself.
4. Task 051 learns flag state from the 503 on a read route.
5. `TASK-INDEX.md` row 040 -> done (not touched by this task, per instruction).
