# Task 108 — Deviations

**Task**: Bicep: recreate `sprk-provisioning-jobs` queue with sessions + dedup (C5.4/C4.6) + drain-verify runbook

## Deviation 1 — Module instead of inline `existing` resource (Path C — pivot to comply)

**POML/DS-5 prose said**: declare the queue directly in `platform-controlplane.bicep` as `resource sbNamespace 'Microsoft.ServiceBus/namespaces@...' existing = { name: serviceBusNamespaceName }` plus a child `queues` resource, both inline in that file.

**What actually shipped**: a dedicated module (`infrastructure/bicep/modules/controlplane-sb-queue.bicep`) invoked from `platform-controlplane.bicep` via `module fleetServiceBusQueue '...' = { scope: resourceGroup(serviceBusResourceGroupName), ... }`.

**Why**: `az bicep build` rejected the inline shape with `BCP165`: *"A resource's computed scope must match that of the Bicep file for it to be deployable... You must use modules to deploy resources to a different scope."* `platform-controlplane.bicep`'s ambient scope is the L2 stamp's own resource group (`rg-spaarke-platform-{env}`, reached via the `rg` resource + `scope: rg` pattern used by every other resource family in the file); the fleet Service Bus namespace lives in a DIFFERENT resource group (`SharePointEmbedded` on dev). Bicep requires a module boundary for any resource whose scope differs from the file's own — this is a hard compiler constraint, not a style choice.

**Why this is still Path C (pivot to comply), not a scope change**: the module is Bicep-managed, deterministic, and idempotent — identical intent to the POML's inline description, just via the mechanism Bicep actually requires for cross-resource-group declarations. Task 110 (SB RBAC role assignments) independently anticipates the same constraint in its own POML text ("use a module deployed at the namespace's RG scope (preferred)"), confirming this is the expected pattern for this wave, not a one-off improvisation.

**Verification**: `az bicep build` exits 0 for both `platform-controlplane.bicep` and the new module. A resource-group-scoped `what-if` against the module (see `notes/queue-recreate-runbook-2026-08.md` §2) confirms the queue resource resolves and evaluates correctly against the live namespace.

## Deviation 2 — Live delete-and-recreate NOT executed (explicit dispatch-level scope split)

**POML steps 4-8** (pre-check message count, delete, deploy, verify, RBAC survival check) describe live Azure operations against the dev queue. **The dispatching orchestrator's task instructions explicitly excluded live execution from this run**: *"Do NOT execute the queue delete-and-recreate against live Azure. This task is AUTHORING the Bicep + runbook; live execution is a separate ceremony run by the operator."*

This task therefore completed:
- Steps 0-2 (rigor + load, Bicep resource authoring, `az bicep build` validation) — fully executed.
- Step 3 (what-if) — executed in a NARROWER, safer form: a resource-group-scoped `what-if` against the new module alone (isolated from the rest of the L2 stamp's known, not-yet-fixed config-key drift — see `notes/design-study-ds5-cat456-remediation.md` C5.1), rather than a full subscription-scope `what-if` against the entire `platform-controlplane.bicep` stack. This still empirically confirms the create-time-only-property claim (§2 of the runbook) without the risk of a subscription-wide what-if surfacing unrelated, already-known drift as noise.
- Steps 4-8 — **NOT executed**; fully documented as a runbook (`notes/queue-recreate-runbook-2026-08.md`) for the operator to run separately, per the dispatch instructions.

**Acceptance criteria impact**: 3 of 5 acceptance criteria (`az bicep build` exits 0; the three `grep` checks for `requiresSession`/`requiresDuplicateDetection`/`duplicateDetectionHistoryTimeWindow`; the runbook documents the exact command sequence + observed pre-check count) are MET. The two live-verification criteria (`az servicebus queue show` post-recreate confirms `requiresSession`/`requiresDuplicateDetection` are `true` on the LIVE queue) are **deferred to the separate live-ceremony run** described in the runbook — this is a same-task scope split analogous to task 089's `scaffolding-complete-owner-invocation-pending` pattern, not a failure to meet the criteria as originally scoped by the POML in isolation.

## Deviation 3 — Live message-count discrepancy (documented, not corrected)

Task-authoring-time context (DS-5, `current-task.md`) assumed **1** stale H0 envelope in the live queue (belonging to the C4.5-deleted dead test run) as the known safe-to-discard message. A read-only `az servicebus queue show --query countDetails` check on 2026-08-19 (during this task's authoring) found **0** active and **0** dead-lettered messages — the message referenced in prior sessions is no longer present (likely TTL-expired, or drained during the 2026-08-18 L2 fix/redeploy cycle). This is documented in `notes/queue-recreate-runbook-2026-08.md` §3 as a live-state snapshot, with an explicit instruction that the operator MUST re-run the pre-check (runbook §4 step 1) immediately before deleting rather than relying on this task's authoring-time observation, since new `/api/runs` traffic could change the count before the live ceremony runs.

## Component justification (CLAUDE.md §11) — carried verbatim from POML `<notes>`

Existing — the queue exists live today with wrong (default) properties; `service-bus.bicep`'s uniform-queue-properties shape does not fit this queue (different dedup/session requirements than `sdap-jobs`/`document-indexing`). Extension — cannot extend `service-bus.bicep`'s shared queue list without forcing this queue's properties onto unrelated queues, or vice versa; a dedicated resource block (module) in `platform-controlplane.bicep`'s own composition (the L2 stamp's own file) is correct. Cost-of-doing-nothing — task 102's session processor throws immediately at `StartProcessingAsync` against a non-session queue; L1 SB dedup stays permanently inert, leaving task 107's attempt-field fix with nothing to protect against.
