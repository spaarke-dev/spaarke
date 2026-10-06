# Current Task State — `customer-provisioning-orchestration-r1`

> **Format (2026-10-06):** this file holds CURRENT state only and is REWRITTEN at each checkpoint — never prepend a new block on top of old ones. Standing directives + environment gotchas live in the project `CLAUDE.md` ("Standing directives & gotchas"). Session history lives in git (checkpoint commit messages) and the verbatim archive `notes/handoff-history/current-task-archive-2026-10-06.md` (do NOT load it on recovery; grep it only if you need a specific past detail). Why: see `.claude/skills/context-handoff/SKILL.md` "State, not history" and `notes/handoff-history/2026-10-06-conversion-review.md`.

> **Last Updated**: 2026-10-06 SESSION 40 END (by context-handoff, pre-/compact) — tree clean; T229 ✅ T230a ✅; T253/T254 filed; owner APPROVED items 1–3 (W7, OwnedContainerIds on spaarke-bff-dev, remove old L2 Contributor on platform sub); G36 explained, awaiting decision. **SESSION 41: items (2)+(3) done; T230b IN PROGRESS (design recorded below).** Converted to state-only format 2026-10-06 from that checkpoint (commit `fd49c6513`).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **T230b** — keyless proof (D13): `tasks/230b-keyless-proof-per-service.poml` (FULL, opus/high, BFF hot path + auth). |
| **Step** | 4 of 6 — steps 1-3 DONE + pushed (`8df18bead` BFF; `5b49ed7f6` H3 + H13). ControlPlane tests 2182/0. |
| **Status** | in-progress (SESSION 41). Owner items (2)+(3) DONE + pushed `f9303ec3e`; W7 waits for the next L2 Api deploy. |
| **Next Action** | Step 4: ArchTest `KeyCredentialCensusTests` (pin AzureKeyCredential/ApiKeyCredential/key-header sites, each with config key; parity with L2 `StampKeySettingCatalog`) + `CustomerStampKeylessTemplateTests` parity with `ArmStampKeylessVerifier` types + RouteAuthorizationGuard ledger entries for `Api/Platform/KeylessProofEndpoints.cs`. KnowledgeDeploymentService CustomerOwned ALREADY removed (step 1). Then step 5 docs, step 6 verify (BFF suite, ArchTests, publish size vs fresh master, CVE) + Step 9.5. |
| **Owner items** | (1) **W7 open**: deploy the L2 Api control-plane template (CustomerRunGuard config) with the next L2 Api code deploy — ask before. (2) ✅ `SharePointEmbedded__OwnedContainerIds` set on spaarke-bff-dev (no slots), /healthz 200. (3) ✅ L2 UAMI (38f7693f…) Contributor on sub 484bc857 deleted (assignment ae4bee5f…); 5 resource-scoped roles remain. (4) **G36** explained, awaiting owner decision — do NOT act. Gotcha: Git Bash mangles `/subscriptions/...` → `export MSYS_NO_PATHCONV=1`. |
| **Order** | … → T229 ✅ → T230a ✅ → **T230b** → T254 → T232 → T233 → T240 → T218 → T235 → T250 → 213.7/207/208/209 → **T253** (G38) → T186. T242c / T241 when the owner wants them. |

### T230b found (record in POML notes)
- Pre-existing F.1 latent: chat endpoints map unconditionally but `IChatClient` registers only when `AzureOpenAI:Endpoint`+`ChatModelName` are set (BFF host won't boot without them) — stamps always set them; note only.
- Model 2 gap: H3 defines the role but does not assign it for `customer-owned-model2` (L2 has no SP in the customer tenant).

### T230b design (decided SESSION 41 — by necessity, per owner directive)

- **BFF endpoint** `POST /api/platform/keyless-proof` (`Api/Platform/KeylessProofEndpoints.cs`, `MapKeylessProofEndpoints`; NOT `/debug/*` — ADR-028/auth.md ban). Filter `KeylessProofAuthorizationFilter` (ADR-008): app-only token (`idtyp=app`, no `scp`) carrying app role `Provisioning.KeylessProof`; else 403 (anonymous → 401 by auth). Returns per-service `{service, outcome, statusCode?, elapsedMs, code}` only — outcome ∈ proved | refused (401/403 or token failure) | key-credential (a key setting is configured → NOT called) | not-configured | unreachable (transport/timeout/5xx/429) | failed.
- **Services**: non-AI probes (Service Bus peek `ServiceBus:QueueName`, Redis PING) in `Infrastructure/Diagnostics/KeylessProofService.cs` (+ `KeylessProbeRunner`); shared strings in source-linked `src/server/shared/Contracts/KeylessProofContract.cs`; AI-owned probes behind NEW facade `Services/Ai/PublicContracts/IAiKeylessProbe` (ADR-013): OpenAI chat (`AzureOpenAI:Endpoint`/`ChatModelName`, 16 tokens) + embeddings (`DocumentIntelligence:OpenAiEndpoint`/`EmbeddingModel`), Document Intelligence (`DocumentIntelligenceAdministrationClient.GetResourceDetails`, free), AI Search (`DocumentIntelligence:AiSearchEndpoint` + `AiSearch:KnowledgeIndexName` → GetDocumentCount), Cosmos (`ReadContainerAsync` metadata), Blob (`SessionFileStore:BlobEndpoint` list 1), Content Safety Prompt Shield + groundedness via a dedicated named client with the SAME `ContentSafetyAuthHandler` and a 30 s timeout (the production client's ~450 ms budget would report timeouts, not auth). Probes build SDK clients from the injected `TokenCredential` — `IOpenAiClient`/`TextExtractorService` are registered only under `DocumentIntelligence:Enabled` (ADR-032 §F.1), so they are not injected. Cost per H13 run: fractions of a cent (escalation trigger not fired).
- **Content Safety fail-open**: PromptShieldService + GroundednessCheckService get a distinct auth outcome (401/403 or credential failure → `failed_open_auth` / `fail_open_auth`, Error log, `PromptShieldResult.AuthRefused`). Still fail OPEN for users.
- **H3**: app role `Provisioning.KeylessProof` (fixed GUID, `AllowedMemberTypes=[Application]`) on the per-customer BFF app reg (create + reconcile) + `appRoleAssignedTo` on the BFF SP for `ControlPlaneIdentity:PrincipalObjectId` (idempotent). L2 already holds AppRoleAssignment.ReadWrite.All (H10 uses it) + owns the app → no new permission.
- **H13**: E2EValidationRunner's 4 sample checks (agent message / search count scope=all / layouts / field-mappings) REMOVED — they need a signed-in user (app-only token can never pass; search count scope=all is refused since UAC task 070), so "fail on auth" would block every stamp. Replaced by ONE keyless-proof call: token `api://{BffAppRegId}/.default`; auth failure / refused / key-credential / not-configured / failed / 404 → Failure (QuarantineRequired); transient (unreachable / 5xx / timeout / transport) → new `Inconclusive` → Resumable. NEW `IStampKeylessVerifier` (`ArmStampKeylessVerifier`, raw ARM REST via named HttpClient, api-versions = the modules'): disableLocalAuth on Search (+ no authOptions)/CognitiveServices(≥3)/ServiceBus/Cosmos/SignalR, Storage allowSharedKeyAccess=false, redisEnterprise databases accessKeysAuthentication=Disabled; App Service prod + every slot: no key setting (closed name catalog + AccountKey=/SharedAccessKey=/SharedAccessSignature value shapes). Failed → QuarantineRequired `h13-stamp-key-auth-enabled`; InfraFault → Resumable.
- **ArchTest** `KeyCredentialCensusTests`: per-file counts of `new AzureKeyCredential(` / `new ApiKeyCredential(` / `AzureNamedKeyCredential` / `AzureSasCredential` / `StorageSharedKeyCredential` / `"Ocp-Apim-Subscription-Key"` / `"api-key"` in src/server (non-test), each entry with its config key + reason; parity: every entry's config key appears in the H13 key-setting catalog.
- **KnowledgeDeploymentService**: the key-only `CustomerOwned` path is unreachable (no caller of SaveDeploymentConfigAsync / ValidateCustomerOwnedDeploymentAsync) → REMOVE (needed→build else remove).

## SESSION 40 outcomes

- **T229 ✅** (`f9627fac1`): `CostEnvelopeIntake` — tier (smb|enterprise|dedicated) + estimatedMonthlyUsd required for every model at POST /api/runs and H0; no shared-trial / warnAndProceed / costEnvelopePolicy / Model-2-only strictness; H0Options + H13 options ValidateOnStart; H13 one `DedicatedStampEnvelopeUsd` = $400 (list prices westus2: $337.04 fixed); skill Step 1b-ter + Step 2 hard stop.
- **T230 split** → 230a ✅ / 230b. **T230a ✅** (`7b55890a7`): I3 = cosmos-db.bicep declared containers/keys (membership drift logged, not failed); H13 runtime invariants I2–I5 (I1 = ArchTest); H13 per-run naming check removed → merge-blocking `naming-conformance` job in `ci-tier1-blocking.yml` (the Router's required check — `sdap-ci.yml` is NOT required); R3 vault rule accepts `sprk-{customerId}-{env}-kv` (no reserved ids/env tokens, case-sensitive, ≤24); Seed-ProductionKeyVault.ps1 VaultName mandatory.
- **New tasks**: **T253** (G38) — H4b runs `pwsh` + a generated script, H6 runs `pac` on a `DOTNETCORE|10.0` Worker with neither tool nor `scripts/` → both fail live; before T186. **T254** (G37, owner: no cap by default, per-customer limit when desired; BFF must map TenantBudgetExceededException → 429, today 500).
- Board #438: 210 / 190.

## Notes for tasks further down the order

- T250 likely superseded by master `bb8ba7251` (SPE Admin runs as the BFF MI) — raise with owner when reached.
- T242c re-scope per D27 (demo = next env for testing the provisioning orchestration; rg-spaarke-demo has no AI Search). Demo lacks `AiSafety__ContentSafety__Endpoint` → T242c must set it.
- Tasks without a POML yet: create from the plan `notes/model1-dedicated-remediation-plan.md` §7 rows (copy `tasks/228-…poml` format), add TASK-INDEX rows, then task-execute.
- E-2 (OpenAI MI 401) was seen only on dev's `AIServices` account — a 401 on a stamp's `kind: OpenAI` is an owner decision (path B), since stamps now have no key fallback (T230b).

## Other open owner items (not approved/decided yet)

1. **G36** — ADR-027 management group for customer subscriptions (explained; awaiting decision — do NOT act).
2. **G31** — H10 grants Model 1 stamps tenant-wide Directory/User write roles in Spaarke's tenant.
3. **Api site's pending slot swap** — every platform-controlplane deploy fails its Api module.
4. Board Status "Active" vs Status Reason "On hold" on Issue #438.

## T186 live checks recorded only here (verify at the first live run)

- (T228) H5 WhoAmI as the Worker in an operator-created env; H1 RG listing under Owner; H6 sign-in right after H10.
- (T227e) Worker identity reads `environmentvariabledefinitions` in the customer env; marker PATCH right after the bind PATCH.
- (T227g) the BFF app registration (H7's identity, before H10) can PATCH `businessunit.sprk_containerid` — a 403 surfaces as Resumable `dataverse-auth-failure` before any env-var write.
- (T227d) marker PATCH on a just-created (inactive) container; `deletedContainers/{id}` returns the marker; Graph refuses app-only container-type operations for a stamp UAMI (plus T227b's two — see the 227b POML).
- (T251) W1 (group Name vs DisplayName).
- (T230b) ADR-028 E-2 measured on the stamp's `kind: OpenAI` account (H13 log line "ADR-028 E-2 measurement"); every keyless-proof service `proved` live (Prompt Shield + groundedness on the stamp Content Safety region; Document Intelligence resource-details read under Cognitive Services User); H3's `appRoleAssignedTo` POST for the L2 identity succeeds (no 400 after the propagation retries); the L2 token for `api://{BffAppRegId}` carries `roles: Provisioning.KeylessProof` and no `scp`; ARM keyless check reads every stamp resource as the L2 identity (Owner).

## Follow-ups parked in POML notes (non-blocking)

T246 follow-ups a–f (worker upgrade with a run in flight; subdomain name pre-check; decommission purge of Cognitive Services accounts; H0 groundedness-region check; pre-T246 stamp drift; `spe-api-dev-67e2xz` named in ~40 docs/skills though the app no longer exists) · T247 follow-ups (a)–(c); pin refresh due before ~2027-01-14 · T244 follow-ups (CI check that checked-in `customer.json` = fresh compile; ACS `disableLocalAuth`; Model 2 Lighthouse Search roles) · G34 (GraphMetadataCache key contract) · T252 (control-plane vault sentinels).

## Live state (dev)

- Dev BFF `spaarke-bff-dev` runs branch build `0911515d7` (= master + health-check fix; master carries it via #1311). Any future dev BFF deploy comes from master ≥ `c8b93b294`. T227d not yet on dev (owner item 2 above).
- Dev Redis memory trend not yet measured — recheck with `performanceCounters | where name has 'Private Bytes'` on `spe-insights-dev-67e2xz`.
