# Task 133: secure provisioning can no longer lock the creator out (C11, #1054)

> **Date**: 2026-10-01/02 · **Branch**: `task/uac-r2-133` (from `integ/uac-r2-batch2`, task 144 merged) · **Rigor**: FULL (opus)
> **Status**: code, tests, client, guide complete. The live proof (POML steps 2 and 8) is a **pending manual gate**: this
> run was read-only against Dataverse by instruction, and the gate needs task 144's live cutover first (the named team
> does not exist in dev yet). Script: [`task-133-live-gate.ps1`](task-133-live-gate.ps1).

---

## 1. The defect, and what replaced it

Provisioning moved a secure record to the memberless owner team and only THEN shared it to its creator. If the share
failed, the creator was locked out; "retry" was impossible (the creator failed the delegation filter's Write check,
anyone else got 409, and a retry would have shared to whoever called).

| Before | After (task 133) |
|---|---|
| WhoAmI after the move | WhoAmI, the record's owner, the creator's current share (complete read) and the container type are all read **before any write** |
| Share after the move | **Share-first** (step 4.5) while the record is where it was created; **proven** after the move (step 5.5) and re-issued if the move dropped it |
| Share failure after the move: 500, ownership left moved | **Compensation**: owner PATCH back to the pre-call owner, read back; the creator's share put back to its pre-call mask (revoked if none), read back |
| Team-owned + no container: 409 | **Resume**: share to the record's `createdby` (never the caller as substitute), colleagues, container, record. Team-owned + container: 409, zero writes (unchanged) |
| Owner read-back throws: "Nothing has been provisioned" | `owner_assignment_unverified`: outcome unknown, the creator's share kept in place; the next call resumes or restarts by observed state |
| Container failures: no reason code | `container_creation_failed` (resumable); `container_not_recorded` now resumable too (the named container is empty) |
| One call, no retry in the wizard | `SecureProvisioningOutcome` renders the "Try securing again" action for the retryable states, in the Create Project wizard AND SummarizeFilesDialog |

**The ordering rule** (owner S5, round 3): every single failure ends either with ownership back as it was (the creator
keeps their pre-call access and passes the Write gate) or with the creator's share in place. Only a double failure
(share + compensating move) can leave nobody — it has its own code, a CRITICAL log line, and an administrator resume.

### Robust to the two unproven platform behaviours

The POML asked to choose share-first OR post-move from a live proof. The proof needs live writes (not allowed in this
run), so the code is built to be correct under every outcome and the live gate records which branch dev takes:

- **(a) GrantAccess to the record's CURRENT owner refused** → for a creator-owned record the share-first failure falls
  back to the post-move grant (compensation covers it). Test `Provisioning_WhenAShareToTheCurrentOwnerIsRefused_GrantsAfterTheMove`.
- **(b) The share does not survive the reassignment** → step 5.5 sees it missing and re-issues it.
  Test `Provisioning_WhenTheMoveDropsTheShare_ReissuesIt`.
- **Pre-call share set unreadable** → share-first not used; grant after the move; compensation's only target is "no
  share". Test `Provisioning_WhenThePreCallSharesCannotBeRead_GrantsAfterTheMoveInstead`.

---

## 2. Step 1: live census (READ-ONLY, spaarkedev1, 2026-10-01)

Script: session scratchpad `task133/census.ps1` (GET only, operator `az` token, explicit URL).

| Check | Result |
|---|---|
| (c) `organization.sharetopreviousowneronassign` | **False** |
| (d) Assign cascade on 1:N of each root | project → `team`, `sharepointdocumentlocation`, `sharepointdocument` (Cascade); matter → the same three; **work assignment → none** |
| `createdby` per creation path | 19 projects: **0** app-created. 59 matters: **1** app-created (`# mi-bff-api-dev`, 2026-09-04 — Office quick-create, `RecordCreationService` app-only), `createdonbehalfby` **empty**. 22 work assignments: 0 app-created |
| Create-time writers of `sprk_containerid` | **0** `sprk_fieldmappingrule` rows target it. Code: `EntityCreationService.applyUserBuDefaults` writes only `sprk_searchindexname` (task 076); `RecordCreationService` never writes it; matter/project/WA services no longer stamp it |
| Census: secure rows owned in the Secure BU | **0** projects, 0 matters, 0 WAs. The one secure project (`65a3fab2`) is owned by an ordinary BU team (`cf15f587`), container set, created by a human |

---

## 3. Escalation triggers, evaluated

| Trigger | Result |
|---|---|
| `sharetopreviousowneronassign` true | **Did not fire** (False) |
| App-created provisionable roots (`createdby` = application user) | **Fires literally** (1 app-created matter; Office quick-create creates matters/projects app-only). **Answered by owner decision F8** (round 3, accepted as recommended): ship the interim default — `createdby` for human-created rows, refusal with its own code (`resume_creator_unavailable`, `creatorState: application-user`) for app-created rows, never share to whoever calls. **Verified live as F8 asked**: `createdonbehalfby` is EMPTY on the app-created matter, so option (b) is unusable and **(a) a new server-stamped creator column is the remaining option** — 🔔 an open owner decision, recommended before app-created rows are routinely made secure (task 150's ribbon will let a user secure an Office-created matter; its resume then needs an administrator) |
| Team-owned row with a non-provisioning container, or a create-time `sprk_containerid` writer | **Did not fire** (0 team-owned rows; 0 rules; no code writer) |
| Assign cascade on a root | **Fires literally** (6 platform-managed relationships). **Treated as answered by owner round 4 item 3**: the cascade to `team`, `sharepointdocumentlocation` and `sharepointdocument` is accepted for secure-root assignments; compensation is the same assign in reverse. Residual, stated: a child of one of those three types whose own owner differed from the root's before the forward move ends with the root's pre-call owner after compensation. 0 such rows exist for secure roots in dev; work assignments have no cascade. 🔔 **OPEN owner confirmation (verifier round 1)**: round 4 item 3 accepted the cascade for task 144's migration and assignment; it did not say it covers COMPENSATION, which can re-own a child whose prior owner differed from the root's to the root's pre-call owner. The POML trigger said STOP and present (a) snapshot+restore each re-owned child / (b) no compensation for cascading roots (resume only) / (c) coordinate with task 148. The shipped code compensates (the executor's reading); the owner must confirm that extension explicitly or pick (a)/(b)/(c). No code changed for this in round 1 |
| Neither share-first nor post-move can be made reliable | **Not evaluable** without live writes — the live gate decides; the code does not depend on either (§1) |

---

## 4. ADR-003 path C (adr-tensions)

Compensation moves a secure-flagged record back from the memberless team to its pre-call owner — the access state every
earlier refusal (and the record since creation) already has. No branch gives anyone access they lacked: a share this
call issued is removed when the move is undone; `sprk_issecure` is never cleared, so uploads still fail closed. The
"no rollback" principle now reads: rollback is for ownership and shares only; container failures are never rolled back
(moving a record out of the Secure BU because storage failed would turn a storage failure into a disclosure). **Path C
(comply)** — the reviewer confirms in the PR.

Two residuals, stated rather than hidden (both favour S5 — someone can open the record — over exact share parity):
- **Unverified owner move**: the creator share is first ENSURED by the complete read (verifier round 1 — it is no longer
  assumed from share-first); only if that read or write fails is it issued without a read, and the response then says
  `creatorShareConfirmed: false` ("issued but NOT confirmed"). If the move had in fact NOT landed, the creator now holds a
  share (with `ShareAccess`) they did not hold before, on a record they already had Write on. Leaving no share would risk
  a record nobody can open if the move DID land.
- **Pre-call share set unreadable + compensation**: the only safe restore target is "no share", so a pre-existing explicit
  creator share is removed with the one this call issued (narrowing, never widening; ownership is restored, so the
  creator keeps whatever their role/ownership gives). Since verifier round 1 the response SAYS so: `sharesRestored:
  false`, `creatorShareRemoved: true`, and a detail naming the removed pre-call share — on this branch the criterion
  "share set equals the pre-call set" does not hold, and the response no longer claims it does.

---

## 5. Placement and component justification (CLAUDE.md §10 / §11)

**Placement**: all server code stays in the existing endpoint (`ProvisionProjectEndpoint`). No new endpoint, service,
DI registration, option, job, package, column or interface.

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| 6 reason codes: `creator_share_failed_resumable`, `resume_creator_unavailable`, `owner_assignment_unverified`, `container_creation_failed`, `container_type_not_configured`, `record_owner_unreadable` | the 16 `sdap.provision.*` codes | Extends that set. No existing code can be reused: each names a state with a different recovery (who, how) and the client must tell them apart | One message for "you can retry" and "an administrator must finish" — one of them false (FR-31) |
| `RecordShareLevels.MaskForRightsCsv` / `RightsCsvForMask` | `RecordShareLevels` (the ONE mask table; `LevelForMask`, `Intersect`) | Extends it with the rights no level carries (Share, Assign, Create) — "a mask is built here and nowhere else" | The creator share (with `ShareAccess`) could not be confirmed exactly, nor restored by ModifyAccess |
| `ProvisionProjectResponse.Resumed` | the DTO | Additive member | An operator cannot tell a resume (shared to `createdby`) from a forward run (shared to the caller) |
| `SecureProvisioningOutcome.tsx` (client) | `CloseProjectDialog` retry (different operation, a modal); grep `provisionSecureProject` → only the two wizard hosts, no retry anywhere | Not an extension of a static `IWizardSuccessConfig` (it cannot hold state); it IS the extension of the wizards' failure state | The server's "the same caller may retry" is unactionable — the user is left with a secure-flagged record that refuses uploads (deviation from the POML's "no new component": the success config is static, so the stateful action needs a component; it is not exported from the barrel) |

Publish size: not measured (the main session measures). No package added; no CVE change.

---

## 6. False statements corrected

`ProvisionProjectEndpoint.cs`: class remarks (`deliberately no rollback`, step list, item 3 `at CREATE time`), the
`a share failure leaves NOTHING orphaned` / `a retry resolves cleanly` comment, the `retry once the share path is
healthy` detail, the `creator_unresolved` detail (now true: it runs before any change), `Nothing has been provisioned`
on an unverified move, the 409 "Reassign the record's owner and retry" detail (the no-container state is now resumed),
`RecordContainerAsync` remarks + log line (`came from the wizard's business-unit cascade`), `RootRow` remarks
(`must never be used as the marker` → the two-half marker and why it is sound since task 076).
`RecordContainerResolver.cs`: the route → `POST /api/v1/external-access/provision-project`.
`provisioningService.ts`: the `owner_assignment_*` "Nothing has been provisioned" fall-through note, the "never advise
try again once ownership may have moved" rule, the hedged already-provisioned copy, the share-failed copy, and the
environment copy's "created as a normal project" (the flag stays set). `CreateProjectWizard.tsx`: `a refusal means
nothing was moved`. `SecureProjectSection.tsx`: step order (sharing now first) and step references.
Found during execution and fixed: `SummarizeFilesDialog.tsx` warning "its documents would go to the shared container"
(false since task 076 — uploads are refused) and its comment that `projectService` cascades `sprk_containerid`;
`projectService.ts` comments claiming it cascades `sprk_containerid`; `secure-project-fields-schema.md`'s
"Also cascaded at project CREATE time" row.
Grep of the seven named phrases over `src/` (excluding generated `bundle.js`): **0 hits**.

---

## 7. Share-writer sites for task 143 (No Access guard — not landed)

Task 143's census cites `:726` / `:754`, which this task moved. Every POA write provisioning now issues:

| Site (method in `ProvisionProjectEndpoint`) | Writes | When |
|---|---|---|
| `EnsureCreatorShareAsync` | GrantAccess / ModifyAccess to the creator | pre-move (share-first), post-move proof, resume (to `createdby`) |
| `MoveWithCreatorShareAsync` (unverified-move branch) | `EnsureCreatorShareAsync` (read, Grant/Modify, read back); if that fails, GrantAccess to the creator without a read | owner read-back threw |
| `RestoreCreatorShareAsync` | RevokeAccess / GrantAccess / ModifyAccess back to the PRE-CALL mask | compensation only — never widens beyond the pre-call state |
| `ShareToColleaguesAsync` | GrantAccess (Collaborate) to named colleagues | after the creator share is proven; on a RESUME only when the caller is the record's `createdby` (verifier round 1) |

A resume whose `createdby` is unusable but where a person already holds a share (verifier round 1) issues NO share — it
reads the shares and the sharee's systemuser only.

On RESUME, a `createdby` on the record's No Access list must be refused before `EnsureCreatorShareAsync` (comment
marks the spot in `EnsureResumeCreatorShareAsync`). N6 (refuse provisioning with a message when the creator is walled)
belongs to 143's guard, before the owner move.

---

## 8. Tests

Rewritten to the new contract (never deleted):
- `ProvisionProject_WhenOwnershipWasClaimedButNoContainerRecorded_IsRefusedAndSaysSo` → `…_ResumesForTheRecordsCreator`: the 409 WAS the lock-out; "owned, no container" is now reliably "stopped after the move".
- `Provisioning_WhenTheCallersIdentityCannotBeEstablished_FailsRatherThanLeavingALockedBox` → `…_RefusesBeforeChangingAnything`: the identity is now resolved before any write, so the assertion strengthens to zero updates.
- `Provisioning_WhenTheCreatorsShareFails_FailsAndCreatesNoContainer` → two tests, before-the-move and after-the-move (compensation), each ending with the same caller's successful retry.
- `ProjectProvisioningSelect_ReadsTheOwningTeamAndRejectsTheRetiredNames` → also pins `_owninguser_value` and `_createdby_value`; the fixture's live-column sets gained `_createdby_value`.

Fixture (`ProvisionProjectTestFixture`): a real share model (grants union, modify replaces, revoke removes; both reads
answer from it — before, reads were derived from `Grants` and a revoke never shrank them), seeded shares, owner and
`createdby` on every row (default: the caller), sequence numbers on shares, and switches for live gates (a)/(b),
post-move share failures, one-off and team-owned share-read failures, a refused owner bind, an owner read-back failure,
systemusers by id, and the container type. `SomeoneCanOpen` asserts the S5 invariant (amendment R3) after success,
compensated failure and resume.

Beyond the stated contract (one line each): the (a)/(b) fallback tests — the design hedges the platform behaviour the
live gate decides; the container-type pre-check test — a new fail-closed branch; the owner-unreadable test — a new
fail-closed branch; `RestoresTheCreatorsPreCallShareRights` — "share set equals the pre-call set" for a pre-existing
share with other rights; the colleague-ordering tests — the "named colleagues only after the creator" constraint.

Client: `provisioningService.test.ts` pins all 22 emitted codes → state + `retryable`, that no message advises a retry
(the advice lives next to the button), and that no message calls the project "a normal project".
`CreateProjectWizard.provisioningRetry.test.tsx` pins the action: rendered with its advice for each retryable state,
re-calls for the SAME project id and shows success; absent (button and advice) for non-retryable states; removed when
the retry lands in an administrator-only state.

**Final runs**: Final runs: BFF unit suite 13,586 passed / 0 failed / 54 skipped (13,640); NetArchTest 341/341; affected classes 126/126; Spaarke.UI.Components jest for the wizard folders 76/76 (CreateProjectWizard + SummarizeFilesWizard); package build (tsc) clean. Full package jest: 16 failures in 11 suites, all pre-existing and unrelated (none imports a changed file: surfaceLaunchRegistry drift, todoScoring sha256, AccessGrantModal, RecordHeader, ...).

### 8a. Perturbation sweep — every new guard bites

Seed → build → run the provisioning classes → restore the original bytes with a fresh timestamp (the incremental-build
hazard task 144 found). Seeds that a constant `false`/`true` made unreachable (CS0162 under warnings-as-errors) were
re-seeded with a runtime-false `Environment.TickCount64 < 0`.

| # | Seeded violation (server) | Result |
|---|---|---|
| P1 | share-first disabled | BITES (4) |
| P2 | compensating move skipped | BITES (4) |
| P3 | share restore a no-op | BITES (3) |
| P4 | unreadable share read treated as "share present" | BITES (3) |
| P5 | resume shares to the caller | BITES (4) |
| P6 | resume accepts an application-user creator | BITES (1) |
| P7 | a provisioned record (container recorded) resumed instead of 409 | BITES (1) |
| P8 | unreadable pre-call read treated as "no share" | BITES (1) |
| P9 | share-to-current-owner fallback removed | BITES (2) |
| P10 | double failure reported as undone | BITES (1) |
| P11 | unverified move not reported | BITES (2) |
| P12 | owner read-back trusted | BITES (3) |
| P13 | container-type pre-check removed | BITES (1) |
| P14 | creator share accepted when not exact | BITES (1) |

| # | Seeded violation (client) | Result |
|---|---|---|
| C1 | retry button rendered regardless of `retryable` | BITES (5) |
| C2 | `creator_share_failed` not retryable | BITES (3) |
| C3 | a message advises "Try again" | BITES (1) |
| C4 | retry targets another project id | BITES (5) |
| C5 | double failure made retryable | BITES (3) |

**19/19 bite.**

---

## 9. Pending manual gates (the main session runs; nothing here was written live)

Prerequisites: task 144's live cutover (named team created, role moved) and a BFF deploy carrying task 133.

1. `.\projects\unified-access-control-r2\notes\task-133-live-gate.ps1 -Step ShareFirstProof -TestUserId <existing non-admin test user> -Apply` → records (a), (b) and the compensation replay (e). The compensating path is proven by replaying the endpoint's exact call sequence; production code has no fault-injection switch. **Method caveat (verifier round 1):** this replays the Web API calls from the script — it does NOT execute the endpoint's compensation code (`MoveWithCreatorShareAsync` → `MoveOwnerAsync` back + `RestoreCreatorShareAsync`), which no live state reaches without a fault. Criterion (e) names "the compensation code path", so 🔔 **the owner must explicitly accept replay as the recorded method when the gate is run** (the code path itself is covered by the fixture tests). The script now derives `CreatorAccessRights` from the source at run time and stops if the constant changes shape (task 139 has not landed; re-checked 2026-10-02: the value is `ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess`).
2. As the test user, create a secure project in the wizard → `-Step Inspect -RecordId <id>`: team-owned, `RetrievePrincipalAccess` = the creator rights, own container recorded (b).
3. Strand a second wizard-created secure project: `-Step StrandForResume -RecordId <id> -Apply`; call provisioning as an administrator (printed command); `-Step Inspect` → the test user (createdby) has access, container recorded (c).
4. Call provisioning again on the completed project → 409 `already_provisioned`; `-Step Inspect` before and after: `modifiedon` and shares unchanged (d).
5. Wizard "Try securing again" (f): the retryable states cannot be produced in the live wizard without a fault, and production code has no fault switch. Method: run the wizard flow with the BFF's `SharePointEmbedded:ContainerTypeId` intact, then strand the project as in step 3 but WITHOUT the revoke (the creator keeps their share — the `container_creation_failed` shape), and drive the same `provisionSecureProject` call the button makes (the component test pins that the button makes exactly this call for the same project id). Record the 200 and `resumed: true`.
6. Owner decision 🔔 F8 follow-up: a persisted human creator for app-created rows (option (a), a new server-stamped column) — `createdonbehalfby` verified empty.

Record each result here (ids truncated to 8).

---

## 9.5 Quality gates (task-execute Step 9.5)

**code-review** (findings, all severities):

| Finding | Severity | Disposition |
|---|---|---|
| `ProvisionProjectEndpoint.cs` grew ~1,030 → ~1,570 lines | Warning (size) | Accepted per COMPONENT-COMPLEXITY: one cohesive endpoint (one reason to change — how a secure root is provisioned), heavy with contract comments. A natural seam if it grows again: the creator-share + owner-move mechanics (`MoveWithCreatorShareAsync`, `EnsureCreatorShareAsync`, `RestoreCreatorShareAsync`, `MoveOwnerAsync`) as their own static class. Not extracted now: a new type needs a §11 case this task cannot make beyond file size |
| Unverified-move blind grant may widen the creator if the move did not land | Warning | Accepted, documented (§4) — S5 over parity |
| `EnsureCreatorShareAsync` sets an existing creator share to EXACTLY the creator rights on success (a pre-existing Full Access share loses Delete) | Suggestion | Intended: the constraint is "exactly `CreatorAccessRights`"; task 139 owns the rights' meaning |
| `ShareToColleaguesAsync` counts a colleague who already holds any share as shared | Suggestion | Intended on resume (not shared again); documented in its remarks |
| `ProvisioningFailureKind` union changed (`container-not-recorded` → `storage-incomplete`, new kinds) | Suggestion | Grep: no consumer outside the two wizard hosts |
| AI-smell scan: no new interface, no catch-log-rethrow, no null-check on non-nullable, no "And" methods | — | Clean. `MoveWithCreatorShareAsync` is long (one sequence with its failure branches); each branch is a distinct, tested outcome |

**adr-check**: ADR-001 (Minimal API; no new endpoint), ADR-002 (no plugin; the invariant stays server-side, WP-6 fail
closed), ADR-003 (every new branch fails closed with a stable `sdap.provision.*` code; path C for compensation, §4),
ADR-007 (`SpeFileStore` only), ADR-008 (delegation filter unchanged), ADR-010 (no new interface or DI registration),
ADR-019 (`Results.Problem`), ADR-021 (Fluent v9, tokens only), ADR-028 (client uses the injected `authenticatedFetch`),
ADR-038 (no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests): **compliant, 0 violations.**

**Lint/format**: `dotnet build` 0 warnings; `dotnet format whitespace --verify-no-changes` clean on the changed C# files;
prettier applied; eslint on changed TS files: 0 errors (1 pre-existing `tenantId` hook-deps warning in
`CreateProjectWizard.tsx`, not introduced here).

---

## 10. Deviations from the POML

- **Step 2 (live proof) and step 8 (manual gate)** not executed — read-only run. The design does not depend on the result (§1).
- **Share-first vs post-move**: both, chosen at runtime by observed state, instead of one chosen from the live proof.
- **New client component** (`SecureProvisioningOutcome`) where the POML's justification expected none (§5).
- **SummarizeFilesDialog** gained the same retry action (second host that provisions; scope found during execution).
- **Six** reason codes, not "up to two": the container failures and the two new pre-mutation refusals had no code, and the constraint requires one on every new branch.
- **TASK-INDEX.md / current-task.md** not edited (main session). Publish size not measured (main session).

---

## 11. Verifier round 1 (2026-10-02, branch `task/uac-r2-133-r1`)

An adversarial verifier found nine items. Each, and what closed it:

| # | Finding | Closed by |
|---|---|---|
| 1 | RESUME shared to whatever `sharePrincipalIds` held, caller included (probe: a non-creator Write holder named themselves → Collaborate share, 200). Broke "the resume caller receives no share unless they are the creator" | `RefuseResumeColleaguesUnlessCreatorAsync`: on a resume, a request naming colleagues (other than `createdby`) is accepted only when the caller (WhoAmI) IS `createdby`; otherwise **403 `sdap.provision.resume_colleagues_not_permitted`, before any write**. Refused rather than ignored: a silent drop would answer 200 with `additionalPrincipalsShared: 0` to a caller who asked for shares. The wizard never sends colleagues, so it never meets this. Test `ProvisionProject_WhenANonCreatorResumesWithSharePrincipalIds_RefusesBeforeAnyWrite` (the probe, then the same call without the list completes, sharing only to the creator) |
| 2 | `resume_creator_unavailable`'s stated recovery ("share through Manage Access, then call again") was impossible: the second call was refused the same way forever | Made the stated recovery TRUE rather than documenting a worse one (reassigning the record out of isolation): when `createdby` cannot be used, the resume completes ONLY if a person — an enabled, non-application systemuser — already holds a share carrying Read (an administrator's deliberate Manage Access share), and then shares to nobody (S5 holds; F8 holds: never the caller). Response: 200, `resumed: true`, `creatorUnavailable: true` (additive), `sharedToCreatorSystemUserId` = that person. A share held only by a disabled or application user does not count. Refusal detail and guide §7a rewritten. Test `ProvisionProject_WhenTheCreatorIsUnusable_TheAdministratorsManageAccessShareLetsTheResumeComplete` (409 → 409 with a disabled sharee → 200 with a real one, zero grants) |
| 3 | Pre-call share set unreadable + compensation revoked a pre-existing creator share, while saying "the share this call issued was removed" and `sharesRestored: true` | The compensation response now tells four states apart: wrote no share ("as they were", true); restore unconfirmed (false); **pre-call unknown → `sharesRestored: false`, `creatorShareRemoved: true`, and a detail naming the removed pre-existing share**; pre-call known and restored (true). The behaviour (a narrowing) is unchanged — it is the §4 residual — but the response no longer claims parity. Tests `Provisioning_WhenCompensatedWithoutAPreCallRead_SaysThePreExistingShareWasRemovedToo` (the verifier's probe) and `Provisioning_WhenCompensatedHavingWrittenNoShare_SaysTheSharesAreAsTheyWere` (the old text was false there too: nothing had been issued) |
| 4 | No test rendered either host; dropping `<SecureProvisioningOutcome>` or inverting the retryable branch would have swallowed a retryable failure silently | `CreateProjectWizard.provisioningHost.test.tsx` (runs the wizard's own `onFinish` through a stub `CreateRecordWizard` that hands over its config) and `SummarizeFilesWizard/__tests__/SummarizeFilesDialog.provisioningHost.test.tsx` (stub shell, follow-on grid and project step drive the dialog's own state). Each: retryable → "Try securing again" rendered, no warning, the action re-calls for the SAME id and shows success; non-retryable → warning, no action, no advice |
| 5 | The unverified owner move issued the creator share with a bare GrantAccess (never read back) and claimed "The creator's share is in place" | The branch now runs `EnsureCreatorShareAsync` (complete read, Grant/Modify, read back) — a share proven before the move is no longer assumed to have survived it. Only if that fails is a share issued without a read, and the response then says "issued but NOT confirmed" with `creatorShareConfirmed: false`, naming the administrator resume if the creator cannot open the record. Client copy for `owner_assignment_unverified` no longer asserts access ("A share to you was issued so that you can open it either way"). Tests `Provisioning_WhenTheOwnerMoveIsUnverified_ProvesTheCreatorsShareByARead` (the move drops share-first's grant: re-proven, re-issued) and `…AndTheShareCannotBeReadBack_SaysTheShareIsNotConfirmed` |
| 6 | `MaskForRightsCsv` / `RightsCsvForMask` had no direct tests; the fixture computes masks with the same function (a tautology) | `tests/integration/auth/UnifiedAccessControl/RecordShareRightsMaskTests.cs`: every right against Dataverse's LITERAL bit and back; the creator/colleague masks computed from those literals over the constants' names (survives task 139); round trip; an unknown name and inexpressible masks refused. Sweep S8b proves the point: moving ShareAccess's bit fails ONLY these tests — every provisioning test stays green |
| 7 | `record_owner_unreadable` classified retryable although deterministic | Client: `retryable: false`, copy names the administrator; the server detail now says calling again repeats the refusal; guide §7a row split out |
| 8 | Stale "wizard's business-unit cascade writes that column today" in `SecureContainerDecision.cs:34` | Rewritten to the current contract (the cascade wrote it until task 076; only provisioning writes it on a provisionable root now) |
| 9 | Live gate (e) replays Web API calls rather than running the endpoint's compensation code; the script hardcoded `CreatorRights` | Script header and §9 state the caveat; 🔔 the owner must explicitly accept replay as the recorded method when the gate runs. The script now derives `CreatorAccessRights` from the source and stops if the constant changes shape. Task 139 has not landed (re-checked: `ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess`) |

**Not closed here, and why**
- **Publish size** (criterion): measured by the main session by instruction — still outstanding. The CVE half is done:
  `dotnet list package --vulnerable --include-transitive` on Sprk.Bff.Api reports no vulnerable packages; no package
  was added or changed in round 1.

**Runs (round 1)**: BFF unit suite 13,606 passed / 0 failed / 54 skipped (13,660; +20 new); NetArchTest 341/341;
affected classes 153/153; Spaarke.UI.Components jest CreateProjectWizard + SummarizeFilesWizard 81/81 (8 suites);
`tsc --noEmit` clean; eslint 0 errors on the changed files; `dotnet format whitespace --verify-no-changes` clean.
- **Manual live gate (a)–(f)**: pending — read-only run; it needs task 144's live cutover and a BFF deploy. (e) additionally needs the owner's explicit acceptance of the replay method (item 9).
- **Owner decisions** (not defects): (i) F8 follow-up — a persisted human creator for app-created rows (`createdonbehalfby` is empty; option (a), a server-stamped column), urgent before task 150; round 1 makes the administrator recovery for those rows real (item 2) but does not decide the column. (ii) Assign cascade under COMPENSATION — §3; the owner must confirm the round-4 acceptance extends to it, or choose (a)/(b)/(c).

**New surface (CLAUDE.md §10/§11)** — all inside the existing endpoint; no new endpoint, service, DI registration, option, job, package or column.

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| Reason code `resume_colleagues_not_permitted` | the `sdap.provision.*` set | Extends it; no existing code means "this request asked for something only the creator may do" | Either the widening stays (finding 1) or the colleagues are dropped silently behind a 200 |
| `ProvisionProjectResponse.CreatorUnavailable` (additive bool) + its TS mirror | `Resumed` | Additive member beside it; `Resumed` alone cannot say that `sharedToCreatorSystemUserId` is NOT the creator | The response would name a person as "the creator shared to" when the call shared to nobody |
| Private helpers `RefuseResumeColleaguesUnlessCreatorAsync`, `UnusablePersonStateAsync` (extracted from the existing `createdby` check, now reused for sharees), `FindPersonHoldingAShareAsync` | `EnsureResumeCreatorShareAsync` | Private methods of the same endpoint | — (methods, not components) |
| ProblemDetails extensions `creatorShareRemoved`, `creatorShareConfirmed` | `sharesRestored`, `ownershipRestored` | Same convention | The two false claims (findings 3, 5) stay |

**Perturbation sweep (round 1)** — seed → build → run → restore + touch:

| # | Seeded violation | Result |
|---|---|---|
| S1 | resume colleague refusal off | BITES (1) |
| S2 | holder fallback off | BITES (1) |
| S3 | the holder's usability not checked (disabled sharee accepted) | BITES (1) |
| S4 | pre-call-unknown compensation reported as restored | BITES (1) |
| S5 | "wrote no share" branch skipped | BITES (1) |
| S6 | unverified branch trusts share-first (no read) | BITES (1) |
| S7 | an unconfirmed grant reported as confirmed | BITES (1) |
| S8b | ShareAccess bit moved in the mask table | BITES (3, all in `RecordShareRightsMaskTests`; provisioning tests stay green — the tautology the finding named) |
| C6 | CreateProjectWizard: outcome render removed | BITES (1) |
| C7 | CreateProjectWizard: retryable branch inverted | BITES (2) |
| C8 | SummarizeFilesDialog: outcome render removed | BITES (1) |
| C9 | SummarizeFilesDialog: retryable branch inverted | BITES (2) |
| C10 | `record_owner_unreadable` retryable again | BITES (1) |

**13/13 bite.**
