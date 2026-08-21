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


---

### ISS-002 — the ADR-010 1:1-interface ratchet is blind to cross-assembly seams

> **GitHub**: [#809](https://github.com/spaarke-dev/spaarke/issues/809)

> **Filed**: 2026-08-21 by `spaarke-auth-v4-dataverse-MI` task 020 · **Severity**: medium
> **Owner**: repo-level (architecture / test tooling) — **not** this project

**Reproduced, not hypothesised.** Task 020 introduced `IClientAssertionProvider` (declared in
`Spaarke.Dataverse`) with exactly one implementation (`ManagedIdentityAssertionProvider`, in the BFF).
The task instructed raising the ratchet ceiling 153 → 154 on the stated grounds that *"without it the
build fails."* It does not. Verified twice — once during execution, once independently at the quality
gate with a throwaway probe that re-ran the counting logic:

| Check | Result |
|---|---|
| `ADR010_DITests` at the **unraised** ceiling 153 | pass |
| Real 1:1 interface count | **151** |
| `IClientAssertionProvider` in the counted list | **absent** |

**Root cause.** `ServicesShouldBeConcreteUnlessSeamRequired` (`ADR010_DITests.cs:106,109`) enumerates
`Types.InAssembly(typeof(Program).Assembly)` — the BFF assembly only. An interface declared in
`Spaarke.Dataverse` or `Spaarke.Core` is never in the candidate set, so **any contract-in-shared-lib /
implementation-in-BFF pair is invisible to this gate, permanently.** That is precisely the shape the
CI-enforced layering (`LayerDependencyTests`, FR-14) *forces* on shared-library types, so the gate is
blind to exactly the seams the architecture requires.

**Two distinct problems:**

1. **The blind spot itself.** One-line fix: union the BFF assembly with `Spaarke.Core` and
   `Spaarke.Dataverse` before filtering.
2. **Live slack.** The ceiling is **153** against a real count of **151**, so two in-assembly 1:1
   interfaces can land today without the justify-or-concrete review the ratchet exists to force. With
   17 active worktrees the slack is being consumed by someone.

**Why this project did not fix it.** Both are repo-wide detector changes. Tightening the ceiling could
redden CI for other in-flight projects that legitimately add an interface — that is an owner call, not
a side effect of an auth task.

**What task 020 did instead**: left the ceiling untouched at 153, recorded the verified numbers in
`ADR010_DITests.cs:164-173` and in `AuthorizationModule.cs`, and booked the *census* half onto task
**061** (its credential census must scan all server assemblies, with a negative control that adds a
scratch confidential-client site in `Spaarke.Dataverse`). **The ratchet itself remains unfixed.**

**Entry-points**
- `tests/Spaarke.ArchTests/ADR010_DITests.cs:106,109,174`
- `projects/spaarke-auth-v4-dataverse-MI/notes/decisions/020-assertion-seam.md` §5

**Estimated effort**: 1–2 hours for the blind spot; the ceiling decision is judgement, not effort
**Blockers**: none
**Related**: task 061 (census half, booked) · ADR-010

---

### ISS-003 — `LayerDependencyTests` enforces `ProjectReference` but not `PackageReference`

> **GitHub**: [#810](https://github.com/spaarke-dev/spaarke/issues/810)

> **Filed**: 2026-08-21 by `spaarke-auth-v4-dataverse-MI` task 020 · **Severity**: low-medium
> **Owner**: repo-level (test tooling) — **not** this project

Every task in this project carries the constraint *"`Spaarke.Dataverse` gains NO ProjectReference **and
no new PackageReference**"*, and FR-14 is cited as the enforcement. Only the first half is actually
enforced: `LayerDependencyTests.cs:109` `ExtractProjectReferences` parses `<ProjectReference Include=…>`
and has no `PackageReference` regex at all. **The PackageReference half rests on reviewer attention.**

It holds today — task 020 verified `Spaarke.Dataverse.csproj` is byte-identical, and the new contract
needed no package because `Microsoft.Identity.Client` 4.87.0 was already referenced. But this task is
exactly the one that makes the rule salient: a future task adding
`Microsoft.Identity.Web.Certificateless` to the shared library "to simplify the seam" would sail through
CI green, silently inverting the layering the gate exists to protect.

**Suggested fix**: extend `ExtractProjectReferences` with a second assertion over `PackageReference`,
using the negative-control shape already at `LayerDependencyTests.cs:81`. Note it needs an allowlist —
the shared lib legitimately carries `Azure.Core`, `Azure.Identity`, `Microsoft.Identity.Client`, etc.;
the rule to enforce is "no NEW package", i.e. a frozen inventory, not "no packages".

**Entry-points**
- `tests/Spaarke.ArchTests/LayerDependencyTests.cs:81,109`
- `src/server/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj`

**Estimated effort**: 2–3 hours including the allowlist baseline
**Blockers**: none
**Related**: ISS-002 (sibling detector gap, same file family)
