# 031 — Write-Identity Decision (ESCALATION RESOLVED)

> **Escalation**: Job B apply (031) — under whose identity does the field-update PATCH run.
> **Resolved by owner**: 2026-07-29. **Path**: §6.5 (A) project-scoped decision, documented here + cited in PR.

---

## Decision: Option 2 — Dataverse impersonation header (`MSCRMCallerID`)

The apply endpoint is **user-initiated** (the human clicked "confirm" in r5's card), so the caller's identity
is already known from the validated bearer token — **no OBO token exchange is required on the write**. The
only open question was *whose privileges gate the PATCH and who is recorded as author*. Owner chose
impersonation because **we want to know the person who confirmed/accepted the change**.

### Mechanism
- App keeps its normal app-only Dataverse connection (`ClientSecretCredential` / MI — unchanged).
- Per apply request, stamp `MSCRMCallerID = {confirming user's systemuserid}` via
  [`DataverseImpersonation.Apply`](../../../src/server/shared/Spaarke.Dataverse/DataverseImpersonation.cs)
  (helper already exists; not yet wired into the write core).
- Resolve `systemuserid` from the token's `oid` (Azure AD `oid` → `systemuserid` via
  `azureactivedirectoryobjectid`, the pattern `DataverseAccessDataSource` / `UserPrivilegeChecker` already use).
- Effective privileges = **intersection** of the BFF app user ∩ the confirming user. `modifiedby` on the
  target record = the confirming human.

### Why NOT the other options
- **Option 3 (true OBO token exchange)** — rejected as unnecessary machinery. We already have the user's
  identity at confirm time; we do not need a per-request user-token-scoped Dataverse client. Impersonation
  achieves the same access-privilege + attribution outcome without a second token exchange or forking the
  ADR-013 session-agnostic `IActionSeam` seam.
- **Option 1 (app/service account)** — rejected: `modifiedby` would show the service principal. Owner wants
  native "the person who accepted" attribution on the matter's own history, not only in the audit log.

### Prerequisites / notes for implementation
- BFF application user MUST hold the Delegate privilege `prvActOnBehalfOfAnotherUser` (go-live config;
  owner/Dataverse-admin task — flag in 060 deploy). Until configured, an impersonated write **fails at
  Dataverse rather than silently widening access** — the correct fail direction.
- The `sprk_emailreviewlog` `Approved` + `Applied` rows still carry the human (`sprk_actortype = Human`) —
  audit attribution is unchanged and now matches native `modifiedby`.
- 031 must thread the confirming user's `systemuserid` from the endpoint → `IActionSeam.UpdateRecordAsync`
  (or the underlying write core). This is the ONE structural addition: give the write path an optional
  impersonation identity. Callers that pass none are byte-unchanged (existing app-only behavior).

---

## Implementation outcome (2026-07-29)

Built under Option 2. Additive optional `Guid? impersonateSystemUserId`/`ImpersonateSystemUserId` threaded through
`IActionSeam.UpdateRecordRequest` → `ActionSeam` → `UpdateRecordActionCore` → `IFieldMappingDataverseService.UpdateRecordFieldsAsync`
→ both impls (`DataverseWebApiService` MSCRMCallerID on PATCH via `CreateAuthenticatedRequestAsync`; `DataverseServiceClientImpl`
via cloned `ServiceClient.CallerId`). New `CommunicationProposalApplyService` + `ICommunicationEnvelopeReader` seam +
`POST /api/communications/proposals/{reviewLogId}/apply`. Build clean; 6 seam tests pass; publish-size delta ≈0 (no new
packages); no new HIGH CVE (the pre-existing `System.Security.Cryptography.Xml` HIGH is branch debt, unchanged by 031).

**Adversarial code-review + ADR-check (Step 9.5): SHIP-WITH-FIXES — all 10 security invariants HOLD** (impersonation
reaches the write with no silent app-only path; additive/optional correct so existing callers unchanged; fail-closed 403;
apply-time allow-list re-validation; citation re-verify; audit-on-apply; ADR-028/013/015/019/010/032 compliant; coercion
safe). No Critical/High. Must-fix items applied: added 409 double-apply-guard test + audit-failure-500 test + tightened
the allow-list mock to assert the `sprk_enabled` gate + strengthened the happy-path value/type assertion.

### Accepted limitations / follow-ups (path-A, documented)
- **Concurrency (TOCTOU double-apply)** — no Dataverse transaction spans walk → PATCH → audit, so two *truly concurrent*
  applies of the same proposal could both pass the open-proposal check. Sequential re-apply IS blocked. Narrowed by r5
  disabling the confirm control on submit; blast radius = one idempotent duplicate field-write + one extra audit row, both
  under the confirming user. **Hard guard deferred to task 060**: add a Dataverse alternate-key / duplicate-detection rule
  on `sprk_emailreviewlog` (operator schema). Documented in `CommunicationProposalApplyService.EnsureStillOpenAsync`.
- **HTTP contract test** — the seam tests cover the service's security behavior; a thin `tests/integration/contract/**`
  test for the endpoint's auth/route/ProblemDetails shape is a recommended follow-up (endpoint handler is a 3-line delegate).
- **`prvActOnBehalfOfAnotherUser`** — BFF app user must hold it for the impersonated PATCH (included in System Administrator,
  which the app user has). Verify in task 060 deploy checklist.

## FR-15 mailbox scope (recorded alongside)
- **051b (M365 group mailbox) — DESCOPED** (owner, §6.5 path A). No forked capture pipeline, no tenant-wide
  `Group.Read.All`.
- **Central / shared mailbox** — already supported by shipped code (051a ✅); onboarding is config only.
- **D-07 amended** to "shared/central mailbox supported; M365 group mailbox deferred."
