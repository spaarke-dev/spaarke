# BFF Deploy Runbook — R3 Phase 2 Activation

> **Project**: spaarke-platform-foundations-r3
> **PR**: [#415](https://github.com/spaarke-dev/spaarke/pull/415)
> **Date authored**: 2026-06-23
> **Status**: READY — awaiting team coordination + PR review

## Purpose

Coordinated deployment sequence for the R3 BFF changes (membership service + scheduling framework + playbook hardening + Daily Briefing P0 wiring) plus the Phase 2 feature-flag activation that turns on the event-driven membership sync.

This runbook **deploys from master only** via the existing `deploy-bff-api.yml` workflow. R3 must be merged to master first.

---

## Pre-Deploy Gates (verify before merging PR #415)

### 1. Branch sync state
- [x] Master merged into `work/spaarke-platform-foundations-r3` (clean, no conflicts, 2026-06-23)
- [x] Build clean: `dotnet build Spaarke.sln` — 0 errors / 18 warnings (all pre-existing)
- [x] Targeted tests pass: Daily Briefing + CreateNotification + Membership (256 tests, 0 failed)
- [ ] Full BFF unit suite green (run before merge)
- [ ] CI all-green on PR #415 (re-runs automatically after merge-into-branch push)

### 2. Cross-PR file conflict check (verified 2026-06-23)
None of the open BFF-touching PRs intersect R3 file paths:
- #401 R6 unification — touches `CapabilityRouter.cs`, `SprkChatAgentFactory.cs`, `ToolHandlerToAIFunctionAdapter.cs` (no overlap)
- #406 smart-todo-r4 UAT — frontend only (no overlap)
- #409 chat-routing-redesign-r1 init — touches `ChatEndpoints.cs`, `PlaybookEmbeddingEndpoints.cs`, `PlaybookEndpoints.cs`, `CapabilityRouter.cs`, `AiModule.cs` (no overlap)

Re-run this check before merge in case state has changed:
```bash
gh pr list --base master --state open --json number,headRefName | jq -r '.[].number' | xargs -I{} sh -c 'echo "PR #{}: " && gh pr diff {} --name-only | grep "Sprk.Bff.Api\|Spaarke.Dataverse\|Spaarke.Scheduling" | head -10 && echo "---"'
```

### 3. Phase 2 infra prerequisites
- [x] Service Bus namespace `spaarke-servicebus-dev` upgraded Basic → Standard (2026-06-23)
- [x] Topic `sprk-membership-changes` deployed (Active, 1 GB, 14d TTL)
- [x] Subscription `recon-junction-updater` deployed (Active, 10 max delivery, 5min lock)
- [x] BFF MI RBAC: Sender on topic + Receiver on subscription
- [x] Dataverse entities deployed to spaarkedev1 (`sprk_backgroundjob`, `sprk_backgroundjobrun`, `sprk_userentityassociation`, `sprk_document` field migrations)
- [ ] UAT/staging/prod Service Bus namespaces verified at Standard tier (`az servicebus namespace show --query sku.name`)
- [ ] UAT/staging/prod Dataverse: run idempotent entity creation scripts

### 4. Semantic interaction with R2.2 hotfix (worth manual check post-merge)
- R3 modified `BriefingService.cs` (Wave 28 — now calls `IMembershipResolverService`)
- R2.2 hotfix (in master) modified `DailyBriefingEndpoints.cs` + `CreateNotificationNodeExecutor.cs`
- These layer cleanly — different files — but worth a manual spot-check that the endpoint still passes the right data shape into BriefingService
- New test file `BriefingServiceTests.cs` (Wave 28, 7 scenarios) covers the wiring; run after merge

---

## Deploy Sequence

### Step 1 — Final PR review + approval
1. Reviewer(s) approve PR #415 on GitHub
2. Verify CI is green on the merged-with-master HEAD of the branch
3. Confirm with R6 (#401), smart-todo (#406), and chat-routing (#409) PR owners that they're aware R3 is merging (no file overlap but courtesy notice)

### Step 2 — Merge to master
- Squash or merge commit per team convention (R3 is a large PR; squash recommended)
- Merge target: `master`
- After merge: `deploy-bff-api.yml` workflow auto-triggers on push to master with `paths: src/server/api/**` (R3 changes touch this path → workflow fires)

### Step 3 — Dev-first validation (recommended before production)

The auto-deploy on master triggers production by default. To validate in dev first, **manually trigger dev deploy** before the auto-prod fires (or temporarily disable the auto-deploy and use manual dispatch only):

```bash
# Manual dev deploy from master
gh workflow run deploy-bff-api.yml --ref master -f environment=dev

# Monitor
gh run list --workflow=deploy-bff-api.yml --limit 1
gh run watch --exit-status
```

Pipeline: build → test → deploy staging slot → verify staging → swap → verify production → (auto-rollback on failure).

### Step 4 — Post-dev-deploy verification

#### Smoke checks (zero-state — Phase 1A only; flags still OFF)
1. `GET /healthz` → 200
2. `GET /api/admin/jobs` (with SystemAdmin token) → returns 2 jobs (`notification-playbook-scheduler`, `membership-reconciliation`)
3. `GET /api/admin/jobs/notification-playbook-scheduler/status` → status object with last 10 runs
4. `GET /api/admin/membership/discovered/sprk_matter` → returns Q4-aligned field list (8 `sprk_assigned*` fields)
5. `GET /api/users/me/memberships/sprk_matter` (as user with known assignments) → returns IDs grouped by role

#### Run task 073 smoke test (validates topic + subscription end-to-end)
```bash
# Publish a test MembershipChangedEvent to the dev topic; verify subscription receives + handler processes
# Procedure documented in projects/spaarke-platform-foundations-r3/tasks/073-bicep-deploy-topic-smoke-test.poml
```

### Step 5 — Flip Phase 2 feature flags (when dev validation passes)

After dev validation, flip the 3 ADR-032 kill-switches OFF → ON to activate the event-driven sync:

```bash
# Get target App Service config
az webapp config appsettings list \
  --resource-group rg-spaarke-dev \
  --name spaarke-bff-dev \
  --query "[?contains(name, 'Membership')]" -o table

# Flip 3 flags ON (and configure topic/namespace settings)
az webapp config appsettings set \
  --resource-group rg-spaarke-dev \
  --name spaarke-bff-dev \
  --settings \
    Membership__EventPublisher__Enabled=true \
    Membership__EventPublisher__TopicName=sprk-membership-changes \
    Membership__JunctionUpdater__Enabled=true \
    Membership__JunctionUpdater__TopicName=sprk-membership-changes \
    Membership__JunctionUpdater__SubscriptionName=recon-junction-updater \
    Membership__JunctionUpdater__ServiceBusNamespace=spaarke-servicebus-dev.servicebus.windows.net \
    Membership__CacheInvalidator__Enabled=true \
    Membership__CacheInvalidator__Channel=membership-cache-invalidate

# App Service auto-restarts on settings change; verify
az webapp show --resource-group rg-spaarke-dev --name spaarke-bff-dev --query state
```

#### Post-flip verification
1. BFF logs: confirm `MembershipEventPublisher` initialized (not `NullMembershipEventPublisher`); same for JunctionUpdater + CacheInvalidator
2. Trigger a matter mutation via Office QuickCreate endpoint (when reachable; currently TODO #026 — see "Known Limitations" below)
3. OR trigger a document POST → confirm `MembershipChangedEvent` published to topic (peek via `az servicebus topic subscription peek-messages`)
4. Confirm `MembershipJunctionUpdaterHost` consumed the message + wrote to `sprk_userentityassociation`
5. Confirm `MembershipCacheInvalidator` published to Redis channel `membership-cache-invalidate`
6. Hit `GET /api/users/me/memberships/sprk_matter` again — confirm cache refreshed with new junction state

### Step 6 — Run task 095 manual UAT in spaarkedev1

Per `projects/spaarke-platform-foundations-r3/tasks/095-manual-uat-h2-scenarios-spaarkedev1.poml`:
- H2 scenario list (PlaybookBuilder OutputVariable rename, branch picker, edge perf hint)
- Daily Briefing scenario (verify real top-priority matter selection vs prior mock)
- Migrated playbook scenarios (notification-new-documents/emails/events produce non-zero notifications for seeded users)

### Step 7 — UAT promotion (after dev sign-off)

When dev sign-off passes:
- Verify UAT Service Bus namespace tier is Standard (`az servicebus namespace show --query sku.name`)
- If not, upgrade per the Option-A pattern documented in `operator-followup-task071.md`
- Deploy topic Bicep to UAT (same module, different namespace param)
- Run Dataverse entity creation scripts in UAT
- Trigger `gh workflow run deploy-bff-api.yml --ref master -f environment=uat` (if env exists) OR follow UAT promotion runbook
- Flip feature flags in UAT
- Smoke test + UAT sign-off

### Step 8 — Production deploy

When UAT signs off:
- Verify prod Service Bus namespace is already Standard (it is: `spaarke-demo-prod-sbus`)
- Deploy topic Bicep to prod
- Run Dataverse entity creation scripts in prod
- Either: let the auto-deploy fire (it will, on the master push), OR explicit `gh workflow run deploy-bff-api.yml --ref master -f environment=production`
- Flip feature flags in prod
- Smoke test
- Sign off

---

## Rollback Procedure

### If `deploy-bff-api.yml` health check fails post-swap
Workflow auto-rolls back via slot re-swap. No manual action; just review logs.

### If post-deploy issues surface after manual validation
**Code rollback**: revert the merge commit on master + push → triggers deploy of the prior state.
```bash
git revert -m 1 <merge-commit-sha>
git push origin master
# deploy-bff-api.yml auto-fires; deploys reverted master
```

**Feature flag rollback only** (recommended first for Phase 2 issues): flip flags OFF without code change.
```bash
az webapp config appsettings set \
  --resource-group rg-spaarke-dev \
  --name spaarke-bff-dev \
  --settings \
    Membership__EventPublisher__Enabled=false \
    Membership__JunctionUpdater__Enabled=false \
    Membership__CacheInvalidator__Enabled=false
# App restarts; Null peers take over; Phase 1A still functional; recon job continues nightly
```

This is the entire point of ADR-032 — you can disable Phase 2 instantly without redeploying.

### Topic rollback (if Bicep was wrong)
Topic + subscription can be deleted via Azure portal or CLI; doesn't affect any queues. RBAC assignments delete with the topic.
```bash
az servicebus topic delete \
  --resource-group SharePointEmbedded \
  --namespace-name spaarke-servicebus-dev \
  --name sprk-membership-changes
```
**Note**: this loses any in-flight messages on the subscription DLQ. Inspect first.

### Namespace tier rollback (NOT possible)
Standard → Basic is not supported by Azure. If R3 needs to be fully reverted, the topic + R3 code go away; the Standard-tier namespace stays. This is acceptable — the upgrade has no downside.

---

## Known Limitations Operating in Dev

### Office QuickCreate endpoint is not currently mapped
- `MapQuickCreateEndpoints` in `OfficeEndpoints.cs:54-55` is commented out with a pre-existing TODO referencing "task 026" (from a different project, not R3)
- R3's matter cluster publisher hookup (task 081) is inside this method
- **Impact**: Matter creation events via QuickCreate won't fire today
- **Workaround**: Matter assignments come from maker-portal edits (per task 080 inventory finding) — these are picked up by the nightly `membership-reconciliation` job (which IS active by default). Real-time matter events come online when the QuickCreate endpoint is restored by whichever project owns task 026.

### Document + Event clusters work fully
- Document mutations (POST/PUT/DELETE on `/api/v1/documents`) fire publisher
- Office save fires publisher
- Event create fires publisher
- These hookups are live regardless of QuickCreate status

### Topic-deploy didn't include a per-environment check
- This runbook focuses on dev (spaarkedev1)
- UAT + prod need their own topic deploys before BFF deploy to those envs
- Prod SB namespace (`spaarke-demo-prod-sbus`) is already Standard tier — no upgrade needed there

---

## Sign-Off Checklist (per environment)

| Step | Dev | UAT | Prod |
|---|---|---|---|
| Branch sync + build clean | [x] | n/a | n/a |
| Cross-PR conflict check | [x] | n/a | n/a |
| SB namespace at Standard tier | [x] | [ ] | [x] (already Standard) |
| Service Bus topic deployed | [x] | [ ] | [ ] |
| Dataverse entities deployed | [x] | [ ] | [ ] |
| BFF deploy via deploy-bff-api.yml | [ ] | [ ] | [ ] |
| /healthz + admin endpoint smoke | [ ] | [ ] | [ ] |
| Task 073 topic smoke test | [ ] | [ ] | [ ] |
| Feature flags flipped ON | [ ] | [ ] | [ ] |
| Post-flip event-flow verified | [ ] | [ ] | [ ] |
| Task 095 manual UAT (dev only) | [ ] | n/a | n/a |
| Production sign-off | n/a | n/a | [ ] |

---

*Authored by R3 wrap-up; coordinated deploy pending team conversation per user directive 2026-06-22.*
