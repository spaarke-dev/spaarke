# ISS-040 — Rule set visibility: native write hardening

Issue: #2901. Branch: `fix/iss-040-ruleset-visibility-fieldsec`. Status: code and guard done; **not applied to any environment**.

## What

`sprk_ruleset.sprk_visibletofirms` is field-secured. Only the "Spaarke BFF-Managed Field Writers" profile (and the platform
System Administrator profile) can create or update it. "Spaarke BFF-Managed Field Readers" can read it, so the form and the
"Visible to Firms" view column still show the value to every user.

## Why

Every firm-visibility reader trusts the flag: it decides whether a rule set's clause quotes and section references are shown to
firms. Only the BFF paths that own it should change it. Secured, a native write by any other user is refused. A rule set created
natively by a non-member keeps the column default, No (it cannot be set to Yes natively, and "No" is the safe side).

## Changes

| File | Change |
|---|---|
| `scripts/ebilling/Set-EbillingRuleSetVisibilityFieldSecurity.ps1` | New. Secures the column, grants Readers read-only and Writers read/create/update, appends the column to the Readers profile description (once, current text read first), verifies by reading back. Modes: `-WhatIf`, plain run, `-Verify`, `-Revert`. Every Web API call sends `Consistency: Strong`. A failed grant after securing reverts the column and republishes. Preconditions refuse a write run on any gap, including the writers-profile membership: the profile's members must be exactly the `-BffApplicationIds` application users (each a member; no other user, human or application; no team). A missing BFF member stops the script before the column is secured, with nothing changed. `-BffApplicationIds` is mandatory, as in the sibling scripts. |
| `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ruleset/Entity.xml` | `sprk_visibletofirms` `IsSecured` 0 to 1. |
| `src/dataverse/solutions/SpaarkeMaster/Other/FieldSecurityProfiles.xml` | A permission row for the column on each profile (Readers read=4 create=0 update=0; Writers read=4 create=4 update=4), and the Readers description names the column. Without the rows a solution import of the secured column would leave nobody able to read it. |
| `tests/Spaarke.ArchTests/RuleSetVisibilityFieldSecuredGuardTests.cs` | New guard (see below). |
| `tests/integration/data-mutation/FinanceSubmission/RuleSetVisibilityFieldSecurityAgreementTests.cs` | New agreement test: pins the two permission rows in `FieldSecurityProfiles.xml` (Readers read=4 create=0 update=0, Writers 4/4/4, no other profile creates or updates the column), `IsSecured` in `Entity.xml`, the script's constants against the solution, and the writers-membership check running before the column is secured. Must-fire controls on each. |
| `docs/data-model/sprk_ruleset.md` | The column is marked field-secured. |

Not done: no Pester test. The pattern script (`Set-EbillingFindingsJsonFieldSecurity.ps1`) has none; the money sibling's Pester
file lifts functions out of its AST against a test double, which is a larger piece than this change warrants. The script was
parse-checked only; it was never run against any environment.

## Writers check (every writer of the flag runs as the app user)

All Dataverse writes of the flag go through `IGenericEntityService`, which the BFF registers as the `IDataverseService` singleton
(`Infrastructure/DI/GraphModule.cs:145`), the app-only service client. None of these four writers uses an impersonated client; they use
the app-only `IGenericEntityService`. (Other services do impersonate a caller with `MSCRMCallerID`, in
`src/server/shared/Spaarke.Dataverse/DataverseImpersonation.cs` and `DataverseWebApiService`; that is not how this flag is written.)
The `callerObjectId` / `CallerObjectId` values in these services feed record ownership, not Dataverse impersonation.

| Writer | Where | Runs as |
|---|---|---|
| Rule set visibility update | `RuleSetService.SetVisibilityAsync` (about :2765, writes at :2783) | app user |
| Procedure rule set create | `RuleSetService.NewProcedureRuleSet` (about :2828, flag at :2835) | app user |
| Procedure type change | `ProcedureService` (about :468) | app user |
| Record-set create | `ProcedureAssignmentService.RecordRuleSetCreateAsync` (about :2859, flag at :2867) | app user |

Other writers searched for and not found: no other `new Entity("sprk_ruleset", ...)` in `src`; the Rule Review code page reaches
the flag only through the BFF (`ruleSetApi.setVisibility`), its one `Xrm.WebApi` use is a read-only firm lookup; no
field-mapping rule, AI topic-registry row or email update field targets the column (the script's precondition p4 checks this live).
Scripts: `Deploy-EbillingPolicyStoreSchema.ps1` creates the column (default No) and `Deploy-EbillingFormsViews.ps1` puts it on the
form and view; neither writes row values. The publish step of the BFF paths (`ProcedureRulesPublishPlan`) updates status and
effective date only, not the flag.

## Guard

`RuleSetVisibilityFieldSecuredGuardTests` requires an explicit `<IsSecured>1</IsSecured>` on `sprk_visibletofirms` in every
`sprk_ruleset` Entity.xml under `src/dataverse/solutions`. Controls: must-not-fire on the real Entity.xml; must-fire when the real
declaration is flipped; an explicit 0, a missing element and no declaration all fire; another table and a look-alike column name
are ignored. It is a separate file from `FindingsJsonFieldSecuredGuardTests`; consolidating the two is a later step.

## Apply order for dev (not done; needs the owner's typed OK)

1. The BFF needs no change (it already writes as the app user in the writers profile).
2. Notice to unified-access-control-r2 before the run.
3. `pwsh scripts/ebilling/Set-EbillingRuleSetVisibilityFieldSecurity.ps1 -BffApplicationIds <the BFF application ids> -WhatIf`, review the output with the owner.
4. Owner's typed OK, then run the script without `-WhatIf` (same `-BffApplicationIds`), then `-Verify`.
5. Verify the Rule Set form and the "Visible to Firms" view column still show the value for an internal user, and that a visibility
   change from Rule Review still works.
6. A user outside the writers profile creates a rule set in the model-driven app: the save succeeds and the flag reads No.
7. Notice to unified-access-control-r2 after the run.

## Known limits

- There is no customer-provisioning check and no release-note entry for this change yet. Follow-up for the release owner: a
  customer environment gets the secured column through the solution import, and its provisioning should run `-Verify`.
- While the script runs there is a short masked window between securing the column and the grants landing: the BFF reads the flag
  as null (read as off) and its writes are refused. The siblings behave the same way; the script reports the window and reverts the
  column if a grant fails.

Rollback: `-Revert` (un-secures, removes the two permission rows, publishes), then revert the source files by a commit.
