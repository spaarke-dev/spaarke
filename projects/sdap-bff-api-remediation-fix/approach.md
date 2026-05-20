# BFF API Remediation & Publish Debt — Approach Document

> **Project**: `sdap-bff-api-remediation-fix`
> **Status**: Pre-design — approach approval needed before scoping tasks
> **Created**: 2026-05-19
> **Driver**: 2026-05-19 BFF deploy package was 75.19 MB (target ~65 MB). Inspection of the publish tree revealed 212 MB of uncompressed content including multi-platform native binaries, duplicated DLLs, and sourcemap files shipping to production. The BFF is the heart of the system — debt accumulation here is high-blast-radius.
> **Audience**: Project owner, ops/deploy team, AI agents executing tasks
> **Related**: [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md), [`.claude/skills/bff-deploy/SKILL.md`](../../.claude/skills/bff-deploy/SKILL.md)

---

## 1. Executive Summary

The BFF API (`Sprk.Bff.Api`) is the single backend serving all Spaarke clients (PCFs, Code Pages, External SPA, Office Add-ins, Dataverse plugins). It hosts ~120 endpoints, ~99 DI registrations, and ~13 background job types.

A 2026-05-19 deploy surfaced that the published artifact has grown ~15% past its documented baseline (65 MB → 75 MB compressed; 212 MB uncompressed on disk). Inspection identified concrete, evidenced sources of bloat:

| Source | Approximate cost | Risk if removed |
|---|---|---|
| Multi-platform native runtimes (QuestPdfSkia + qpdf + libstdc++ on `win-x64`, `win-x86`, `osx-x64`, `osx-arm64`, `linux-musl-x64`, `linux-arm64`) | ~30 MB compressed, ~50 MB uncompressed | **LOW** — Azure App Service deploys to a single known RID (`linux-x64`); non-Linux binaries are pure waste |
| Duplicate Cosmos `ServiceInterop.dll` (root + `runtimes/win-x64/native/`) | ~10 MB uncompressed | **LOW** — duplicate, not used on Linux |
| `wwwroot/playbook-builder/assets/fluent-vendor-*.js.map` (production sourcemap) | ~7 MB uncompressed | **LOW** — sourcemap doesn't belong in prod |
| Potentially-unused packages (BouncyCastle, etc. — to be verified in Phase 1) | TBD | **MEDIUM-HIGH** — needs usage audit before any removal |
| Outdated transitive dependencies | TBD | **MEDIUM** — version drift can hide CVEs |

But size is the *symptom*. The *underlying issue* is the absence of a structured audit / pruning / verification process. Without one, debt re-accumulates, security-vulnerable transitives slip in unnoticed, and "I think this is fine to remove" decisions are made without evidence.

**This project delivers**:

1. A full inventory of the BFF's runtime composition (every direct + transitive dep, every native binary, every wwwroot asset)
2. A risk-classified cleanup plan with explicit GO/NO-GO criteria per candidate
3. A staged execution (dev → demo → prod) with measurable baselines + rollback paths
4. Permanent guardrails (CI checks, deploy-script size gates, ADR) so the debt doesn't return

---

## 2. Out of Scope (explicitly)

To prevent scope creep on the heart-of-system codebase:

- ❌ No refactoring of BFF business logic (auth, endpoints, services)
- ❌ No new features
- ❌ No PublishTrimmed (`<PublishTrimmed>true</PublishTrimmed>`) — Graph SDK + Microsoft.Identity.Web + EF + JSON serializers + DI rely on reflection that trimming breaks silently
- ❌ No AOT (`<PublishAot>true</PublishAot>`) — same reason, even more aggressive
- ❌ No upgrading the .NET SDK / target framework (e.g., 8.0 → 9.0) — separate concern, separate risk profile
- ❌ No Graph SDK version changes (Kiota chain footgun — `CLAUDE.md` mandates all Kiota packages stay version-matched)
- ❌ No infrastructure changes (App Service Plan SKU, region, runtime stack)

If any of these is later identified as necessary, it becomes a separate project with its own design and risk review.

---

## 3. Guiding Principles

These constrain every decision in the project. If a step violates one, escalate before proceeding.

| Principle | Why |
|---|---|
| **Evidence before edit** | No package, file, or config removal without (a) verified non-use, (b) test plan, (c) rollback path |
| **One change per deploy** | Multi-change deploys make it impossible to attribute regressions; observation windows require isolation |
| **Dev → demo → prod with baking time** | Each promotion stage gets 24–48h observation; production never receives an unbaked change |
| **Reflection-safe (no trimming, no AOT)** | The BFF's Graph SDK / DI / serialization stack is fundamentally reflection-driven |
| **Reversible via git + redeploy** | Every change must be revertable by `git revert` + `Deploy-BffApi.ps1` within 10 minutes |
| **Codify or it returns** | Phase 6 (Prevention) is mandatory — without CI guards + ADR, the debt rebuilds |
| **Don't touch prod-only state from non-prod work** | All audits, baselines, observations against `spe-api-dev-67e2xz` only unless explicitly cleared for spaarke-demo or prod |
| **Sub-agent write boundary respected** | Per root CLAUDE.md §3, sub-agents cannot write to `.claude/` paths; main session applies those edits |

---

## 4. Six-Phase Execution Model

Each phase produces concrete evidence that the next phase depends on. **Phases are not parallelizable** — they're a strict pipeline. Within a phase, individual audit/inspection commands can run in parallel.

### Phase 1 — Inventory (READ-ONLY, no risk)

**Goal**: Produce `INVENTORY.md` — the authoritative snapshot of the BFF's runtime composition at the start of the project. Every subsequent decision is justified against this document.

**Outputs**:

1. **Direct package list**: `dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package` — captured to file
2. **Transitive package list**: `dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --include-transitive` — captured to file
3. **Vulnerable packages**: `dotnet list ... package --vulnerable --include-transitive`
4. **Outdated packages**: `dotnet list ... package --outdated`
5. **Project reference graph**: enumerate `dotnet list reference` recursively across Sprk.Bff.Api → Spaarke.Core → Spaarke.Dataverse → others
6. **Direct-package usage map**: for each direct package in `Sprk.Bff.Api.csproj` + referenced project csprojs, grep the codebase for `using {PackageNamespace}` and `[assembly: ...]` to confirm it's actually called
7. **Native binary inventory**: enumerate `publish/runtimes/*/native/` after a clean `dotnet publish` — capture per-platform file lists with sizes
8. **wwwroot asset inventory**: enumerate `publish/wwwroot/` — flag `.map` files, README/LICENSE artifacts, source-only files
9. **Published file size table by category**: group `publish/` into (managed dll, native lib, wwwroot, config, runtime metadata) — sum sizes per category
10. **App Service runtime confirmation**: `az webapp show --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --query "{kind:kind,os:reserved,linuxFxVersion:siteConfig.linuxFxVersion,sku:sku.name}"` — proves the RID we actually need
11. **Current deploy SHAs**: SHA-256 of all DLLs in `deploy/api-publish/` — the rollback target
12. **Current package size**: zip size + uncompressed size + file count

**Risk**: None. All commands are read-only.

**Duration**: 1–2 hours.

**Gate**: `INVENTORY.md` reviewed and signed off by project owner before Phase 2 starts.

---

### Phase 2 — Categorize Cleanup Candidates by Risk

**Goal**: Produce `CANDIDATES.md` — every potential cleanup, with explicit risk tier, evidence, test plan, and rollback procedure.

**Risk tier definitions** (binding for the project):

| Tier | Definition | Approval required |
|---|---|---|
| **SAFE** | Affects deploy artifact only (publish-time configuration). No source code change. No runtime behavior change. Examples: `--runtime linux-x64`, exclude `*.js.map` from zip, exclude `runtimes/win-x64/` from copy. | Owner ack (1 person) |
| **MEDIUM** | Affects source code (csproj edit). Removes a direct package whose using-statements have been removed elsewhere or never existed. Compile-time verifiable. Reflection use must be ruled out. | Owner approval + code review |
| **HIGH** | Removes a transitive-only dep by removing its parent direct ref. May break reflection-loaded code. Requires full integration test pass + 48h dev observation. | Owner approval + code review + explicit go-ahead per item |
| **REJECT** | `PublishTrimmed=true`, AOT, Graph SDK removal, Microsoft.Identity.Web removal | Not in scope |

**For each candidate, capture**:

- Name + file/package
- Current cost (KB / MB compressed and uncompressed)
- Evidence of non-use (grep results, dependency tree analysis, RID confirmation)
- Risk tier
- Removal action (csproj edit / publish flag / wwwroot exclusion / etc.)
- Test plan (what proves the BFF still works correctly after removal)
- Rollback (the git commit hash to revert to, and the redeploy command)

**Risk**: None. Read + plan only.

**Duration**: 4–8 hours depending on candidate count.

**Gate**: Owner reviews `CANDIDATES.md` and approves which candidates proceed to Phase 4 (and in what order — typically SAFE first, then MEDIUM, then HIGH).

---

### Phase 3 — Baseline Current Behavior (BEFORE any changes)

**Goal**: Capture the system's current verified-good behavior so Phase 4 has a reference for "did anything regress."

**Outputs** (all stored under `projects/sdap-bff-api-remediation-fix/baseline/`):

1. **Test suite results**: `dotnet test src/server/api/Sprk.Bff.Api.Tests/` → captured to file with pass/fail counts + duration
2. **Endpoint smoke test results**: every documented endpoint from [`auth-deployment-setup.md §9`](../../docs/guides/auth-deployment-setup.md) + an expanded list (TBD per BFF's full route table) — hit each, record HTTP status + response shape
3. **App Insights baseline metrics** (24h window):
   - Request count per endpoint
   - Error rate per endpoint
   - P50 / P95 / P99 latency per endpoint
   - Exception counts by type
   - Dependency call latency (Graph, Dataverse, Service Bus, Cosmos, Redis)
4. **Deployed file SHA-256s**: capture all 6 critical-file hashes via Kudu VFS (same mechanism the deploy script's hash-verify uses)
5. **Current publish size + zip size + file count**: from `Deploy-BffApi.ps1` output

**Risk**: None. Observation only.

**Duration**: 24–48h calendar time for the App Insights baseline; ~1h active work.

**Gate**: Baseline document committed to repo before any Phase 4 change is attempted.

---

### Phase 4 — Apply Changes (one at a time, staged)

**Goal**: Execute approved cleanups from `CANDIDATES.md`, one per deploy, with observation between each.

**Per-candidate procedure** (mandatory — no shortcuts):

1. Create a single git commit on a feature branch with ONLY this change
2. `dotnet build --warnaserror` — must compile clean (zero warnings, zero errors)
3. `dotnet test` — same pass/fail counts as Phase 3 baseline (any new failure = abort, revert, escalate)
4. Run `Deploy-BffApi.ps1 -DryRun` (if it supports dry-run) or examine publish output without deploying — verify size reduction is in expected range
5. `Deploy-BffApi.ps1` to **dev only** (`spe-api-dev-67e2xz`) — never to `spaarke-demo` or prod in this phase
6. Re-run Phase 3's full endpoint smoke test pass — every check must match Phase 3 result
7. **Bake 24–48h** in dev. Watch App Insights:
   - No new exception types
   - Error rate unchanged
   - P95 latency within 10% of baseline
   - Dependency call success rates unchanged
8. If stable → log the size delta, mark candidate complete, move to next
9. If anything is off → `git revert` + redeploy + investigate before continuing

**Order of execution** (recommended, SAFE first):

| Order | Candidate | Tier | Expected savings |
|---|---|---|---|
| 1 | Publish with `--runtime linux-x64` | SAFE | ~25–30 MB uncompressed, ~10 MB compressed |
| 2 | Exclude `wwwroot/**/*.js.map` from publish | SAFE | ~7 MB uncompressed |
| 3 | Remove duplicate Cosmos ServiceInterop (if RID trim didn't already) | SAFE | ~10 MB uncompressed |
| 4 | (per CANDIDATES.md) | MEDIUM / HIGH | TBD |
| ... | ... | ... | ... |

**Risk**: LOW per change (only dev affected; 24–48h observation; immediate rollback path).

**Duration**: ~1 week calendar time per 3-5 changes (due to baking windows).

**Gate**: Each candidate's per-step results logged to `EXECUTION-LOG.md` before next candidate starts.

---

### Phase 5 — Promote to spaarke-demo, then prod

**Goal**: Replicate the now-validated changes to the demo and production environments.

**Procedure**:

1. After all approved Phase 4 changes are stable in dev (full bake window complete on the last change), deploy the cumulative changeset to `spaarke-demo`
2. Run the same smoke tests against demo
3. Bake **48h minimum** in demo (longer than dev — demo has the wider test audience)
4. If stable → schedule production deploy with owner + ops team
5. Production deploy follows whatever release process is canonical (currently TBD — may need to be defined as part of this project)
6. Post-prod-deploy: continue App Insights monitoring for 7 days; record any drift in metrics

**Risk**: MEDIUM. Production is real. Mitigations: (a) full Phase 4 validation in dev, (b) cumulative change pre-tested in demo, (c) deploy script's hash-verify catches Windows file-lock failure mode, (d) rollback is `git revert` + redeploy.

**Duration**: 1–2 weeks calendar (mostly waiting for bake windows + scheduled prod windows).

**Gate**: Owner approval at each stage transition (dev → demo, demo → prod).

---

### Phase 6 — Prevention (codify so the debt doesn't return)

**Goal**: Make it impossible (or at least loud and obvious) for the debt to silently re-accumulate.

**Outputs**:

1. **Deploy-script size guard**: modify `Deploy-BffApi.ps1` to fail the deploy if the publish zip exceeds a documented threshold (e.g., baseline + 10%) unless `-AllowOversize` is passed. Forces explicit acknowledgment of any size growth.
2. **CI check**: add a workflow step in `.github/workflows/sdap-ci.yml` that runs `dotnet publish --runtime linux-x64` and fails if non-Linux native runtimes are present in the output
3. **CI check**: workflow step that fails if any `*.js.map` files exist in `wwwroot/` of the publish output
4. **ADR-029** (new): "BFF deploys with framework-dependent `linux-x64` runtime only. Multi-platform RIDs are rejected at CI." Records the decision, rationale, and review cadence.
5. **CLAUDE.md update** (`src/server/api/Sprk.Bff.Api/CLAUDE.md`): add a "Publish Hygiene" section documenting the constraint and pointing to the deploy script size guard
6. **Skill update** (`.claude/skills/bff-deploy/SKILL.md`): add a "Publish Hygiene" section + reference to ADR-029
7. **Quarterly review reminder**: add to project ops calendar — "BFF publish-debt audit" each quarter (lightweight: re-run Phase 1 inventory, check for new bloat)

**Risk**: LOW. CI changes affect future PRs; size guard is opt-in opt-out per deploy.

**Duration**: 1–2 days.

**Gate**: All CI checks pass on a fresh PR. ADR published. Skill updated. Owner sign-off.

---

## 5. Decision Authority

| Decision | Owner |
|---|---|
| Approve `INVENTORY.md` (Phase 1 output) | Project owner |
| Approve cleanup candidates from `CANDIDATES.md` (which ones proceed, in what order) | Project owner |
| Per-candidate go-ahead for MEDIUM / HIGH tier (Phase 4) | Project owner + code review |
| Promotion dev → demo | Project owner |
| Promotion demo → prod | Project owner + ops team |
| ADR-029 approval | ADR review process (per project conventions) |
| Rollback after any regression | Anyone — no permission needed to revert |

---

## 6. Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Reflection-loaded code breaks at runtime after package removal | Medium (Graph SDK / DI is reflection-heavy) | High (silent failures in production) | Tiered risk classification + 24–48h dev bake + App Insights monitoring; **REJECT** tier for trimming/AOT |
| Non-Linux RID needed for a code path we didn't catch in inventory | Low (App Service is unambiguously Linux) | Medium (deploy fails at startup) | RID trim deploys to dev first; if startup fails, revert immediately; the deploy script's healthz check catches this |
| Removing a "duplicate" file that was actually being loaded from one specific path | Low | Medium | Per-candidate test plan must verify the specific code paths that loaded the file |
| CI guard added in Phase 6 blocks a future legitimate need for a non-Linux RID | Low | Low | Guard supports `-AllowOversize` / `-AllowMultiPlatform` flags with documented justification |
| Production deploy regresses despite full dev + demo validation | Very low | Very high | Cumulative pre-tested changeset, immediate rollback procedure documented, ops team standby for first prod deploy |
| Scope creep ("while we're at it, let's also upgrade .NET 9") | Medium | High | §2 "Out of Scope" is binding; any addition becomes a separate project |
| The project starves due to long bake windows | Medium | Low | Document expected calendar duration (2–4 weeks elapsed for full Phase 1–6) up front |

---

## 7. Estimated Effort

| Phase | Active work | Calendar time |
|---|---|---|
| 1 — Inventory | 1–2h | 1 day |
| 2 — Categorize | 4–8h | 1–2 days |
| 3 — Baseline | 1h active + 24–48h wait | 2–3 days |
| 4 — Apply changes | 1–2h per change | 1–2 weeks (bake windows dominate) |
| 5 — Promote demo + prod | 2–4h active + bake windows | 1–2 weeks |
| 6 — Prevention | 1–2 days | 2–3 days |
| **Total** | **~3–5 days active work** | **~3–5 weeks calendar** |

The calendar duration is dominated by mandatory observation windows, not engineering effort. This is intentional — it's the cost of doing high-rigor change against the heart of the system.

---

## 8. Success Criteria

The project is complete when:

1. ✅ `INVENTORY.md` and `CANDIDATES.md` are committed and approved
2. ✅ All approved SAFE-tier candidates have been deployed to prod and observed stable for 7+ days post-deploy
3. ✅ Compressed deploy package size has reduced by ≥10 MB from the 75 MB starting baseline (target: ≤65 MB, ideally ≤60 MB)
4. ✅ Zero new exception types or error-rate regressions in App Insights vs Phase 3 baseline
5. ✅ Deploy-script size guard is in place and configurable
6. ✅ CI checks for RID + sourcemap exclusion run on every PR
7. ✅ ADR-029 is published
8. ✅ Documentation updated (CLAUDE.md, bff-deploy SKILL.md)
9. ✅ A `LESSONS-LEARNED.md` captures any surprises, gotchas, or future risks for the next quarterly review

---

## 9. Open Questions (for owner before kickoff)

1. **Production environment access**: Phase 5 requires production deploy. Who owns prod operationally? What's the canonical prod deploy process today?
2. **App Insights baseline window length**: 24h or 48h? (Longer = stronger signal but delays Phase 4 start.)
3. **Tolerance for size regression on legitimate adds**: should the CI guard be hard-fail or warning?
4. **Quarterly review cadence**: who owns running it?
5. **Scope of "BFF" for this project**: just `Sprk.Bff.Api.csproj`, or do we also audit `Spaarke.Core` + `Spaarke.Dataverse` + `Spaarke.Bff.Plugin.Proxy` published outputs?

---

## 10. Next Steps to Initiate

Once this approach is approved:

1. Create `projects/sdap-bff-api-remediation-fix/` standard project files:
   - `README.md` (project overview)
   - `CLAUDE.md` (AI agent context)
   - `current-task.md` (active task tracker)
   - `tasks/TASK-INDEX.md` (numbered task list mapped to phases above)
   - `tasks/*.poml` (per-task work items, one per cleanup candidate)
2. Run `/project-pipeline projects/sdap-bff-api-remediation-fix` to generate phase-aligned tasks
3. Begin Phase 1 (inventory) — owner can do this manually or invoke `task-execute` for the first task

---

*Approach awaiting owner approval. No work begins until §9 Open Questions are answered and §4 Phase 1 is explicitly authorized.*
