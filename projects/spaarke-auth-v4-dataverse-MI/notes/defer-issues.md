# Deferred Work & Uncovered Issues — spaarke-auth-v4-dataverse-MI

> Two-write rule per [`/project-defer-issue-tracking`](../../../.claude/skills/project-defer-issue-tracking/SKILL.md):
> every entry here also exists as a GitHub Issue so it is visible outside this project's `notes/` folder.

---

## Deferrals

### DEF-001 — Power BI → managed-identity principal (Workstream D / tasks 040–042)

| Field | Value |
|---|---|
| **Status** | Open (deferred) |
| **Urgency** | someday — re-open when Spaarke actually adopts Power BI |
| **Filed** | 2026-08-19 |
| **Source** | Owner decision during task 001 kickoff: *"we can ignore Power BI if it is not readily available and defined — we are not yet using Power BI (it will be in the future but we can address the MI at that time)."* |
| **GitHub Issue** | [#804](https://github.com/spaarke-dev/spaarke/issues/804) |

**Description**

`spaarke-auth-v4-dataverse-MI` set out to eliminate every secret-backed confidential credential in the BFF.
Workstream D (spec FR-D1/D2/D3, tasks 040–042) covered Power BI specifically: move it off
`ConfidentialClientApplication` + `PowerBi:ClientSecret` onto a **user-assigned managed identity as the Power BI
principal** (Microsoft's documented model), then delete the secret.

Power BI is not yet in use at Spaarke, so the owner deferred the workstream on 2026-08-19.
**Consequence: `PowerBi:ClientSecret` remains in place.**

This is safe for the OBO migration. `PowerBi:ClientSecret` (`PowerBiOptions.cs:44-45`) is a *genuinely separate*
credential from `BFF-API-ClientSecret` — no OBO path reads it — so deferring does not weaken FR-C3 (task 033)
and does not touch the fail-closed surface. Task 033 already carries a negative acceptance criterion asserting
the Power BI secret is left untouched.

**The real risk of deferring is invisibility, not breakage.** This project exists precisely because a previous
audit's correct inventory was buried under a wrong conclusion. So the deferral is made *loud* rather than silent:

- **Task 060** (FR-F1 ArchTest ban) allowlists the Power BI sites **with this deferral as the written reason**.
  Un-deferring is then a one-line allowlist removal, not an archaeology exercise.
- **Task 061** (FR-F2 census) keeps both Power BI sites as **secret-backed** entries, so the census reports the
  estate as it actually is rather than cleaner than it is.
- **Task 090** marks success criterion 10 **waived with reason**, not silently dropped.

**Carried forward unanswered**: whether Power BI **service-principal profiles** (used by
`ReportingProfileManager`) are supported when the principal is a managed identity. Deferral does not answer it.
It travels with task 040 and must be settled *before* 041/042 are attempted. If unsupported, the fallbacks are
MI-FIC on the existing service principal, or retaining the Power BI secret under a documented ADR-028 exception.

**Entry-points**

- `projects/spaarke-auth-v4-dataverse-MI/tasks/040-verify-powerbi-profiles-under-mi.poml` (start here on re-open)
- `projects/spaarke-auth-v4-dataverse-MI/tasks/041-powerbi-uami-principal.poml`
- `projects/spaarke-auth-v4-dataverse-MI/tasks/042-remove-powerbi-secret.poml`
- `projects/spaarke-auth-v4-dataverse-MI/spec.md` — "Workstream D" (retained verbatim as the re-open spec)
- `src/server/api/Sprk.Bff.Api/Api/Reporting/ReportingEmbedService.cs:77-81`
- `src/server/api/Sprk.Bff.Api/Api/Reporting/ReportingProfileManager.cs:74-78`
- `src/server/api/Sprk.Bff.Api/Configuration/PowerBiOptions.cs:44-45`

**Suggested fix** (on re-open)

Run task 040 first — it is a pure verification spike and it gates the other two. Everything downstream of it is
already specified; nothing about the FR-D requirements was found wrong, only untimely.

**Estimated effort**: 1–3 days (depends entirely on the 040 outcome)
**Blockers**: Spaarke adopting Power BI; a Power BI tenant admin for FR-D1
**Related**: spec FR-D1/D2/D3 · success criterion 10 · plan.md Phase 4 · risk R4 · ADR-028 (A4, E-3)

---

## Issues

### ISS-001 — `keyVaultReferenceIdentity` + deployment slots missing from Bicep IaC

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round (was `now` before the severity correction) |
| **Filed** | 2026-08-20 |
| **Source** | Task 001 — reproduced live while creating the dev staging slot |
| **GitHub Issue** | [#805](https://github.com/spaarke-dev/spaarke/issues/805) |

**Description**

`spaarke-bff-dev` sets `keyVaultReferenceIdentity` to the UAMI `mi-bff-api-dev`, but that property appears
**nowhere** in `infrastructure/bicep/` — it was set out-of-band and is unmanaged drift. Deployment slots are
likewise absent from `infrastructure/bicep/modules/app-service.bicep`.

The App Service has **no system-assigned identity**, and `keyVaultReferenceIdentity` defaults to
`SystemAssigned` — so any slot created from it cannot resolve `ConnectionStrings__Redis`, `AzureOpenAI__ApiKey`,
`ServiceBus__ConnectionString`, `AiSearch__ReferencesApiKey`, `DocumentIntelligence__AiSearchKey`,
`RecordSync__AiSearchApiKey` or `Communication__WebhookSigningKey`.

> **⚠️ Corrected same day.** This entry first claimed re-applying the IaC would break production Key Vault
> references. **Wrong.** The Bicep stack would create `sprkspaarkedev1dev-api` in `rg-spaarke-spaarkedev1-dev`,
> not `spaarke-bff-dev` in `rg-spaarke-dev` — a re-apply builds a *parallel* environment and does not touch the
> running one. Corrected finding: the live dev environment is simply **not IaC-managed**, so there is no
> declarative record of the identity wiring or the slot. Cost is reproducibility, not availability.
> Severity high → medium.

**Reproduced, not hypothesised.** Creating the `staging` slot with `--configuration-source` copied all 213 app
settings but not this site-level property; the slot defaulted to `SystemAssigned`, could resolve no Key Vault
reference, and the container aborted at startup with **exit code 134 (SIGABRT)** after 8.2 s. Setting the
property to the UAMI fixed it. The failure signature is misleading — it presents as a generic container abort,
not as an unresolved Key Vault reference.

**Entry-points**

- `infrastructure/bicep/modules/app-service.bicep` — no `keyVaultReferenceIdentity`, no slot resource
- `az webapp show -n spaarke-bff-dev -g rg-spaarke-dev --query keyVaultReferenceIdentity -o tsv`
- `projects/spaarke-auth-v4-dataverse-MI/notes/decisions/001-slot-creation.md` — Finding A

**Suggested fix**

Parameterise `keyVaultReferenceIdentity` in `app-service.bicep` (default to the supplied UAMI resource ID), add
an optional slot resource, then diff against live before merging — this app serves dev traffic and OBO fails
closed.

**Estimated effort**: half a day plus verification
**Blockers**: none — filed by `spaarke-auth-v4-dataverse-MI`, not owned by it
**Related**: tasks 031/032 now carry pre-checks for this · decision record 001

