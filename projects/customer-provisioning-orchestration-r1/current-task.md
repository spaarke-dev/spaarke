# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2 "Binding rules", §3 "Owner directives", §6 "Gotchas" (one dated line each). Decisions + superseded rules → `notes/decisions.md`. Session narrative → checkpoint commit messages. History: git + `notes/handoff-history/` (do not load on recovery). Review limits: task-execute Step 9.5.

> **Last Updated**: 2026-10-07 SESSION 42 — T233 ✅ (BFF TenancyModel names + tenancy wording); PR #1365 open (CI green); next T240.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **T240** — Shared M365 clients → per-customer BFFs (G14; owner D9/D11 approach approved: ONE shared add-in + ONE Teams package, runtime customer discovery, H3 pre-authorizes the shared client apps on every customer BFF app-reg). **No POML yet** — create it from plan §7 row T240 + `notes/g14-shared-clients-auth-chain.md` (copy `tasks/233-…poml` shape), add the TASK-INDEX row (7 columns). |
| **Step** | 0 — not started. T233 ✅ (SESSION 42) — see its POML notes. |
| **Status** | pending. |
| **Next Action** | Read plan §7 T240 + `notes/g14-shared-clients-auth-chain.md`; create `tasks/240-…poml` + TASK-INDEX row; `task-execute` 240. Watch PR #1365 (CI: all green except the legacy SDAP "Build & Test (Debug)", continue-on-error); merging it is the owner's call. No live action without owner approval. |
| **Branch** | `work/customer-provisioning-orchestration-r1` — master merged again 2026-10-07 SESSION 42 (0 behind at merge). Measure with `git rev-list --count HEAD..origin/master`; merge master again before T186 and before any BFF deploy from this branch. The merge re-routed master's new task-171 app-only SPE calls through `SpeContainerOwnershipGuard` (see CLAUDE.md §6). |
| **Order** | T254 ✅ → T232 ✅ → T233 ✅ → T240 → T218 → T235 → T250 → 213.7/207/208/209 → **T253** (G38) → **T255** (INCOMING-141) → **T256** (INCOMING-145; waits on #1364) → T186. T252 also carries the Bicep "shared BFF app-reg" description fix (T233 hand-off). T242c / T241 when the owner wants them. |

## Owner items

0. ✅ 2026-10-07: owner approved T254's `AiSpendMonth` cache key and both §6.5 Path A exceptions, and T232's PAYG per-app meter. T254's client change (`useSseStream.ts`, `SprkChat.tsx`) reaches users only with the next code-page/PCF build.

1. **W7 (approved, open)**: deploy the L2 Api control-plane template so the Api gets `CustomerRunGuard` config — the code defaults `CustomerRunGuard:Enabled=true`, the Worker sets it false; the Api ACQUIRES the guard and the Worker RELEASES it. **Blocked by item 3 below** (every `platform-controlplane` deploy fails its Api module on the pending slot swap). Ask before deploying.
2. ✅ SESSION 41: `SharePointEmbedded__OwnedContainerIds` on spaarke-bff-dev; L2 UAMI's platform-subscription Contributor removed.
3. **Api site's pending slot swap** — every platform-controlplane deploy fails its Api module; resolves W7.
4. **G36** — ADR-027 management group for customer subscriptions: explained; awaiting the owner's decision — do NOT act.
5. **G31** — H10 grants Model 1 stamps tenant-wide Directory/User write roles in Spaarke's tenant.
6. Board Status "Active" vs Status Reason "On hold" on Issue #438.

## Cross-project deliveries (track until delivered)

- **2026-10-07: one relay message for UAC-r2 handed to the owner** (the owner relays it). It covers:
  - the acknowledgement of INCOMING-141/145 (accepted as T255/T256);
  - the `sprk_noaccessentry` request — issue **#1364** (`notes/defer-issues.md` ISS-001);
  - the demo-grant marker defect in their task-171 code — issue **#1363** (ISS-002);
  - the §13.9(b) do-not-mint note (`notes/coordination/2026-10-06-uac-r2-task165-13-9b-mint-secret.md`);
  - how the merge re-routed their app-only SPE calls through `SpeContainerOwnershipGuard`.

  Still open: UAC-r2's answer on #1364, which T256/T218/T186 wait on, and its fix for #1363.

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
