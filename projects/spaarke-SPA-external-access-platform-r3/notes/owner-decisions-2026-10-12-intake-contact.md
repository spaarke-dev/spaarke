# Owner decisions 2026-10-12: contact ownership (ISS-005) and intake (department, partner invention, NDA)

Worktree branch `worktree-agent-af842f88050a4b5b9`, based on the R3 head `b60f11898`. Code commit `5a3b935fa`.

| Item | Outcome |
|---|---|
| 1. ISS-005 workforce-contact owner | **Not implemented.** No defined source for "the customer's primary business unit" exists in the BFF. Candidates and a recommendation below. |
| 2. Intake department from the contact | Implemented. |
| 3. Invention Submission optional for partners | Implemented. |
| 4. NDA review AND issue | **No change.** The server enforces size and shape only. |

## 1. ISS-005: investigation (nothing implemented)

**Question.** What defines "the customer's primary business unit" today, so the binder can bind `ownerid` to that unit's default team?

**What exists.**

| Place | Finding |
|---|---|
| `ContactIdentityStore.cs:318-331` (`CreatePayload`) | No `ownerid`. Two callers: `ContactIdentityBinder.cs:493` (first sign-in create) and `:717` (systemuser link create). |
| `RecordOwnershipResolver.cs` (`ResolveOwnerOnlyAsync` :862) | Resolves a team only from (a) a named parent record's owning BU, or (b) the acting user's BU. `ResolveDefaultOwnerTeamAsync(businessUnitId)` (:1914) is private and takes a BU id, but nothing hands it the customer BU. A first sign-in has no parent record, and its acting identity is a contact with no systemuser, so the resolver would refuse (`NoOwnerSource`). |
| BFF configuration (`appsettings.template.json`, `WorkforceIdentityOptions`, `ExternalAccessOptions`, `Configure-ProductionAppSettings.ps1`, `config/spaarke-resources.yaml`) | **No customer business unit key.** `WorkforceIdentity:CustomerTenantIds` names the customer's Entra tenants, not a Dataverse unit. `SecureRecord:BusinessUnitName` names the Secure Record unit only. `Demo...BusinessUnitName` (`appsettings.template.json:388`) belongs to the demo-registration flow. |
| `PowerBiOptions.CustomerBusinessUnitId` (`Api/Reporting/PowerBiOptions.cs:100`) | A real key, but per Power BI workspace, optional, and read only by reporting. Not a stamp-wide setting. |
| Dataverse | No customer/stamp table or account with a business-unit lookup (`docs/data-model`, `package-scope.json`). |
| Provisioning (`customer-provisioning-orchestration-r1`, T259 / ISS-010) | **The unit is defined, but only in the control plane.** H10 finds or creates the customer's own unit (intake `displayName`) as a direct child of the root and creates both BFF application users *in it* (`InterStepState.CustomerBusinessUnitId`, `src/server/services/Sprk.Provisioning.ControlPlane.Core/Models/InterStepState.cs:214`; `.claude/constraints/provisioning.md:254`). H7 links that unit to the container (`H7DataverseEnvVarValuesHandler.cs:307`). Nothing writes the id (or name) to the BFF's settings or to a Dataverse record the BFF reads. |

**Reading.** On a T259-provisioned stamp the BFF application user already sits in the customer's unit, so a contact created with no `ownerid` already has that unit as `owningbusinessunit`. What is wrong is the owner kind (a user, not the unit's default team, so outside I-6), and on dev the app user is in the root unit. The BFF has no named setting or record from which to bind a team. Using the application user's own unit (WhoAmI) would be a new, undeclared dependency on a provisioning invariant, and on dev it would resolve to the root unit, which is the behaviour the owner is correcting. I did not choose one.

**Candidates.**

| # | Source | For | Against |
|---|---|---|---|
| A | **New key `ExternalAccess:CustomerBusinessUnitId`** (GUID), set by provisioning from `InterStepState.CustomerBusinessUnitId` | Explicit, one value per stamp, no inference. The resolver gets the BU id directly, so a new public `ResolveDefaultTeamOfBusinessUnitAsync(Guid)` on `IRecordOwnershipResolver` (a thin wrapper over the private method, keeping the Secure Record rule) is the whole code change. Same style as `WorkforceIdentity__CustomerTenantIds__N`. | Needs a provisioning change; until set, binder creates must refuse (fail closed), so the setting must ship before or with the code. |
| B | The BFF application user's own business unit (WhoAmI / `systemusers?applicationid`) | No new key. Equals the customer unit on every T259 stamp by construction. | Implicit; relies on a provisioning invariant the BFF does not check; on dev it is the root unit. Adds a Dataverse read per create. |
| C | A unit **name** key (like `SecureRecord:BusinessUnitName`) | Human-readable. | A name is not unique across the tree; the existing name-based lookup already needs a TOP 2 ambiguity refusal. Weaker than A. |
| D | The matched systemuser's unit | Needs no config. | Only licensed staff have a systemuser; workforce contacts (the actual binder-create population) have none. Not usable. |
| E | `PowerBiOptions.CustomerBusinessUnitId` | Exists. | Per workspace, optional, reporting-only. Wrong scope. |

**Recommendation: A.**

*CLAUDE.md section 11:*
1. **Existing.** It overlaps `PowerBi:AllowedWorkspaces:N:CustomerBusinessUnitId` (reporting scope only) and `SecureRecord:BusinessUnitName` (a different unit). No stamp-wide customer-unit setting exists (grep of `src/server`, `config`, `scripts`, 2026-10-12).
2. **Extension.** The two existing keys cannot be reused, since each names a different thing. The setting belongs in the existing `ExternalAccess` options class (`ExternalAccessOptions`), validated with the existing `ValidateOnStart` binding (reject the all-zero GUID).
3. **Cost of doing nothing.** Binder-created workforce contacts stay owned by the application user, outside I-6 (ISS-005). Task 035 then owns an intake request by the default team of the requester contact's unit, so a customer whose staff sit in a child unit never sees those intakes.

*Provisioning would set it:* H4b (the bulk app-settings writer, which already writes `WorkforceIdentity__CustomerTenantIds__N`; `PerEnvSourceCatalog.cs`, `IAppServiceSettingsWriter.cs`) writes `ExternalAccess__CustomerBusinessUnitId` on both slots from H10's `CustomerBusinessUnitId`. H10 runs before H4b's consumers need it (H10 is "right after H3 + H5"), so ordering needs a check. Owner: provisioning project (R3 builds no provisioning). Behaviour once set: the binder resolves the unit's default owner team, binds `ownerid` as `/teams({id})` in `CreatePayload`, and an unresolved team (or an unset key) means NO contact is created and the existing `DenyContactCreateFailed` / `DenyContactCreateUnavailable` path answers. Tests: owner bound; unresolved or unset, no create; existing binder tests stay green.

I can do the BFF half of A as soon as the owner picks it and provisioning agrees to write the key.

## 2. Intake: requester department (implemented)

- **Attribute.** The standard `contact.department` (confirmed live on spaarkedev1: a SQL `SELECT department FROM contact` succeeds; no contact there carries a value today).
- **Read.** `ExternalDataService.GetContactDepartmentAsync(contactId)`: one app-only `GET contacts({id})?$select=department`, through the same helper as the other external reads (`GetSingleOrThrowAsync`). There was **no existing contact read to widen**: the principal carries no contact attributes (the binder's `ContactSelect` and the principal resolvers are shared sign-in paths), and the ownership resolver's contact read selects only the owning unit and team. So this is one added read, made after validation so a rejected submission costs nothing.
- **Storage.** `ExternalIntakeEndpoints.WithSystemKeys` writes `requesterDepartment` into the stored answers. Any client key of that name (any casing) is dropped first. No department on the contact (missing, blank, contact absent) means the key is omitted, and the client text is stored verbatim. Other properties are copied in order, duplicates included (duplicates were already stored verbatim, K-e).
- **Fault.** A Dataverse fault on the department read answers 503 `EXT_INTAKE_DATAVERSE_UNAVAILABLE`, nothing created. Rationale: storing "no department" for "could not read" would silently corrupt demand reporting, and the same Dataverse must take the create anyway. This is a judgment call; the alternative (omit on fault) is a one-line change.
- **Cap.** The 100 KB cap still applies to the client's answers; the stored text can exceed it by the key (the column allows 1 MB).

## 3. Invention Submission for partners (implemented)

- **Option.** `ExternalAccessOptions.PartnerInventionIntakeEnabled` (default false), bound by the same `AddOptions<ExternalAccessOptions>().BindConfiguration("ExternalAccess").ValidateOnStart()` as `PartnerServiceRequestsEnabled`. A non-boolean value, including empty, fails startup. App Service form `ExternalAccess__PartnerInventionIntakeEnabled`.
- **One rule, two readers.** `ModuleEntitlementResolver.IsIntakeTypeAvailable(principal, type)` and `IntakeRequestTypesFor(principal)`. Workforce (staff and workforce contacts): all four types. CIAM: all except Invention Submission (`100000001`) unless the flag is on. Unknown type: never available.
- **Enforcement.** The submit route asks the rule right after the module-entitlement check, before the contact check, validation and any Dataverse read. A CIAM caller submitting type 100000001 with the flag off gets **403 `sdap.external.intake.invention_not_available`**, sent as both `reasonCode` (the 403 convention on this route) and `errorCode` (the owner's wording). It is distinct from `sdap.external.intake.module_not_entitled`, because the partner may still use the other three types. Workforce callers are unaffected.
- **SPA discovery.** The SPA learns about `PartnerServiceRequestsEnabled` only through `GET /api/v1/external/me/entitlements` (the `legal-front-door` module code appears or not). I mirrored that: the response gains **`intakeRequestTypes: number[]`** (the `sprk_requesttype` values the caller may submit; empty when the caller is not entitled to `legal-front-door`). The field is additive. The SPA should hide a tile whose value is absent. I did not touch `src/client`.
- **The flag is inert without `PartnerServiceRequestsEnabled`** (a partner then has no Legal Front Door at all). No startup validator enforces the pairing, since an unpaired flag is harmless and a new startup failure would be a deployment risk; a test pins the inert behaviour.
- **Config surfaces.** `PartnerServiceRequestsEnabled` is carried in exactly one config file, `appsettings.template.json` (value plus a `_..._comment`). It is **not** in `Configure-ProductionAppSettings.ps1`, `config/spaarke-resources.yaml`, or any provisioning catalog (grep 2026-10-12), so the new key follows it and is added to `appsettings.template.json` only. Absent means off.

*CLAUDE.md section 11:*
1. **Existing.** `ExternalAccessOptions.PartnerServiceRequestsEnabled` and `ModuleEntitlementResolver` (module-level gate); `MeEntitlementsResponse`.
2. **Extension.** The module gate cannot express "this module, but not this type", so a second switch and a per-type method on the same resolver and options class are the extension. No new service, DI registration, endpoint or package. `intakeRequestTypes` extends the existing `/me/entitlements` response.
3. **Cost of doing nothing.** Without the flag, partners either get all four intake types or none, which contradicts the owner decision (Invention Submission is workforce-only unless a customer opts in). Without `intakeRequestTypes`, the SPA would show a tile the server refuses.

## 4. NDA: review and issue (no change)

`ExternalIntakeEndpoints.Validate` checks only `requestType` (one of four), `title` (1-850) and `answers` (a JSON object, at most 100 KB). It does not look inside `answers` for any type (task 035 notes, decision 4: answers are stored verbatim). So signer name/email, term, governing law, the conditional `neededBy`, email format and per-field caps are **wizard-side validation (tasks 052/053)**, and nothing was changed on the server.

Recommendation for the wizard contract, since the owner asked for a code: use an explicit `ndaAction` (`"review"` | `"issue"`) rather than inferring from `paperSource`, because `paperSource = ours` (our paper) can also mean "assess an earlier signed NDA". Suggested keys when `ndaAction = "issue"`: `signerName` (2-200), `signerEmail` (email, at most 254), `term` (a code), `governingLaw` (at most 100), and `neededBy` required. Per-field server enforcement would be new surface with one caller (section 11 question 3); if the owner wants it, say so and it becomes a separate change. Document generation is out of scope.

## Test map

| Behaviour | Test (all in `tests/unit/Sprk.Bff.Api.Tests`) |
|---|---|
| Department present, stored | `ExternalIntakeEndpointsTests.RequesterDepartment_PresentOnTheContact_IsStoredAsTheSystemKey_BesideTheClientAnswers` |
| Department absent, omitted, text verbatim | `...RequesterDepartment_AbsentOnTheContact_TheKeyIsOmitted_AndTheAnswersAreStoredVerbatim` |
| Client-supplied value ignored (3 casings; with and without a contact value) | `...RequesterDepartment_AClientSuppliedValue_IsIgnored_TheContactsValueWins` (x3), `..._WithNoDepartmentOnTheContact_IsDropped_NotKept` |
| Read fault, 503, nothing created, no text leaked | `...RequesterDepartment_ADepartmentReadFault_Gets503_...` |
| Invalid submission, no read | `...RequesterDepartment_AnInvalidSubmission_NeverReadsTheContact` |
| Duplicate keys survive | `...WithSystemKeys_KeepsDuplicateClientKeys_AndNeverThrowsOnThem` |
| The read itself (column, trim, blank, 404, fault) | `ExternalDataServiceServiceRequestCreateTests.ContactDepartment_*` (10 cases) |
| CIAM + invention + flag off, 403 distinct code, no Dataverse read | `ExternalIntakeInventionFlagOffTests.Ciam_Invention_FlagOff_Gets403_...` |
| Other types unchanged for CIAM | `...Ciam_TheOtherTypes_FlagOff_AreUnchanged_Gets201` (x3) |
| Workforce accepted, flag off and on | `...Workforce_Invention_FlagOff_IsAccepted`, `ExternalIntakeInventionFlagOnTests.Workforce_Invention_FlagOn_IsAccepted` |
| Flag on, accepted | `ExternalIntakeInventionFlagOnTests.Ciam_Invention_FlagOn_IsAccepted_Gets201` |
| `/me` lists the types per caller and flag | `Me_Ciam_FlagOff_...`, `Me_Workforce_FlagOff_...`, `Me_Ciam_FlagOn_...`, `Me_ACallerNotEntitledToTheFrontDoor_ListsNoIntakeTypes` |
| Flag inert without the partner switch | `ExternalIntakeInventionFlagWithoutPartnerSwitchTests.*` |
| The rule itself | `ModuleEntitlementResolverTests.IsIntakeTypeAvailable_*`, `IntakeRequestTypesFor_*`, `PartnerInventionIntakeEnabled_DefaultsOff` |

## Fail-before evidence

The new members do not exist in the old code, so the old tests cannot be run against it. Proof by mutation on the finished code (build, run, restore):
- `IsIntakeTypeAvailable` forced to `true` and `WithSystemKeys` forced to return the client text: `FullyQualifiedName~ExternalIntake|ModuleEntitlementResolver` gave **10 failed / 100 passed** (110). Failing: `IsIntakeTypeAvailable_Ciam_InventionSwitchOff_RefusesOnlyInvention`, `IntakeRequestTypesFor_...`, `Ciam_Invention_FlagOff_Gets403_...`, `Me_Ciam_FlagOff_...`, the four client-supplied-department cases, `WithSystemKeys_KeepsDuplicateClientKeys_...`, `RequesterDepartment_PresentOnTheContact_...`.
- Restored: all green.

## Verification

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "External\|Intake\|ContactIdentity\|Ownership"` | **3,346 passed, 0 failed** |
| `dotnet test tests/Spaarke.ArchTests` | 941 passed, 0 failed (one ledger prose citation, `ExternalIntakeEndpoints.cs:139` to `:161`, updated for the moved line) |
| Full unit suite | Not run (per the brief) |

## Publish size (bff-hygiene item 4)

Method as task 039: fresh `git archive` of each commit to a short path, `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` (framework-dependent linux-x64), `Compress-Archive -CompressionLevel Optimal`, PDBs included, 192 files each.

| Tree | Zip bytes | MB |
|---|---|---|
| fresh `origin/master` (`37470dacc`) | 38,232,047 | 36.46 |
| this branch (`5a3b935fa`) | 38,300,239 | 36.53 |

Delta vs fresh master **+68,192 bytes (+0.07 MB)**; that figure includes the rest of the unmerged R3 work, not only this change. Total 36.53 MB, under the 55 MB review line and the 60 MB hard stop. No package added.

## Known limits and things noticed

- `ExternalIntakeEndpoints` ledger citations in `RouteAuthorizationGuardTests.Ledger.cs` are by line number in one prose string; they drift whenever the file changes.
- `PartnerInventionIntakeEnabled` is not in `Configure-ProductionAppSettings.ps1` / provisioning, like its sibling. If operators should be able to set either from provisioning, that is a provisioning change.
- A contact with a department set on dev does not exist, so the live read is proven by the HTTP-level tests only; a live submit with a department set on a contact is the closing check.
