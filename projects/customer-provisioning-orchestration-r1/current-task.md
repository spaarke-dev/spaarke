# Current Task State — `customer-provisioning-orchestration-r1`

> **Format (2026-10-06):** this file holds CURRENT state only and is REWRITTEN at each checkpoint — never prepend a new block on top of old ones. Standing directives + environment gotchas live in the project `CLAUDE.md` ("Standing directives & gotchas"). Session history lives in git (checkpoint commit messages) and the verbatim archive `notes/handoff-history/current-task-archive-2026-10-06.md` (do NOT load it on recovery; grep it only if you need a specific past detail). Why: see `.claude/skills/context-handoff/SKILL.md` "State, not history" and `notes/handoff-history/2026-10-06-conversion-review.md`.

> **Last Updated**: 2026-10-06 SESSION 40 END (by context-handoff, pre-/compact) — tree clean; T229 ✅ T230a ✅; T253/T254 filed; owner APPROVED items 1–3 (W7, OwnedContainerIds on spaarke-bff-dev, remove old L2 Contributor on platform sub); G36 explained, awaiting decision. **SESSION 41: items (2)+(3) done; T230b ✅. Next: T254.** Converted to state-only format 2026-10-06 from that checkpoint (commit `fd49c6513`).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **T254** — optional per-customer OpenAI spend limit (G37): `tasks/254-optional-openai-spend-limit.poml` (FULL, opus/high, BFF hot path). |
| **Step** | 0 — not started. |
| **Status** | pending. T230b ✅ (SESSION 41) — tree clean, pushed. |
| **Next Action** | `task-execute` 254. No live action without owner approval. |
| **Owner items** | (1) **W7 open**: deploy the L2 Api control-plane template (CustomerRunGuard config) with the next L2 Api code deploy — ask before. (2) ✅ / (3) ✅ done SESSION 41 (OwnedContainerIds on spaarke-bff-dev; L2 platform-sub Contributor removed). (4) **G36** explained, awaiting owner decision — do NOT act. Gotcha: Git Bash mangles `/subscriptions/...` → `export MSYS_NO_PATHCONV=1`. |
| **Order** | … → T230a ✅ → T230b ✅ → **T254** → T232 → T233 → T240 → T218 → T235 → T250 → 213.7/207/208/209 → **T253** (G38) → T186. T242c / T241 when the owner wants them. |

## SESSION 41 outcomes

- Owner items (2) + (3) applied on dev and verified.
- **T230b ✅** keyless proof: BFF `POST /api/platform/keyless-proof` (app role `Provisioning.KeylessProof`, held only by the L2 Worker identity via H3); H13 calls it (token `api://{BffAppRegId}`) — any refusal fails, transient = Resumable; ARM keyless verifier; key-credential census ArchTest; H13's four user-workflow sample checks removed (they could never run app-only). Blob probe is `not-in-use` until compose-r8 task 063 sets `SessionFileStore__BlobEndpoint` (accepted for blob only). Publish +0.01 MB. Details: the 230b POML notes.
- Found: BFF does not boot without `AzureOpenAI:Endpoint`+`ChatModelName` (latent F.1; stamps set both). No alert on `failed_open_auth`.

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
