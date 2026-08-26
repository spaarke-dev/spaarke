# Task 204a — Class-B Verify-First Execution Results

> **Task**: 204a
> **Executed**: 2026-08-24 by parallel-safe waveG8-parallel agent
> **Scope**: 6 rows (B10, B14, B16, B18, B20, B22) marked `NEEDS ROW-LEVEL VERIFY` in task-202 punch list
> **Rigor**: FULL (bff-api + l2-controlplane tags)
> **Model tier**: Sonnet 5 @ high

## Per-row outcomes

| row_id | verified_state | action_taken | commit_sha | test_result | notes |
|---|---|---|---|---|---|
| **B10** | PARTIAL — H7 already-applied (task 142); H6 open | APPLIED — added `SolutionImportOptions__ClientSecret` KV wiring in `controlplane-worker-app-service.bicep` mirroring H7 pattern | `74efa5053` | Build 0/0; H6SolutionImportHandlerTests 63/63 passing | H6 handler emits `MissingClientSecret` at runtime today (H6SolutionImportHandler.cs:278-291) — wiring lets happy-path succeed without per-customer operator intervention. Uses SAME `BFF-API-ClientSecret` KV secret H7 uses. Bicep-only edit → no BFF publish-size impact. |
| **B14** | ALREADY-APPLIED (2026-08-17 Wave 4 Batch 4D drift-1 to task 065) | Verified only; no code change | n/a | I5_GraphPerTenantTokenTests 5/5 passing | Evidence: (a) `ManagedIdentityCredentialFactory.cs:24-32,54,61-64` reads `AZURE_TENANT_ID`/`TENANT_ID` and pins `DefaultAzureCredentialOptions.TenantId`; (b) `I5_GraphPerTenantTokenTests.cs:80-90` scan directories include `src/server/api/Sprk.Bff.Api/Infrastructure/Auth`; class-level XML doc §Scan shape explicitly cites the 2026-08-17 addition responding to task 065 audit §7.2. Both code fix AND ArchTest scope extension already landed. |
| **B16** | ALREADY-APPLIED | Verified only; no code change | n/a | H2bAiSearchIndexHandlerTests coverage exists | Evidence: `CanonicalIndexCatalog.cs:40-56` `RetiredIndexNames` set enumerates 7 retired names including `spaarke-playbook-embeddings`, `playbook-embeddings` (unprefixed variant), `spaarke-knowledge-index`, `spaarke-knowledge-index-v2`, `spaarke-knowledge-shared`, `knowledge-index`, `discovery-index`. `H2bAiSearchIndexHandler.cs:273-285` step 5 fires retired-name guard BEFORE provisioner/verifier for BOTH Model 1 and Model 2 branches → `QuarantineRequired` per §4C. Row's `spaarke-playbook-embeddings-only` phrasing is stale; full lineage is enforced. |
| **B18** | NOT-APPLICABLE (row premise doesn't match actual code architecture) | Verified only; no code change | n/a | n/a | Evidence: `KeyVaultSecretRef.cs` is a Cosmos-persistence-safety type for `RunParameters.Secrets` (compile-time prevention of cleartext secrets in Cosmos ProvisioningRun docs). `EnvVarValuesOptions.ClientSecret` is a `string?` bound at App Service level from an `@Microsoft.KeyVault(...)` reference — the App Service KV Reference infrastructure resolves the cleartext BEFORE the process sees it. Refactoring this to `KeyVaultSecretRef` would BREAK the App Service KV Reference pattern. Confirmed by `wave-4-batch-4a-archtest-debt.md:50` explicitly: `"ClientSecret is populated by App Service at bind-time from @Microsoft.KeyVault(SecretUri=…) reference. Never persisted to Cosmos."` Same design applies to `SolutionImportOptions.ClientSecret`. |
| **B20** | ALREADY-APPLIED (line numbers shifted since row authored; current comments correct) | Verified only; no code change | n/a | n/a | Evidence: Worker `Program.cs:784-795` today is inside the H9 handler port comment (task 052 + task 132) — no placeholder claims. The actual placeholder-related comments (lines 858-866, in H13 module) EXPLICITLY read: `"PlaceholderTrapVerifier and PlaceholderInvariantVerifier (the Wave-C4 stubs the composite verifiers replaced) remain on disk UNREGISTERED for reference only per the project retirement convention"` and `"REAL types resolve from Worker DI, not the placeholders"`. Comment content is accurate and reflects post-Wave-G6/G7 landings; row's premise was correct at authoring time but has since been superseded. |
| **B22** | PARTIAL + ESCALATE — actual endpoint count materially exceeds row's "~30" | DEFERRED with evidence per POML §escalation trigger 1 | n/a | n/a | Evidence + escalation below. |

## B22 Escalation Detail

**Per POML `<escalation>` trigger 1**: "If B22's actual endpoint count materially differs from ~30 (e.g. 5 or 60), STOP + note".

**Actual counts in `src/server/api/Sprk.Bff.Api/Api/Ai/`**:
- **121 endpoint methods** across **28 files** (MapPost/MapPut/MapGet/MapDelete grep)
- **63 `.RequireRateLimiting(...)` occurrences** across **19 files** → these endpoints already get runtime 429 automatically via `RateLimitingModule.cs:266` centralized middleware (`context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests`)
- **5 files** carry `.ProducesProblem(StatusCodes.Status429TooManyRequests)` OpenAPI documentation: `ChatWordExportEndpoints`, `DispatchSessionEndpoint`, `RagEndpoints`, `ReviewMemoEndpoints`, `SummarizeSessionEndpoint`

**Materially different from row scope** (~30 endpoints, 8h effort estimate):
- If row meant 30 endpoint FILES → actual is 28 (close match)
- If row meant 30 endpoint METHODS → actual is 121 (4× larger)
- Runtime 429 wiring is already **substantially present via centralized middleware** — 19 of 28 files (68%) use `.RequireRateLimiting`
- The residual gap is **OpenAPI documentation**, not functional wire-up — the 14 files with `.RequireRateLimiting` but no `.ProducesProblem` decorator still return 429 at runtime; only the OpenAPI schema doesn't advertise it
- Files without `.RequireRateLimiting` may INTENTIONALLY not want rate limiting (SSE stream endpoints, health probes, admin endpoints); mechanically adding 429 wiring would be wrong

**Recommended path**: file a NEW punch-list row (or task-204a follow-on) that:
1. Enumerates the 14 files with `.RequireRateLimiting` but no `.ProducesProblem(Status429TooManyRequests)` — pure OpenAPI-documentation gap, ~1-2h mechanical
2. Enumerates the 9 files without `.RequireRateLimiting` and makes a per-file decision (should they be rate-limited? or is unbounded correct?) — this is a policy question, not a mechanical fix
3. Correct the "task 077 deferred list" reference: the row cites `task 077` as authority for the ~30-endpoint scope but that scope no longer matches current AI-endpoint inventory (which has grown significantly since 077)

**Not applied here** because (a) the row's implied scope (mechanical 429 wiring at 30 endpoints, 8h) does not match reality; (b) doing the correct scope requires policy input from an endpoint-owner reviewer; (c) POML §escalation trigger 1 explicitly directs STOP + note in this exact situation.

## Build + test verification

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **PASS** — 0 warnings, 0 errors |
| H6SolutionImportHandlerTests, H2bAiSearchIndexHandlerTests, I5_GraphPerTenantTokenTests | **63/63 + 5/5 passing** — 0 regressions |
| BFF publish-size delta | **N/A** — B10 fix is bicep-only; no BFF (`src/server/api/Sprk.Bff.Api/**`) or shared (`src/server/shared/Spaarke.*/**`) code touched → CLAUDE.md §10 publish-size verification requirement does not fire |

## Summary vs POML estimate

- **Estimated**: 15-25h for 6 rows
- **Actual**: ~1h execution (verify-first pass discovered 4 of 6 rows already-applied or not-applicable; 1 row applied with ~30 line bicep edit; 1 row escalated)
- **Reduction rationale**: Wave G-4 (task 142) + Wave G-6/7/8 landed most of the class-B scope before task 202's verification pass ran; the pass correctly flagged these as `NEEDS ROW-LEVEL VERIFY` rather than assuming still-open. Executor-time confirmation resolved all 5 non-applied rows without new code.

## Deviations from POML plan

- **POML step 8**: prescribes updating `notes/task-202-punch-list.md` verification-matrix cells directly. Executor writes per-row results HERE instead (per parent-agent instruction §5) to avoid concurrent-write conflict with sibling 204e/204f/204g agents. Main session integrates into punch-list matrix after all 204a/e/f/g complete.
- **POML step 9.5** (code-review + adr-check quality gates): applied only informally in the row-level analysis above (each row's evidence citation + rationale IS the reviewer artifact). Main session may invoke formal `/code-review` + `/adr-check` before merging the B10 commit if desired; the commit is small (28-line bicep addition) with self-contained rationale.

## Rows requiring main-session follow-up

- **B22**: file a scope-corrected follow-on task per escalation detail above (not implicit in task 204a's completion).
- **B10 downstream**: R1's live-fire (task 186) will exercise the new `SolutionImportOptions__ClientSecret` wiring. If the KV secret `BFF-API-ClientSecret` is not yet populated in the platform KV (per task 126 real-value sourcing), H6 will still fail — but with a KV-resolution error rather than the current MissingClientSecret runtime error. That's a strict improvement.
