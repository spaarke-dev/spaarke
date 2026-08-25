# Open Questions — auth-v4 change-request integration (2026-08-25)

> **Date**: 2026-08-25
> **Status**: ✅ **ALL 11 RESOLVED 2026-08-25 (owner)** — see disposition table below. Individual Q sections retained for reasoning trail.
> **Deliverable**: 4 of 4 (auth-v4 integration response) — every open question in root CLAUDE.md §6 escalation format
> **Source**: auth-v4 canonical `PROVISIONING-CHANGE-REQUEST.md` (§5.1 DECIDED block, §9.2, §10 addendum + CORRECTION, §10.6) + adversarial verification of the 5 dimensional analyses + fresh git/code verification 2026-08-25
> **Companions**: [`auth-v4-integration-remediation-plan.md`](auth-v4-integration-remediation-plan.md) §9 · [`decisions/adr-028-a4-integration-conflict-resolution.md`](decisions/adr-028-a4-integration-conflict-resolution.md) · [`auth-v4-integration-draft-punch-rows.md`](auth-v4-integration-draft-punch-rows.md)

## ✅ Resolution table (owner 2026-08-25)

| # | Priority | Disposition | Downstream impact |
|---|---|---|---|
| Q1 | LOW | **SKIP** discharge reply — auth-v4 project complete/merged; §10.6 drift stays as their historical artifact | Removes S4 from sequencing. `auth-v4-coord-response` file not created. |
| Q2 | CRITICAL | **RATIFY reading (a)** — stamp's own UAMI as FIC issuer for customer-tenant Model 2 | A42 ports tenancy guard per §9.2 either way; `GraphAppRegistrationProvisioner.cs:547-557` already reading-(a)-consistent; formal §6 escalation doc filed in this resolution |
| Q3 | HIGH | **SIGN as proposed at 2026-11-23** — §6.5 hybrid (Path C for BFF-API-ClientSecret + time-boxed Path A for Dataverse-ClientSecret) | `decisions/adr-028-a4-integration-conflict-resolution.md` marked APPROVED; EDITs 1-4 unblocked (fire post-A35) |
| Q4 | MEDIUM | **PORT master's MI environment contract into `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`** during A35 conflict resolution; keep `auth-deployment-setup.md` stub | A35 conflict handling shape confirmed |
| Q5 | MEDIUM | **CONTRACT-PARITY** — C# provisioner + `-FicOnly` script both under one contract, parity tests pin (issuer,subject,audience) semantics + AADSTS70025 + exit-2 reporting | A42 scope confirmed |
| Q6 | HIGH | **SECRET-FREE-BY-DEFAULT** once A36-A42 land; **NO Model 2 customer provisioning** until A36-A42 land | H4 default set; task 186 gate confirmed |
| Q7 | LOW-MED | **No live prod exists** — only spaarkedev1 is live; prong 3 of §6.5 narrows to spaarkedev1 + hypothetical greenfield stamps | §6.5 resolution prong 3 narrowed (see decision doc); `Seed-ProductionKeyVault.ps1` + `Configure-ProductionAppSettings.ps1` reduced to aspirational-not-active |
| Q8 | LOW | **WORDING DRIFT confirmed** — Model 1 uses ONE shared BFF UAMI per environment (`sprk-{env}-shared-bff-uami`); fix design.md D3 alongside A41; add Model 1 UAMI naming row to Naming Standards table (§9.2 in design.md, line ~658) | A41 scope +30 min: D3 wording fix + Naming Standards row |
| Q9 | HIGH | **CONFIRMED**: task 186 targets "dev Model-2-**Spaarke-hosted** stamp" (POML line 20 + 60) → profile `spaarke-hosted-model2` → intra-tenant → §9.2 (Q2) does NOT affect task 186 | Task 186 unaffected by §9.2; can proceed after A35-A42 land |
| Q10 | MEDIUM | **OWNER manages freeze manually** — 3-4 active projects; owner pauses if necessary; skip broadcast; rely on `/conflict-check` per-PR + owner judgment | Coordination doc will note freeze-broadcast skipped by owner directive |
| Q11 | MEDIUM | **r1 owns BFF startup credential self-proof** — H9 gate + BFF warmup change tracked as BFF-touching task with §10 obligations: Placement Justification + publish-size measurement + `tests/unit/Sprk.Bff.Api.Tests/` update | Not 186-blocking; queued for post-186 Phase-F planning |

**Net effect**:
- One CRITICAL escalation now formally answered (Q2 = reading (a); guard-port in A42 either way).
- Two owner sign-offs recorded (Q3, Q4).
- Two verifications performed + confirmed (Q7 = no live prod; Q9 = 186 is Spaarke-hosted).
- One scope adjustment (Q8 → A41 +30min).
- One process choice (Q10 — owner-manages).
- One BFF-touch obligation tracked (Q11 → future task with §10 checklist).
- One action removed (Q1 discharge reply skipped).

## Executive summary (original — retained for context)

11 open questions. **One is critical-path and genuinely undecided (Q2 — §9.2 Model 2 customer-tenant FIC issuer)**; one is a decided item needing only discharge + drift cleanup (Q1); the rest are sign-offs, date confirmations, and verification items surfaced by the adversarial verifiers. Priority order: Q2 → Q3 → Q9 → Q6 → Q5 → Q4 → Q1 → Q7 → Q10 → Q8 → Q11.

---

## Q1 — §5.1 Model 1 app-reg topology: discharge the DECIDED block; reconcile §9 vs §10.6 (priority: LOW — decision already made)

🔔 **Human Input Required**

- **Situation**: auth-v4's canonical doc carries "✅ DECIDED 2026-08-25 (owner): Reading 1 — ONE shared multitenant app registration for Model 1." This *ratifies* the split r1's owner already applied 2026-08-19 (spec.md:253 MUST split; design.md:60 D2 correction; design.md:1083-1085 §9.1 v3.5 note; FR-39/FR-40; R23 CLOSED at design.md:1528). The DECIDED block's residual ask ("please edit spec.md:236 / design.md:57 — the one place the estate still contradicts") is **stale** — those edits landed six days earlier at today's line numbers 253/60, and §9 of the same canonical doc already accepts them "as applied." Meanwhile §10.6 of the same doc (same-day drift) still says "§5.1's open decision … is still open and still yours."
- **Options**: (a) send a discharge reply via `auth-v4-coord-response` citing current line numbers + ask auth-v4 to fix §10.6's internal drift; (b) also re-edit our spec/design (nothing substantive to edit — only append ratification cites); (c) do nothing.
- **Recommendation**: (a) + append ratification cites to spec.md:253 and design.md:60 ("Ratified 2026-08-25: owner DECIDED block, auth-v4 canonical §5.1"). No substantive edit exists to make. Note in the reply that BOTH script flags are real (verified on master: `-FicOnly` ×16, `-SkipClientSecret` ×4 — the "flag discrepancy" is a coexistence, with `-FicOnly` the consumption contract).
- **Owner decision needed by**: before the discharge reply is sent (this week).
- **Consequences of not deciding**: auth-v4 keeps carrying a phantom open item; future readers of §10.6 believe §5.1 is undecided; the stale-ask loop repeats.

## Q2 — §9.2 Model 2 customer-tenant FIC issuer: reading (a) vs (b) (priority: CRITICAL — auth-sensitive, blocks customer-owned Model 2)

🔔 **Human Input Required**

- **Situation**: auth-v4 asked (2026-08-21, reiterated 2026-08-25): for Model 2 in a **customer's tenant**, does the stamp's app-reg federate (a) **its own stamp UAMI** (same tenant), or (b) the **shared Spaarke UAMI** (cross-tenant)? Entra requires app-reg + UAMI in the SAME tenant; a cross-tenant FIC creates successfully and fails only at token exchange (silent). r1's own 2026-08-19 response TL;DR used reading-(b) phrasing ("a FIC trusting the shared BFF UAMI") — the ambiguity that triggered §9.2 — while task 130's SHIPPED code implements (a): **verified 2026-08-25 by direct read**, `GraphAppRegistrationProvisioner.cs:547-557` computes issuer per profile (`customer-owned-model2` → customer tenant; else Spaarke tenant), header `:73-78` states the per-profile recipe, subject = `InterStepState.MiObjectId` (stamp UAMI from H2a `uami.bicep`). Structural evidence for (a) is decisive twice over: (b) is a cross-tenant pair (unsupported), AND customer-tenant compute cannot mint assertions as a Spaarke-tenant UAMI (MIs are tenant-bound). ⚠️ However: the C# provisioner has **NO cross-tenant refusal guard** (verified — `Assert-SpaarkeFicTenancy` exists only in master's PS script), L2 cannot exchange-verify (GOTCHA 2), and task 130 shipped before §9.2 was formally answered.
- **Options**: (a) ratify reading (a) — stamp's own UAMI, always intra-tenant; formalize via decision record + discharge reply + port the tenancy guard into `CreateFic` (punch row A42); (b) reading (b) — structurally impossible for MI-FIC; ADR-028 A4 standing guard then mandates the **KV-certificate** path ("dropped, not deferred") — reopening per-stamp cert issuance/renewal/rotation, the unexercised ordered-provider middle tier, H3/H4 cert branches, T4 probe changes.
- **Recommendation**: **(a)** — shipped code, TENANCY-AND-CREDENTIALS §3 row 3 (note: that doc *assumes* (a); it restates rather than independently confirms), the §5.1 DECIDED block's own parenthetical, and platform constraints all converge; (b) buys nothing (a) doesn't provide. This is an auth-sensitive topology decision → owner ratification required per §6 regardless of how decisive the evidence is. Port the guard either way (it is safe under (a); under (b) it correctly refuses an impossible shape — flagging loudly instead of failing silently at first customer OBO).
- **Owner decision needed by**: BEFORE any Model 2 customer-owned-tenant provisioning run, and before the A42 reconciliation row dispatches its customer-owned branch. Auth-v4 asked twice for an answer "before Wave G-3 task 130 executes" — 130 has already shipped, so the debt is overdue.
- **Consequences of not deciding**: a literal-execution agent wiring a customer-owned stamp can pass the shared Spaarke UAMI's principalId with a customer-tenant issuer — the FIC creates fine (no guard in the C# path, no L2 exchange-verify) and fails weeks later at the customer's first OBO login as an opaque AADSTS error: the exact silent failure auth-v4 flagged twice.

## Q3 — §6.5 resolution sign-off + Path-A sunset date: 2026-11-22 vs 2026-11-23 (priority: HIGH)

🔔 **Human Input Required**

- **Situation**: the never-delete conflict-resolution record ([decisions doc](decisions/adr-028-a4-integration-conflict-resolution.md)) proposes Path C for `BFF-API-ClientSecret` + time-boxed Path A for `Dataverse-ClientSecret`. Root §6.5 requires the human reviewer to choose/refine the path. Additionally, two candidate sunset dates exist: soft-delete recovery runs to **2026-11-22**; auth-v4 obligation 051-E cites **2026-11-23**. The record uses 11-23 as the outer bound.
- **Options**: (a) sign as proposed (11-23, coordinate with auth-v4); (b) tighten to 11-22 (recovery horizon governs); (c) refine paths.
- **Recommendation**: (a), with a one-line confirmation from auth-v4 (via `auth-v4-coord-response`) on which date governs their 051-E execution.
- **Owner decision needed by**: before the main session applies EDITs 1-4 + companion sweep (post-A35-merge).
- **Consequences of not deciding**: the stale blanket rule keeps directing H4/rotation to treat a deleted secret as mandatory estate (boot-refusal or silent-FIC-mask on re-seed), while nothing polices re-creation/purge — both live failure directions stay open.

## Q4 — Doc home for the MI environment contract: port-to-canonical vs un-stub (priority: MEDIUM — decide at merge time)

🔔 **Human Input Required**

- **Situation**: r1 task 001 stubbed `docs/guides/auth-deployment-setup.md` (8 lines → pointer to `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`). Auth-v4's §10 CORRECTION then promoted the MI environment contract INTO master's 898-line version (§1 prereqs, §5.1 UAMI data-plane RBAC, §6 Dataverse app user) declaring it "the operational source." At the A35 merge, keep-ours deletes the contract's declared home; keep-theirs silently reverses task-001 consolidation.
- **Options**: (a) port master's §1/§5.1/§6 content into `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`, keep the stub with refreshed pointers, notify auth-v4 the operational source moved; (b) un-stub `auth-deployment-setup.md` and accept two operator guides.
- **Recommendation**: (a) — preserves the single-authoritative-guide decision (root CLAUDE.md §17 names SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md as the single source and lists auth-deployment-setup.md among its superseded stubs).
- **Owner decision needed by**: during A35 conflict resolution (the file WILL conflict — both sides modified it).
- **Consequences of not deciding**: the merge resolver improvises; either the live MI contract loses its home or the doc estate forks into two "authoritative" operator guides.

## Q5 — Task-130 C# provisioner vs `-FicOnly` script: converge or contract-parity? (priority: MEDIUM-HIGH — shapes A42)

🔔 **Human Input Required**

- **Situation**: r1's task 130 landed its own C# FIC creation (Graph SDK) — the §9.3-pre-authorized fallback ("take the task-130 fallback path and we will reconcile — do not block on us"). Master's script is the live-verified operator path (exit codes 0/1/2, triple-keyed idempotency, AADSTS70025 exact-match retry, tenancy refusal). Two implementations of the same credential operation now exist.
- **Options**: (a) H3 shells out to the script (subprocess, never dot-source) — one implementation, but pwsh-from-L2 tension with the Option-D no-shell-out invariant (spec.md:279: sole sanctioned PowerShell path is the H14a sidecar); (b) keep both under ONE written contract + parity tests (C# = L2 runtime path, script = operator path).
- **Recommendation**: (b) short-term — respects the Option-D invariant, keeps the operator path; A42 ports the missing tenancy guard + 70025 exact-match + exit-2-equivalent "persisted-verified vs exchange-verified" reporting into the C# path, with parity tests pinning both to the same (issuer,subject,audience) semantics.
- **Owner decision needed by**: at A42 dispatch (task 205).
- **Consequences of not deciding**: the two estates drift — exactly the duplicate-estate failure auth-v4's §10 DELIVERED warned against; §11's two live invariants (wrong-subject detection, propagation-flap retry) never get a designated owner and stay unproven end-to-end.

## Q6 — H4 default credential type for NEW Model 2 customers during the FR-39 transition (priority: HIGH)

🔔 **Human Input Required**

- **Situation**: FR-39 keeps both credential paths pluggable until auth-v4's Phase 5 — but Phase 5 already executed on dev (E-3 closed), while the rollout is per-environment. Nothing currently states what H4 seeds for a **newly-provisioned Model 2 customer today**: a client-secret entry in the per-customer `kv-{customerId}-{secretsVer}` (legacy path — fleet decays toward secrets), or secret-free from birth (FIC + §10.2 settings — requires the A36-A42 chain in place). The verifiers flagged this as unanswered in both the change request and the analyses; it directly governs H4's day-to-day behavior and the per-customer KV/RBAC surface at scale (4 role grants + settings replicated per customer vault).
- **Options**: (a) secret-free-by-default for all new environments (recommended by the §10.2 contract's shape; requires A36-A42 landed + ordering guard honored); (b) secret-by-default until an explicit per-run flag flips it (safer against sequencing bugs, but every new customer joins the migration backlog and the ordered selector carries a secret beneath MI-FIC — the silent-absorb trap).
- **Recommendation**: (a) once A36-A42 land — new estate should never join a retirement backlog; until they land, no Model 2 customer provisioning should run at all (they are blocks-186 rows).
- **Owner decision needed by**: before task 186 E2E (the greenfield stamp instantiates whichever default is chosen).
- **Consequences of not deciding**: H4's executor guesses; a secret-by-default fleet quietly undoes the migration per customer, discovered at cutover as per-customer firefighting.

## Q7 — Production estate: does anything live still consume either secret? (priority: MEDIUM)

🔔 **Human Input Required**

- **Situation**: auth-v4's rollout is dev-only (§9.3); `Seed-ProductionKeyVault.ps1` / `Configure-ProductionAppSettings.ps1` imply a prod estate exists (e.g. `sprksharedprod-api` appears in punch-list history). Prong 3 of the §6.5 resolution covers unmigrated environments conservatively, but no reader verified live prod config.
- **Options**: (a) verify live prod App Service settings + KV references before any prod credential work; (b) assume prong-3 coverage suffices.
- **Recommendation**: (a) — one `az` read, coordinated with auth-v4; also confirms whether the Office add-in deploy path (lowercase `bff-api-client-secret` alias, deleted 2026-08-24) was re-pointed on master before any add-in redeploy from this worktree.
- **Owner decision needed by**: before any prod-touching credential task; not 186-blocking (186 targets a greenfield stamp).
- **Consequences of not deciding**: a prod redeploy from stale templates re-introduces retired credentials, or a sweep breaks a still-secret-based prod BFF.

## Q8 — design.md D3: "Managed Identity" listed as a per-customer DEDICATED resource for Model 1 (priority: LOW)

🔔 **Human Input Required**

- **Situation**: design.md D3 lists Managed Identity among Model 1 per-customer dedicated resources, yet Model 1's shared BFF runs the single `sharedBffUami` (`model1-shared.bicep`; punch row A25). Affects H10 row-2 identity source for Model 1 (A41).
- **Options**: (a) wording drift — fix D3 to "shared UAMI for Model 1"; (b) a real per-customer Model 1 MI exists for some purpose — document it.
- **Recommendation**: (a) presumed; A41's executor verifies against `model1-shared.bicep` before touching H10.
- **Owner decision needed by**: at A41 dispatch. **Consequences**: H10 could register a nonexistent per-customer identity as a Dataverse app user for Model 1 — the app-user row would 401 silently (the Δ5 trap, self-inflicted).

## Q9 — Task 186 greenfield stamp (sub `cd95fcec`): tenancy shape unverified (priority: HIGH — blocks 186 confidence)

🔔 **Human Input Required**

- **Situation**: the A42 analysis asserted the 186 target is Spaarke-hosted ("same-tenant, §9.2-independent") — the adversarial verifier correctly flagged this as **uncited**. If the stamp is customer-owned-tenant-shaped, the E2E run itself — not a hypothetical later customer — hits the unresolved §9.2 ambiguity live, with no tenancy guard in the C# path.
- **Options**: (a) confirm from the 186 task POML / run parameters that profile = `spaarke-hosted-model2` (or model 1); (b) dispatch and rely on the (absent) guard.
- **Recommendation**: (a) — a one-minute read; record the answer in the 186 pre-flight.
- **Owner decision needed by**: before 186 dispatch. **Consequences**: the first live-fire E2E could silently create a cross-tenant FIC and "pass," poisoning the acceptance evidence.

## Q10 — Watchlist freeze enforcement across ~45 active parallel agents (priority: MEDIUM — operational)

🔔 **Human Input Required**

- **Situation**: the coordination plan freezes watchlist files (the 4 both-sides-modified scripts, stubbed guides, BFF auth infra, `dev.bicepparam`) until A35 lands. This session's roster shows ~45 active agents, several on credential/UAMI/config surfaces (`ds8-uami-dv-appuser`, `task-153-h12c-credential-config`, `task-160-h14-kv-reader-swap`, `Wave-3E-053-H10-AppUser`, `g1-task-109-bicep-config-drift`). An unannounced freeze is unenforceable.
- **Options**: (a) `main` broadcasts the freeze via SendMessage to the named credential-adjacent agents + re-sequences queued tasks behind A35; (b) rely on `/conflict-check` at PR time only.
- **Recommendation**: (a) — cheap, explicit; (b) catches conflicts after the work is already done.
- **Owner decision needed by**: at A35 scheduling. **Consequences**: pre-merge edits to watchlist files double the A35 conflict surface and can regress master's FIC estate during resolution.

## Q11 — BFF startup credential self-proof (SF-4/SF-20 mitigation): who builds it? (priority: MEDIUM)

🔔 **Human Input Required**

- **Situation**: only the deployed BFF can mint the MI assertion (L2 cannot — GOTCHA 2; Kudu sidecar lacks IDENTITY_ENDPOINT). The proposed mitigation — startup/warmup logs "built with credential ManagedIdentityFederated" + one real OBO exchange, consumed by H9's post-swap gate instead of bare health-200 — is **new BFF runtime behavior**, which per root CLAUDE.md §10 requires the bff-extensions checklist (placement decision, publish-size check, test updates) and §11 three-question justification. The 031 log line already exists at credential-build time; what's missing is the warmup exchange + the gate consuming it.
- **Options**: (a) r1 builds it (H9 owns the gate; BFF-touching task under §10 hygiene); (b) hand to auth-v4 as a follow-on FR (their code, our gate consumes the log).
- **Recommendation**: (b) offered first in the discharge reply (auth-v4 owns BFF auth surfaces and the log-line idiom); (a) as fallback with full §10/§11 justification if they decline. Either way H9's gate change (consume the log) is r1's.
- **Owner decision needed by**: Phase-F planning (not 186-blocking; 186 can use the existing credential-build log line as a weaker gate).
- **Consequences of not deciding**: swaps keep gating on anonymous health-200 — a slot with a broken FIC that still boots swaps to production green (SF-20), and §11's invariants never get a structurally-possible home.
