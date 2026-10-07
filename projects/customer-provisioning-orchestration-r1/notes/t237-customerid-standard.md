# T237 — customerId standard at intake: record

> **Task**: `tasks/237-customerid-standard-at-intake.poml` (owner D10 adopting unified-access-control-r2 D-14;
> `INCOMING-CUSTOMERID-STANDARD.md` §3.1–§3.4). **Date**: 2026-09-30, SESSION 27.

## 1. The rule and where it is now enforced

`^[a-z][a-z0-9]{2,7}$` — 3–8 lowercase letters and digits, starting with a letter
(`docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` § "The customerId standard").

| Enforcement point | How | Before T237 |
|---|---|---|
| `POST /api/runs` | `CustomerIdStandard.IsValid` → 400 before the registry lookup, guard, Cosmos write, enqueue | blank check only |
| `intake.schema.json` (batch) | `pattern` = the standard; parity test pins it to `CustomerIdStandard.Pattern` | kebab-case 3–32 |
| `/provision-environment` Step 1a | `-cnotmatch '^[a-z][a-z0-9]{2,7}\z'`; re-prompt (interactive) / hard stop (batch) | prose only, no check |
| Operator scripts `-CustomerId` | `ValidatePattern(…, Options = 'None')` in Provision-Customer, Decommission-Customer, Deploy-Release, Initialize-ReportingCustomer, tests/bicep-e2e-dry-run | `{3,10}` / kebab variants |
| Registry column `sprk_customerid` | MaxLength 8 (the only rule a text column can enforce) | 64 |
| BFF runtime | `CustomerIdResolver.CustomerIdPattern` (unchanged; already the standard) | — |

**Reserved ids** (code-review W3): `platform`, `shared`, `byok` match the pattern but name non-customer resource
groups (`rg-spaarke-platform-{env}` hosts the BFF + L2). Intake now refuses them — `CustomerIdStandard.IsReserved`
→ 400 at `POST /api/runs`, `"not": {"enum": …}` in the schema, the skill's Step 1a loop — matching the BFF's existing
`CustomerIdResolver.NonCustomerResourceGroupSegments`; `Decommission-Customer.ps1`'s deny list gained the
`shared`/`byok` groups; the naming doc records the reservation. (The operator scripts' `ValidatePattern` does not
check reserved ids; `Provision-Customer.ps1` carries a deprecation banner.)

Two .NET/PowerShell traps closed: `$` matches before a final `\n` in .NET (so `IsValid` also requires the match to
cover the whole value; the skill uses `\z`), and PowerShell `ValidatePattern` / `-match` are case-INsensitive by
default (`Options = 'None'`, `-cmatch` — `ACME` was accepted without it, verified).

## 2. Abbreviation recorded once (§3.3)

Intake now carries an optional `displayName` (schema + skill Step 1a-bis). The Step 1f placeholder create writes
`sprk_name = displayName` (default customerId) next to `sprk_customerid`. Nothing in code derives an id from a name
(sweep 2026-09-30). Consequence handled: `Deploy-Release.ps1` used to find the customer row by `sprk_name`; it now
looks up `sprk_customerid` first and falls back to `sprk_name` / `sprk_envaccountdomain` only among rows with
`sprk_customerid eq null` (platform rows such as Dev) — so an unregistered id equal to some customer's display name
cannot resolve to that customer's environment (adr-check W3).

## 3. Live changes — spaarkedev1 (owner-approved 2026-09-30, operator identity ralph.schroeder@spaarke.com)

| # | Change | Before | After (read back) |
|---|---|---|---|
| a | Pre-check: rows with a non-empty `sprk_customerid` | none | none (escalation trigger 2 did not fire) |
| b | `sprk_customerid` MaxLength via `scripts/Add-CustomerIdColumn.ps1` | 64 | **8** (DatabaseLength 128 is the physical width; MaxLength is enforced); description updated; alt key `sprk_customerid_key` untouched — the decrease was accepted (trigger 1 did not fire) |
| c | Stale placeholder `trial-2026-08-18` (id `87d7b4a7-399b-f111-b8de-7ced8ddc4a05`, created 2026-08-18, no URL/type/customerId) | Active (0/1) | **Inactive (1/2)** — not deleted |
| d | `sprk_tenancymodel` labels (G22) via `UpdateOptionValue` + `PublishXml` | 0=Model1Shared, 1=Model2Dedicated | **0=Model1, 1=Model2** (descriptions from `Extend-DataverseEnvironmentSchema-v3.3.ps1`); no consumer reads the label text (BFF reads the integer) — trigger 3 did not fire |

Script fix found on the way: `Add-CustomerIdColumn.ps1` logged "8 -> 8" because `$attr` and `$current.Data` are the
same object; it now captures the original MaxLength first.

**`spaarke-demo` (read-only check)**: the registry columns `sprk_customerid` and `sprk_tenancymodel` do not exist
there (404), although the skill's Step 1f names `spaarke-demo` as the registry for `environment=demo`. A demo-env run
would fail at the placeholder create. Not changed. **Owner 2026-09-30: address later** → plan G23.

## 4. Fixtures

~45 test files across `Sprk.Provisioning.ControlPlane.Tests`, `tests/integration` (LoadTests, BFF integration),
`tests/unit/Sprk.Bff.Api.Tests/Onboarding` and `tests/scripts` (Pester). Old → new: `trial-2026-08-18`→`trial18`,
`test-customer`→`testcust`, `acme-corp`→`acme`, `other-customer`→`other`, `cust-1`→`cust1`, `customer-01`→`cust01`,
generated smoke/seam ids → a letter + 7 lowercase hex, etc. Mismatch tests keep two DIFFERENT compliant ids
(`acme`/`beta`, `acme`/`diffcust`, `custa`/`custb`, REG-07 `testcust`/`other`). Deliberate negatives kept:
`CustomerIdResolverTests` (BFF), blank inputs, `?customerId=x` GET query, the new hyphen/underscore cases. Not
customerIds, left: `customer-acme-1` (ARM deployment name), resource names. Extra sweep hits fixed:
`CosmosSmokeTests` `other-{guid}`, `EnqueueLatencyScenario` `warmup-{i}` / `customer-load-{i}` (POST bodies — would
now 400).

Pre-existing break fixed: `tests/integration/Sprk.Provisioning.ControlPlane.LoadTests` did not compile since
2026-08-28 (`StaticActiveRunScanner` lacked `IActiveRunScanner.QueryStaleTerminalRunsAsync`, added by `f5ef16231d`).

## 5. Quality gates (Step 9.5)

- **adr-check**: 0 violations, 8 warnings. Fixed: W2 (REG-07 assertion now checks the parsed detail
  `belongs to customer 'other'`), W3 (Deploy-Release fallback limited to `sprk_customerid eq null`; both lookups
  also `statecode eq 0`), W5 (comments no longer claim both suites pin identical cases), W7 (`pac` fallback guards
  `;`/`=` in the display name; schema `displayName` requires a non-blank character). Recorded: W1, W4 (below), W6
  (tests live in the project's existing test project, outside the ADR-038 KEEP paths — project convention), W8
  (`.husky/_/*` phantom changes left out of the commit).
- **code-review**: 0 Critical, 5 warnings, 11 suggestions. Fixed: W1 (script help text), W2 (as above), W3
  (reserved ids — §1), S1, S2 (rejection test asserts the echoed value, parsed from ProblemDetails `detail` because
  the JSON encoder escapes `'`), S5, S6 (interactive Step 1a prompts first instead of reporting an empty id),
  S7 (as above), S9 (six Core `<param>` docs said "3-10 lowercase alphanumeric"; two envelope examples used
  `acme-corp`). Recorded: W4, W5, S3, S4, S8, S11 (below).
- **Acceptance criterion 3 — met after a follow-up fix (2026-09-30, owner: "do not defer")**: the plain
  `npx ajv-cli@5 compile --spec=draft2020` failed on `format: uuid` (pre-existing, also on `HEAD` before T237). The
  `provisioning-prereqs-validate` workflow now loads `ajv-formats` (`-c ajv-formats`), and the skill's batch path uses
  the same invocation instead of `--strict false`. With it the schema compiles and validates the sample intakes
  correctly (acme ✓; acme-x, platform, abcdefghi, blank displayName, malformed tenantId ✗). `validate.ps1` and
  `IntakeSchemaProfileParityTests` pass. Record: `notes/prereqs-validate-ajv-formats-coord-pr.md`.

## 6. Also found (recorded, not fixed here)

- **adr-check W1 (ADR-019)**: the new 400 — like every control-plane 400 — carries no stable `errorCode`
  extension, so callers can only match text. Follow-up: give `RunsEndpoints.BadRequest` an `errorCode` parameter
  (e.g. `CUSTOMER_ID_NONSTANDARD`).
- **adr-check W4 (Deploy-Release narrower input)**: `-CustomerId` is now case-sensitive and at most 8 chars, so a
  platform row is selectable only by a compliant value (`dev` → row "Dev" via the case-insensitive Dataverse `eq`).
  Before T237 the same was effectively true ("Demo 1" has a space, the account domains contain dots), so nothing
  that worked is lost; recorded per escalation trigger 4.
- **CI** ✅ fixed (follow-up commit): `provisioning-prereqs-validate.yml`'s ajv step failed on every run (`format:
  uuid` without ajv-formats) — see §5 criterion 3.
- **Skill Step 1c/1e** tenancy/profile prose is pre-T224 → recorded under plan G6 (T225b). T237 fixed only the
  Step 1f `$tenancyModelMap` keys (`Model1Shared`/`Model2Dedicated` → null for every current value).
- `ai-foundry-stack.bicep` `@maxLength(10)` composes `sprk${customerId}${environment}` as a base name — follow-up
  only if that stack is still deployed (not part of the customer stamp).
- `stacks/model1-*.bicep` length rules (10/20) — retired by T225a.
- **code-review W5**: `infrastructure/bicep/stacks/{dev,staging,prod}.bicepparam` pass `spaarkedev1` / `spraakestg` /
  `spraakeprod` as `customerId` to `model2-full.bicep` (`@maxLength(8)`) — they cannot deploy; the template comments
  in `infrastructure/bicep/**` `.bicepparam` files still say 3-10. Follow-up with the Bicep tasks (T225a/T244).
- **S3**: the 400 echoes the submitted value without a length cap (JSON-encoded, not logged — no injection risk).
- **S4**: the scripts' `ValidatePattern` accepts a value with a trailing newline (.NET `$`); a CLI argument cannot
  realistically carry one, and the pattern string stays byte-identical to the standard.
- **S8**: skill examples near Step 1 still show `Model1Shared` (and the Step 1.0 sample intake fails the schema on
  it) — part of G6 / T225b.
- **S11**: `RunsEndpoints.cs` still fully qualifies `Core.Models.TenancyModelParser` although the namespace is now
  imported — cosmetic.
- `docs/procedures/ci-cd-workflow.md` §"Customer Provisioning: provision-customer.yml" describes a workflow deleted
  in `902bebc49c` (its `validate-inputs` job said 3-10 chars) — added to the CI coord note.
