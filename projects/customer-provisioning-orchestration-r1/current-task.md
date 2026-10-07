# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing directives + gotchas → project `CLAUDE.md` "Standing directives & gotchas". Decisions → notes. Session narrative → checkpoint commit messages. History: git + `notes/handoff-history/current-task-archive-2026-10-06.md` (do not load on recovery). Review limits (repo procedure 2026-10-06): F1–F4 fix now / K1–K4 known limit; ≤ 2 fix rounds re-verifying only the fix diff + direct callers/callees; 1 verifier pass per task (2 for auth/security); escalate an F1 still open instead of a round 3.

> **Last Updated**: 2026-10-07 SESSION 41 — T254 closed (f06296312); T232 POML created; owner chose PAYG licensing.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **T232** — H11 makes Model 1 guests usable (D2, G10). POML: `tasks/232-h11-b2b-guests-payg-dataverse-users.poml`. Owner 2026-10-07: licensing = **pay-as-you-go** (operator links the environment to a billing policy on the stamp subscription; no per-user licences). |
| **Step** | Step 9.5 done to the round cap: round 1 `07a92eca3`, round 2 `79ca02b3c`. ControlPlane 2273/0 (1 skip), ArchTests 836/836, Worker + Api 0 warnings. **Final verifier: 1 Critical OPEN (C1) — ESCALATED to owner, no round 3.** |
| **Status** | blocked — awaiting owner decision on C1 (2026-10-07). |
| **Next Action** | On owner approval: fix C1 — SKILL.md Step 1e-bis PRQ-C-10 `$ppEnvId` call: `az.cmd` on Windows breaks on the `)` in `RetrieveCurrentOrganization(AccessType=@p)` → use `az account get-access-token --resource $dvRes` + `Invoke-RestMethod` (recommended), also W1 (anchor `Enabled` with -cmatch once T186 shows the shape; record in K3), W2 (tests for the consent-timeout filter + timeout message), S1 (two stale lines). Then close: POML notes, TASK-INDEX ✅, current-task → T233, devops sync (Tasks Completed 193). |
| **Branch** | `work/customer-provisioning-orchestration-r1` — **23 behind / 44 ahead of `origin/master`** (measured 2026-10-06). Merge master before T186 and before any BFF deploy from this branch. |
| **Order** | T254 ✅ → T232 → T233 → T240 → T218 → T235 → T250 → 213.7/207/208/209 → **T253** (G38) → **T255** (INCOMING-141) → **T256** (INCOMING-145) → T186. T242c / T241 when the owner wants them. |

## Owner items

0. **T254 owner asks** (raise at close): approve the 17th `SystemCacheKeys` entry `AiSpendMonth` (stamp-wide Redis key); confirm the two §6.5 Path A exceptions in design §17 (Set-AiSpendLimit.ps1 single-setting writes vs provisioning.md; no ValidateOnStart for AiSpendLimit options). Client change (`useSseStream.ts`, `SprkChat.tsx`) reaches users only with the next code-page/PCF build.

1. **W7 (approved, open)**: deploy the L2 Api control-plane template so the Api gets `CustomerRunGuard` config — the code defaults `CustomerRunGuard:Enabled=true`, the Worker sets it false; the Api ACQUIRES the guard and the Worker RELEASES it. **Blocked by item 3 below** (every `platform-controlplane` deploy fails its Api module on the pending slot swap). Ask before deploying.
2. ✅ SESSION 41: `SharePointEmbedded__OwnedContainerIds` on spaarke-bff-dev; L2 UAMI's platform-subscription Contributor removed.
3. **Api site's pending slot swap** — every platform-controlplane deploy fails its Api module; resolves W7.
4. **G36** — ADR-027 management group for customer subscriptions: explained; awaiting the owner's decision — do NOT act.
5. **G31** — H10 grants Model 1 stamps tenant-wide Directory/User write roles in Spaarke's tenant.
6. Board Status "Active" vs Status Reason "On hold" on Issue #438.

## Cross-project deliveries (track until delivered)

- **INCOMING-141 + INCOMING-145 from UAC-r2** — on master since #1312; ACCEPTED here as T255 / T256 (2026-10-06). Still to do: acknowledge to UAC-r2 (owner relays, or a #1094 comment — **ask the owner first**).
- **`sprk_noaccessentry` prerequisite — NOT RECEIVED as a hand-off** (2026-10-06). UAC-r2 documents it only in its own notes (`DEPLOY-CHECKLIST.md`, `batch4-integration-steps.md`, `docs/data-model/INDEX.md`); no INCOMING file tells provisioning how a new environment gets the table (solution component? which solution? before H9?). Ask UAC-r2 (via the owner) for it; T256 / T218 depend on it.
- **To UAC-r2: do NOT run `task-165-admin-surfaces.md` §13.9(b)/§14.9(b) step 2 (`-MintClientSecret` on `bfac7f6e`)** — conflicts with D16/T250 and is unnecessary since SPE Admin runs as the BFF identity (2026-10-04). Note: `notes/coordination/2026-10-06-uac-r2-task165-13-9b-mint-secret.md`. **NOT YET DELIVERED** — ask the owner how to deliver (relay or #1094).

## Open items (no task yet)

- `RoutingConsumerTypeHealthCheck FAILED: AI catalog drift` on the dev BFF — reported to the owner, no outcome recorded.
- BFF MI lacks `SecurityEvents.Read.All` (§6B Security tab) — no owner/task.
- Dev Redis memory trend — recheck `performanceCounters | where name has 'Private Bytes'` on `spe-insights-dev-67e2xz` (`spe-infrastructure-westus2`).
- No alert on `failed_open_auth` (Prompt Shield) — only H13 catches a refused Content Safety identity, at provisioning.
- Latent: the BFF does not boot without `AzureOpenAI:Endpoint` + `ChatModelName` (chat endpoints unconditional, `IChatClient` conditional); stamps always set both.

## T186 (first live E2E) — open questions + live checks

- **Target customer undefined** after D-12/T228 (SESSION 11's trial1 answers are superseded; trial1 was never created). Needs: the customer's own subscription, Dataverse environment `spaarke-{customerId}`, container type id. Do NOT use `runs/trial1-intake.json`.
- H12a seeds 4 of 12 artifacts (playbooks + playbook consumers pending — task 150).
- (T228) H5 WhoAmI as the Worker in an operator-created env; H1 RG listing under Owner; H6 sign-in right after H10.
- (T227e) Worker reads `environmentvariabledefinitions`; marker PATCH right after the bind PATCH.
- (T227g) the BFF app registration can PATCH `businessunit.sprk_containerid` (403 → Resumable `dataverse-auth-failure`).
- (T227d) marker PATCH on a just-created container; `deletedContainers/{id}` returns the marker; Graph refuses app-only container-type operations for a stamp UAMI.
- (T251) W1 (group Name vs DisplayName).
- (T230b) every keyless-proof service `proved` live; ADR-028 E-2 measured on the stamp's `kind: OpenAI` account (H13 log "ADR-028 E-2 measurement"); H3's `appRoleAssignedTo` POST succeeds; the L2 token carries `roles: Provisioning.KeylessProof`, no `scp`; ARM keyless check reads the stamp as L2 (Owner). Blob = `not-in-use` until compose-r8 task 063.

## Notes for later tasks

- T250 likely superseded by master `bb8ba7251` (SPE Admin as the BFF MI) — raise with the owner when reached.
- T242c re-scope per D27 (demo = next env for the orchestration; `rg-spaarke-demo` has no AI Search; demo lacks `AiSafety__ContentSafety__Endpoint`).
- T241: now lists the shared tier's non-Azure leftovers (Dataverse env, two Entra apps, a do-not-sweep list) — owner decision first on `spaarke-model1-prod` references.
- T218: replace the hand-uploaded H6 artifacts in `sprkcpartifactsdev/provisioning-artifacts`; ship `sprk_noaccessentry` (T256).
- Tasks without a POML: create from `notes/model1-dedicated-remediation-plan.md` §7 (copy `tasks/228-…poml`), add TASK-INDEX rows.
- Parked follow-ups: T246 a–f, T247 (a)–(c) (pin refresh before ~2027-01-14), T244, G34, T252 — see those POML notes.

## Live state (dev)

- Dev BFF `spaarke-bff-dev` runs branch build `0911515d7`; future dev BFF deploys only from master ≥ `c8b93b294`. T227d not on dev yet (OwnedContainerIds is set, ready for it).
- Dev BFF MI holds application `full` on the Model 1 container type (owner option A) — see CLAUDE.md Keep.
