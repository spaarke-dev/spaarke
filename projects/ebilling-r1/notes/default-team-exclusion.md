# A reserved business unit's default team is left out of reader profiles

Branch `task/ebilling-default-team-exclusion`. A reserved business unit ("Spaarke Ontology Writer", a direct child of the root unit, no members) has a default team that must hold no field-security profile. The eBilling scripts that check or add "every default team" on a reader profile leave that team out, in apply and in `-Verify`.

## Helper

`scripts/common/OntologyWriterUnit.ps1`, copied byte-for-byte from `origin/work/customer-provisioning-orchestration-r1` at `77446a5222` (customer provisioning task 267 owns it; the unit's name is a single constant in it). Each site dot-sources it and calls:

- `Split-DvDefaultTeamsByOntologyWriterUnit` - separates the reserved unit's default team from the teams that belong on the reader profile. No unit: nothing is left out. Two units of that name, or a failed read: it throws (never "no such unit").
- `Get-DvOntologyWriterUnitTeamsOnProfile` - the reserved unit's teams that hold the reader profile.

The default-team read now selects `_businessunitid_value` (the helper requires it).

## Sites

| Script | Change |
|---|---|
| `Set-EbillingFirmReasonFieldSecurity.ps1`, `Set-EbillingInvoiceMoneyFieldSecurity.ps1`, `Set-EbillingInvoiceFirmHoldColumn.ps1`, `Set-EbillingResubmissionOfFieldSecurity.ps1` | The inline "reader profile on every default team" check is now `Test-ReaderOnDefaultTeam` (the test seam). These scripts add no team to a profile; they expect the reader profile on every default team except the reserved one's, print the team left out (or "none"), and FAIL in every mode if the reserved team holds the reader profile. |
| `Deploy-EbillingProcedureSchema.ps1` (`Test-ProfilePreconditions`) | Same expectation change. `-CreateProfiles` adds only the remaining teams (`$pre.DefaultTeams`); the dry run lists them and the team left out (or "none"). The reserved team on an existing reader profile is a precondition failure (escalation, nothing written). |
| `Deploy-EbillingStoredFileSchema.ps1` (`Test-ProfilePreconditions`) | Same, in its named-business-unit check. Adds no team (reuse only). |

Not changed: `Deploy-EbillingProcedureSchema.ps1` `Test-ExternalRolePrivileges` (per-business-unit default-team read). It reads the roles that flagged-external users inherit through their own unit's default team; it adds nothing to a profile, and leaving a team out of that scan could only hide a privilege. `Deploy-EbillingFormsViews.ps1` queries saved views. `Set-EbillingFindingsJsonFieldSecurity.ps1` and `Set-EbillingSourceEmailFieldSecurity.ps1` only fail when ANY default team holds the writer profile, which already covers the reserved team. A reserved team on a writer profile is already a FAIL in all scripts ("has TEAM").

No site adds default teams to a security role.

## Tests

`tests/scripts/EbillingDefaultTeamExclusion.Tests.ps1` (Pester 5.7.1, 50 tests): each script's function is lifted from its syntax tree and run against a test double of the Dataverse calls.

- Must-fire: the reserved team is never in the apply set; `-Verify` fails when it holds the profile; two matching units refuse; a failed unit read refuses; the team is not demanded on the reader profile.
- Must-not-fire: no reserved unit behaves as before; the reserved unit exists and holds nothing, and the check passes; another team missing from the profile still fails.

`tests/scripts/Deploy-EbillingStoredFileSchema.Tests.ps1` and its fixture were adapted to the extra reads (`businessunits` returns nothing, teams carry `_businessunitid_value`).
