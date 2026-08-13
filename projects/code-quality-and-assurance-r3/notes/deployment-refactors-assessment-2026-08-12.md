# r3 Assessment — Deployment-Complexity Refactor Ask from `customer-provisioning-orchestration-r1`

> **Assessed by**: code-quality-and-assurance-r3 (this program), 2026-08-12
> **Ask**: [`notes/deployment-complexity-refactors-ask-2026-08-12.md`](deployment-complexity-refactors-ask-2026-08-12.md)
> **Method**: grounded against live `src/server/api/Sprk.Bff.Api/` on `work/code-quality-and-assurance-r3` (verify-before-acting) — NOT a full Fable assessment; that is proposed below for #1.
> **Status**: ✅ **DECIDED 2026-08-13** — owner accepted into r3. Decisions: (1) **accept** all four; (2) **#1** → assess-first (task 017 → `workstreams/config-deployment/`); (3) **#3** → **split** (see §Addendum 2026-08-13 below, after Fable grounding corrected the premise): **#3a** = task 060 (drop the vestigial separate Dataverse S2S app-reg, scripts/docs/KV, no code), **#3b** = the shared-lib `ClientSecret`→MI migration → folded into the NG1/task-011 track; no ADR-028 amendment; (4) **#2 + #4** → r3 owns with ArchTest+CI (tasks 061/062 + rules in 040/042 — rule c is a report-only flag for #3b). **NG1** → **assess-then-decide** track owned by task 011, now covering the access-stack unification + #3b. Tasks wired 2026-08-13; see spec.md FR-24..FR-27 + plan.md Phase 6 + TASK-INDEX.

---

## 0. Headline finding — the code is AHEAD of the ask on #2 and #3

r1's scope estimates were written from the deployment/provisioning vantage. Grounding against live BFF code changes the picture materially:

| Refactor | r1's framing | Live-code reality (grounded 2026-08-12) | Net effect on r3 scope |
|---|---|---|---|
| **#3 App-reg consolidation** | "1–2 week refactor: swap S2S ClientCredentials → UAMI federated" | **Credential half already landed** — `appsettings.template.json:79,100` document **AUTHV2-042 Phase C**: BFF Dataverse service-context flows already use `DefaultAzureCredential` (MI); `BFF-API-ClientSecret` is a "no-op, safe to remove after deploy verification". BFF CLAUDE.md:104,221 confirm. | Shrinks to **finish + enforce + provision-cleanup**, not greenfield. Likely **no ADR amendment** (Phase C already under ADR-028). |
| **#2 Fail-fast validation** | "annotate 26 IOptions from scratch" | **Partially done** — 56 `ValidateOnStart`/`IValidateOptions` + 80 `[Required]` across 152 IOptions-touching files; coverage is **inconsistent**, not absent. | Becomes a **consistency sweep + ArchTest**, smaller and a cleaner r3 forcing-function fit. |
| **#1 KV federation** | "20+ tokens; add /api/config/client" | **94** deploy-time tokens in `appsettings.template.json`; `/api/config/client` **already exists** (`Api/ConfigEndpoints.cs:38`, anonymous, MSAL config); shared `SecretClient` already registered (SpeAdminModule). | Real medium refactor, but with existing building blocks. Touches surfaces r3 hasn't assessed (code-pages/external-spa). |
| **#4 Graph app-role parity** | "list lives in a runbook script" | Confirmed — list is in `scripts/Register-EntraAppRegistrations.ps1`; **no code constant**. | Genuinely not done; clean small r3 fit. |

**Implication**: r3's contribution is mostly *finishing, making-uniform, and forcing-function-enforcing* work already in flight — which is precisely r3's charter (harden so the grade holds). This is cheaper and more accurate than accepting r1's greenfield estimates.

---

## 1. Charter fit vs the rubric (D1–D11)

Deployment/config complexity is squarely a quality concern under the R3 rubric:
- **D3 Security** — secrets in KV, credential model, least privilege (drives #1, #3, #4).
- **D6 Consistency** — uniform config-validation discipline (#2), single-source-of-truth role list (#4).
- **D10 ALM/build hygiene** — reproducible builds, no drift-prone token substitution, clean provisioning (#1, #3, #4).
- **D8 Dependency/supply-chain** — dropping a rotating client secret removes an ongoing operational liability (#3).

All four legitimately raise the BFF (and cross-surface) grade. **This ask is on-charter.** The question is *which structural home* and *how much to commit now vs assess first*.

---

## 2. Per-refactor recommendation

### #2 Fail-fast configuration validation — ✅ ACCEPT (forcing-function)
- **What r3 does**: a **consistency-hardening task** — bring the existing partial `[Required]`/`ValidateDataAnnotations().ValidateOnStart()`/`IValidateOptions<T>` coverage to uniform coverage on customer-critical options; add cross-property invariants; fresh BFF missing a required setting crashes at startup with a clear list. PLUS an **ArchTest** (task 040): "every customer-critical `IOptions<T>` registered in DI is annotated + validated-on-start." PLUS a CI gate (042) if warranted.
- **Home**: new task in the forcing-functions phase, + a rule appended to 040/042.
- **Scope**: small–medium (sweep over an already-mostly-compliant surface).
- **Dependencies**: none. Can land early.

### #4 Graph app-role parity via code constants — ✅ ACCEPT (forcing-function)
- **What r3 does**: move the ~11-role expected list into a compile-time `GraphAppRoles.cs` (GUID + display name + owning module + why-required) + a Graph-SDK verification helper; add an **ArchTest** that the constant is the single source of truth; `Register-EntraAppRegistrations.ps1` + r1's H10 become *consumers* of the constant.
- **Home**: new small task + rule in 040/042.
- **Scope**: small (1 constants file + 1 helper).
- **Dependencies**: none. Coordinates with r1 (r1's H10 consumes the constant) — clean code/provisioning split (r3 owns the code constant + BFF verifier; r1 owns applying grants).

### #1 KV federation (single source of truth) — 🔎 ROUTE THROUGH r3 ASSESSMENT FIRST
- **Why not just build it**: it's cross-surface (BFF + PCF `environmentVariables.ts` + ~28 code-page SPAs + external-spa + Office add-ins + `Deploy-Release.ps1` + `appsettings.template.json`), it touches surfaces r3 has **not yet assessed** (code-pages/build-sprawl = task 014; external-spa isn't even in r3's surface list), and the grounding already shows the premises drift (94 tokens not 20+, endpoint already exists). Committing a 2–4 week cross-surface refactor on r1's estimate alone violates r3's assessment-first principle.
- **What r3 does**: add a **configuration-architecture assessment** (Fable-verified, via the `quality-assessment` workflow) producing a verified `workstreams/config-deployment/design.md` — inventory the 5 config sources, the 94 tokens, the client-config path, the cache ceremony, KV-provider wiring — then task remediation from the verified design (assessment-first, same as every surface).
- **Home**: a new **Deployment & Configuration Hygiene** workstream (`workstreams/config-deployment/`).
- **Scope**: assessment now (read-only, conflict-free); remediation sized by the design.
- **Dependencies**: prefers #2 to land first (validate KV-sourced config before dropping tokens).

### #3 App-reg consolidation (UAMI federated for Dataverse S2S) — ✅ ACCEPT the bounded slice; keep NG1 deferred
- **Reframe (critical)**: the credential-model half is **already code-complete** (AUTHV2-042 Phase C — MI/`DefaultAzureCredential` for Dataverse service-context). What remains is bounded: (a) finish the deprecation removal (`BFF-API-ClientSecret` for the Dataverse path), (b) drop the redundant **Dataverse S2S app-reg + KV secret + rotation** in provisioning/deploy, (c) verify (MI as Dataverse Application User — r1's trap T2), (d) an ArchTest/forcing-function preventing regression to a secret-based Dataverse path.
- **#3 ≠ NG1.** NG1 (Idea #742) = unifying the two Dataverse **access stacks** (`ServiceClient` vs raw-HTTP client implementations). #3 = the **credential/identity model** for the app-only Dataverse path. Different axes; #3's code is largely landed, NG1's is not. **Recommendation: own the bounded #3 finish in r3, coordinated with task 023 (coherent auth story); keep the broader NG1 stack-unification deferred (#742).**
- **ADR**: verify whether ADR-028 already sanctions MI-for-Dataverse-S2S (the "Phase C" language strongly implies yes). If yes → **no amendment needed**, just completion + forcing-function. If a gap exists → an ADR-028 amendment (Path B, §6.5) authored before the code lands.
- **Home**: a task paired with/adjacent to task 023 in the BFF workstream (Tranche A/B per contention).
- **Scope**: small–medium (code mostly done; provisioning + verification + forcing-function remain).
- **Dependencies**: coordinate with 023; possible ADR-028 amendment (likely not).

---

## 3. Proposed structural additions to r3 (IF accepted)

A new **Deployment & Configuration Hygiene** workstream + forcing-function extensions, slotting into the existing phase model:

| New/changed | Type | Phase/home | Refactor |
|---|---|---|---|
| `workstreams/config-deployment/` + config-architecture **assessment** task | assessment (Fable) | Phase 1 (assessment) | #1 |
| **config-validation hardening** task (uniform `[Required]`+ValidateOnStart+IValidateOptions) | remediation | Phase 3/4 | #2 |
| **Graph app-role constants** task (`GraphAppRoles.cs` + verifier) | remediation | Phase 3/4 | #4 |
| **finish AUTHV2-042 Phase C** task (drop redundant Dataverse S2S app-reg/secret; verify; forcing-function) | remediation | Phase 2 (BFF), paired w/ 023 | #3 |
| ArchTest rules in **task 040**: (a) IOptions-validated-on-start, (b) GraphAppRoles single-source, (c) no secret-based Dataverse credential path | forcing-function | Phase 4 | #2/#3/#4 |
| CI-gate rules in **task 042**: config-validation + role-parity checks | forcing-function | Phase 4 | #2/#4 |
| #1 remediation tasks | remediation | Phase 5 (deferred) | #1 — created after the #1 assessment design |

Rubric/scorecard: add a **"Deployment & Config" line** to `SCORECARD.md` (or fold into D10) so the improvement is measured. NG1 Idea #742: annotate that the bounded #3 credential-slice is being addressed in r3; the stack-unification remains deferred.

Net task delta if fully accepted: ~4 new tasks + 3 ArchTest rules + 2 CI-gate rules (≈ 29 → ~33 tasks). No new NuGet expected; publish-size neutral-to-down.

---

## 4. Owner decisions required (CLAUDE.md §6 / §6.5)

1. **Accept the ask into r3?** (Recommend **yes** — on-charter; r3 is the right home; grounding shows the work is mostly finish/enforce.)
2. **#2 + #4 ownership** — r3 owns them with ArchTest+CI rigor (Recommend **yes**; r1 then removes its D20/Phase E absorption and references r3 as prerequisite). Alternative: leave to r1's Phase E and r3 only adds the ArchTests later.
3. **#3 scope + ADR** — own the **bounded** #3 finish (drop redundant Dataverse S2S app-reg/secret + forcing-function), paired with task 023, **keep NG1 (#742) deferred** (Recommend **yes**). Confirm whether an ADR-028 amendment is needed (likely **not** — Phase C already landed) or is a Path-B pre-req.
4. **#1 routing** — assess-first via a new config-deployment workstream (Recommend **A**) vs commit a remediation workstream now vs spin as a separate project.

## 5. Coordination

- **r1 is paused** pending this decision; it resumes with r3's landed state as baseline. Whatever r3 accepts, r1 removes the corresponding Phase E / D20 absorption and references r3's implementation as a prerequisite.
- **Task 023** (auth closure via `@spaarke/auth`) and **#3** should land as one coherent auth story — sequence them together.
- **BFF contention** (19 worktrees): all four are BFF-touching; `/conflict-check` before each PR; #1's cross-surface breadth needs extra coordination (external-spa/code-pages).
- **Hot-path**: r3's single declaration already covers bff/spaarkeai/ci/skills; #1 adds external-spa touch — note in the INDEX row if #1 remediation is accepted.

---

## Addendum 2026-08-13 — #3 split after Fable grounding (owner decision: 060 = clean app-reg drop; migration → NG1/task-011)

**Correction to §0/§2:** the "#3 credential half already landed" claim was **wrong for the shared-lib camp**. AUTHV2-042 (commit `c4bb4a4e7`, 2026-05-19) migrated only the `Services/Ai` raw-HTTP camp (`DataverseHttpServiceBase` + 13 files, DI `TokenCredential` from `Program.cs:44-47`). The BFF's **own** shared-lib Dataverse path is **still `ClientSecret`-based in prod** (verified 2026-08-13). ADR-028 verdict: **NO amendment** — `ADR-028:24` already mandates `DefaultAzureCredential` for "Dataverse service identity", so the remaining secret paths are **violations to fix** (Path C compliance), not new scope.

**Owner decision (2026-08-13):** split #3 —
- **#3(a) → task 060 (clean, in-scope now):** drop the **vestigial separate `spaarke-dataverse-s2s-*` app-registration + `Dataverse-S2S-*` KV secrets + rotation** from provisioning/deploy/docs. It has **zero code consumers** (`grep Dataverse-S2S|DATAVERSE_CLIENT_ID` across `src/` → none; consolidated to `API_CLIENT_SECRET` 2026-01-07 per `auth-azure-resources.md:407`). Scripts/docs/KV only — no BFF code change. Delivers r1's literal "2 app-regs → 1" ask.
- **#3(b) → NG1 / task 011 (assess-then-decide):** migrate the BFF's own shared-lib Dataverse path `ClientSecret`→MI. It touches **exactly the two files NG1 is about** and carries an **identity-attribution change** (Dataverse writes attribute to the MI app user, not the app-reg user), so it is decided on task 011's verified NG1 design.

### #3(b) grounded detail (for task 011 — do NOT lose this)
Still secret-based in prod (the migration scope for 011's NG1 design):
- `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs:40-64` — `AuthType=ClientSecret` from `API_CLIENT_SECRET` (the **BFF app-reg's** secret, not a separate S2S app-reg); throws if absent; prod `IDataverseService` singleton at `GraphModule.cs:46-51` (9 narrow interfaces).
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:40,51-56` — hard-requires `Dataverse:ClientSecret`; `GraphModule.cs:56-63` (`IEventDataverseService`, `IFieldMappingDataverseService`).
- `DataverseWebApiClient.cs:39-53` — secret-preferred ordering (MI fallback exists but `API_CLIENT_SECRET` is always set for OBO → secret wins).
- `DataverseOptions.cs:32 [Required] ClientSecret` + `ConfigurationModule.cs:30-34 ValidateOnStart` → removing the secret today **crashes startup** (so the `appsettings.template.json:79` "no-op, safe to remove" comment is **drift**).
- **Migration pattern to copy**: the migrated camp `Services/Ai/DataverseHttpServiceBase.cs` (DI `TokenCredential`) + `GraphClientFactory.cs:104-148` (flag-gated MI/local-dev branch, task 041); ServiceClient supports a `tokenProviderFunction` ctor → **no new NuGet**.
- **Prescriptive ordering when 011 decides to do it**: MI-verify (r1 trap T2, MI PrincipalId `56ae2188-c978-4734-ad16-0bc288973f20` as Dataverse Application User in both envs, `auth-deployment-setup.md §6/§9c`) → migrate code → relax `DataverseOptions [Required]` → drop `Dataverse:ClientSecret`/`BFF-API-ClientSecret`-for-Dataverse (keep `BFF-API-ClientSecret` for **OBO**).
- **DO NOT TOUCH**: OBO (`BFF-API-ClientSecret` for OBO), SpeAdmin per-tenant container secrets (`SpeAdminGraphService.cs:4054,4177-4184`, ADR-028:24 exception 1), `DataverseAccessDataSource.cs:49-76` (user-context/OBO path Phase C preserved).
- **Tracking**: task 040 rule (c) FLAGS this remaining secret path (report-only / `[Skip]`) until 011's NG1 design lands the migration, then it becomes enforcing.
- **Stale doc to fix when (b) lands**: `docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md:1007` ("Managed Identity: Rejected — doesn't work for Dataverse S2S") contradicts ADR-028:24 + the live §9c smoke test.
