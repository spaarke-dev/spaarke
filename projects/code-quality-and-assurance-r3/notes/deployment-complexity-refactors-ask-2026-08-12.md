# Deployment Complexity Refactor — Ask from `customer-provisioning-orchestration-r1`

> **From**: `customer-provisioning-orchestration-r1` (paused pending this ask)
> **To**: `code-quality-and-assurance-r3`
> **Date**: 2026-08-12
> **Owner**: Ralph Schroeder
> **Status**: Inbound ask — r3 assesses and determines approach
> **r1 branch**: `work/customer-provisioning-orchestration-r1` @ commit `164f36c7c` (design.md v3 shipped; refresh in progress)

---

## TL;DR

Four refactors surfaced during r1's 2026-08-12 design refresh that would materially reduce Spaarke's deployment complexity. **All four are BFF-touching quality concerns that fit r3's charter.**

- **#2 Fail-fast configuration validation** — small forcing-function; fits r3 tasks 040 (ArchTests) / 042 (CI gates)
- **#4 Graph app-role parity via code constants** — small forcing-function; fits r3 tasks 040 / 042
- **#1 KV federation (single source of truth for config)** — medium architecture refactor; new scope; no existing r3 task; touches BFF + PCF + code-pages + external-spa
- **#3 App-reg consolidation (UAMI federated credentials for Dataverse S2S)** — architecture refactor; **essentially the same axis as r3's existing NG1** ("Two Dataverse access stacks unification, differing OBO/app-only auth — needs its own ADR — explicitly deferred"). Ask: revisit NG1 given r1's evidence.

**r1's ask**: assess these four refactors against r3's charter and determine which fit, in what order, and whether NG1 should be brought in-scope. r1 is pausing after its design.md v3 is complete; when r3 completes the refactors it accepts, r1 will resume with the refactored architecture as its baseline.

---

## Why r1 is asking r3

r1 (`customer-provisioning-orchestration-r1`) is productizing Spaarke's customer-standup pipeline. During its 2026-08-12 design refresh, it catalogued six **silent-fail traps** in the current deployment (design.md v3 §4B) — each traceable to a specific class of complexity in app-registration management + environment-variable / configuration plumbing. r1's design absorbs guards for each trap (verification queries as handler post-conditions), but the underlying complexity remains. That underlying complexity is a **code-quality concern**, not a deployment concern — which puts it squarely in r3's charter.

r1 could ship as-designed and every customer inherits the same brittleness. r3 is the correct home for the structural fix.

**References**:
- r1 design.md v3: `projects/customer-provisioning-orchestration-r1/design.md` (on branch `work/customer-provisioning-orchestration-r1`)
- r1 2026-08-12 assessment: `projects/customer-provisioning-orchestration-r1/PROJECT-UPDATE-2026-08-12.md`
- r1 component inventory: `projects/customer-provisioning-orchestration-r1/COMPONENT-INVENTORY.md`
- r1 silent-fail trap catalog: r1 design.md §4B (T1–T6)

---

## The four refactors — detailed

### #1 KV Federation — single source of truth for configuration

**Problem**: Configuration values live in **5 places** with drift risk:
- Bicep outputs (deploy-time)
- Key Vault secrets
- Dataverse environment-variable values (client-side)
- App Service settings (with `#{TOKEN}#` substitution at deploy time)
- `appsettings.template.json` deploy-time tokens (20+ tokens per r1 §10.4)
- Client-side localStorage cache (5-min in-memory + 60-min localStorage per r1 §10.3)

**Evidence from r1**:
- r1 §10.3: 7 per-customer Dataverse env-vars set at H7; hardcoded URL fallbacks REMOVED per task 024 precisely because of silent breakage
- r1 §10.4: 26 IOptions sections; ~25 settings "discovered only by startup exceptions" per PROJECT-UPDATE §6 Gap 4
- Cross-source drift risk: `sprk_BffApiBaseUrl` value must match BFF App Service URL, must match app-reg redirect URIs, must match CORS AllowedOrigins, must match customer's PowerPages BaseUrl — **five places, one URL**
- Silent-fail traps T1 (`keyVaultReferenceIdentity`) and T5 (staging-slot MI parity) are direct consequences of the current KV reference pattern

**Proposed refactor**: Elect Key Vault as the single source of truth for all customer-specific configuration. Everything else reads from KV at startup:
- BFF: drop `appsettings.template.json` token substitution; read via `Microsoft.Extensions.Configuration.AzureKeyVault` at startup with SDK caching
- Client-side: replace Dataverse env-var reads with a `/api/config/client` BFF endpoint returning the 7 client-visible values (all sourced from KV); eliminates 5-min/60-min cache invalidation ceremony
- App Service settings: keep only `KeyVaultUri`, `UAMIClientId`, `Environment` — everything else moved to KV
- External SPA / Office add-ins: replace baked-at-build-time BFF host with runtime `/config.json` fetch (pairs naturally with this refactor)

**Scope estimate**: **Medium — 2–4 weeks**
- BFF startup + config layer
- PCF client env-var utility (`environmentVariables.ts`)
- Code-page config utilities (all ~28 SPAs)
- External SPA + Office add-ins (runtime config)
- `Deploy-Release.ps1` (drops token substitution)
- `appsettings.template.json` (slimmed dramatically)

**Files affected** (rough count, needs r3 assessment to verify): ~40–60 files across BFF, PCF, code-pages, external-spa

**Dependencies**: Prefers **#2 Fail-fast validation** to land first (want to validate KV-sourced config correctly at startup before dropping the token pattern).

**Impact if not done**:
- Silent-fail traps T1, T5 persist per-customer
- Every URL change requires updating 5 places atomically
- Client-side cache invalidation ceremony continues (5-min + 60-min windows where stale config runs)
- Every new BFF app-setting requires a new deploy-time token + `appsettings.template.json` edit + KV secret creation

**Migration for existing deployments**:
- Additive during transition: KV federation reader added to BFF; token substitution kept for backward compat
- Cut over per-customer during scheduled maintenance windows (~1 hour each)
- Full removal of token pattern after all customers migrated

**r3-charter fit**: Medium — this is BFF **quality architecture**, not just cleanup. r3's forcing-functions (ArchTests, CI gates) could enforce "no deploy-time token substitution" as an ArchTest once landed.

---

### #2 Fail-fast configuration validation ([Required] + ValidateOnStart)

**Problem**: ~25 BFF settings fail at first-use, not at startup. Silent-fail traps T1/T2/T3 currently require handler post-condition verification queries (external work) rather than surfacing at BFF startup.

**Evidence from r1**:
- r1 §10.4: 26 IOptions sections; ~25 settings "discovered only by startup exceptions" per PROJECT-UPDATE §6 Gap 4 — an admission that config discipline is inconsistent
- Silent-fail traps T1 (`keyVaultReferenceIdentity` not PATCHed to UAMI), T2 (UAMI not registered as Dataverse App User), T3 (MI Graph app-role parity broken) — all currently caught only at first BFF request that exercises the missing config
- "Validated but not wired" defect class (R7): value parsed but never applied

**Proposed refactor**:
- Annotate every `IOptions<T>` property with `[Required]` on customer-critical settings
- Wire `IValidateOptions<T>` for cross-property invariants (e.g., "if `Feature.X.Enabled=true` then `Feature.X.Endpoint` MUST be present")
- Register with `.ValidateDataAnnotations().ValidateOnStart()` in DI setup
- Fresh BFF that's missing a required setting **crashes at startup with a clear list of missing/invalid settings**, not at first user request

**Scope estimate**: **Small — days, not weeks**
- Attribute additions across 26 `IOptions<T>` classes
- One middleware for cross-property invariants
- Program.cs registration change

**Files affected**: ~26–40 config option classes + Program.cs

**Dependencies**: None. Can land independently.

**Impact if not done**:
- Silent-fail traps T1/T2/T3 continue to require external verification queries per-customer
- Missing config surfaces at first-user-request (production incident) instead of at deploy verification (`/health` probe)

**r3-charter fit**: **High** — perfect fit for r3 tasks 040 (expand ArchTests) or 042 (CI gates). An ArchTest could enforce "every IOptions class registered in DI must be annotated with [Required] on customer-critical properties + validated on start."

---

### #3 App-reg consolidation (UAMI federated credentials for Dataverse S2S)

**⚠️ This is essentially r3's existing NG1 axis.** See r3 workstream bff-api design.md NG1: *"Unifying the two Dataverse access stacks (ServiceClient vs raw-HTTP, ~22 files, **differing OBO/app-only auth**). This is a genuine architecture project (~25-30 files, 4-6 weeks, high risk, needs its own ADR). Explicitly deferred."*

r1's #3 is one specific slice of that broader problem: **consolidate from 2 app-registrations (BFF + Dataverse S2S) to 1** by moving Dataverse S2S authentication from client-secret to UAMI federated credentials.

**Problem**: Two Entra app registrations per customer:
- BFF app-reg (OBO delegated + user impersonation)
- Dataverse S2S app-reg (S2S only, with 24-month rotating client secret)

= two app regs to provision (H3), two client secrets to store in KV, two rotation cycles, two consent flows for Model 2.

**Evidence from r1**:
- r1 §9.1: two app-reg definitions with 24-month secret expiry each
- r1 §7.7 KV secrets: `BFF-API-ClientSecret` + `Dataverse-S2S-ClientSecret` both required
- Client secrets are the largest ongoing operational burden after ~24 months when rotation cascades hit
- r3 NG1 already identifies this axis as needing an ADR

**Proposed refactor**:
- Use **UAMI for BFF→Dataverse S2S** via federated credentials (no secret at all)
- Requires:
  - UAMI registered as Dataverse Application User (r1 already does this — silent-fail trap T2)
  - BFF S2S code path swapped from `ClientCredentials` to `DefaultAzureCredential`
  - Drop the Dataverse S2S app-reg entirely
  - Drop `Dataverse-S2S-ClientSecret` from KV
  - Drop the corresponding rotation ceremony

**Scope estimate**: **Medium — 1–2 weeks** for this specific slice; **larger if paired with r3 NG1** (both Dataverse access stacks unified) — 4–6 weeks per r3 NG1 estimate

**Files affected**: `src/server/api/Sprk.Bff.Api/Services/Dataverse/**` (~15–20 files); auth setup in `Program.cs`; deploy scripts (drop S2S app-reg creation)

**Dependencies**:
- **ADR-028 amendment** (auth architecture is codified)
- Should coordinate with r3 task 023 (`bff-auth-closure-spaarke-auth`) which is closing auth code via `@spaarke/auth` — closure + consolidation should land together for a coherent auth story

**Impact if not done**:
- Two app regs, two secrets, two rotation cycles persist per customer forever
- 24-month rotation coordination hits every customer eventually
- r3 NG1 (Two Dataverse access stacks) remains deferred with no forcing function to close it

**Migration for existing deployments**:
- New customers get the new pattern
- Existing customers migrated per-customer during scheduled maintenance windows
- Old S2S app-reg + secret dropped after cutover verified per customer

**r3-charter fit**: **High** — this is exactly the axis r3's NG1 identified as "needs its own ADR." If r3 is going to tackle NG1, r1's #3 is one concrete deliverable within that scope. If r3 wants to keep NG1 deferred, #3 could be spun as its own smaller project.

---

### #4 Graph app-role parity via code constants

**Problem**: Silent-fail trap T3: the ~11 Graph app-role grants on the BFF app-reg must be replicated onto the UAMI service principal, or app-only Graph calls (SPE, mail, groups, Teams) silently 403 despite delegated flow working. The expected role list is maintained in **a runbook script**, not in code.

**Evidence from r1**:
- r1 §9.2 lists ~11 Graph app-roles required on UAMI SP (updated from v2's 7)
- r1 §4B trap T3 diagnoses this; the fix is "H10 post-step queries and asserts count matches expected list"
- The expected list lives in a PowerShell script (`Register-EntraAppRegistrations.ps1` or a sibling)
- Adding a new capability requiring a new Graph role currently requires: (1) add to BFF app-reg grant script, (2) add to UAMI SP grant script, (3) hope both scripts stay in sync — no forcing function

**Proposed refactor**:
- Move the expected Graph app-role list to a **compile-time constant** in the BFF (`GraphAppRoles.cs` or similar)
- Include: role GUID + display name + owning module + why-required comment
- H10 reads the constant, applies grants to UAMI SP, and verifies
- Adding a new capability that needs a new Graph role = **one code change in one place**; H10 automatically syncs

**Scope estimate**: **Small — days**
- One constants file + one Graph SDK helper method + integration into H10

**Files affected**: 1 constants file + 1 helper file + `Register-EntraAppRegistrations.ps1` becomes a consumer of the code constant (or is retired)

**Dependencies**: None. Can land independently.

**Impact if not done**:
- Silent-fail trap T3 remains a runbook-verified concern per customer
- Drift risk between BFF app-reg grants and UAMI SP grants persists
- Every new module adds two grant-list edits

**r3-charter fit**: **High** — perfect fit for r3 tasks 040 (expand ArchTests) as a compile-time enforcement rule. Could also fit as a forcing-function in r3 task 042 (CI gates): "if new Graph role added, both grant scripts + UAMI SP + constant list must all agree."

---

## Overlap map: r1 refactors × r3 existing scope

| Refactor | r3 task overlap | Fit assessment |
|---|---|---|
| **#1 KV federation** | None currently | New scope; would need a new task in r3 workstream. Config architecture, not code hygiene. |
| **#2 Fail-fast validation** | Task 040 (ArchTests), Task 042 (CI gates) | Small additive item; fits existing forcing-function scope naturally |
| **#3 App-reg consolidation** | **NG1** (Two Dataverse stacks unification — explicitly deferred, needs ADR); Task 023 (bff-auth-closure-spaarke-auth) | Same axis as NG1; r3 should decide whether to bring NG1 in-scope or spin #3 as a separate project |
| **#4 Graph app-role parity** | Task 040 (ArchTests), Task 042 (CI gates) | Small additive item; fits forcing-function scope naturally |

## Adjacent complexity drivers (informational, not part of this ask)

These are related complexity drivers r1 identified but that are NOT part of this ask. r3 may already own them via existing tasks:

| Driver | Current r3 owner | Notes |
|---|---|---|
| BFF at 269 DI registrations | **Partially** — Task 026 (`bff-di-decompose-and-finance-rename`), G7 (decompose CommunicationModule from 75 registrations) | Full 269 → target reduction would be a larger follow-on beyond current r3 scope |
| Config-seed manifest as authoritative | Not in r3 | Small follow-on to r1's H12a/b/c work |
| Code-page monorepo consolidation | **Assessment scope in r3** (Task 014: `assess-code-pages-build-sprawl`) | Remediation deferred pending assessment |
| Solution dependency topology (auto-order from manifests) | **Assessment scope in r3** (Task 013: `assess-dataverse-model-alm`) | Remediation deferred pending assessment |
| SPA runtime config (kill baked-at-build-time BFF host) | Not in r3 | Pairs naturally with #1 KV federation; could be included in that scope |

---

## What r1 is doing in the meantime (informational — not a prescription)

r1's design.md v3 currently absorbs **#2 fail-fast config validation** + **#4 Graph app-role parity** into r1 **Phase E** (see r1 design.md D20 locked decision). r1 can ship without waiting on r3, and Phase E provides the fail-fast + code-constant discipline immediately.

**However, per the owner's 2026-08-12 direction, r1 is pausing after design.md v3 completion pending r3's assessment.** If r3 chooses to own #2 + #4 with more rigor (e.g., ArchTests + CI gates rather than one-off code additions in r1's Phase E), r1 will:
- Remove D20 from r1 design.md
- Remove the Phase E absorption tasks
- Reference r3's implementations as prerequisites in r1's success criteria

If r3 chooses to leave #2 + #4 to r1, r3 should note in its own plan.md that these forcing functions land via r1 first.

**In either case, r1 is not doing #1 or #3 — those need r3 (or a separate project r3 spawns).**

---

## Coordination timeline

```
2026-08-12          r1 design.md v3 in progress → completion imminent
2026-08-12+         r1 pauses; hands off this ask to r3
                    r3 assesses this ask (owner-timed)
                    r3 determines approach:
                      • absorb which refactors (#1/#2/#3/#4)
                      • revise NG1 status
                      • add new tasks or amend existing
                    r3 executes its plan
r3 complete →       r1 resumes: revisits design.md, updates references,
                    confirms architecture matches r3's landed state
r1 continues →      /design-to-spec → /project-pipeline → build → ship
```

---

## Suggested decision framework for r3

For each of the four refactors:

1. **Charter fit** — is this within r3's "BFF quality + forcing functions + hygiene" charter? (#2, #4 = yes trivially; #1 = medium; #3 = yes if NG1 comes off deferred)
2. **Scope size** — small enough to add as a task without derailing r3's plan? (#2, #4 = yes; #1 = medium new task; #3 = large + ADR work)
3. **Existing r3 prior art** — does an existing task cover it? (#2/#4 → 040/042 forcing functions; #3 → NG1 axis; #1 → nothing)
4. **Dependencies** — what needs to land first? (#2 → nothing; #4 → nothing; #1 → prefers #2 first; #3 → ADR-028 amendment)
5. **Sequencing vs r3's existing tasks** — does adding this delay r3's stated Graduation Criteria (A+ grade)? Or does landing it strengthen the grade?

**A likely r3 decision framework**:
- Definitely absorb #2 + #4 as forcing-function extensions (small, high-leverage)
- Bring NG1 in-scope OR spin #3 as its own project (owner decision)
- Consider #1 as a new r3 task if config architecture fits the charter; otherwise spin as separate project

r1 has no preference on the specific approach r3 takes — the ask is that r3 assesses and decides, and r1 aligns to r3's decision.

---

## Contact / questions

- r1 design.md v3 (current state): `projects/customer-provisioning-orchestration-r1/design.md` (branch `work/customer-provisioning-orchestration-r1`)
- r1 companion docs:
  - `PROJECT-UPDATE-2026-08-12.md` (2026-08-12 assessment)
  - `COMPONENT-INVENTORY.md` (bill of materials)
  - `notes/pricing-research-2026-08-12.md` (verified Azure + M365 pricing)
- r1 silent-fail trap catalog (T1–T6): r1 design.md §4B
- r1 D20 locked decision (Phase E absorption of #2 + #4): r1 design.md §3

For clarifying questions during r3's assessment, refer to owner Ralph Schroeder or open a session in the r1 worktree.
