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

**Addendum, round r2 (2026-10-02) — #1081 decided** (relayed by word-add-in-r2; recorded in the owner-decisions note,
"Peer report: #1081"). The dev root team "Spaarke" now holds Spaarke Basic User (verified live by the peer; Office
creates owned by it work again). At the root, Basic User's read reaches the whole org — Secure Record included — for
every root-team member. Under owner round 5 this is a dev-only artifact: in production users sit in the customer's child
business unit and the BFF application user in the customer business unit, so nothing is codified for the root team and
no production user is affected. Nothing in this task's code changes. **Consequence for this task's live gates (§15.5):**
in dev, any root-team member can open every secure record whoever it is shared to, so "someone can open it" proven by a
root-team member's read proves nothing about provisioning. Live-gate access proofs read the record's SHARES (the strict
share read / `RetrievePrincipalAccess` for the creator), or use a test user outside the root business unit.

---

## 3. Escalation triggers, evaluated

| Trigger | Result |
|---|---|
| `sharetopreviousowneronassign` true | **Did not fire** (False) |
| App-created provisionable roots (`createdby` = application user) | **Fires literally** (1 app-created matter; Office quick-create creates matters/projects app-only). **Answered by owner decision F8** (round 3, accepted as recommended): ship the interim default — `createdby` for human-created rows, refusal with its own code (`resume_creator_unavailable`, `creatorState: application-user`) for app-created rows, never share to whoever calls. **Verified live as F8 asked**: `createdonbehalfby` is EMPTY on the app-created matter, so option (b) is unusable and **(a) a new server-stamped creator column is the remaining option** — 🔔 an open owner decision, recommended before app-created rows are routinely made secure (task 150's ribbon will let a user secure an Office-created matter; its resume then needs an administrator) |
| Team-owned row with a non-provisioning container, or a create-time `sprk_containerid` writer | **Did not fire** (0 team-owned rows; 0 rules; no code writer) |
| Assign cascade on a root | **Fires literally** (6 platform-managed relationships). **Treated as answered by owner round 4 item 3**: the cascade to `team`, `sharepointdocumentlocation` and `sharepointdocument` is accepted for secure-root assignments; compensation is the same assign in reverse. Residual, stated: a child of one of those three types whose own owner differed from the root's before the forward move ends with the root's pre-call owner after compensation. 0 such rows exist for secure roots in dev; work assignments have no cascade. 🔔 **OPEN owner confirmation (verifier round 1)**: round 4 item 3 accepted the cascade for task 144's migration and assignment; it did not say it covers COMPENSATION, which can re-own a child whose prior owner differed from the root's to the root's pre-call owner. The POML trigger said STOP and present (a) snapshot+restore each re-owned child / (b) no compensation for cascading roots (resume only) / (c) coordinate with task 148. The shipped code compensates (the executor's reading); the owner must confirm that extension explicitly or pick (a)/(b)/(c). No code changed for this in round 1. **✅ ANSWERED 2026-10-03 — owner round 10 item 4: (c) then (a). Implemented in round c1 (§16): one primitive, `AssignCascadeChildOwners`, snapshots each cascaded child's own owner before any write and restores it after a verified undo** |
| Neither share-first nor post-move can be made reliable | **Not evaluable** without live writes — the live gate decides; the code does not depend on either (§1) |

---

## 4. ADR-003 path C (adr-tensions)

Compensation moves a secure-flagged record back from the memberless team to its pre-call owner — the access state every
earlier refusal (and the record since creation) already has. No COMPENSATED branch gives anyone access they lacked: a
share this call issued is removed when the move is undone (the one widening on this path is the UNVERIFIED owner move,
the first residual below — verifier round 2 narrowed this sentence, which had claimed "no branch"); `sprk_issecure` is never cleared, so uploads still fail closed. The
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

A resume whose `createdby` is unusable issues NO share and creates nothing — it is refused (verifier round 2 withdrew the
round-1 path that completed when another person already held a share; §12).

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
- **A record that KEEPS its own container is refused when its pre-call share set cannot be read** (round r1; listed here
  in round r2 — verifier round 5 finding 9). The closed criterion "the pre-call share set cannot be read → the post-move
  grant path runs instead" (§1, `Provisioning_WhenThePreCallSharesCannotBeRead_GrantsAfterTheMoveInstead`) still holds
  for every record without a container of its own, including one whose shared container is replaced. For a record that
  keeps its own container, provisioning now refuses BEFORE ANY WRITE (`creator_share_failed`, `containerKept: true`;
  transient — the same caller retries) instead of moving it with no share. Fail closed: once the team owns a record that
  records a container, the 409 marker answers every later call, so a move with no share could strand it beyond any
  provisioning call (§14.1 row 2; `Provision_KeepingItsOwnContainer_WhenThePreCallSharesCannotBeRead_RefusesBeforeAnyWrite`).

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

---

## 12. Verifier round 2 (2026-10-02, branch `task/uac-r2-133-r1-r2`)

A second adversarial verifier re-ran everything green, then seeded guards; several seeds survived and two contracts were
named open. Each item, and what closed it (or why it is not closed):

| # | Finding | Closed by |
|---|---|---|
| 3 / 15 | The resume's `creator_share_failed` branch was untested (seed V6 left all green). Regressed, a resume records a container on a team-owned record nobody holds a share on — permanent, because the next call answers 409 | Test `ProvisionProject_WhenTheResumesCreatorShareFails_StopsBeforeTheContainer`: `creator_share_failed`, `resumed: true`, no container created, no `sprk_containerid` update, `SomeoneCanOpen` unchanged, and the next call (fault cleared) finishes. V6 now BITES (1) |
| 4 / 14 | Round 1's holder path (unusable `createdby` + someone else holding a Read share → 200) contradicted the closed AC ("refused with its own reasonCode, zero grants, zero containers") and owner decision F8's interim default, and turned a transient `unreadable` into a permanent completion | **Withdrawn — path C (comply with the owner-decided contract).** An unusable `createdby` is refused again whoever holds a share: 409 for `absent` / `disabled` / `application-user`, **500 for `unreadable`** (transient). `FindPersonHoldingAShareAsync`, `ReadMask`, `ProvisionProjectResponse.CreatorUnavailable` and its TS mirror are removed (the DTO is byte-identical to its pre-round-1 version). The stated recovery is rewritten to paths that work against this code, each driven by a test: `unreadable` → the same caller calls again once the read works; `disabled` → an administrator re-enables the creator and calls again (`…WhenTheCreatorIsDisabled_ReEnablingThemLetsTheResumeComplete`); any state → an administrator ASSIGNS the record to the person who should hold it (it leaves the owner team), and that person's own call runs from the start and shares to them (`…WhenTheCreatorIsAnApplication_AssigningTheRecordToAPersonLetsThemProvisionIt`). The refusal theory now seeds another enabled person's Read share and asserts zero grants and containers anyway. 🔔 If the owner prefers the round-1 holder path, it is an AC amendment for the owner to accept; that code is in `fb4b70fdf` |
| 5 / 15 | Holder-path filters (V1 Read bit, V2 SystemUser kind) unproven; V5 (null `isdisabled` treated as enabled) and V11 (`createdby` not excluded from the resume colleague list) survived | V1/V2: the code they guarded is gone (item 4). V5: the fixture now emits a null `isdisabled`; test `…WhenResumingAndTheCreatorsDisabledFlagIsNull_TreatsThemAsDisabled` — BITES (1). V11: test `…WhenANonCreatorResumeNamesOnlyTheCreator_CompletesWithTheCreatorShareOnly` (200, one grant to the creator at `CreatorAccessRights`, `additionalPrincipalsShared: 0`) — BITES (1) |
| 6 / 15 | `shareIssued \|= creatorShareProven;` removable (V3); `ShareToColleaguesAsync`'s skip-existing branch removable (V4) | V3: `Provisioning_WhenTheMoveIsUnverifiedAndNoShareCanBeIssuedAfterIt_TheShareFirstGrantStillCounts` — share-first proven, move unverified, post-move read and grant both fail → `owner_assignment_unverified`, `creatorShareConfirmed: false`, no CRITICAL log, share in place — BITES (1). V4: `ProvisionProject_WhenAColleagueAlreadyHoldsAShare_IsNotSharedAgain` (no grant to that colleague, their View-only mask unchanged, counted) — BITES (1) |
| 7 | `MoveWithCreatorShareAsync` remarks and note §4 said "no branch gives anyone access they lacked", contradicted by the unverified branch | Narrowed to "no COMPENSATED branch", and the unverified-move widening is named in place (code remarks and §4) |
| 8 | Client mapped `owner_assignment_unverified` to "A share to you was issued so that you can open it either way" without reading `creatorShareConfirmed` | `provisionSecureProject` reads `creatorShareConfirmed` from the problem body and passes it to `classifyProvisioningFailure(reasonCode, extensions)`. Confirmed → "Your share on it was read back, so you can open it either way." Unconfirmed or absent → "…a share to you was issued but could not be confirmed. If you cannot open the project, an administrator needs to finish securing it." `retryable` stays true: if the move did not land, or the share took, the retry works; if not, the retry is refused at the Write gate and the generic copy names the administrator too. Tests pin all three (true / false / absent); client seeds C11–C13 BITE |
| 9 | `unreadable` + caller IS `createdby` + some holder → named colleagues shared with no creator share proven | Gone with item 4 (unreadable is refused before colleagues). Test `…WhenResumingWithAnUnreadableCreator_SharesNoColleague` (500, zero grants, zero containers) |
| 10 / 16 | No test asserted `additionalPrincipalsShared` | `Provisioning_WhenAColleaguesShareFails_StillSucceeds` now names two colleagues, one failing: 200, grants = creator + the reachable colleague, `additionalPrincipalsShared == 1`. Seed R3 (count failures too) BITES (1) |

**Perturbation sweep (round 2)** — seed → build → run the five affected classes → restore the original bytes + touch:

| # | Seeded violation | Result |
|---|---|---|
| V6 | resume's creator-share failure ignored | BITES (1) |
| V3 | `shareIssued \|= creatorShareProven` neutralised | BITES (1) |
| V4 | colleague skip-existing removed | BITES (1) |
| V5 | null `isdisabled` treated as enabled | BITES (1) |
| V11 | `createdby` not excluded from the resume colleague list | BITES (1) |
| R1 | unusable `createdby` not refused (resume continues) | BITES (8) |
| R2 | `unreadable` reported as 409 | BITES (2) |
| R3 | colleague count includes failed shares | BITES (1) |
| C11 | client: absent `creatorShareConfirmed` treated as confirmed | BITES (1) |
| C12 | client: the extension not read from the problem body | BITES (1) |
| C13 | client: unconfirmed copy asserts access again | BITES (2) |

**11/11 bite.**

**New surface (CLAUDE.md §10/§11)**: none on the server — round 2 REMOVES surface (`CreatorUnavailable`, the holder
helper). Client: `IProvisioningFailureExtensions` (exported type) plus an optional second parameter on the existing
`classifyProvisioningFailure` — existing: the `REASON_STATES` table and that function; extension: yes, one optional
parameter on it; cost of doing nothing: the wizard asserts "you can open it either way" when the server says the share
is NOT confirmed (finding 8). No new endpoint, service, DI registration, option, job, package or column.

**Runs (round 2)**: BFF unit suite 13,613 passed / 0 failed / 54 skipped (13,667; +7 net: 8 new, the holder-path test
removed); NetArchTest 341/341; affected classes (ProvisionProject*, SecureProjectShareTests, RecordShareRightsMaskTests,
DelegationRule*, SecureNamedOwnerTeam*) 153/153; Spaarke.UI.Components jest CreateProjectWizard + SummarizeFilesWizard
84/84 (8 suites); package build (`tsc`) clean; prettier clean and eslint 0 errors on the changed TS files;
`dotnet format whitespace --verify-no-changes` clean on the changed C# files.

**Not closed here, and why**
- **Items 11 / 17 — Assign cascade under COMPENSATION** (POML trigger: STOP, options (a)/(b)/(c)). Owner round 4 item 3
  ("Accepted: team, sharepointdocumentlocation and sharepointdocument") answered task 144's escalation (a) — the
  migration and the forward assignment. It does not say whether compensation's reverse assign, which can re-own a child
  whose own prior owner differed from the root's to the root's pre-call owner, is covered. 🔔 **Owner confirmation
  required before merge**: confirm the extension, or pick (a) snapshot and restore each re-owned child / (b) no
  compensation for cascading roots (project, matter) / (c) coordinate with task 148. No code changed for this (0 such
  rows in dev; work assignments have no cascade).
- **Item 12 — owner items** (not defects): the F8 follow-up (a persisted human creator column for app-created rows;
  `createdonbehalfby` is empty), urgent before task 150 — round 2's assign-to-a-person recovery is the working interim
  path for those rows; the owner's explicit acceptance of the (e) replay method; `sharetopreviousowneronassign` False.
- **Items 13 / 19 — MANUAL LIVE GATE (a)–(f)**: pending — read-only run; needs task 144's cutover and a BFF deploy (§9).
- **Item 18 — publish size**: the main session measures by instruction. CVE: no package changed in round 2.

---

## 13. Round b2 (2026-10-02, branch `task/uac-r2-133-b2`) — owner round 7 item 2, the orphaned-container defect, verifier round 3

Base: `task/uac-r2-133-b1` (the WIP commit `b38756ba6`, cut off by a usage limit, "UNVERIFIED, do not merge") with
`work/unified-access-control-r2` merged in (origin/master `93634db58` + batch 3: tasks 138, 139, 141, 152, 155 and the
Office save fix). **baseSha `43bcd3abe`** (HEAD right after that merge).

**Merge.** One conflict, `Services/Access/RecordShareLevels.cs`: both sides only ADDED members (task 139: `CanRead`,
`WouldRemoveRights`; task 133: `MaskForRightsCsv`, `RightsCsvForMask`) — both kept. The doc of task 133's
`AllShareRights` table said "the three rights no level carries (Create, Share, Assign)"; since task 139 Share IS in the
levels, so it now says "the two (Create, Assign)" and uses the `Share` constant. **Semantic fallout of task 139**, found by
running the resolved file's tests: Collaborate is now EXACTLY the creator rights (mask 262167), so
`Provisioning_WhenCompensatedWithoutAPreCallRead_SaysThePreExistingShareWasRemovedToo`, which seeded a pre-existing
Collaborate share as "a share with other rights", no longer reached compensation (the share was already exact). Its seed
is now View Only — the contract is unchanged (reason in the test's remarks). The live-gate script derived
`CreatorAccessRights` only from the pre-139 shape (`CollaborateRights + ",ShareAccess"`) and would have refused to run;
it now accepts both shapes.

**What b1 left, judged.** Kept: `RecordCreatorPerson` (one holder of the column's name and write shapes), the Office,
work-assignment and chat-create stamps, the AI refusal of an item naming the column, the resume reading
`sprk_createdbyperson` in its own query, the container-classification skeleton, and the `creator_unresolved` answer for a
resume naming colleagues whose caller cannot be identified. **Removed: the Assign-cascade child-owner snapshot/restore**
(`AssignCascadeChildren`, `SnapshotCascadeChildOwnersAsync`, `RestoreCascadeChildOwnersAsync`, the `childOwnersRestored`
extension). b1 had implemented option (a) of an escalation the owner has NOT answered (§13.6); its methods did not even
exist (the commit did not compile). Fixed: the resume's person rule (below), the classification (configured shared
containers; an exhaustive switch), the docs b1 left claiming a navigation-property name the script never set.

### 13.1 Owner round 7 item 2 — `sprk_createdbyperson`

| Piece | What |
|---|---|
| Column | `sprk_createdbyperson` (schema `sprk_CreatedByPerson`), lookup → `systemuser`, on `sprk_project`, `sprk_matter`, `sprk_workassignment`; relationship `sprk_systemuser_<table>_createdbyperson`, no Assign/Share/Unshare/Reparent/Merge cascade, Delete RemoveLink |
| Lock | Field-secured. "Spaarke BFF-Managed Field Writers" (read/create/update) = the BFF application user(s), explicitly; "Spaarke BFF-Managed Field Readers" (read) = every business unit's default team. Any other writer profile = `FAIL` in `-Verify`. Named for the class of column: task 150 adds `sprk_issecure` to the same two profiles |
| Schema | `scripts/Set-RecordCreatorPersonSchema.ps1` — dry run by default, `-Apply`, `-Verify` (exit 1 names each gap); idempotent; publishes; adds everything to SpaarkeCore; reports (never writes) how many app-created rows have no person. **Not run** (no live writes). Doc: `src/solutions/SpaarkeCore/entities/sprk_project/created-by-person-schema.md`; guide `SECURE-PROJECT-ENVIRONMENT-SETUP.md` §7b; `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §6.5.2 |
| Writers (every BFF create path of the three tables — grep: `new Entity("sprk_…")` and every `CreateAsync`) | `RecordCreationService` matter + project: the Office caller, in the app-only payload, set last and protected from field mapping. `POST /api/v1/work-assignments`: the caller by WhoAmI (OBO; never the body), in the payload; an unresolvable caller is refused **403 `sdap.workassignment.creator_unresolved` before the create**. `dataverse.create_record` (user-OBO): an app-only update right after the create with the row's OWN `createdby`; non-fatal. An AI item naming the column (any casing, read form, bind) is refused pre-suspend and on execute |
| Reader | The resume (below) — in its own query, never the Step 1 select, so a BFF ahead of the schema still provisions forward |

**The resume's person** (owner: "shares to createdby when it is a human, else to this column; still refuses when neither
is a usable human"): `createdby` when it is a usable person (enabled, not an application user) → else the column's person
when usable → else `resume_creator_unavailable` (409; nothing written; never the caller — F8). An **unreadable**
`createdby` stops there (500, `creatorColumn: createdby`) rather than falling through to the column — it may well be the
usable person the rule names first. A column that cannot be read (including an environment the schema has not reached)
is 500 `unreadable`, `creatorColumn: sprk_createdbyperson`. Extensions: `creatorState` (the deciding state),
`creatorColumn`, `createdByState`, `creatorPersonState`.

⚠️ **Deploy order (stated in the script, both guides and the schema doc): schema BEFORE the BFF.** A BFF carrying this
round writes the column on every Office quick-create and work-assignment create; Dataverse refuses a create naming a
column it lacks. Peers deploy master to dev — so the schema must be applied to dev **before task 133 merges to master**.

### 13.2 The orphaned container (live 2026-10-02, task 144 note §13.4)

Provisioning `65a3fab2`, which already recorded its OWN container, created a second one and overwrote
`sprk_containerid`; `b!HBRbo…` is now referenced by no record. Now, before any write, a recorded value on a record not
owned by the team is classified:

| Recorded container is… | Outcome |
|---|---|
| a business unit's (`businessunit.sprk_containerid`) — checked FIRST, because many records share it legitimately | replaced by the record's own; the business unit keeps it |
| configured for many records (`Communication:ArchiveContainerId`, `EmailProcessing:DefaultContainerId`, `Email:DefaultContainerId`, `SharePointEmbedded:StagingContainerId`) | replaced; the configuration keeps it |
| also recorded on another project / matter / work assignment | **refused 409 `sdap.provision.container_shared_with_another_record`** (`otherRecordType`; the other id goes to the log only), zero writes |
| unreadable (any read failed) | **refused 500 `sdap.provision.container_ownership_unreadable`**, zero writes; the same caller may retry |
| anything else — the record's own | **KEPT**: owner move + creator share as usual, no container created, `sprk_containerid` never rewritten; the next call is the ordinary 409 |

Residuals, stated: a kept container is not checked against the SPE container type or for existence in SPE (the request
named the BU / other-root checks); `b!HBRbo…` itself stays orphaned (its deletion is an operator decision — it held
nothing). The live gate's `StrandForResume` step clears a just-provisioned record's container by hand; it now prints that
container's id so the operator deletes it.

### 13.3 Verifier round 3 (findings on `task/uac-r2-133-r1-r2`) — each item

| # | Finding | Closed by |
|---|---|---|
| S19 | An unverifiable COMPENSATION reported as reverted survived (fixture could not produce it) | Fixture `FailOwnerReadBackAfterBindTo` (fails only the read-back after a bind to that principal). Test `Provisioning_WhenTheCompensatingMoveCannotBeReadBack_IsTheAdministratorOnlyState`: `creator_share_failed_resumable`, `ownershipRestored: false`, `ownershipVerified: false`, CRITICAL log, no "nothing"/"retry". Seed S19 now BITES |
| S10 | The unverified-move + no-share branch (code and CRITICAL) untested | Test `Provisioning_WhenTheMoveIsUnverifiedAndNoShareExistsOrCanBeIssued_IsTheAdministratorOnlyState` (existing switches). Seeds S10 (code) and S10b (CRITICAL→Error) BITE. Its detail no longer says "it resumes" unconditionally: if the move did not land, the creator calls again |
| S22b | A restore that writes but reads back wrong never exercised | Fixture `RevokeNotAppliedFor` (accepted, not applied). Test `Provisioning_WhenTheShareRestoreReadsBackWrong_SaysTheSharesAreNotRestored` (`sharesRestored: false`). Seed BITES |
| client | `resume_creator_unavailable` + `creatorState: unreadable` shown as administrator-only | `provisionSecureProject` reads `creatorState`; `unreadable` → `not-started`, `retryable: true`, copy "could not be looked up … Nothing about the project changed"; other states stay `needs-administrator`. Jest: the five states pinned, and the wizard component renders "Try securing again" for it |
| cascade | Assign cascade under COMPENSATION | **NOT CLOSED — owner escalation, §13.6** |
| (ii) | F8 follow-up: persisted human creator | **Closed by owner round 7 item 2** (§13.1) |
| (iii) | Owner acceptance of the (e) replay method | Open owner item (live-gate method), unchanged |
| (iv) | Publish size | Main session |
| minor | Resume naming colleagues + unresolvable caller told "not the record's creator" | Already answered `creator_unresolved` in b1; now pinned by `Resume_NamingColleagues_WhenTheCallerCannotBeIdentified_SaysSoRatherThanNotTheCreator` |

### 13.4 Tests

New classes: `ProvisionRecordedContainerTests` (9), `ProvisionResumeCreatorPersonTests` (14),
`CreatorPerson/RecordCreatorPersonStampTests` (7), `CreatorPerson/RecordCreatorPersonSchemaAgreementTests` (6 — the script
and `RecordCreatorPerson` agree on name, schema name, target, tables and field security; the parser is itself seeded).
Added to existing: `SecureProjectShareTests` +3 (S19, S10, S22b), `DataverseCreateRecordHandlerTests` +10 (stamp on the
three roots from the row's own `createdby`, no app-only write for other tables, stamp failure non-fatal ×2, the column
refused ×4). Client: `provisioningService.test.ts` +7 (five `creatorState` cases; the two new codes in the emitted-code table, now
25 codes); `CreateProjectWizard.provisioningRetry.test.tsx` +3. Package jest for the two wizard folders: 84 → 94.
Rewritten setup (never deleted), one line each: `…WizardCascadedContainerId_StillProvisions` now records the cascaded
value on a business unit (it IS the user's business-unit container; without that it reads as the record's own and is
kept); `…CompensatedWithoutAPreCallRead…` seeds View Only (task 139 made Collaborate exact). `P2LoopInjectionEvalSuiteTests`
passes a Strict app-only seam to the handler's new constructor (an `sprk_event` create stamps nothing).

**Perturbation sweep (round b2)** — seed → build → run the affected classes → restore the original bytes + touch
(`Environment.TickCount64 < 0` where a constant would be unreachable code under warnings-as-errors):

| # | Seeded violation | Result |
|---|---|---|
| K1 | business unit's shared container not recognised | BITES (2) |
| K2 | a container another root records treated as the record's own | BITES (3) |
| K3 | an unreadable classification treated as "its own" | BITES (1) |
| K4 | the kept container ignored (a new one created) | BITES (4) |
| K5 | configured shared containers not recognised | BITES (1) |
| K6 | the record itself not excluded from the other-roots check | BITES (6) |
| K7 | the other record's id returned to the caller | BITES (3) |
| P1 | `sprk_createdbyperson` never consulted | BITES (10) |
| P2 | the column wins over a usable `createdby` | BITES (7) |
| P3 | an unreadable `createdby` falls through to the column | BITES (1) |
| P4 | a disabled person accepted | BITES (5) |
| P5 | an unreadable column read as "nobody recorded" | BITES (1) |
| S19 | an unverifiable compensation reported as reverted (`== Moved` → `!= NotMoved`) | BITES (1) |
| S10 | unverified move + no share given the retryable code | BITES (1) |
| S10b | that branch not logged CRITICAL | BITES (1) |
| S22b | a restore that reads back wrong reported as restored | BITES (1) |
| W1 | work-assignment create not stamped | BITES (1) |
| W2 | work-assignment WhoAmI failure treated as someone | BITES (1) |
| O1 | Office matter create not stamped | BITES (2) |
| O1b | Office project create not stamped | BITES (2) |
| O2 | column removed from the matter mapping protection ONLY | **SURVIVES — by design**: the stamp is also set after field mapping, so either guard alone holds |
| O2b | protection removed AND the stamp moved before field mapping | BITES (1) |
| H1 | chat create of a secure root not stamped | BITES (4) |
| H2 | chat stamp names the wrong person | BITES (3) |
| H3 | pre-suspend refusal of an item naming the column removed | BITES (4) |
| H4 | execute-path refusal removed | BITES (4) |
| H5 | a failed stamp fails the user's create | BITES (1) |
| C1 | client: `creatorState: unreadable` mapped to the administrator state | BITES (2) |
| C2 | client: `creatorState` not read from the problem body | BITES (1) |
| C3 | client: `container_ownership_unreadable` not retryable | BITES (2) |
| C4 | client: `container_shared_with_another_record` offered as a retry | BITES (2) |

**30/31 bite; the one survivor (O2) is a deliberately redundant guard, and removing both halves (O2b) bites.** The schema
agreement test seeds its own parser (four drifts: field security off, a table dropped, wrong target, wrong column name).

**Runs (round b2)**: full BFF unit suite **14,309 passed / 0 failed / 54 skipped (14,363)**; NetArchTest **345/345**;
affected classes (ProvisionProject*, SecureProjectShare, ProvisionRecordedContainer, ProvisionResumeCreatorPerson,
SecureNamedOwnerTeam*, CreatorPerson/*, DataverseCreateRecordHandler, RecordShareRightsMask, DelegationRule*,
InternalUserShare, P2LoopInjection) **350/350** after the final whitespace pass; the Office/AI neighbours (RecordCreation*,
DataverseToolNameFreeze, BusinessSliceDeterminism, OfficeQuickCreate*, OfficeRecordOwnership, InlineNotification) green in
the 444-test affected run. Spaarke.UI.Components jest CreateProjectWizard + SummarizeFilesWizard **94/94 (8 suites)**;
`tsc` build clean; prettier + eslint clean on the changed TS. `dotnet build` 0 warnings; `dotnet format whitespace
--verify-no-changes` clean on every changed C# file (one pre-existing flag in `P2LoopInjectionEvalSuiteTests.cs:1204`, not
this round's). No package changed (no CVE delta). Publish size: not measured (main session).

### 13.5 Placement and component justification (CLAUDE.md §10 / §11)

**Placement**: all server code stays in existing types (`ProvisionProjectEndpoint`, `RecordCreationService`,
`WorkAssignmentEndpoints`, `DataverseCreateRecordHandler`) plus one static holder; no new endpoint, service, interface,
option, job or package; **no new DI registration** (the work-assignment handler now asks for `CallerRecordAccessProbe`,
and the chat handler for `IGenericEntityService` — both registered unconditionally already, so no asymmetric
registration, §10 F.1).

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| Column `sprk_createdbyperson` (+ relationship) on 3 tables — owner-mandated (round 7 item 2, option a) | `createdby` (the app user on app-only creates); `createdonbehalfby` (empty, live 2026-10-01); `sprk_assignedtointernal` (a CONTACT lookup, user-editable, means "for", task 152) | No existing column records the person who asked for an app-only create and cannot be edited by a user | A stranded secure record created by Office or `POST /work-assignments` can only be refused at resume — the S5 risk the owner closed |
| FLS profiles "Spaarke BFF-Managed Field Readers/Writers" | "Spaarke Identity Link Readers/Writers" (task 141), same membership | Not reused: those are named and described for the identity binding; putting an unrelated column in them makes an administrator's view of "who can write identity links" wrong. The new pair is generic — task 150 puts `sprk_issecure` in it, so one pair serves the class | Without FLS any user with Write could name someone else as a record's creator, and the resume would share the record to them |
| `RecordCreatorPerson` (static class) | none for this column | — | One column name, two write shapes and the AI refusal duplicated across four writers and a reader, free to drift |
| Reason codes `container_shared_with_another_record`, `container_ownership_unreadable` | the `sdap.provision.*` set | Extends it; each names a state with a different recovery (administrator vs same caller) | Orphaning (the live defect) or an untruthful refusal |
| Reason code `sdap.workassignment.creator_unresolved` | the endpoint had no 403 | New refusal on an existing endpoint | A work assignment created with nobody recorded |
| `SharedContainerConfigKeys` (internal constant list, not an option) | the four existing config keys | Reads existing configuration | A record carrying a configured shared container would be "kept" as its own — secure files into shared storage |
| Fixture switches (`CreatorPersonColumnExists`, `BusinessUnitContainers`, `ContainerOwnershipReadFails`, `RevokeNotAppliedFor`, `FailOwnerReadBackAfterBindTo`, `SystemUserReadFailsFor`) | the fixture's existing switches | Extends the fixture | Seeds S19 / S22b / K* / P3 could not be made to bite |

### 13.6 🔔 STOP — Assign cascade under COMPENSATION (✅ answered 2026-10-03, owner round 10 item 4 — implemented in §16)

> **Answer (BINDING):** "coordinate with task 148's child-ownership logic, then snapshot and restore each re-owned
> child's own owner (options (c) then (a))". Implemented in round c1 as ONE reusable primitive for task 148 — §16.1–16.3.
> The text below is the question as it stood.

Owner round 4 item 3 accepted the Assign cascade (team, sharepointdocumentlocation, sharepointdocument) for task 144's
migration and forward assignment. Rounds 5–7 and the #1081 peer report do not mention compensation. The compensating move
is the same Assign in reverse, so a child whose own owner differed from the root's before the forward move ends with the
root's PRE-CALL owner afterwards. Per the binding rule ("an escalation trigger NOT answered is a first-class stop"), this
round did NOT pick an option; b1's half-built option (a) was removed. Options (POML trigger): **(a)** snapshot each
re-owned child's owner before the forward move and restore it after compensation; **(b)** no compensation for cascading
roots (project, matter) — resume only; **(c)** coordinate with task 148, which owns child ownership at provisioning.
Recommendation: (c) then (a). Exposure today: 0 such rows in dev; work assignments have no Assign cascade.

### 13.7 Pending manual gates (main session; nothing here was written live)

1. **Schema (owner round 7 item 2 approves it as part of this gate), BEFORE merging/deploying a BFF carrying this round**:
   `.\scripts\Set-RecordCreatorPersonSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c`
   (dry run) → same with `-Apply` → same with `-Verify` (exit 0).
2. Live gate (a)–(f) as §9 (needs a BFF deploy carrying task 133; (e) still needs the owner's acceptance of replay).
3. (g) creator person: as a non-admin test user, quick-create a project from the Office add-in → `task-133-live-gate.ps1 -Step Inspect -TestUserId <user> -RecordId <id>` shows `sprk_createdbyperson` = that user; mark it secure, `-Step StrandForResume -Apply`, call provisioning as an administrator → 200, `resumed: true`, `sharedToCreatorSystemUserId` = the test user; `-Step Inspect` → their RetrievePrincipalAccess.
4. (h) own container kept: provision a secure record that already records a container no business unit / other record
   holds → 200 with `speContainerId` = that container; read back: `sprk_containerid` unchanged, no new container.
5. The orphan from 2026-10-02 (`b!HBRbokLXnUGzaDLSTdNFvM5RFHtaaUZCi0Jm-xs-hDQV_6QuLuKmR4jrMdC6UgMm`, empty, referenced
   by no record): deletion is an owner/operator decision.

### 13.8 Handoffs to sibling tasks

- **Task 146** (G5 for the AI create handlers): when `dataverse.create_record` creates AS THE APP, stamp
  `sprk_createdbyperson` in that create's payload with the OBO caller and drop this round's follow-up update; keep the
  refusal of an item naming the column. Expect a merge conflict in `DataverseCreateRecordHandler` (constructor + execute).
  **Recorded 2026-10-03 (owner-decisions note, round 10, "decided by the main session under existing decisions;
  reversible"):** 133's interim app-only stamp is superseded AT INTEGRATION by 146's create-as-the-app (owner round 7
  item 3), which writes the stamp in the create payload. Under create-as-the-app the row's `createdby` is the BFF
  application user, so the value 146 stamps is the OBO caller's own systemuserid (WhoAmI on the caller's token) — never
  the row's `createdby`, which is what the interim update copies today. 133 does not pre-empt 146: round c1 leaves the
  handler untouched (its class remarks already say "replaced by task 146, which creates as the app and stamps the column
  in that payload"). Whichever of 133/146 merges second resolves the conflict to 146's shape. Until 146 lands, the interim
  update is what runs, under the §6.5 path-B citation in §14.7.
- **Task 150** (lock `sprk_issecure`): add it to "Spaarke BFF-Managed Field Readers/Writers" (same script pattern).
- **Task 143** (No Access): the resume now shares to `sprk_createdbyperson` too — the No Access check before that share
  covers whichever person `ResolveResumeCreatorAsync` returns.

### 13.9 Quality gates (round b2)

**code-review** (all severities):

| Finding | Severity | Disposition |
|---|---|---|
| `ProvisionProjectEndpoint.cs` grows to ~2,200 lines | Warning (size) | Accepted per COMPONENT-COMPLEXITY (as §9.5): one reason to change. The container classification (`ClassifyRecordedContainerAsync` + its two types) and the resume's person rule are the next natural seams if it grows again |
| The work-assignment endpoint now refuses (403) when WhoAmI cannot resolve the caller — a new failure mode on an existing route | Warning | Intended (fail closed; same posture as Office quick-create's `owner_unresolved`). The route has no client caller in `src/` today (grep) |
| The chat handler gains an app-only write in an otherwise user-OBO class | Warning | Authorised by owner round 7 items 2 + 3 (§6.5 path B); one column, the row's own `createdby`, non-fatal; cited in the class remarks. Task 146 replaces it |
| A kept container is not checked against the SPE container type or for existence | Suggestion | Residual, stated (§13.2); the request named the BU / other-root checks |
| The other record's id was returned to the caller | Fixed | Log only (the TopologyRefusal precedent); seed K7 bites |
| AI-smell scan: no new interface, no catch-log-rethrow, no null-check on a non-nullable, exhaustive switch with a fail-closed default | — | Clean |

**adr-check**: ADR-001 (no new endpoint), ADR-002 (no plugin — the lock is field-level security, the stamp server-side
in every BFF writer, WP-1/WP-3), ADR-003 (every new branch fails closed with a stable code; unreadable ≠ usable / own /
shared), ADR-008 (delegation filter unchanged), ADR-010 (no new interface or registration), ADR-019 (ProblemDetails +
`reasonCode`), ADR-038 (no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests; the schema-agreement source
guard follows `ContactIdentitySchemaAgreementTests`): **compliant, 0 violations**. The §6.5 path-B citation for the chat
handler's app-only write is owner round 7 item 3.

---

## 14. Round r1 (2026-10-02, branch `task/uac-r2-133-b2-r1`) — verifier round 4

Base: `task/uac-r2-133-b2` (`30373dba1`) with `work/unified-access-control-r2` merged in — `8531711d6` (integration
fixtures caught up with batch 3) and `ea6484102` (the integration-suite hard gate). **baseSha `d10db4252`** (HEAD right
after that merge; clean, no overlap with this task's files).

### 14.1 Finding 1 (BLOCKING) — the defect, proven first, and why the marker was NOT changed

**Proven before any fix.** Ten new or rewritten tests were written first and run against the b2 logic: all ten failed,
reproducing both probes (probe 2 — the S10 setup plus a container of the record's own — answered
`creator_share_failed_resumable`, and its next call the 409, with nobody able to open the record).

**Root cause, wider than reported.** The marker's premise — "on a team-owned record a recorded container means Step 7
ran" — is false for ANY record that enters the owner move with `sprk_containerid` already set. b2 created two such paths:

- a **kept own** container (the verifier's finding);
- a **replaced shared** container (a business unit's, or a configured one — found while fixing finding 1): the shared
  value stayed recorded from the move (Step 5) until Step 7 overwrote it. So a `container_creation_failed`,
  `container_not_recorded` or double failure on such a record left it team-owned WITH the shared container recorded —
  409 forever, while `RecordContainerResolver` sent the secure record's uploads to the business unit's shared container
  (to the resolver, a secure record's `sprk_containerid` is its own container). Both details promised "the same caller
  may call again: it resumes" — false. Same defect class as findings 1/13/14, so it is closed in this round.

**Why the marker was not changed (verifier fix options (a)/(b)).** The closed acceptance criterion pins
`ProvisionProject_WhenAlreadyOwnedBySecureTeam_IsRefusedAndWritesNothing` UNMODIFIED, and that test's seed — owned by
the team, a container recorded, no share at all, `createdby` = the caller — is observably IDENTICAL to a kept-container
record stranded with nobody shared. So no reading of the share state can resume the stranded record and keep that test
green; and "a provisioning-complete signal other than `sprk_containerid`" is a new column. Either changes what the
marker means — the owner's call (§14.3), and POML trigger 3's rule is "do not widen the resume condition on a guess".

**What was done instead — verifier option (c), "do not keep the container until after the creator share is proven", and
its counterpart for shared containers:**

| # | Change | Effect |
|---|---|---|
| 1 | **Shared container unlinked BEFORE the owner move** (new Step 4.2: `sprk_containerid` set to null, after every read and before share-first; the business unit / configuration keeps the container) | Every failure after the move now leaves "owned by the team, NO container", which the next call resumes (tested for Step 6 and for the double failure). A failed unlink stops the run with nothing moved and no share issued: new code `sdap.provision.shared_container_not_cleared` (500, retryable). Each later failure detail says the link was removed ("unlinked"). Not undone by compensation: a secure-flagged record with no link refuses uploads (fail closed), which is stricter than the shared storage it pointed at |
| 2 | **A record that keeps its own container is moved only when share-first is possible**: an unreadable pre-call share set refuses before any write (`creator_share_failed`, `containerKept: true`, transient — the same caller retries), instead of skipping share-first and moving it with no share | Closes probe 2 (the S10 state cannot be reached through an unreadable pre-call read). The fallback to a post-move grant for a creator who OWNS the record (live gate (a)) is kept: refusing there would refuse every re-securing for good (the unsecure endpoint assigns the record to the caller) if Dataverse never accepts a share to a record's owner |
| 3 | **Every failure after a kept record's move tells the truth about the next call** — new extension `containerKept` on `owner_assignment_unverified` and `creator_share_failed_resumable`; the details say the next call answers `already_provisioned` and does not resume, and name the recovery that works without it: **an administrator shares the record to its creator through Manage Access** (or Share in the model-driven app). The CRITICAL log lines name it too. The double-failure detail also says when the creator's share was confirmed before the move and has not been removed (it still stands unless the move dropped it) | No response promises a resume that the marker makes impossible (findings 11, 13, 14) |
| 4 | Every other stale claim: the comment that was at :442, the 409 `already_provisioned` detail ("Re-provisioning would create a second container…"), `owned_by_other_secure_team`'s detail and comments (same stale reason since b2), the `ProducesProblem(409)` comment, `RootRow.IsProvisioned` / `RecordContainerAsync` remarks ("Step 7 is the ONLY writer" → provisioning writes it in exactly two places), the class summary, the two reason-code docs, the double failure's "it resumes" when the undo is UNVERIFIED (now "while the team owns it", plus what applies if the undo landed), guide §4.3 / §7a | Finding 14 |

**Kept-container record after each failure after the move** (with the hardening; each row tested; "next call" = the
call the detail names):

| Failure after the move | Response | Someone can open it? | Next call |
|---|---|---|---|
| Share proof fails, undo fails (probe 1) | `creator_share_failed_resumable`, `containerKept: true`, CRITICAL | **Yes** — the share proven before the move stands | 409 — and TRUE: team-owned, own container, creator holding exactly the creator rights |
| Owner read-back fails, share read and grant fail | `owner_assignment_unverified`, `creatorShareConfirmed: false`, `containerKept: true` | **Yes** (same) | 409, as the detail says |
| Owner read-back fails, share read back | `owner_assignment_unverified`, confirmed, `containerKept: true` | Yes | 409 (provisioning complete), as the detail says |
| Pre-call shares unreadable (probe 2) | refused before any write | Yes (nothing moved) | 200, container kept |
| Share to the current owner refused (live gate (a)) + read-back, read and grant all fail after the move | `creator_share_failed_resumable`, `containerKept: true`, CRITICAL | **No** — the residual (§14.3) | 409, as the detail says; recovery = the administrator's Manage Access share |

**Residual, stated.** A kept-container record can be left with nobody able to open it only when (i) the move drops the
creator's proven share (live gate (b) disproved) AND the re-grant AND the undo (or the owner read-back) fail, or (ii)
Dataverse refuses a share to the record's current owner (live gate (a)) AND, after the move, the grant AND the undo (or
the owner read-back) fail. Each is CRITICAL-logged with the recovery named. That is the same failure class a record with
no container has (the double failure) — the difference is only the recovery: a Manage Access share instead of a
provisioning call.

### 14.2 Every verifier-round-4 item

| # | Finding | Disposition |
|---|---|---|
| 1 | BLOCKING: keeping the record's own container breaks the marker; the record cannot be resumed | **Closed** by §14.1 (prevention + truthful recovery, marker unchanged). Tests for the kept container under each failure after the move (S10-like via the pre-call read and via the (a) fallback, double failure, unverified unconfirmed, unverified confirmed), each making the NEXT call and asserting what the detail said, and `SomeoneCanOpen` wherever the design guarantees it. Where the verifier asked "the next administrator call resumes": it cannot without changing the marker — §14.3 |
| 2 | Close to POML trigger 3; not surfaced | **Surfaced** — §14.4 (treated as fired by code; resolved by prevention, resume condition unchanged) and the owner question §14.3 |
| 3 | Re-run results match | Confirmation — no action |
| 4 | Hard gate not run; the PR must cite it | **Closed**: the work branch is merged in (baseSha above) and BOTH integration suites were run in full on the final commit (POML outcome); the PR text is prepared in §14.7 |
| 5 | Round-3 items really closed | Confirmation — no action (the S19 test gained one assertion, §14.5) |
| 6 | Owner round 7 item 2 implemented as decided | Confirmation — no action |
| 7 | Schema script low gaps | **Closed**: (a) every mode now lists the WRITER profile's members and reports FAIL for any systemuser that is not one of the `-BffApplicationIds` users (a human, or another application user) and for any team — `-Verify` exits 1 on them; reported, never removed; (b) the informational count follows `@odata.nextLink` (`Measure-DvRows`, `Prefer: odata.maxpagesize=5000`). The script parses (PowerShell parser); not run live (no live writes) |
| 8 | Merge resolved correctly | Confirmation — no action |
| 9 | Chat handler cites item 3 for the interim app-only stamp; the PR should state path B explicitly | **Closed**: the class remarks now state it as a §6.5 path-B exception to the spec's "User-OBO ONLY" rule — item 2 (column BFF-written only) + item 3 (supersession for this handler) — and that neither item names this interim shape explicitly (the executor's reading, limited to one column, replaced by task 146). PR text in §14.7 |
| 10 | Absent column → `unreadable` → a wizard retry that fails until the schema is applied | **Closed**: a 400 on the creator-column read (`HttpRequestException` with `StatusCode` 400 — how `DataverseWebApiClient` surfaces "Could not find a property named …") is `creatorState: column-missing` (500, deterministic): the detail names `Set-RecordCreatorPersonSchema.ps1 -Apply`/`-Verify` and says calling again repeats the refusal; the client maps it to `needs-administrator`, NOT retryable (jest). Any other failed read stays the transient `unreadable`. The fixture now raises the 400 in the real client's shape. Test rewritten (reason in §14.5) + a transient-read test added |
| 11 | Criterion: the resumable code names an unreachable resume for a kept container | **Closed** — kept-container details name the Manage Access recovery and the `already_provisioned` answer; never "it resumes" (tested; seeds R3/R5/R12) |
| 12 | Criterion: S5 / R3 — unverified move + no share + kept container leaves nobody and no resume | **Closed for the reported path** (probe 2 is refused before any write; tested). Amendment R3 lists success, compensated failure and resume — after each of those someone can open a kept record. The administrator-only states keep a named, working recovery; the residual is §14.1's, routed to §14.3 |
| 13 | Criterion: after an unverified owner read-back, the next call follows the observed state — for a kept container it always reads 409 | **Closed**: for a kept container the 409 IS the rule for the observed state, and the response now says so; in the tested cases the 409 describes a record that really is provisioned (creator share exact) |
| 14 | Criterion: every comment / detail describes what happened | **Closed** — §14.1 row 4; plus the shared-container class |
| 15 | Publish size | **Not closed** — the main session measures (instruction) |
| 16 | Pending live gate | **Not closed** — no live writes allowed; §14.8 |

### 14.3 🔔 OWNER QUESTION — should a record that keeps its own container be resumable after a failure after its move?

> **✅ DECIDED 2026-10-03 — owner round 10 item 5 (BINDING): (C), as shipped.** "A kept-container record failing after its
> move: as shipped. No automatic resume; the response names the Manage Access recovery." No code change (round c1, §16.4).

Today (shipped): no. Once the secure owner team owns a record with a container recorded, every call answers
`already_provisioned`; a failed run on a kept-container record names the administrator's Manage Access share instead.

- **(A) The marker reads the share state:** owned by the team + a container recorded + NO principal holding a share →
  resume (keep the container; share to `createdby`, else `sprk_createdbyperson`; never the caller). Small change. It
  also repairs a secure record emptied of shares outside the BFF, which S5 says must not exist. Requires amending the
  acceptance criterion and its pinned test `ProvisionProject_WhenAlreadyOwnedBySecureTeam_IsRefusedAndWritesNothing`,
  whose seed (no share at all) is exactly that state.
- **(B) A BFF-written "provisioning state" column** (field-secured like `sprk_createdbyperson`): in-progress before the
  move, complete at the end; the marker reads it. Exact, but a schema change with a deploy-order rule and a backfill for
  rows provisioned before it.
- **(C) As shipped:** no resume; the Manage Access recovery, named in every such response.

**Recommendation:** keep (C) now; choose (A) if provisioning should be the ONE administrator recovery for every
stranded secure record. Not decided here (POML trigger 3: do not widen the resume condition on a guess).

### 14.4 Escalation triggers, re-evaluated (finding 2)

| Trigger | This round |
|---|---|
| 3 — team-owned row with a non-provisioning container, or a create-time `sprk_containerid` writer | The live census's literal conditions did not fire (§2). But b2's own code could MANUFACTURE such rows — the kept container (verifier) and the replaced shared container (§14.1). Treated as **fired by code**, resolved by **prevention**: a shared container can no longer reach the team; a kept container reaches it only through share-first or the (a) fallback, and each failure after its move names a working recovery. The resume condition is unchanged — the trigger's "do not widen on a guess" holds. Residual → §14.3 |
| Assign cascade under COMPENSATION (§13.6) | Unchanged: **STOP, still open** |
| The others | Unchanged from §3 / §13 |

### 14.5 Tests

**New** (`ProvisionRecordedContainerTests`, +9): kept container — pre-call shares unreadable (probe 2) refused before any
write, next call 200; share proof + undo fail (probe 1) → truthful detail, someone can open it, next call 409 with the
creator share exact; unverified + share unconfirmed; unverified + confirmed; the (a)-fallback residual → Manage Access
named, CRITICAL, next call 409. Shared container — Step 6 fails → next call resumes; configured container + share and undo
fail → an administrator's call resumes; unlink fails → stopped before the move, next call 200; share-first fails after the
unlink → "unlinked" stated, next call 200. **New** (`ProvisionResumeCreatorPersonTests`, +1): transient column read →
`unreadable`, the same caller's retry shares to the recorded person.

**Rewritten, one line each:** `Resume_WhenTheColumnCannotBeRead_Refuses500_NamingTheColumn` →
`Resume_WhenTheColumnDoesNotExist_RefusesAsColumnMissing_NotAsARetry` — it pinned `unreadable` (offered as a retry) for
an absent column, which finding 10 shows fails every time. **Strengthened:**
`Provisioning_WhenTheCompensatingMoveCannotBeReadBack_IsTheAdministratorOnlyState` now also asserts "While the team owns
it" and "If the move back did take effect" (the resume is no longer promised unconditionally when the undo is
unverified). **Comment only:** `SecureNamedOwnerTeamProvisioningTests`' retired-team test summary (stale "second
container" reason). **Fixture:** `ContainerClearSucceeds` (a null `sprk_containerid` write throws), `ContainerStampSucceeds`
now applies to non-null writes only (its two existing users write non-null values — unchanged), `CreatorPersonReadFails`
(transient), and the missing-column 400 raised as `HttpRequestException` with `StatusCode` 400 (the real client's
shape). **Client:** `provisioningService.test.ts` +2 (`column-missing` → needs-administrator, not retryable; the new code
in the emitted-code table, now 26).

**Perturbation sweep (round r1)** — seed, build, run the affected classes, restore the original bytes + touch
(`Environment.TickCount64 < 0` where a constant would be unreachable code under warnings-as-errors):

| # | Seeded violation | Result |
|---|---|---|
| R1 | kept container: refusal on unreadable pre-call shares removed | BITES (1) |
| R2 | shared container not unlinked before the move | BITES (4) |
| R3 | `keepsOwnContainer` never passed (kept-container texts and extension off) | BITES (5) |
| R4 | double failure never says the share proven before the move stands | BITES (1) |
| R5 | CRITICAL log line not kept-container-aware | BITES (2) |
| R6 | a 400 on the creator column read as transient `unreadable` | BITES (1) |
| R7 | every creator-column failure read as `column-missing` | BITES (1) |
| R8 | `column-missing` answered 409 instead of 500 | BITES (1) |
| R9 | details never say the shared link was removed | BITES (2) |
| R10 | the "if the move back did take effect" sentence dropped | BITES (1) |
| R11 | an unlink failure ignored (the run continues) | BITES (1) |
| R12 | kept container, unverified + unconfirmed: the resume promised | BITES (1) |
| C1 | client: `shared_container_not_cleared` not classified | BITES (2) |
| C2 | client: `column-missing` not read (falls to the default copy) | BITES (1) |

**14/14 bite.** The schema script's new membership check and paging have no automated test (they need a live
environment); the schema-agreement test still parses the script (its constants are unchanged).

**Runs (round r1):** full BFF unit suite **14,319 passed / 0 failed / 54 skipped (14,373)**; NetArchTest **345/345**;
**hard gate** — `Sprk.Bff.Api.IntegrationTests` **104/104** and `Spe.Integration.Tests` **403 passed / 0 failed / 25
skipped (428)**, both in full on this branch; the verifier's affected classes **452 passed / 0 failed / 3 skipped**;
Spaarke.UI.Components jest CreateProjectWizard + SummarizeFilesWizard **96/96 (8 suites)**, package build (`tsc`) clean,
prettier + eslint clean on the changed TS; `dotnet build` 0 warnings; `dotnet format whitespace --verify-no-changes`
clean on the changed C#. No package changed.

### 14.6 Placement and component justification (CLAUDE.md §10 / §11)

All server code stays in `ProvisionProjectEndpoint` (plus a remarks-only change in `DataverseCreateRecordHandler`). No
new endpoint, service, interface, DI registration, option, job, package or column.

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| Reason code `sdap.provision.shared_container_not_cleared` | the `sdap.provision.*` set; `container_ownership_unreadable` is a READ failure before any write | Extends the set; no existing code means "a write before the move failed, nothing moved, retry" | The client would show a generic error for a state the same caller can finish |
| ProblemDetails extension `containerKept` (bool, on `owner_assignment_unverified` / `creator_share_failed_resumable` / the kept-container refusal) | `sharesRestored`, `creatorShareConfirmed` (same convention) | Extension member, additive | A client cannot tell whether the administrator's recovery is a provisioning call or a Manage Access share. **Correction (r2, verifier round 5 finding 8):** as built in r1 NO client read it — this cell overstated what was built. Since r2 `provisioningService.ts` reads it (§15.2) |
| `creatorState` value `column-missing` | the existing `creatorState` vocabulary | Extends it | The wizard offers a "Try securing again" that fails until the schema is applied (finding 10) |
| Step 4.2 (one `UpdateAsync` clearing `sprk_containerid`) | Step 7's write of the same column | Same column, same writer | A secure record can be 409'd forever while its uploads go to shared storage |
| Private helpers `KeptContainerRecovery`, `AdministratorRecoveryForLog`, `IsColumnMissing`, `UnusableColumnMissing` | — | — | The same sentence duplicated across four branches, free to drift |
| Fixture switches `ContainerClearSucceeds`, `CreatorPersonReadFails` | the fixture's switches | Extends the fixture | Seeds R11 / R7 could not be made to bite |

**Removed:** `RecordContainerAsync`'s `previousContainerId` parameter and its "Overwriting sprk_containerid" warning —
dead once a shared value is unlinked before the move (the unlinked value is logged at Step 4.2).

### 14.7 PR description — required citations

Paste into the PR (with the publish-size numbers the main session measures):

> **Task 133 (#1054), round r1.** ADR-003 **path C (comply)**: compensation restores ownership and shares, never
> `sprk_issecure`; the shared-container unlink (Step 4.2) is not undone — a secure-flagged record with no container
> refuses uploads (fail closed). **CLAUDE.md §6.5 path B**: `DataverseCreateRecordHandler`'s interim app-only stamp of
> `sprk_createdbyperson` is an exception to the spec's "User-OBO ONLY" rule — owner round 7 item 2 (the column is
> BFF-written only) with item 3 (that rule superseded for this handler, the G5 pattern); neither names this interim shape
> explicitly, and task 146 replaces it — reviewer sign-off requested. **Integration hard gate (`ea6484102`):**
> `Sprk.Bff.Api.IntegrationTests` and `Spe.Integration.Tests` run in full on this branch — counts in the task POML.
> **Open owner items:** §13.6 Assign cascade under compensation (STOP); §14.3 kept-container resume; the (e) replay
> method. **Deploy order:** `Set-RecordCreatorPersonSchema.ps1` (dry run, `-Apply`, `-Verify` exit 0) in dev BEFORE this
> reaches master.

### 14.8 Pending manual gates (main session; nothing here was written live)

1. **Schema, BEFORE this reaches master** — unchanged command (§13.7 item 1); `-Verify` now also fails on any
   writer-profile member besides the BFF application user(s).
2. Live gate (a)–(f) (§9) and (g)/(h) (§13.7) after a BFF deploy. (h) is unchanged: a record that already records its
   own container → 200, `sprk_containerid` unchanged, no new container.
3. **NEW (i) shared container unlinked:** provision a secure record whose `sprk_containerid` is a business unit's
   container → 200; read back: the record records a NEW container of its own, and the business unit's
   `sprk_containerid` is unchanged.
4. Owner: §14.3, §13.6, the (e) replay acceptance. Publish size: main session.

### 14.9 Quality gates (round r1)

**code-review** (all severities):

| Finding | Severity | Disposition |
|---|---|---|
| `ProvisionProjectEndpoint.cs` grows to ~2,430 lines | Warning (size) | Accepted per COMPONENT-COMPLEXITY (as §13.9): one reason to change. Seams if it grows again: the container classification + Step 4.2, and the resume's person rule |
| Step 4.2 adds a write BEFORE the owner move that compensation does not undo | Warning | Intended and stated (class remarks, guide §7a, PR text): unlinking a shared container from a secure-flagged record makes its uploads fail closed instead of landing in shared storage; nothing is orphaned (the business unit / configuration keeps the container). Every later failure detail says "unlinked" |
| Kept records keep the live-gate-(a) post-move-grant fallback, which retains a nobody-can-open residual (with two further failures) | Suggestion | Deliberate (refusing would refuse every re-securing if (a) is disproved); residual stated, CRITICAL-logged, recovery named; owner question §14.3 |
| A 400 on the creator-column read is classified `column-missing` without reading the body | Suggestion | `DataverseWebApiClient` surfaces only the status (`EnsureSuccessStatusCode`); for a by-id, one-column read a 400 has no other realistic cause, and either way it is deterministic — the property that decides retryability |
| AI-smell scan: no new interface, no catch-log-rethrow, no null-check on a non-nullable, no swallowed failure (the unlink's catch returns a refusal) | — | Clean |

**adr-check**: ADR-001 (no new endpoint), ADR-002 (no plugin), ADR-003 (every new branch fails closed: a failed unlink stops before the move; an unreadable share set refuses a kept record before any write; `column-missing` and `unreadable` both refuse with nothing written; no branch gives anyone access they lacked), ADR-008 (delegation filter unchanged), ADR-010 (no new interface or registration), ADR-019 (ProblemDetails + `reasonCode`; `containerKept` follows the extension convention), ADR-038 (no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests; the fixture models Dataverse's exception shape): **compliant, 0 violations**. CLAUDE.md §6.5: the chat handler's interim app-only stamp is cited as a path-B exception for reviewer sign-off (§14.7).

---

## 15. Round r2 (2026-10-02, branch `task/uac-r2-133-b2-r2`) — verifier round 5

Base: `task/uac-r2-133-b2-r1` (`b26efd090`). Not re-merged: the work branch has moved on (`df2d05a5c..62ea6a8ee`),
the verifier confirmed it still merges cleanly, and none of its files overlap this round's. **No production server code
changed in this round** — `ProvisionProjectEndpoint.cs` is byte-identical to r1. Server changes are tests and the test
fixture; the client gained the `containerKept` consumer.

### 15.1 Every verifier-round-5 item

| # | Finding | Disposition |
|---|---|---|
| 1–4 | Re-runs, static checks, merge check, the round-4 blocking item | Confirmations — no action |
| 5 | Seed P5 survives (`creatorText = keepsOwnContainer`): the negative direction of the double failure's creator sentence is unpinned, on the nobody-can-open path | **Closed.** New `Provision_KeepingItsOwnContainer_WhenTheFallbackShareAndTheUndoFailAfterTheMove_NeverClaimsAConfirmedShare`: a kept container whose creator owns it; Dataverse refuses the share to the current owner (live gate (a)) → the post-move fallback with nothing proven; the move lands (read back); the proof after it fails; the undo is refused. Asserts `creator_share_failed_resumable`, `containerKept: true`, `ownershipRestored: false`, a detail WITHOUT "confirmed before the move" and WITH "may not be able to open it", `already_provisioned` and Manage Access; no grant ever accepted; `SomeoneCanOpen` **false**; the CRITICAL line; the next call is the 409 the detail names. Seed P5 fails it |
| 6 | Seeds P7 / P8 survive: "every later failure detail says unlinked" is unproven at two sites | **Closed at every site, not only the two.** `unlinkedNote` is used in SIX failure details after the unlink; r1 pinned two (the share-first refusal, the double failure). Four new tests pin the other four, each making the next call its detail names: the refused move (`owner_assignment_failed` → 200), the unverified move with the share confirmed (`owner_assignment_unverified`, **P8** → resumed 200), the unverified move with no share issued (`creator_share_failed_resumable` via the (a) fallback → an administrator's call resumes, creator share exact), and the verified undo (`creator_share_failed`, **P7** → 200 from the start). Removing the note at any one site fails exactly that site's test |
| 7 | Seed P14 survives: the fixture raised the transient creator-column failure as `InvalidOperationException`, so "any `HttpRequestException` is column-missing" passed | **Closed.** Fixture switch `CreatorPersonReadFails` (bool) → `CreatorPersonReadFailsWith` (`HttpStatusCode?`), raising `HttpRequestException` carrying the status — the real `DataverseWebApiClient` shape (`EnsureSuccessStatusCode`). The transient test is now a theory over **503, 429 and 500**; seed P14 fails all three rows |
| 8 | `containerKept` has no consumer; §14.6 justified it by a client distinction nobody built | **Closed by building the consumer** (§15.2), and §14.6's row carries a correction saying r1 overstated it |
| 9 | The r1 kept-container refusal changes the literal behaviour of a closed criterion and is not in §10 | **Closed** — §10 entry added |
| 10 | `DataverseCreateRecordHandler`'s interim app-only stamp of `sprk_createdbyperson` is a §6.5 path-B exception needing explicit approval | **Correctly surfaced; still open.** Not closable here: the owner or the PR reviewer signs it off before merge (PR text §14.7). No code changed |
| 11 | Publish size not measured | **Not closed** — the main session measures (instruction). No package changed; no production server code changed this round |
| 12 | Live gates (a)–(i) and the schema not run | **Not closed** — no live writes. Read-only fact added 2026-10-02: a Dataverse MCP query naming `sprk_createdbyperson` on `sprk_project` answers *"entity doesn't contain attribute with Name = 'sprk_createdbyperson'"* — the column is NOT in the connected dev environment, so the deploy-order rule binds (schema `-Apply`/`-Verify` before this reaches master). New gate caveat from #1081 (§2 addendum, §15.5) |
| 13 | "Every detail describes what happened" only partly proven (P5, P7/P8, P14) | **Closed** by items 5–7; the two further `unlinked` sites are pinned too |

### 15.2 Client — the `containerKept` consumer (finding 8)

`provisioningService.ts` reads `containerKept` from the problem body into `IProvisioningFailureExtensions` (beside
`creatorShareConfirmed` and `creatorState`). Two copies change, and only when it is `true`:

| Code | Copy without the flag (unchanged) | Copy with `containerKept: true` | Kind / retryable |
|---|---|---|---|
| `creator_share_failed_resumable` | "…An administrator needs to finish securing it." | "Securing the project stopped partway, and you may not be able to open it. If you cannot, an administrator needs to share it with you through Manage Access." | `needs-administrator` / false (unchanged) |
| `owner_assignment_unverified`, share NOT confirmed | "…If you cannot open the project, an administrator needs to finish securing it." | "…If you cannot open the project, an administrator needs to share it with you through Manage Access." | `interrupted` / true (unchanged: if the team does not own it the retry provisions it; if it does, the retry answers `already-provisioned`, whose copy is true) |

Unchanged on purpose: a CONFIRMED unverified move ("you can open it either way" — true either way) and the kept
container's pre-move `creator_share_failed` refusal ("its ownership is as it was" — nothing was written). Why: once the
team owns a project that kept its container, every securing call answers `already_provisioned` and finishes nothing, so
"an administrator needs to finish securing it" named a recovery that does not exist for it; the server's detail names the
one that does. Reach is unchanged and stated: neither wizard host can meet a kept-container project today (new projects
have no container since task 076). The classifier is the shared one any later host that secures an EXISTING record
inherits, which is where this state can occur.

### 15.3 Tests

**New server** (`ProvisionRecordedContainerTests`, +5 facts): P5 above; the four `unlinked` sites (§15.1 item 6), with a
seed helper `SeedRecordingABusinessUnitsContainer`. **Changed server:** the transient creator-column test
(`ProvisionResumeCreatorPersonTests`) is a theory over 503 / 429 / 500 (+2 rows); fixture switch replaced (§15.1 item 7).
**New client** (`provisioningService.test.ts`, +7): `containerKept` read from the body for both codes, true / false /
absent, each asserting kind, retryable, the Manage Access copy (or its absence) and no "try again" advice; plus the
confirmed-share copy kept for a kept container.

**Perturbation sweep (round r2)** — seed, build, run, restore the original bytes (`git checkout` for the endpoint, which
this round does not change; a saved copy for the client file) + touch:

| # | Seeded violation | Result |
|---|---|---|
| P5 | double failure: `creatorText = keepsOwnContainer` (the confirmed-share sentence without a proven share) | BITES (1) |
| P7 | verified-undo `creator_share_failed` detail without `unlinkedNote` (:1345) | BITES (1) |
| P8 | `owner_assignment_unverified` detail without `unlinkedNote` (:1267) | BITES (1) |
| U1 | refused-move detail without `unlinkedNote` (:1200) | BITES (1) |
| U2 | unverified-move, no-share detail without `unlinkedNote` (:1281) | BITES (1) |
| P14 | `IsColumnMissing` = any `HttpRequestException` | BITES (3) |
| C3 | client: `containerKept` not read from the body | BITES (3) |
| C4 | client: kept-container copy for `creator_share_failed_resumable` off | BITES (1) |
| C5 | client: kept-container copy for `owner_assignment_unverified` off | BITES (2) |

**9/9 bite.**

### 15.4 Placement and component justification (CLAUDE.md §10 / §11)

No new server surface: no endpoint, service, interface, DI registration, option, job, package or column; the endpoint is
unchanged. Client: one optional member on the existing exported `IProvisioningFailureExtensions` and two private copy
constants. Three questions for the member — **Existing:** `creatorShareConfirmed` / `creatorState` on the same interface
(grep: the only reader of ProblemDetails extensions in the package is `provisionSecureProject`). **Extension:** yes — it
extends that interface and that reader; no new type. **Cost of doing nothing:** a project that kept its container and was
left owned by the team without a confirmed creator share tells its user "an administrator needs to finish securing it",
a call that answers `already_provisioned` and finishes nothing. Fixture: a switch replaced (bool → status), not added.

### 15.5 Pending manual gates (main session; nothing here was written live)

Unchanged from §14.8 — schema dry run / `-Apply` / `-Verify` in dev BEFORE this reaches master (now confirmed absent in
dev, §15.1 item 12); live gate (a)–(f) and (g)–(i) after a deploy; owner §14.3, §13.6, the (e) replay acceptance;
publish size; the §6.5 path-B sign-off (§14.7). **New caveat (#1081, §2 addendum):** every gate that proves who can open
a record — (a)/(b) and the "someone can open it" checks of (g)–(i) — reads the record's SHARES (the strict share read, or
`RetrievePrincipalAccess` for the creator) or uses a test user outside the root business unit. A root-team member opens
every secure record in dev through Basic User's org-wide read, whoever it is shared to.

### 15.6 Quality gates (round r2 — TEST-MODIFYING, so code-review + adr-check run unconditionally)

**code-review** (all severities):

| Finding | Severity | Disposition |
|---|---|---|
| The four `unlinked` tests share their seed | Suggestion | Factored into `SeedRecordingABusinessUnitsContainer`; each test keeps its own fault switches and next call, which are what differ |
| The client copy swaps key on `(reasonCode, containerKept)` alone | — | Intended: the copy asserts only what those two establish ("may not be able to open it", "if you cannot") — the FR-31 rule in the file's own header |
| The new client copy names "Manage Access" to an end user | Suggestion | It is the administrator's action, named so the user can ask for it; the same name the server's detail and guide §7a use |
| AI-smell scan: no new interface, no catch-log-rethrow, no null-check on a non-nullable, no swallowed failure, no anti-laziness scaffolding | — | Clean |

**adr-check**: ADR-038 (no `Mock<HttpMessageHandler>`, no DI-registration test, no ctor null-check test; the fixture now
raises the real client's exception type — a fidelity fix, not a mock), ADR-019 (no ProblemDetails change), ADR-003 /
WP-6 (no server change), ADR-001 / 002 / 008 / 010 (not touched): **compliant, 0 violations**. §6.5: no new exception;
the path-B item (§14.7) still awaits sign-off.

### 15.7 PR description — addition to §14.7

> **Round r2 (verifier round 5).** Tests only on the server (5 new, 1 widened to a theory; fixture raises the real
> client's `HttpRequestException`), 9/9 perturbation seeds bite; the client reads `containerKept` and names the Manage
> Access recovery for a project that kept its own container. `ProvisionProjectEndpoint.cs` unchanged from r1.

### 15.8 Runs (round r2)

Full BFF unit suite **14,326 passed / 0 failed / 54 skipped (14,380)** — r1's 14,373 plus the 5 new facts and the 2
new theory rows; NetArchTest **345/345**; **hard gate** (`ea6484102`), both in full on this branch:
`Sprk.Bff.Api.IntegrationTests` **104/104**, `Spe.Integration.Tests` **403 passed / 0 failed / 25 skipped (428)**; the
verifier's provisioning class set (ProvisionProject*, SecureProjectShare, ProvisionRecordedContainer,
ProvisionResumeCreatorPerson, SecureNamedOwnerTeam*, CreatorPerson*, DataverseCreateRecordHandler, RecordShareRightsMask,
DelegationRule*) **251/251** (r1 244 + 7); Spaarke.UI.Components jest CreateProjectWizard + SummarizeFilesWizard
**103/103 (8 suites)** (r1 96 + 7); package production build (`tsc`) clean; prettier + eslint clean on the changed TS;
`dotnet build` 0 warnings; `dotnet format whitespace --verify-no-changes` clean on the changed C#. No package changed.
Publish size: not measured (main session).

---

## 16. Round c1 (2026-10-03, branch `task/uac-r2-133-c1`) — owner round 10 items 4 and 5; round 7 item 3 handoff

Base: `task/uac-r2-133-b2-r2` (`5a8b66c15`) with `work/unified-access-control-r2` merged in (owner rounds 8–11, the
route-authorization sweep tasks 159–170, `ContactIdentityStore` and the ADR-052 drift-walk fixes). The merge had **no
conflicts** and touched none of this task's files. **baseSha `bb0e989ab`** (HEAD right after that merge).

| Item | Source (owner-decisions note) | Disposition |
|---|---|---|
| 1 | Round 10 item 4 — compensation's reverse Assign cascade: (c) then (a) | **Built**: ONE primitive, `AssignCascadeChildOwners` (§16.2), wired into provisioning (§16.3); tests + seeds (§16.7) |
| 2 | Round 10 item 5 — the kept-container resume stays as shipped | **Recorded** (§16.4, §14.3, POML); no code change |
| 3 | Round 10 "decided by the main session" + round 7 item 3 — the interim `sprk_createdbyperson` stamp is superseded at integration by 146 | **Handoff kept accurate** (§13.8); handler untouched — 146 not pre-empted |
| 4 | Escalation trigger 4 answered | POML `<status>`, status note and outcome updated; §3, §13.6 marked answered |

### 16.1 Live facts (READ-ONLY, spaarkedev1, 2026-10-03)

Scripts: session scratchpad `task133/cascade-children.ps1`, `cascade-children-query.ps1`, `location-attrs.ps1` (GET only,
operator `az` token, explicit URL).

| Check | Result |
|---|---|
| Assign-cascading 1:N of each root | `sprk_project_Teams` → `team.regardingobjectid`, `sprk_project_SharePointDocumentLocations` → `sharepointdocumentlocation.regardingobjectid`, `sprk_project_SharePointDocuments` → `sharepointdocument.regardingobjectid` — Assign, Share, Unshare and Reparent all `Cascade`. Matter: the same three. **Work assignment: none** (unchanged from §2) |
| `team` | **`BusinessOwned`** — attributes `administratorid`, `businessunitid`, `regardingobjectid`; **no `ownerid` / `owninguser` / `owningteam`**. An Assign cannot re-own a team: there is no owner to snapshot or restore. 0 teams with any regarding in dev |
| `sharepointdocumentlocation` | `UserOwned`, stored (`ownerid`, `owninguser`, `owningteam`). **0 rows** with any regarding in dev. A create needs only `name` (`ownerid`/`servicetype` default) |
| `sharepointdocument` | `UserOwned`, `TableType` Standard, but listed from SharePoint, not stored. A plain read returns 0 rows; **a read filtered `_regardingobjectid_value eq <a project or matter id>` is REFUSED: 400, `0x80071017` "SharePoint S2S and MSTeams integration is not enabled for this org"** |

**Consequence (found before building, not after):** a fail-closed snapshot that read `sharepointdocuments` on every call
would have refused every project and matter provisioning in dev. So the documents are read only under a document
location (§16.2): with no location there is no SharePoint folder for a document to come from — the shape of every
record in dev, where documents live in SharePoint Embedded. Under a location, a refused read refuses the run.

### 16.2 The primitive — `AssignCascadeChildOwners` (task 148: reuse it, do not fork it)

`src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/AssignCascadeChildOwners.cs` — a static helper beside
`SecureRecordOwnerTeam` (no DI registration, no interface), keyed by the root's LOGICAL NAME so `Services/Access` code (148's
`SecureChildReconciler`) calls it without depending on the `Api` layer.

| Member | Contract |
|---|---|
| `TablesFor(rootLogicalName)` | The cascade tables per root (live metadata above): project / matter → `team` (`NoOwner`, never read), `sharepointdocumentlocation` (`Always`), `sharepointdocument` (`UnderDocumentLocations`); work assignment → none; any other table throws (an empty list would read as "no cascade"). A new cascading relationship is added HERE, once |
| `SnapshotAsync(client, rootLogicalName, rootId, ct)` → `CascadeSnapshotResult` | Read-only. Every owner-bearing child, `_regardingobjectid_value eq {root}`, with its own owner — or a failure naming the table: `Unreadable` (any failure but a 400, 401 or 403 — the next call may pass) or `Refused` (a 400; a 401/403 — the service's sign-in or its Read privilege on the table refused, *since round c1-r2, §18.4*; a FULL page of 5,000 rows, because `QueryAsync` reads one page; a row without an id or an owner — all deterministic). Never a partial snapshot. *Each rule pinned by a test — the id-less row since round c1-r2 (§18.2)* |
| `RestoreAsync(client, snapshot, logger, ct)` → `CascadeRestoreReport` | Keyed on observed state, child by child: read → already on its own owner = `AlreadyOwned` (nothing written); gone = `Gone`; otherwise `ownerid@odata.bind` PATCH (its own operation) then read back = `Restored`, or `Refused` / `NotApplied` / `Unverified` / `Unreadable`. Never throws for one child. `AllRestored`, `NotRestored` |
| `CascadeChildSnapshot.NotOwnedBy(owner)` | The children whose own owner is not `owner` — those a cascading move to `owner` leaves on the wrong one |
| `CascadeChild.RestoreCall` | The exact Web API call that puts the child back by hand: `PATCH /api/data/v9.2/{set}({id}) {"ownerid@odata.bind":"/systemusers(…)"}` (or `/teams(…)`) — what a failed restore names |

**How task 148 uses it (record for 148's executor — the main session may copy this into 148's POML; this task does not
edit another task's POML):**

- Around EVERY owner move of a root in 148's transitions (unsecure's Step 3, and any undo of a failed transition):
  `SnapshotAsync` BEFORE the move; refuse the move when it fails (the same fail-closed rule as here — a move whose cascade
  cannot be undone child by child is not attempted). The snapshot is also the "previous owner recorded in the run report
  before the write" 148's every-assign constraint asks for, for the cascade tables.
- After the move: call `RestoreAsync` when the cascade must NOT decide the children's owners (an undo; or a transition
  whose reconciler places them deliberately); do not call it when following the cascade is intended (the forward move into
  the secure owner team — owner round 4 item 3). Report `NotRestored` child by child with `RestoreCall`, fail closed.
- ✅ **Decided — owner round 13 item 1 (2026-10-03, BINDING):** when a record is UNSECURED, its
  `sharepointdocumentlocation` / `sharepointdocument` rows are re-owned by **the same rule task 146 applies to any
  non-secure child**, not left with the new root owner by the Assign cascade — one ownership invariant everywhere. *(This
  bullet was the open design point "the new root owner, as the cascade does, or the owner the task-146 rule gives a
  non-secure child"; the owner chose the second.)* For 148's unsecure: `SnapshotAsync` before the owner move stays the
  fail-closed read and the record of each row's previous owner; after the move, each row is put on the owner the task-146
  non-secure-child rule gives it (146's POML is the authority for that rule). `RestoreAsync` is NOT how unsecure places
  them — it would put them back on their snapshotted owner, the secure owner team the forward cascade gave them — but it
  remains the undo when 148's unsecure fails after its move. The owners these rows had before PROVISIONING are not stored
  anywhere once the forward move succeeds (the snapshot here lives for one provisioning call), and under this decision none
  is needed. Implementation is task 148's; nothing in this task changed for it.
- ⚠️ **Unproven assumption — `sharepointdocument` in an org WITH SharePoint integration** (verifier c1 item 12; round
  c1-r1, §17.3). There its rows are listed from SharePoint, not stored. Whether they carry an owner, whether the Assign
  cascade actually re-owns them, and whether an `ownerid` PATCH on them is accepted are all UNPROVEN — dev has the
  integration off, so none of the three can be observed there. Both ways it can be wrong fail closed: rows read without an
  owner refuse every move of a root that has documents, deterministically (here `cascade_children_unreadable`,
  `refused`); a PATCH Dataverse does not support ends every undo of such a root `cascade_children_not_restored`, naming a
  `nextCall` that fails the same way. 148 does not treat a document's restore as proven until it is checked live in an
  integration-on org.
- `sprk_*` child tables (documents, events, to-dos, communications …) are NOT cascade tables: 148's reconciler re-owns those
  through `IRecordOwnershipResolver`; this primitive covers only what Dataverse's own Assign cascade moves.

### 16.3 Wiring in provisioning (`ProvisionProjectEndpoint.MoveWithCreatorShareAsync`)

| Point | Behaviour |
|---|---|
| Before any write (after the owner read, before the pre-call share read, the Step 4.2 unlink and share-first) | `SnapshotAsync`. Failure → **`sdap.provision.cascade_children_unreadable`** (500, nothing written; `childTable`, `cascadeChildState: unreadable \| refused`) |
| Forward success | Nothing restored — the children stay with the team (round 4 item 3) |
| Compensation, move back VERIFIED | `RestoreAsync`. All back → the existing `creator_share_failed` (+ `childOwnersRestored: true`). Any not back → **`sdap.provision.cascade_children_not_restored`** (500): ownership restored, the share text as before, each child named in the detail and in `childOwnersNotRestored` (`table`, `id`, `ownerType`, `ownerId`, `outcome`, `nextCall`), a CRITICAL line naming each child and call, and "before provisioning is called again" (another run would snapshot the wrong owner) — never "retry" |
| Compensation, move back UNVERIFIED | Not restored (whether the record moved is unknown; putting a child on its own owner while the team may own the record would pull it out of the team). The children `NotOwnedBy(preOwner)` are named — `childOwnersAtRisk` + the detail's "If the move back did take effect …" sentence + a CRITICAL line with each call. None → text unchanged |
| Compensation, move back NOT applied (read back) | Unchanged: the record and its children stay with the team, as the move out left them |
| Resume; work assignment | No owner move → no snapshot; a work assignment has no cascade tables → no read. *Pinned since round c1-r2 (§18.3)* |

Comments rewritten to the new contract: the class summary (step 4's reads, step 5.5's compensation, "Rollback is for
ownership and shares only" now says whose ownership), `MoveWithCreatorShareAsync`'s remarks (new paragraph). The guide
§7a gains the cascaded-children paragraph and both codes' rows; `creator_share_failed` and
`creator_share_failed_resumable` rows name `childOwnersRestored` / `childOwnersAtRisk`.

### 16.4 Item 2 — the kept-container resume stays as shipped (owner round 10 item 5)

Decided: **(C)** of §14.3 — no automatic resume for a record that keeps its own container; the response names the Manage
Access recovery. **No code change.** Recorded in §14.3, the POML and the guide §7a, whose sentence "is an open owner
question" was the one statement the decision made false. The code comments ("an owner question recorded in the task 133
note §14", "an owner decision (task 133 note §14)") stay true and are unchanged. *(Round c1-r1, verifier c1 item 11: both
still read as open, so both now say "decided as shipped by owner round 10 item 5 (task 133 note §14.3)" — §17.2.)*

### 16.5 Item 3 — the interim `sprk_createdbyperson` stamp (round 7 item 3; round 10 main-session record)

Unchanged code. §13.8's 146 handoff now carries round 10's record and states precisely what 146 stamps (the OBO caller's
own systemuserid by WhoAmI — under create-as-the-app the row's `createdby` is the application user). The §14.7 path-B
citation remains the authority while the interim shape is the one running (until 146 integrates).

### 16.6 Placement and component justification (CLAUDE.md §10 / §11)

Placement: one new static helper in `Infrastructure/Dataverse` (beside `SecureRecordOwnerTeam`); everything else in the
existing endpoint. **No new endpoint, service registration, interface, option, job, package or Dataverse column.**

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `AssignCascadeChildOwners` (+ its records/enums: `CascadeChildTable`, `CascadeChild`, `CascadeChildSnapshot`, `CascadeSnapshotResult`, `CascadeRestoreReport`, `CascadeChildRestore`, `CascadeChildRead`, `CascadeReadFailure`, `CascadeChildRestoreOutcome`) | grep `_regardingobjectid_value`, `sharepointdocumentlocation`, `Cascade` in `src/server`: no code reads or restores cascade children; `ProvisionProjectEndpoint.MoveOwnerAsync` moves ONE root row (private, root-shaped `RootRow`); `RecordOwnershipResolver` decides owners, it does not snapshot them; `scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1` only *accepts* the cascade list | Not an extension of `MoveOwnerAsync` (private to one endpoint, root DTO) — the owner asked for ONE reusable primitive that task 148 reuses for transitions; a private helper would be forked by 148 | Compensation silently re-owns every child whose own owner differed from the record's to the record's pre-call owner (round 10 item 4) |
| Reason code `cascade_children_unreadable` | the `sdap.provision.*` set; `record_owner_unreadable` is deterministic and about the ROOT | Extends the set; no code means "the cascaded rows could not be read, nothing written" | A move whose cascade could not be undone child by child would run anyway — or a generic error for a state the same caller can retry |
| Reason code `cascade_children_not_restored` | `creator_share_failed` (means "retry"); `creator_share_failed_resumable` (means the record itself is stranded) | Neither fits: the record is back, the children are not, and a retry would record the wrong owners | The client would offer a retry that destroys the evidence of each child's own owner |
| ProblemDetails extensions `childTable`, `cascadeChildState`, `childOwnersRestored`, `childOwnersNotRestored`, `childOwnersAtRisk` | `creatorState`, `sharesRestored`, `containerKept` (same convention) | Additive members | The owner's "named in the response … with the next call to make" is not met; the client cannot tell a retry from an administrator's job |
| Client: `cascadeChildState` on `IProvisioningFailureExtensions`, two `REASON_STATES` entries, one copy constant | `creatorState` / `containerKept` on the same interface and reader | Extends both | The wizard would show the generic error copy for both codes, and offer "Try securing again" on a deterministic refusal |
| Fixture: cascade children + the Assign cascade, `SharePointDocumentReadRefused` (default true — dev's live answer), `CascadeChildSnapshotReadFailsWith`, `FailChildOwnerBindFor`, `Queries` | the fixture's switches | Extends the fixture | Seeds C1–C16 could not bite |

Publish size: not measured (main session, by instruction). No package changed → no CVE delta.

### 16.7 Tests

**New class `ProvisionAssignCascadeChildOwnerTests` (8 methods, 12 cases):**

| Test | Pins |
|---|---|
| `Compensation_PutsEveryCascadedChildBackOnItsOwnPreCallOwner` (project, matter) | **THE owner-mandated test**: a location sharing the record's owner, a location and a document with owners of their own; after the compensated call each is on its OWN pre-call owner (read back), the shared-owner one never written, and exactly two child binds sent |
| `Compensation_WhenAChildCannotBePutBack_NamesItWithTheCallThatPutsItBack` | Fail closed and report: `cascade_children_not_restored`, the child + own owner + exact `nextCall` in the body, CRITICAL with the call, no "retry", the others still restored; after the administrator's call the creator's next call is 200 |
| `Provisioning_WhenTheCascadedRowsCannotBeRead_RefusesBeforeAnyWrite` (503, 429 → `unreadable`; 400 → `refused`) | Refused before any write; the transient ones' retry succeeds |
| `Provisioning_WhenTheCascadedRowsAreReadIncompletely_RefusesBeforeAnyWrite` (full page, owner-less row) | The two deterministic incompleteness rules. *The third — a row without an id — was claimed here but unpinned until round c1-r2 added the `idless-row` case (§18.2)* |
| `Provisioning_UnderADocumentLocation_WhenSharePointDocumentsAreRefused_RefusesBeforeAnyWrite` | Dev's live 400 under a location → `refused`, `childTable: sharepointdocument` |
| `Provisioning_WithNoDocumentLocation_NeverReadsSharePointDocuments` | *Beyond the closed set — justification:* pins §16.1's live finding; without the gate every project/matter provisioning in dev is refused |
| `Provisioning_WhenItSucceeds_LeavesTheCascadedRowsWithTheTeam` | *Beyond the closed set — justification:* a restore that also ran on success would undo the owner-accepted forward cascade (round 4 item 3) |
| `Compensation_WhenTheMoveBackCannotBeVerified_NamesTheRowsItWouldLeaveOnTheWrongOwner` | Unverified undo: nothing written to children, only the differing-owner child named (`childOwnersAtRisk`, detail, CRITICAL) |

No existing test changed (all 251 of the verifier's provisioning set pass unmodified; the fixture's cascade is a no-op
for them — they seed no children). **Client** (`provisioningService.test.ts`, +5): the two codes in the emitted-code
table (26 → 28) and `cascadeChildState` read from the body (`unreadable` / absent → retryable, `refused` → not, the
administrator copy).

**Perturbation sweep (round c1)** — seed, build, run the provisioning classes (154 tests) or the client file, restore the
original bytes + touch (`Environment.TickCount64` where a constant would be unreachable code under warnings-as-errors):

| # | Seeded violation | Result |
|---|---|---|
| **C1** | **restore skipped** (`RestoreAsync` replaced by an empty report — the owner-mandated seed) | **BITES (3)** |
| C2 | snapshot failure ignored (empty snapshot) | BITES (6) |
| C3 | documents read without a location (ungated) | BITES (69 — every project/matter provisioning, dev's 400) |
| C4 | documents never read | BITES (3) |
| C5 | a 400 read as transient | BITES (2) |
| C6 | restore failures not reported | BITES (1) |
| C7 | the child read-back trusted | BITES (1) |
| C8 | a child already on its owner written anyway | BITES (2) |
| C9 | unverified undo: children at risk not named | BITES (1) |
| C10 | `NotOwnedBy` names every child | BITES (1) |
| C11 | a full page read as complete | BITES (1) |
| C12 | an owner-less row accepted | BITES (1) |
| C13 | the not-restored line not CRITICAL | BITES (1) |
| C14 | restore also run on success | BITES (1) |
| C15 | matter not described as cascading | BITES (1) |
| C16 | the restore bind names the wrong owner | BITES (4) |
| CL1 | client: `cascadeChildState: refused` not swapped | BITES (1) |
| CL2 | client: `cascadeChildState` not read from the body | BITES (1) |
| CL3 | client: `cascade_children_not_restored` offered as a retry | BITES (1) |
| CL4 | client: `cascade_children_unreadable` not offered as a retry | BITES (3) |

**20/20 bite.** Scripts: scratchpad `task133/perturb_c1.py`, `perturb_c1_js.py`; sources verified restored (`git status`, no
`TickCount64` left).

*Round c1-r1 (verifier c1 item 1):* the sweep above missed two fail-open seeds on the primitive — S19 (the read BEFORE a
child's restore fails → `AlreadyOwned`) and S20 (the read-back AFTER its PATCH fails → `Restored`) — which survived every
test. Pinned by a new theory (2 cases) and two fixture switches; S19–S22 now bite (§17.1). The class is 9 methods / 14
cases.

### 16.8 Pending manual gates (main session; nothing here was written live)

Unchanged from §15.5 (schema before master; live gate (a)–(i) after a deploy; the (e) replay acceptance; publish size).
*Round c1-r2: the (e) replay acceptance is ANSWERED — owner round 13 item 2 (2026-10-03, BINDING): (e) on a throwaway
TEST project is covered by round 11's approval. It stays a main-session live step (§18.5).* Two notes on the gates for
this round:

- **(e) compensating reassignment** now also runs the child snapshot and restore. In dev every root has 0 document
  locations, so (e) proves the no-children path (the snapshot reads `sharepointdocumentlocations`, finds none, and never
  reads `sharepointdocuments`); the restore of a real child cannot be proven in dev without SharePoint integration.
- **NEW (j), optional — the dev refusal shape on a TEST record (round 11 approves probes on test records):** on a
  throwaway secure project `<P>` that is not yet provisioned, create one location
  `POST {Api}/sharepointdocumentlocations` `{"name":"probe-133c1","absoluteurl":"https://probe.invalid/133c1","regardingobjectid_sprk_project@odata.bind":"/sprk_projects(<P>)"}`;
  call provisioning for `<P>` → expect 500 `sdap.provision.cascade_children_unreadable`, `childTable: sharepointdocument`,
  `cascadeChildState: refused`, and `<P>`'s owner and `modifiedon` unchanged; then `DELETE {Api}/sharepointdocumentlocations(<id>)`
  and delete `<P>`, recording both deletions.

**Observation for tasks 145 / 146 / 148 (not this round's scope; unproven; no code changed for it):** the codified
"Secure Record Owner" role (`config/secure-record-owner-role.json`) holds no privilege on `sharepointdocumentlocation`
(the "SharePoint four" were stripped by §5.4 on 2026-10-01). If Dataverse checks the assignee's Read on each table an
Assign CASCADES to — as it does for the assigned row itself ("refuses team ownership without Read") — then the FORWARD
move of a project or matter that has a document location is refused. That is the existing, fail-closed
`owner_assignment_failed` (owner read back unchanged, nothing moved), not a new hole; dev has 0 such roots, so it cannot
be observed there without a probe like (j) extended past the snapshot (which (j) never reaches).

**Unproven assumption, next to the observation above (verifier c1 item 12; round c1-r1, §17.3):** in an org WITH
SharePoint integration, `sharepointdocument` rows are listed from SharePoint, not stored. Whether they carry an owner,
whether the Assign cascade re-owns them, and whether an `ownerid` PATCH on them is accepted are all unproven, and none can
be shown in dev (integration off; 0 document locations, re-confirmed read-only by the verifier). Both ways it can be wrong
fail closed: rows read without an owner → every provisioning of a record with documents is refused before any write
(`cascade_children_unreadable`, `refused`); a PATCH Dataverse does not support → every compensation of such a record ends
`cascade_children_not_restored`, naming a `nextCall` that fails the same way. Gate (e) proves only the no-children path in
dev; a real child restore — a location's or a document's — cannot be shown live there. The first integration-on
environment should run (j) past the snapshot and (e) with a document location before relying on the document path.

### 16.9 Quality gates (round c1 — FULL rigor, and TEST-MODIFYING, so code-review + adr-check run)

**code-review** (all severities):

| Finding | Severity | Disposition |
|---|---|---|
| `ProvisionProjectEndpoint.cs` grows to ~2,590 lines | Warning (size) | Accepted per COMPONENT-COMPLEXITY (as §13.9/§14.9): one reason to change. The new mechanism went into its own file (`AssignCascadeChildOwners`, 400 lines) — the seam named in earlier rounds, taken for the part with a second consumer (148) |
| Nine public types in one Infrastructure file | Suggestion | Kept together (one concept) and top-level so 148's `Services/Access` code names them directly; no name collides (grep) |
| Restore is per child (read, PATCH, read) | Suggestion | Compensation is rare and a root's document locations are few; a full page refuses before any write, so N ≤ 4,999 |
| A 400 is classified `refused` from the status alone | Suggestion | `DataverseWebApiClient` surfaces only the status (the `IsColumnMissing` precedent); for these by-regarding / by-id reads a 400 has no transient cause, and either way it is deterministic — the property that decides retryability |
| Child ids and owners returned to the caller | — | Owner-mandated ("named in the response"); the caller holds Write on the record these rows hang off |
| The unverified-undo branch names children but does not restore | — | Intended (§16.3): restoring while the team may still own the record would pull children out of it |
| AI-smell scan: no new interface, no catch-log-rethrow, no null-check on a non-nullable, no swallowed failure (each child outcome is reported), exhaustive table switch with a throwing default | — | Clean |

**adr-check**: ADR-001 (no new endpoint), ADR-002 (no plugin; the restore is server-side in the BFF), ADR-003 (an
unreadable or incomplete snapshot refuses before any write; an unverifiable child restore is a failure, never "restored"
— *unpinned in round c1 (verifier c1 items 1/14: seeds S19/S20 survived); pinned in round c1-r1, §17.1*;
`Gone` is not a failure because nothing is left to restore), ADR-008 (the delegation filter unchanged), ADR-010 (no
interface, no registration), ADR-019 (ProblemDetails + stable `reasonCode`; extensions follow the existing convention),
ADR-038 (no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests; the fixture models Dataverse's cascade and
its 400 in the real client's exception shape), ADR-052 (no background work): **compliant, 0 violations**. §6.5: no new
exception; the §14.7 path-B item is unchanged (§16.5).

### 16.10 PR description — addition to §14.7 / §15.7

> **Round c1 (owner round 10 items 4–5).** Compensation's reverse Assign cascade: ONE primitive,
> `Infrastructure/Dataverse/AssignCascadeChildOwners` (snapshot each cascaded child's own owner before any write; restore
> after a verified undo; fail closed and name each child with the call that puts it back) — task 148 reuses it. Two reason
> codes (`cascade_children_unreadable`, `cascade_children_not_restored`), client-classified. Live (read-only) finding: dev
> refuses `sharepointdocuments` reads (S2S integration off), so documents are read only under a document location. The
> kept-container resume stays as shipped (round 10 item 5). 20/20 perturbation seeds bite.

### 16.11 Runs (round c1)

Full BFF unit suite, once, on the final code: **14,338 passed / 1 failed / 54 skipped (14,393)**, 25 m 35 s with four
other agents' test hosts running. The one failure,
`OfficeVersionSaveRevertTests.EmailSave_ResentWithNoClientKey_IsStillAnsweredDuplicate_AndReadsNoFile`, is unrelated (its
own host and world, the Office email-save duplicate path; no file this round touches) and **passed on an isolated re-run
(the class 9/9)** — contention; both results reported. NetArchTest **346/346**. **Hard gate**, both in full on this
branch: `Sprk.Bff.Api.IntegrationTests` **104/104**, `Spe.Integration.Tests` **403 passed / 0 failed / 25 skipped (428)**.
The verifier's provisioning class set (ProvisionProject*, SecureProjectShare, ProvisionRecordedContainer,
ProvisionResumeCreatorPerson, SecureNamedOwnerTeam*, CreatorPerson*, DataverseCreateRecordHandler, RecordShareRightsMask,
DelegationRule*, ProvisionAssignCascadeChildOwner) **263/263** (r2 251 + 12). Spaarke.UI.Components jest
CreateProjectWizard + SummarizeFilesWizard **108/108 (8 suites)** (r2 103 + 5); package build (`tsc`) clean; prettier +
eslint clean on the changed TS. `dotnet build` 0 warnings / 0 errors; `dotnet format whitespace --verify-no-changes` clean
on the four changed C# files. No package changed (no CVE delta). Publish size: not measured (main session).

Fresh-worktree note for whoever re-runs the client tests: `@spaarke/sdap-client` and `@spaarke/auth` are `file:` packages
whose `dist/` is not committed; build each (`npm install --legacy-peer-deps --no-audit --no-fund` + `npm run build` in
`src/client/shared/Spaarke.SdapClient` and `Spaarke.Auth`) before the component package's jest or `tsc`, or four suites
fail to resolve them.

## 17. Round c1-r1 (2026-10-03, branch `task/uac-r2-133-c1-r1`) — the verifier's findings on round c1

Base: `task/uac-r2-133-c1` @ **`27d14cbcf`** (baseSha), branched as `task/uac-r2-133-c1-r1`. Binding sources re-read from
`work/unified-access-control-r2` (owner rounds 10–11). No escalation trigger fired: every item is answered by round 10
item 4 or 5, or is a test, a comment or a note.

| Item | Verifier finding | Disposition |
|---|---|---|
| 1 | MEDIUM — seeds S19 / S20 (a child's read before / after its restore fails → `AlreadyOwned` / `Restored`) survive every test | **Closed** (§17.1): two fixture switches, one theory (2 cases); S19–S22 bite |
| 2–10 | VERIFIED (seed sweep, runs, merge, POML, snapshot order, restore-after-verified-undo only, `QueryAsync` 400 shape, 0 live locations, restore order) | No change; nothing to close |
| 11 | LOW — `ProvisionProjectEndpoint.cs:61` still reads as an open owner question | **Closed** (§17.2) — and the same wording at `:2573` |
| 12 | LOW / OBSERVATION — `sharepointdocument` in an integration-on org is unproven | **Recorded** (§17.3): §16.8 live-gate notes + §16.2 task 148 handoff; no code (both failure shapes fail closed) |
| 13 | LOW — "a child that no longer exists is Gone" overstates: a row deleted after its PATCH is `NotApplied`/`Refused` | **Closed** (§17.4): wording only; behaviour unchanged (fails closed) |
| 14 | Criterion not met: round 10 item 4 "a child whose restore fails is named" — `Unreadable` / `Unverified` untested | **Closed** by item 1 |
| 15 | Publish size not measured | **Not closed — main session** (by instruction; no package changed, no CVE delta) |
| 16 | Manual live gate (a)–(j), schema dry run / -Apply / -Verify | **Not closed — main session** (approved by owner round 11); §17.3 adds the dev limit on (e) |
| 17 | Step 9, TASK-INDEX status cell | **Not closed — main session** (by instruction this executor does not edit TASK-INDEX.md / current-task.md) |

### 17.1 Items 1 and 14 — the two unpinned fail-closed outcomes

The c1 primitive was correct; nothing pinned two of its four failure outcomes. The fixture had no way to fail a child's
**by-id** read (`CascadeChildSnapshotReadFailsWith` fires only on the snapshot's by-regarding read).

**Fixture** (`ProvisionProjectTestFixture`), two switches in the real client's exception shape
(`HttpRequestException`, 503):

- `FailChildOwnerReadFor` — a by-id read of the named child throws: the restore's read of its current owner, BEFORE any
  PATCH. The snapshot's by-regarding read is unaffected.
- `FailChildOwnerReadBackAfterBindFor` — once an `ownerid` PATCH on the named child has been APPLIED, its by-id read
  throws: the read-back after the PATCH. The read before it still answers. (Applied binds are tracked per child;
  `Reset` clears both switches and the set.)

**Test** — `ProvisionAssignCascadeChildOwnerTests.Compensation_WhenAChildsRestoreCannotBeConfirmed_NamesItAsNotRestored`,
a theory over `Unreadable` / `Unverified`. Two document locations with owners of their own (a user and a team); the
post-move share proof fails, so the move is undone (read back). Asserted: `sdap.provision.cascade_children_not_restored`;
`ownershipRestored: true`, `childOwnersRestored: false`; exactly one named child with `table`, `id`, `ownerType`,
`ownerId`, **`outcome` = the case** and the exact **`nextCall`**; the detail names the child and says "before
provisioning is called again", never "retry"; a **CRITICAL** line naming `sharepointdocumentlocation {id} ({outcome})`
and the call; the record's owner restored; the other child put back; no container. `Unreadable` → no write to the child
(it stays on the owner the undo's cascade left); `Unverified` → exactly one bind to it, with its own owner (the PATCH was
sent; only its read-back failed — and the response still does not claim it).

**Seeds** (scratchpad `task133/perturb_c1r1.py`; each seed applied to `AssignCascadeChildOwners.cs`, the test project
built, two filters run, the original bytes restored and the file touched; `git status` verified after):

| # | Seeded violation | `DataMutation.ExternalAccess` WITHOUT the new test (202) | The new theory (2) |
|---|---|---|---|
| **S19** | the read before a child's restore fails → `AlreadyOwned` | SURVIVES (0 failed) — the verifier's finding, reproduced on a wider set | **BITES** — `Unreadable` case |
| **S20** | the read-back after its PATCH fails → `Restored` | SURVIVES (0 failed) | **BITES** — `Unverified` case |
| S21 | `Unreadable` counted `IsBack` | SURVIVES (0 failed) | **BITES** — `Unreadable` case |
| S22 | `Unverified` counted `IsBack` | SURVIVES (0 failed) | **BITES** — `Unverified` case |

Each seed fails exactly the case it should and no other. With c1's 20 seeds, the sweep is **24/24 bite**.

### 17.2 Item 11 — the kept-container comment

`ProvisionProjectEndpoint.cs` class remarks (`:61`): "… — an owner question recorded in the task 133 note §14" →
"… — decided as shipped by owner round 10 item 5 (task 133 note §14.3)". The `RootRow.IsProvisioned` remarks (`:2573`)
said "an owner decision (task 133 note §14)", which also reads as pending; it now carries the same words. Comments only.

### 17.3 Item 12 — the unproven `sharepointdocument` assumption

Recorded where the verifier asked: the live-gate notes (§16.8, beside the forward-move privilege observation) and task
148's handoff (§16.2). In an org WITH SharePoint integration, `sharepointdocument` rows are listed from SharePoint; whether
they carry an owner, whether the Assign cascade re-owns them and whether an `ownerid` PATCH on them is accepted are all
unproven, and none is observable in dev. Both ways it can be wrong fail closed (ownerless → `cascade_children_unreadable`
`refused`, before any write; PATCH unsupported → `cascade_children_not_restored` with a `nextCall` that fails the same
way). No code changed for it. Gate (e) in dev proves only the no-children path. **For the main session:** if 148's POML
is amended from §16.2, copy this bullet with it.

### 17.4 Item 13 — what `Gone` means

`AssignCascadeChildOwners` remarks, the `Gone` / `Refused` / `NotApplied` member docs and `IsBack`'s summary now say what
the code does: only the read BEFORE the restore decides `Gone`; a child that disappears after its PATCH is reported
`Refused` or `NotApplied` — a failure, fail closed; `Unreadable` and `Unverified` are never "back". Behaviour unchanged.
`Gone` itself stays untested, deliberately: no seed on it is fail-open (counting it as a failure refuses more; patching a
missing row fails closed as `Refused`), and the item asked for wording.

*Correction (round c1-r4, §20.1–20.2):* "no seed on it is fail-open" holds only for the read BEFORE the restore, which
decides `Gone`. The read AFTER the PATCH was unpinned: seeds S13 / S16 (a row gone after its PATCH counted `Restored`) were
fail-open and survived all 211 tests; they are now pinned. And "patching a missing row fails closed as `Refused`" assumed
an update-only PATCH — the restore's PATCH sends no `If-Match`, so Dataverse UPSERTS a missing row (§20.2, recorded, not
changed).

The guide §7a `cascade_children_not_restored` row said the named rows "are NOT back on their own owners" — true for
`Refused`/`NotApplied`, not known for `Unreadable`/`Unverified`, which item 1 now pins. It now says "NOT confirmed back"
and gives each `outcome`'s meaning.

### 17.5 Placement and component justification (CLAUDE.md §10 / §11)

**No new production surface**: no endpoint, service, registration, interface, option, job, package, column or reason
code; the BFF change is comments only. New test surface only:

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| Fixture switches `FailChildOwnerReadFor`, `FailChildOwnerReadBackAfterBindFor` (+ the applied-bind set) | `CascadeChildSnapshotReadFailsWith` (by-regarding read only), `FailChildOwnerBindFor` (the PATCH only), `FailOwnerReadBackAfterBindTo` (the ROOT's read-back) | Extends the fixture's switch set, in the existing switches' shape | S19 and S20 stay unseen: an unreadable or unverifiable child restore could be reported "restored" with every test green |
| Theory `Compensation_WhenAChildsRestoreCannotBeConfirmed_NamesItAsNotRestored` | `Compensation_WhenAChildCannotBePutBack_…` pins only `Refused` | A separate theory: the setup and the per-outcome write assertions differ | Owner round 10 item 4 is unpinned for two of the four failure outcomes |

Publish size: not measured (main session, by instruction). No package changed → no CVE delta.

### 17.6 Quality gates (TEST-MODIFYING → code-review + adr-check unconditionally)

**code-review** (all severities, on the diff): no production behaviour change (two comment blocks, five doc members).
Fixture: the by-id switch keys on the child id in the filter and only when the read is NOT by regarding, so the snapshot
is untouched; the read-back switch keys on an APPLIED bind, so it cannot fire before the PATCH (S20's case is reached
only through the PATCH — asserted by the one recorded bind). Theory: each case asserts its distinguishing effect (no write
vs one write), so a fixture that failed the wrong read would go red. AI-smell scan: no interface, no swallowed failure, no
assertion on a mock's call count standing in for state. **No findings.**

**adr-check**: ADR-003 (an unreadable or unverifiable child restore is a failure, never "restored" — now pinned, as
§16.9 claimed); ADR-038 (KEEP path `tests/integration/data-mutation/**`; no `Mock<HttpMessageHandler>`, no
DI-registration or ctor-null tests; the fixture fails in `DataverseWebApiClient`'s real exception shape); ADR-002 / 010 /
019 / 052 untouched. **Compliant, 0 violations.** §6.5: none.

### 17.7 Runs (round c1-r1)

Affected first: `ProvisionAssignCascadeChildOwnerTests` **14/14** (c1's 12 + 2); the verifier's provisioning class set
(as §16.11) **265/265** (263 + 2); `DataMutation.ExternalAccess` **204/204**. `dotnet build` of the test project 0
warnings / 0 errors; `dotnet format whitespace --verify-no-changes` and the pre-commit hook's own `dotnet format
--include` leave the four changed C# files byte-identical.

Then once, on the final code:

- **Full BFF unit suite: 14,205 passed / 136 failed / 54 skipped (14,395)**, 1 h 9 m — run while **ten** full BFF unit
  suites from other agents' worktrees ran at once (CPU 83–99 %, free memory 1–42 MB of 63 GB, measured during the run).
  135 of the 136 are `TaskCanceledException` (HttpClient's 100 s timeout, 2–6 min per test); the 136th,
  `OrganizationMembershipReadTests.GetGrantSetAsync_CallerCancelsDuringTheJunctionRead_KeepsTheDirectGrants`, is a
  cancellation-timing test. None is in a class this round touches (two `Api.ExternalAccess` classes — the external
  project-event and internal-user-share contracts — timed out like the rest; no provisioning class failed). **Every one of
  the 53 classes with a failure, re-run in isolation: 499 passed / 0 failed / 10 skipped (509)** — contention; both
  results reported.
- NetArchTest **346/346**.
- **Hard gate**, both in full on this branch: `Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests`
  **403 passed / 0 failed / 25 skipped (428)**.

No client file changed (no jest / prod build needed). No package changed (no CVE delta). Publish size: not measured
(main session).

### 17.8 PR description — addition to §16.10

> **Round c1-r1 (verifier on c1).** Pinned the two child-restore outcomes c1 left untested — a child whose owner cannot
> be read before its restore (`Unreadable`) or read back after it (`Unverified`) — as `cascade_children_not_restored`,
> named with its `nextCall` and a CRITICAL line (owner round 10 item 4). Seeds S19–S22 bite (24/24 with c1's). Comments:
> the kept-container decision (round 10 item 5) and what `Gone` means. Recorded: the `sharepointdocument` behaviour in an
> integration-on org is unproven (fails closed either way).

## 18. Round c1-r2 (2026-10-03, branch `task/uac-r2-133-c1-r2`) — the verifier's findings on round c1-r1; owner round 13

Base: `task/uac-r2-133-c1-r1` @ **`fde7441f7`** (baseSha), branched as `task/uac-r2-133-c1-r2`. Binding sources re-read from
`work/unified-access-control-r2` (owner rounds 1–13 and the #1081 peer report). No escalation trigger fired: items 1–4
are tests, one failure classification and docs inside owner round 10 item 4; items 13 and 14 are owner round 13's answers,
recorded.

| Item | Verifier finding | Disposition |
|---|---|---|
| 1 / 10 | MEDIUM — the `NotApplied` child restore is untested; seed V3 (`NotApplied` counted `IsBack`) is fail-open and survives all 204 `DataMutation.ExternalAccess` tests | **Closed** (§18.1): fixture `IgnoreChildOwnerBindFor`; a `NotApplied` case on the not-restored theory; V3 bites |
| 2 | LOW — the snapshot's "a row without an id is refused" rule is untested; V18 (id check dropped, child recorded under `Guid.Empty`) survives | **Closed** (§18.2): fixture `ChildRowReadWithoutIdFor`; an `idless-row` case; V18 bites |
| 3 | LOW — "Resume and work assignments make no snapshot reads" has no test; V17 (`TablesFor("sprk_workassignment")` returns the project/matter tables) survives | **Closed** (§18.3): a new theory (work-assignment move; project and matter resume); V17 and R1 (a resume that snapshots) bite |
| 4 | LOW (classification) — a 401/403 on the snapshot read is `Unreadable`, so the caller is told to retry a read that fails every time | **Closed — classified `Refused`** (§18.4): one predicate in `FailureOf`; the `refused` detail names the Read privilege; guide §7a row; two cases; R2–R4 bite |
| 5–9 | VERIFIED (runs, seeds, snapshot/restore order, merge, owner decisions) | No change |
| 11 | Criterion: publish size; the full BFF unit suite green in one clean run | **Publish size NOT closed — main session** (by instruction; no package changed, no CVE delta). The full suite: **closed** — one clean run on the final code, 14,348 passed / 0 failed / 54 skipped (§18.9) |
| 12 | Criterion: manual live gate (a)–(f), (g)–(j), the `sprk_createdbyperson` dry run / -Apply / -Verify | **NOT closed — main session** (approved by owner round 11 for integration); in dev (e) proves only the no-children path |
| 13 | Owner round 13 item 2 — gate (e) (the compensation replay on a throwaway TEST project) is covered by round 11 | **Recorded** (§18.5): POML status-note, §16.8, the live-gate script header. Stays a main-session live step |
| 14 | Owner round 13 item 1 — unsecure re-owns the location rows by the task-146 non-secure-child rule | **Recorded** in §16.2 (the open design point for 148, now decided) |

### 18.1 Item 1 — `NotApplied` is pinned

The production code was already correct: a child PATCH Dataverse accepts but does not apply reads back on another owner,
is `NotApplied`, is not `IsBack`, and ends the call `cascade_children_not_restored`. Nothing pinned it, and the fixture
had no way to produce it (its root-level twin, `OwnershipPatchIsApplied = false` → `owner_assignment_not_applied`, is
pinned).

- **Fixture** — `IgnoreChildOwnerBindFor`: an `ownerid` PATCH on the named cascade child is recorded (as every PATCH is,
  first) and returns with no error, and the child keeps its owner. `Reset` clears it.
- **Test** — `Compensation_WhenAChildsRestoreCannotBeConfirmed_NamesItAsNotRestored` gains `[InlineData("NotApplied")]`
  (now 3 cases). Asserted, as for the other two: `cascade_children_not_restored`, `ownershipRestored: true`,
  `childOwnersRestored: false`, exactly one named child with `outcome: "NotApplied"` and the exact `nextCall`, no "retry",
  a CRITICAL line naming `sharepointdocumentlocation {id} (NotApplied)` and the call, the other child put back. Specific
  to `NotApplied`: **exactly one bind** to the child, with its own owner, and the child still on the record's pre-call
  owner (where the undo's cascade left it).

### 18.2 Item 2 — a row without an id is refused, pinned

- **Fixture** — `ChildRowReadWithoutIdFor`: a read of the named cascade child returns its row WITHOUT its id column (the
  owner columns still present). `Reset` clears it.
- **Test** — `Provisioning_WhenTheCascadedRowsAreReadIncompletely_RefusesBeforeAnyWrite` gains `idless-row` (now 3 cases):
  one location with a real owner, read without its id → `cascade_children_unreadable`, `childTable:
  sharepointdocumentlocation`, `cascadeChildState: refused`, nothing written. Under V18 the snapshot records the child
  under `Guid.Empty`, provisioning succeeds (200), and a later restore's by-id read of `Guid.Empty` would find no row →
  `Gone` → counted back; the case fails on the 200. A second seed, R6 (skip the id-less row instead), fails it the same
  way.
- §16.2 and §16.7 claimed the rule; both now say it is pinned. Unreachable in practice (Dataverse returns the selected id
  column), so this is coverage, not a defect.

### 18.3 Item 3 — no snapshot where nothing cascades, pinned

New theory `Provisioning_WhenNoOwnerMoveCascades_ReadsNoCascadedRows`:

| Case | Shape | Asserted |
|---|---|---|
| `workassignment`, not a resume | moved to the team; its Assign cascades to no table (live metadata §16.1) | 200, `resumed: false`, owned by the team, **no** `sharepointdocumentlocations` / `sharepointdocuments` query |
| `project`, resume | already owned by the team, no container: no owner move | 200, `resumed: true`, no such query |
| `matter`, resume | the same | the same |

*Beyond the closed set — justification:* it pins §16.3's "Resume; work assignment" row, which V17 survived. V17 is not
fail-open (it adds a read and refuses more), so this is a contract pin. R1 (a resume that snapshots, result ignored) fails
the two resume cases; V17 fails the work-assignment case.

### 18.4 Item 4 — 401 / 403 on the snapshot read is `Refused`

**Decision: classify, not just record.** `DataverseWebApiClient` renews its token five minutes before expiry
(`DataverseWebApiClient.cs:124-144`), so a 401 is the service's credential or application user being refused, and a 403
is its Read privilege on the table missing (e.g. a production BFF application user without Read on
`sharepointdocumentlocation`). Both repeat on every call until an administrator acts. As `Unreadable` they told the
caller "the same caller may retry once Dataverse is reachable", and the client offered "Try securing again", which
failed every time. Both outcomes were fail-closed (nothing written); only the recovery was wrong.

- **Production** — `AssignCascadeChildOwners.FailureOf`: `HttpRequestException { StatusCode: BadRequest or Unauthorized or
  Forbidden }` → `Refused`; everything else (5xx, 429, timeouts, non-HTTP faults) stays `Unreadable`. Member docs on
  `CascadeReadFailure` updated. Task 148 inherits the classification through the primitive.
- **Endpoint text** — the `refused` detail of `cascade_children_unreadable` now says Dataverse refused "the read of those
  rows, or the service's permission to read them", and the administrator looks at the `childTable` rows "and the
  service's Read privilege on that table" first. The reason-code doc says the same.
- **Client** — unchanged: `cascadeChildState: refused` already maps to the non-retryable "an administrator needs to look
  at those records first" copy (round c1, CL1). *(Round c1-r3, §19.1: the copy now also names the service's permission
  to read them — the verifier's item 10.)*
- **Guide §7a** — the `cascade_children_unreadable` row lists the 401/403 cause and the privilege check, and "a row came
  back without an id or an owner".
- **Test** — `Provisioning_WhenTheCascadedRowsCannotBeRead_RefusesBeforeAnyWrite` gains 401 and 403 → `refused` (now 5
  cases); for every `refused` case the detail must say "Calling again repeats this refusal", name the "Read privilege" and
  not contain "retry".

**Observation, not changed (outside item 4's primitive) — for the main session:** the endpoint's OTHER read refusals keep
the old shape. `container_ownership_unreadable` and `resume_creator_unavailable` with `creatorState: unreadable` treat
every failure but a 400 (the column-missing case, `IsColumnMissing`) as the same caller's retry, so a 401/403 there also
offers a retry that fails every time. Fail closed (nothing written) in every case. Changing them is a classification
change on reason codes the client already reads, so it is left to a decision rather than folded in here.
*(Round c1-r4: DECIDED by owner round 14 item 3 — classify 401/403 as Refused — and implemented, §20.7.)*

### 18.5 Item 13 — gate (e) is covered (owner round 13 item 2)

Owner round 13 item 2 (2026-10-03, BINDING): gate (e) — replaying the provisioning compensation on a throwaway TEST project
— is covered by round 11's approval of the batch-4 live steps. That answers the question carried since verifier round 1
item 9 ("whether round 11 covers REPLAY as the (e) method"). Recorded in the POML status-note, in §16.8, and in the
`task-133-live-gate.ps1` header (the "owner must accept replay … explicitly" caveat now cites the acceptance; comment
only). **It stays a main-session live step**, run with the rest of (a)–(j) at integration; nothing was run here.

### 18.6 Perturbation sweep (round c1-r2)

Script: scratchpad `task133/perturb_c1r2.py` — each seed applied to its source file, the test project built, the whole
`Sprk.Bff.Api.Tests.DataMutation.ExternalAccess` set (211 tests) run, the original bytes restored and the file touched;
an MD5 check of both source files after the sweep.

| # | Seeded violation | Result (211 tests) | Fails |
|---|---|---|---|
| **V3** | `NotApplied` counted `IsBack` (the verifier's fail-open survivor) | **BITES (1)** | `…NamesItAsNotRestored(NotApplied)` |
| **V17** | `TablesFor("sprk_workassignment")` returns the project/matter tables | **BITES (1)** | `…ReadsNoCascadedRows(workassignment)` |
| **V18** | the snapshot's id check dropped; an id-less row recorded under `Guid.Empty` | **BITES (1)** | `…ReadIncompletely…(idless-row)` |
| R1 | a resume snapshots the cascaded rows (result ignored) | BITES (2) | `…ReadsNoCascadedRows(project resume, matter resume)` |
| R2 | 401/403 read as transient (round c1's 400-only rule) | BITES (2) | `…CannotBeRead…(Unauthorized, Forbidden)` |
| R3 | 401 read as transient (403 still refused) | BITES (1) | `…CannotBeRead…(Unauthorized)` |
| R4 | 403 read as transient (401 still refused) | BITES (1) | `…CannotBeRead…(Forbidden)` |
| R5 | an accepted-but-not-applied child bind reported `Refused` (not `NotApplied`) | BITES (1) | `…NamesItAsNotRestored(NotApplied)` |
| R6 | an id-less row silently skipped (snapshot taken without it) | BITES (1) | `…ReadIncompletely…(idless-row)` |

**9/9 bite**, each failing exactly the case(s) it targets and no other test. V3, V17 and V18 survived all 204 tests at
`fde7441f7` (the verifier's run); each now fails one new case. R5 is written `patchRefused || Environment.TickCount64 !=
0 ? …` so `patchRefused` stays used under warnings-as-errors. MD5 of both source files after the sweep: identical to before
(`restored original bytes: OK`); the test project rebuilt and the class re-ran 21/21 on the restored sources. With round
c1's 20 and round c1-r1's 4, the sweep is **33/33 bite**.

### 18.7 Placement and component justification (CLAUDE.md §10 / §11)

**No new production surface**: no endpoint, service, registration, interface, option, job, package, column or reason
code. The production change is one predicate (`FailureOf`) plus doc and response text. New test surface only:

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| Fixture switch `IgnoreChildOwnerBindFor` | `OwnershipPatchIsApplied` (the ROOT's accepted-not-applied bind), `FailChildOwnerBindFor` (a child PATCH that throws) | Extends the fixture's child switch set in the existing shape; neither existing switch models a child bind accepted and ignored | V3 stays unseen: a child whose restore did not land could be reported restored, and the caller told to retry — the next run snapshots the wrong owner |
| Fixture switch `ChildRowReadWithoutIdFor` | the ownerless-row case seeds `Guid.Empty` as the OWNER; nothing omits the id column | Extends the fixture | V18 stays unseen: an id-less row would be recorded under `Guid.Empty` and later count as `Gone` (back) |
| Theory `Provisioning_WhenNoOwnerMoveCascades_ReadsNoCascadedRows` | `Provisioning_WithNoDocumentLocation_NeverReadsSharePointDocuments` (project/matter forward move only) | A separate theory: different roots and the resume path | §16.3's no-snapshot claim stays unpinned (V17, R1 survive) |

Publish size: not measured (main session, by instruction). No package changed → no CVE delta.

### 18.8 Quality gates (FULL — a `.cs` change; and TEST-MODIFYING, so code-review + adr-check run)

**code-review** (all severities, on the diff):

| Finding | Severity | Disposition |
|---|---|---|
| A 401/403 classified deterministic from the status alone | Suggestion | Same basis as the 400 (the round c1 `IsColumnMissing` precedent): `DataverseWebApiClient` surfaces only the status. A 401 after a token renewed five minutes before expiry has no transient cause the next call fixes; mis-classifying a rare transient 401 as `refused` sends an administrator to look — fail closed, recoverable |
| The endpoint's other reads keep the 400-only rule | Observation | Recorded (§18.4) for the main session; outside item 4 |
| `IgnoreChildOwnerBindFor` returns before the applied-bind set is updated | — | Intended: nothing was applied, so `FailChildOwnerReadBackAfterBindFor` cannot fire on it |
| The no-cascade theory asserts on the query log | — | The claim IS about reads (§16.3); the existing `NeverReadsSharePointDocuments` test uses the same log; each case also asserts the outcome (200, `resumed`, owner) |
| AI-smell scan: no new interface, no swallowed failure, no catch-log-rethrow, no null-check on a non-nullable | — | Clean |

**adr-check**: ADR-003 (an accepted-not-applied child restore is a failure, never "restored" — now pinned; an id-less row
refuses the snapshot — now pinned; a deterministic read refusal is no longer offered as a retry); ADR-019 (ProblemDetails
+ stable `reasonCode`; no new code or extension, only the `refused` text); ADR-038 (KEEP path
`tests/integration/data-mutation/**`; no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests; the fixture
fails in `DataverseWebApiClient`'s real exception shape); ADR-002 / 010 / 052 untouched. **Compliant, 0 violations.**
§6.5: none.

### 18.9 Runs (round c1-r2)

Affected first: `ProvisionAssignCascadeChildOwnerTests` **21/21** (c1-r1's 14 + 7: `NotApplied`, `idless-row`, 401,
403, and the three no-cascade cases); the verifier's provisioning class set (as §16.11) **272/272** (265 + 7);
`DataMutation.ExternalAccess` **211/211** (204 + 7). `dotnet build` of the test project 0 warnings / 0 errors; `dotnet
format whitespace --verify-no-changes` clean on the four changed C# files, and the pre-commit hook's own `dotnet format
--verbosity quiet --include …` leaves them byte-identical (MD5 checked).

Then once, on the final code:

- **Full BFF unit suite: 14,348 passed / 0 failed / 54 skipped (14,402)**, 26 m 22 s — **green in one clean run**, which
  closes the second half of the verifier's item 11 (round c1-r1 had shown it green only per class under contention).
- NetArchTest **346/346**.
- **Hard gate**, both in full on this branch: `Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests`
  **403 passed / 0 failed / 25 skipped (428)**.

No client file changed (no jest / prod build needed: `cascadeChildState: refused` was already non-retryable). No package
changed (no CVE delta). Publish size: not measured (main session).

### 18.10 PR description — addition to §17.8

> **Round c1-r2 (verifier on c1-r1; owner round 13).** Pinned the last unpinned child-restore failure — a child PATCH
> accepted but not applied (`NotApplied`) is named, never restored — plus the snapshot's id-less-row refusal and the
> no-snapshot rule for resumes and work assignments (seeds V3, V17, V18, R1 bite). A 401/403 on the snapshot read is now
> `refused` (an administrator checks the BFF's Read privilege), not a retry that fails every time. Recorded: owner round
> 13 item 2 (gate (e) replay covered by round 11) and item 1 (unsecure re-owns location rows by the task-146 rule — for
> task 148).

## 19. Round c1-r3 (2026-10-03, branch `task/uac-r2-133-c1-r3`) — the verifier's findings on round c1-r2

Base: `task/uac-r2-133-c1-r2` @ **`02710b6bb`** (baseSha). **Branch name:** the round was instructed as `git switch -c
task/uac-r2-133-c1-r1 task/uac-r2-133-c1-r2`. That name already exists — it is round c1-r1 (`fde7441f7`, an ancestor of
c1-r2, checked out in another worktree) — so `git switch -c` refused it ("a branch named … already exists"), and
re-pointing it would rewrite an existing round's branch. This round therefore runs on **`task/uac-r2-133-c1-r3`**, branched
from c1-r2 exactly as instructed; c1-r1 and c1-r2 are untouched. Binding sources re-read from
`work/unified-access-control-r2` (owner rounds 1–13 and the #1081 peer report). No escalation trigger fired: the one change
is client copy inside owner round 10 item 4's already-decided `refused` state.

| Item | Verifier finding | Disposition |
|---|---|---|
| 1 | No executor code fix remains; the publish-size half of the build criterion is unmeasured, so the verdict is needs-fixes; the fix is a main-session measurement before merge | **NOT closed — main session, by instruction** ("skip publish-size measurement"). The exact procedure is in §19.4. No package changed in any round of this task's c1 series (no CVE delta) |
| 2 | Item 1/10 (`NotApplied`) closed — seeds A1–A3 bite; `IgnoreChildOwnerBindFor` is a genuine model, not tautological | Verified by the verifier; no change |
| 3 | Item 2 (id-less row) closed — A4, A5 bite | Verified; no change |
| 4 | Item 3 (no snapshot where nothing cascades) closed — A10, A11 bite | Verified; no change |
| 5 | Item 4 (401/403 → `Refused`) closed — A6–A9 bite; the client needs no change for retryability | Verified; no change. Re-checked: `DataverseWebApiClient.GetAccessTokenAsync` (`:121-149`) renews the token 5 minutes before expiry (`:124`, `:138`); `cascadeChildState: refused` maps to the non-retryable `CASCADE_CHILDREN_REFUSED` (its swap in `classifyProvisioningFailure`) |
| 6 | Regression seed A12 (snapshot failure ignored) fails 9 tests | Verified; no change |
| 7 | The verifier's own runs on `02710b6bb` | Recorded; this round's runs are §19.3 |
| 8 | Hygiene (merge-tree clean, 8 files, no TASK-INDEX / current-task / `.claude/**`, POML parses, 7 phrases 0 hits, script comment-only, no live write) | Re-checked for this round (§19.3) |
| 9 | Owner decisions respected (round 10 item 4, round 13 items 1–2, round 4 item 3) | Verified; this round's change touches none of them |
| 10 | MINOR (copy precision): for a 401/403 the client's `CASCADE_CHILDREN_REFUSED` copy does not mention the service's Read privilege, which the server detail and guide §7a name | **Closed** (§19.1) |
| 11 | Criterion "BFF build green … publish size … ≤60 MB; no new HIGH CVE": publish size unmeasured | **Publish size NOT closed — main session** (as item 1). The rest of the criterion is met on this branch (§19.3); no package changed |
| 12 | Criterion "MANUAL LIVE GATE (a)–(f)" plus (g)–(j) and the `sprk_createdbyperson` dry run / -Apply / -Verify | **NOT closed — main session at integration** (owner round 11; (e) by replay covered by owner round 13 item 2). In dev (e) proves only the no-children path |
| 13 | Step 9 (TASK-INDEX.md status cell → done) | **NOT closed — main session** (the executor may not edit TASK-INDEX.md) |

### 19.1 Item 10 — the `refused` copy names the service's permission to read the records

| | Copy (`CASCADE_CHILDREN_REFUSED.errorMessage`) |
|---|---|
| Before (round c1) | "Securing the project did not start, because the records linked to it that move together with it could not be read. Nothing about the project changed; an administrator needs to look at those records first." |
| After (round c1-r3) | "… Nothing about the project changed; an administrator needs to look at those records, **and the service's permission to read them**, first." |

- **Why both checks, not a 401/403-only message.** The client receives only `cascadeChildState: refused`; the server folds a
  400, a 401, a 403, a full page and a row without an id or owner into it, and its `refused` detail names BOTH checks for
  every one of them ("an administrator looks at the record's {table} rows, and the service's Read privilege on that table,
  first"), as guide §7a does. The copy therefore says what the administrator looks at — true for every cause — and does
  not claim which cause happened. It says "the service" as the server detail does; no code, table name or HTTP status
  reaches the user (task 068's copy rules). Still non-retryable, still no "try again" (the existing never-advise-retry
  test covers it).
- **Doc comments**: `CASCADE_CHILDREN_REFUSED` now states what `refused` covers since c1-r2 (a 400, or a 401/403 refusing
  the service's sign-in or Read privilege, or an incomplete answer) and why the copy names both checks;
  `IProvisioningFailureExtensions.cascadeChildState` names the 401/403 case.
- **Test**: `provisioningService.test.ts` — `reads cascadeChildState=%s from the problem body for
  cascade_children_unreadable`: the `refused` case now also asserts `/the service's permission to read them/i`; its comment
  says why. No new test (the suite count is unchanged at 108).
- **Seed CL5** (round c1's copy put back): **BITES** — exactly `…cascadeChildState=refused…` fails (107 passed / 1 failed
  of 108 across the CreateProjectWizard + SummarizeFilesWizard suites). Restored from a byte copy: MD5 `11a2a3d8…`
  identical to before the seed, file touched. With rounds c1 (20), c1-r1 (4) and c1-r2 (9) the sweep is **34/34 bite**.
- Line anchors quoted by earlier rounds and by the verifier for `provisioningService.ts` (`:425-430`, `:499-500`) move by
  +3 lines (the longer doc comment); the code they point at is unchanged.

### 19.2 Placement and component justification (CLAUDE.md §10 / §11)

No new surface of any kind: no endpoint, service, registration, interface, option, job, package, column, reason code,
ProblemDetails extension, component or copy constant — one existing constant's text and two doc comments changed. **No C#
changed this round** (the BFF code is round c1-r2's, byte-identical). Publish size: not measured (main session, §19.4).
No package changed → no CVE delta (`npm install` was run only to test; its incidental `package.json` / lock-file and
`.husky/_` edits were reverted before commit).

### 19.3 Quality gates and runs (round c1-r3)

Rigor: FULL inherited (a `.ts` change on the task) and TEST-MODIFYING (tag `testing`; a test file changed), so code-review
and adr-check run.

**code-review** (all severities, on the diff):

| Finding | Severity | Disposition |
|---|---|---|
| The copy names the service's permission for every `refused` cause, not only a 401/403 | Observation | Intended: the client cannot tell the causes apart, and the server detail / guide §7a send the administrator to both checks for every `refused` case. Phrased as what to look at, not as the cause — not false for any cause |
| Earlier rounds' line anchors in `provisioningService.ts` shift by +3 | Observation | History not rewritten; noted in §19.1 |
| AI-smell scan: no new export, no swallowed failure, no copy that advises a retry, no code in user-facing copy | — | Clean |

**adr-check**: ADR-003 (the `refused` state stays non-retryable and fail closed; no classification changed); ADR-019 (no
server change; the client still keys on `reasonCode` + `cascadeChildState`); ADR-038 (jest under the package's
`__tests__`, no banned shapes; the assertion pins user-facing truth, as the surrounding FR-31 tests do). **Compliant, 0
violations.** §6.5: none.

**Runs** (on this branch; the C# under test is byte-identical to `02710b6bb`):

- Affected first — `Spaarke.UI.Components` jest, CreateProjectWizard + SummarizeFilesWizard: **108/108** (8 suites), on
  the final source (before the seed, and again after the MD5-identical restore). Prettier 3.8.1 (repo `.prettierrc.json`)
  and eslint clean on the two changed TS files; the package build (`npm run build` = `tsc`) clean. (`Spaarke.SdapClient`
  and `Spaarke.Auth` were built locally first — the package's jest resolves `@spaarke/sdap-client` from their `dist`.)
- Then once, by instruction (no C# changed this round): `dotnet build` of `tests/unit/Sprk.Bff.Api.Tests` **0 warnings /
  0 errors**; **full BFF unit suite 14,348 passed / 0 failed / 54 skipped (14,402)**, 31 m 33 s, one clean run; NetArchTest
  **346/346**; **hard gate**, both in full: `Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests` **403
  passed / 0 failed / 25 skipped (428)**.

**Hygiene**: no edit to `TASK-INDEX.md`, `current-task.md` or `.claude/**`; no live Dataverse / Azure / Entra call of any
kind this round; the 7 stale phrases of the criterion return 0 hits in `src`; the POML parses (minidom); the `npm install`
side effects (`package.json` gained a stray `"spaarke": "file:../../../.."`, the lock file, five `.husky/_` files, a
`node_modules/spaarke` link to the worktree root) were reverted / removed before commit, so the commit holds only the four
files of this round (the client, its test, this note, the POML).

### 19.4 Main session — the publish-size measurement (items 1 / 11)

Per CLAUDE.md §10 (hazards three and four): a fresh worktree of `origin/master` and a fresh worktree of this branch's head,
each at a SHORT path (e.g. `C:\wt133m`, `C:\wt133b` — never this agent worktree or a scratchpad path); `dotnet publish -c
Release` of `src/server/api/Sprk.Bff.Api` into each side's `deploy/api-publish`; `Compress-Archive -CompressionLevel
Optimal` over `deploy/api-publish/*` on both sides (the `scripts/Deploy-BffApi.ps1` method); confirm equal file counts;
report both absolute sizes, the delta and the zip tool; ≤60 MB. No package changed, so there is no CVE delta to check.

*Round c1-r4 (§20.3):* the verifier of round c1-r3 MEASURED it by this procedure (against the work-branch merge base,
not `origin/master`): **45.66 → 45.69 MB, +0.04 MB, 212 = 212 files**. Recorded there, with the PR text.

### 19.5 PR description — addition to §18.10

> **Round c1-r3 (verifier on c1-r2).** Client copy only: for `cascade_children_unreadable` with `cascadeChildState:
> refused`, the wizard now tells the user an administrator needs to look at the records "and the service's permission to
> read them" — the check the server's detail and guide §7a name since 401/403 became `refused` (seed CL5 bites). No C#
> changed. Publish size is the main session's measurement before merge.

## 20. Round c1-r4 (2026-10-03, branch `task/uac-r2-133-c1-r3.-r2`) — the verifier's findings on round c1-r3

Base: `task/uac-r2-133-c1-r3` @ **`3062d960f`** (baseSha). **Branch name:** instructed as `git switch -c
task/uac-r2-133-c1-r3.-r2 task/uac-r2-133-c1-r3.`. The start point with its trailing period is not a ref (`git rev-parse
--verify "task/uac-r2-133-c1-r3."` fails: a ref name cannot end in "."), so it was read as `task/uac-r2-133-c1-r3`; the new
branch carries the instructed name exactly (`task/uac-r2-133-c1-r3.-r2`, a valid ref). This note calls the round c1-r4.
Binding sources re-read from `work/unified-access-control-r2` (owner rounds 1–13 and the #1081 peer report), and again
mid-round at `0c007772c`, which adds **owner round 14** (commit `9d88da386`) and rounds 15–16.

The round has two parts:

- **The verifier's items** (§20.1–20.6). The one code change is test coverage for a rule owner round 10 item 4 already
  decides ("a child whose restore fails is named").
- **Owner round 14 item 3** (§20.7, BINDING). The main session relayed it mid-round in a worktree note, and it is
  corroborated by the work branch's decisions file. `container_ownership_unreadable` and `resume_creator_unavailable`
  (creatorState unreadable) now classify a 401/403 as REFUSED, as the cascade reads already do; 503/429 stay
  retryable. This part changes production C#, the client, guide §7a and the schema note.

No escalation trigger fired: both parts are answered by owner decisions.

| Item | Verifier finding | Disposition |
|---|---|---|
| 1 | Scope of c1-r3 (4 files, no C#); the `refused` copy non-retryable, no code / table / status, matches the server detail (`ProvisionProjectEndpoint.cs:1502-1505`) and guide §7a; doc comments match | Verified by the verifier; no change |
| 2 | Seed CL5 bites (59 passed / 1 failed of `provisioningService.test.ts`) | Verified; no change |
| 3 / 12 | LOW, fail-open in principle — "a child that disappears after its PATCH is `Refused` / `NotApplied`" is untested; seeds S13 and S16 (counted `Restored`) survive all 211 tests | **Closed** (§20.1): fixture `RemoveChildOnBindFor`; two cases on the not-restored theory; S13, S16 and three more seeds bite. §17.4's claim corrected |
| 4 | Seed sweep: 14 of 16 bite; S13, S16 survive | S13 and S16 closed by item 3; the 14 verified; no change |
| 5 / 11 | Publish size measured by the verifier but recorded nowhere in the note or PR | **Recorded** (§20.3, and the PR text in §20.6): the verifier's measurement, attributed. Not re-measured here (instructed to skip) |
| 6 / 7 | The verifier's runs: both integration suites, NetArchTest, jest, build green; full unit suite 7 contention failures that pass in isolation | Verified; this round's own runs are §20.5 |
| 8 | Hygiene (POML parses, merge-tree clean, no TASK-INDEX / current-task / `.claude/**`, 7 phrases 0 hits, 28 reason codes, script comment-only, no live write) | Re-checked for this round (§20.5) |
| 9 | Owner decisions not contradicted (round 10 items 4–5, round 4 item 3, round 13 items 1–2, round 11) | Verified; this round touches none of them |
| 10 | Branch-name deviation of c1-r3 acceptable | Verified; this round's own branch note is above |
| 13 | Manual live gate (a)–(f), (g)–(j) and the `sprk_createdbyperson` dry run / -Apply / -Verify | **NOT closed — main session at integration** (owner round 11; (e) by replay covered by owner round 13 item 2). In dev (e) proves only the no-children path |
| 14 | Step 9 (TASK-INDEX.md status cell → done) | **NOT closed — main session** (the executor may not edit TASK-INDEX.md) |
| R14.3 | **Owner round 14 item 3** (BINDING, 2026-10-03): the endpoint's other read refusals classify 401/403 as Refused; 503/429 stay retryable (the open question carried since c1-r2, note §18.4) | **Closed** (§20.7): one rule (`AssignCascadeChildOwners.IsRefusedRead`); `containerOwnershipState`, `creatorState: refused`; client copy; guide §7a; 15 server and 6 client tests; seeds K1–K10 and CL6–CL10 bite |

### 20.1 Items 3 / 12 — a child that is gone after its PATCH is pinned as a failure

The production code was already correct: `RestoreOneAsync` counts a child `Restored` only when its read-back finds the row
AND it reads as its snapshotted owner (`AssignCascadeChildOwners.cs:227`); a read-back that finds no row falls through to
`Refused` (the PATCH threw) or `NotApplied` (it did not). Only the read BEFORE the restore decides `Gone`. Nothing pinned
the after-PATCH half, and the fixture had no way to make a child's row stop reading after its bind. **No production code
changed for this item.**

- **Fixture** — `RemoveChildOnBindFor`: once an `ownerid` PATCH on the named cascade child has been sent (it is recorded
  first, as every PATCH is), the row is removed, so the restore's read-back finds NO row. Alone, the PATCH is accepted;
  with `FailChildOwnerBindFor` on the same child, the PATCH is also refused. The read before the PATCH still finds the row,
  so the child is never `Gone`. `Reset` clears it.
- **Test** — `Compensation_WhenAChildsRestoreCannotBeConfirmed_NamesItAsNotRestored` now takes `(shape, outcome)`, five
  cases: the three existing ones, renamed by shape (`read-fails-before-restore` → `Unreadable`, `read-back-fails` →
  `Unverified`, `bind-accepted-not-applied` → `NotApplied`; their switches and assertions unchanged), plus
  **`row-gone-after-accepted-bind` → `NotApplied`** and **`row-gone-after-refused-bind` → `Refused`**. For both new cases
  the shared assertions hold — `cascade_children_not_restored`, `ownershipRestored: true`, `childOwnersRestored: false`,
  exactly one named child with that `outcome` and the exact `nextCall`, the detail names it with no "retry", a CRITICAL line
  naming `sharepointdocumentlocation {id} ({outcome})` and the call, the other child put back — plus exactly one bind to the
  child with its own owner, and the child no longer reading at all. An unknown shape throws (no silent no-op case).

**Seeds** (scratchpad `task133/perturb_c1r4.py` — each seed applied to `AssignCascadeChildOwners.cs`, the test project
built, the whole `Sprk.Bff.Api.Tests.DataMutation.ExternalAccess` set (213 tests) run, the original bytes restored and the
file touched; MD5 `7973cb0d…` before and after, `restored original bytes: OK`; rebuilt and re-run 213/213 afterwards):

| # | Seeded violation | Result (213 tests) | Fails |
|---|---|---|---|
| **S13** | `!after.Exists \|\| after.Owner == child.Owner` → `Restored` (the verifier's survivor) | **BITES (2)** | both `row-gone-*` cases |
| **S16** | only a REFUSED PATCH followed by no row → `Restored` (the verifier's narrower survivor) | **BITES (1)** | `row-gone-after-refused-bind` |
| D1 | only an ACCEPTED PATCH followed by no row → `Restored` (S16's mirror) | BITES (1) | `row-gone-after-accepted-bind` |
| D2 | a row gone after its PATCH reported `Gone` (counted back) | BITES (2) | both `row-gone-*` cases |
| D3 | a refused PATCH followed by no row reported `NotApplied` (not `Refused`) | BITES (1) | `row-gone-after-refused-bind` |

**5/5 bite**, each failing exactly the case(s) it targets and no other test — so without the two new cases every one of them
survives all 211 earlier tests, as the verifier found for S13 and S16. Under S13 the response becomes `creator_share_failed`
with `childOwnersRestored: true` and "The same caller may retry"; each new case fails on its `reasonCode`. With rounds c1
(20), c1-r1 (4), c1-r2 (9) and c1-r3 (1), the sweep is **39/39 bite**.

**§17.4 corrected** (an italic note under it): "no seed on it is fail-open" was true only for the read before the restore.

### 20.2 Observation, NOT changed — the restore's PATCH upserts a deleted row

Found while modelling item 3; outside the verifier's items, so recorded for the main session and task 148, not fixed.

- `RestoreOneAsync` puts a child back with `DataverseWebApiClient.UpdateAsync` (`DataverseWebApiClient.cs:204-214`), a
  PATCH with no `If-Match`. A Web API PATCH without `If-Match` **upserts**: if the id does not exist, Dataverse creates a
  row with it. The repo already records this and fixed it for the recalculate routes (task 130,
  `IFieldMappingDataverseService.cs:56-61`, `UpdateExistingRecordFieldsAsync` sends `If-Match: *`).
- **The window:** a child deleted BETWEEN the restore's read (which found it) and its PATCH. The PATCH may then re-create
  it as a row carrying only that id and the owner bind — no regarding, no name — if Dataverse accepts such a create for
  `sharepointdocumentlocation` (unproven; not observable without a live write). The read-back finds it on its own owner and
  reports `Restored`.
- **Severity: LOW.** It is not fail-open on access: nothing is left on the wrong owner, and the reported state is true of
  the row that now exists. What is wrong is a resurrected orphan row, after a race of milliseconds during a compensation
  that is itself a failure path.
- **The fix, for task 148 or a follow-up round:** send the child bind update-only (`If-Match: *`). A PATCH on a missing row
  is then refused (404), and the read-back finds no row, so the outcome is `Refused`. That is exactly the
  `row-gone-after-refused-bind` shape pinned in §20.1, so the test needs no change. It is not done here because it changes
  production code and the primitive's client contract (`DataverseWebApiClient.UpdateAsync` takes no header; the
  update-only method lives on `IFieldMappingDataverseService`), which the round's instruction excludes ("do not change
  anything else").
- §17.4's "patching a missing row fails closed as `Refused`" assumed update-only semantics; the italic correction under
  §17.4 says so.

### 20.3 Items 5 / 11 — publish size: the verifier's measurement, recorded

**Measured by the verifier of round c1-r3**, by the CLAUDE.md §10 procedure (hazards three and four), and recorded here
as reported. This executor did not re-measure (instructed to skip).

| Side | Commit | Fresh detached worktree (short path) | Zip (bytes) | Zip (MB) | Files |
|---|---|---|---|---|---|
| Base: merge base of `work/unified-access-control-r2` and this branch | `6b243f092` | `C:\wt133m` | 47,874,537 | **45.66** | 212 |
| Branch (round c1-r3 head) | `3062d960f` | `C:\wt133b` | 47,913,382 | **45.69** | 212 |
| **Delta** | | | **+38,845** | **+0.04** | equal |

- **Method:** `dotnet publish -c Release` of `src/server/api/Sprk.Bff.Api` on both sides, then PowerShell `Compress-Archive
  -CompressionLevel Optimal` over `deploy/api-publish/*` (the `scripts/Deploy-BffApi.ps1` method). PDBs are included, and
  the file counts are equal.
- **Thresholds:** ≤60 MB ceiling, yes. Under the +5 MB single-task escalation line and the 55 MB architecture-review line.
- **Baseline choice:** the base is NOT `origin/master`, because `origin/master` (`62277d50a`) is not an ancestor of the
  branch. The work-branch merge base isolates this task's own contribution. That is the purpose of §10's "fresh build of
  master" rule, which exists so that other projects' merges are not attributed to this task.
- **Main session:** if it wants §10's literal `origin/master` baseline, it re-measures at the work-branch → master merge.
- **For this head:** the verifier's numbers are for `3062d960f`. Round c1-r4's verifier part changes tests only, but its
  owner-round-14 part (§20.7) changes BFF C#: `ProvisionProjectEndpoint.cs` and `AssignCascadeChildOwners.cs` gain
  response strings and one private-to-internal predicate. There is no new type, file, registration or package, so any
  delta is expected to be bytes. It is **not re-measured here** (instructed to skip). The main session measures the final
  head at integration, by the §10 procedure.
- **CVE:** re-checked this round, `git diff --name-only 6b243f092 HEAD` lists no `*.csproj`, `Directory.Packages.props`,
  `package.json` or lock file across the whole task. No package changed, so there is no CVE delta.

### 20.4 Placement and component justification (CLAUDE.md §10 / §11)

No new endpoint, service, DI registration, interface, option, job, package, column, reason code or file. The verifier
part changes tests only. The owner-round-14 part (§20.7) adds small surface, justified here:

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| Fixture switch `RemoveChildOnBindFor` | `FailChildOwnerBindFor` (the PATCH throws; the row stays), `IgnoreChildOwnerBindFor` (accepted; the row stays on its owner), `FailChildOwnerReadBackAfterBindFor` (the read-back throws → `Unverified`) — none makes the read-back find NO row | Extends the fixture's child switch set in the same shape; the refused variant combines it with `FailChildOwnerBindFor` rather than adding a second switch | S13 / S16 stay unseen: a child gone after its PATCH could be counted restored, and the caller told "The same caller may retry" while that child's owner is unconfirmed |
| `AssignCascadeChildOwners.IsRefusedRead(Exception)` (internal) | The private `FailureOf` already held exactly this rule (400/401/403 → `Refused`); the endpoint's `IsColumnMissing` holds only the 400 | It IS the extension: `FailureOf`'s body, exposed so the endpoint reuses the one rule (`FailureOf` now calls it). No copy of the rule, no new type, no registration | Two copies of the 400/401/403 rule would drift. Owner round 14 asks for the other reads to classify "as the cascade reads already do" |
| ProblemDetails extension `containerOwnershipState` (`unreadable` / `refused`) on the existing `container_ownership_unreadable` | `cascadeChildState` on `cascade_children_unreadable` (the same split); the code itself had no state | Follows the existing extension pattern on an existing reason code; no new reason code (the client's 28-code inventory is unchanged) | The client cannot tell a retry from an administrator's job, so it keeps offering "Try securing again" for a 401/403 that fails every time — the defect owner round 14 orders fixed |
| `creatorState` value `refused` on `resume_creator_unavailable` | The extension exists, with `unreadable` / `column-missing` / `absent` / `disabled` / `application-user` | A new value on the existing extension | As above, for the resume's creator reads |
| Client constants `RESUME_CREATOR_REFUSED`, `CONTAINER_OWNERSHIP_REFUSED`; `IProvisioningFailureExtensions.containerOwnershipState` | `CASCADE_CHILDREN_REFUSED`, `RESUME_CREATOR_COLUMN_MISSING` (the same swap-in pattern) | Same pattern, same file | The wizard would render a retry that cannot succeed |
| Fixture switches `ContainerOwnershipReadFailsWith`, `SystemUserReadFailsWith` (HTTP status) | `ContainerOwnershipReadFails`, `SystemUserByIdReadSucceeds`, `SystemUserReadFailsFor`, all of which throw a non-HTTP fault; `CreatorPersonReadFailsWith` already carries a status (reused for the column read) | Status-carrying twins of the existing switches, in the real client's exception shape; the targeting switches are unchanged | No test could produce a 401/403 on those reads, so the classification would be unpinned (seeds K1–K10 would survive) |

### 20.5 Quality gates and runs (round c1-r4)

Rigor: **FULL** (the owner-round-14 part changes `.cs` and `.ts` production code), and TEST-MODIFYING (tests and the fixture
changed). Both mean code-review and adr-check run.

**code-review** (all severities, on the whole round's diff):

| Finding | Severity | Disposition |
|---|---|---|
| The restore's PATCH upserts a row deleted between its read and the PATCH | LOW (production, pre-existing) | Recorded (§20.2) for the main session / task 148; not changed (outside the items) |
| A **400** on the container check and on the resume's user reads is now `refused` too, not only the 401/403 owner round 14 names | Interpretation (owner-reversible) | Round 14 says "as the cascade reads already do", and the cascade rule is 400/401/403 (c1-r2). One rule in one place (`IsRefusedRead`) avoids a second, narrower copy. A 400 repeats on every call (a malformed query, or a missing column), so offering a retry is the same defect. The person-column read keeps its more specific `column-missing` for a 400, which is checked first. Reversing it is one predicate at two call sites |
| `resume_creator_unavailable` + `refused` maps to the `needs-administrator` kind; `container_ownership_unreadable` + `refused` maps to `not-started` | — | Each follows its own code's deterministic peer: `column-missing` (`needs-administrator`) and `cascade_children_unreadable` + `refused` (`not-started`). Neither is retryable |
| The theory's signature changed from `(outcome)` to `(shape, outcome)`, so the three existing cases' display names changed | Observation | Intended: two shapes now share an outcome (`NotApplied`), so the outcome alone no longer names the case; their switches and assertions are unchanged |
| In the fixture the row is removed before the refusal throws | — | Intended: it models a row deleted while the PATCH is in flight (or an update-only PATCH refused because the row is gone); the PATCH is still recorded first |
| `dotnet format` re-indented the new braced `case RecordedContainerKind.Unreadable:` block (+4); two lines are about 125 characters | Observation | The hook's own output, so kept as is. The seed sweep was re-run on the formatted code (§20.7) |
| AI-smell scan: no new interface, no swallowed failure, no catch-log-rethrow, no copy that advises a retry or names a code / table / status, no assertion on a mock's call count standing in for state | — | Clean |

**adr-check**:

- **ADR-003 (fail closed):** a child gone after its PATCH is a failure, never "restored" or "gone", now pinned. A refused
  read stays a refusal before any write, and only its recovery changes: it is no longer offered as a retry.
- **ADR-019:** ProblemDetails with a stable `reasonCode`. There is no new reason code: one new extension and one new
  extension value, mirrored in the client and guide §7a.
- **ADR-038:** the KEEP paths are `tests/integration/data-mutation/**` and the package's `__tests__`. No
  `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests. The fixture fails in `DataverseWebApiClient`'s real
  exception shape.
- **ADR-010:** no new interface or registration; `IsRefusedRead` is an internal static on an existing static helper.
- **ADR-002 / ADR-052:** untouched.

**Compliant, 0 violations.** §6.5: none.

**Runs:**

- **Verifier part, affected** (before round 14 arrived): `ProvisionAssignCascadeChildOwnerTests` **23/23** (21 + 2);
  `DataMutation.ExternalAccess` **213/213** (211 + 2), before its sweep and after the restore.
  - A full pass also ran on that code: NetArchTest 346/346, `Sprk.Bff.Api.IntegrationTests` 104/104,
    `Spe.Integration.Tests` 403 / 0 / 25 (428).
  - The full BFF unit suite read 14,250 / 100 / 54 (14,404), 1 h 3 m. CPU was at 92–100 % with about 270
    dotnet/testhost processes from other agents. 99 failures were `TaskCanceledException`; the other was a
    shared-counter tag cross-talk in `PinnedMemoryEndpointsContractTests`.
  - The 58 failing classes re-run in isolation gave 880 / 1 / 2 (883); the 1 was another timeout, and its class then
    passed alone, 21/21.
  - That pass is **superseded** by the final runs below, because round 14 then changed production code.
- **Round 14 part, affected:**
  - The three provisioning classes it touches: **78/78**.
  - `DataMutation.ExternalAccess`: **228/228** (213 + 15). Run before the seed sweeps and again after the restores.
  - `Spaarke.UI.Components` jest, CreateProjectWizard + SummarizeFilesWizard: **114/114** across 8 suites (108 + 6).
    Run before the client sweep and again after its restore.
  - Prettier 3.8.1 (repo `.prettierrc.json`) and eslint are clean on the three changed TS files. The package build
    (`npm run build` = `tsc`) is clean. `Spaarke.SdapClient` and `Spaarke.Auth` were built locally first; the
    `node_modules` and `dist` they produce are git-ignored, and `git status` showed no side effect.
  - `dotnet build` of the test project: **0 warnings / 0 errors**.
  - The pre-commit hook's own `dotnet format --verbosity quiet --include …` changed only the endpoint's indentation (above).
- **Then once, on the final code** (rebuilt after the last seed restore: BFF and every test project 0 errors; the BFF and
  unit-test project 0 warnings; `Spe.Integration.Tests` shows 5 pre-existing CA2024 warnings in the untouched
  `AnalysisEndpointsIntegrationTests.cs`):
  - **Full BFF unit suite: 14,365 passed / 0 failed / 54 skipped (14,419 = 14,402 + 2 + 15)**, 24 m 12 s, one clean run.
  - **NetArchTest: 346/346.**
  - **Hard gate, both in full on this branch:** `Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests`
    **403 passed / 0 failed / 25 skipped (428)**.
  - Client: jest **114/114** (above); package `tsc` build clean.

**Hygiene:**

- No edit to `TASK-INDEX.md`, `current-task.md` or `.claude/**`.
- No live Dataverse, Azure or Entra call of any kind this round.
- No package changed, so there is no CVE delta.
- The POML parses (minidom).
- The 7 stale phrases of the grep criterion return 0 hits in `src`.
- The main session's `NOTE-FROM-MAIN.md` (its own instruction: do not commit it) was deleted before the commit.
- The merge-tree against `work/unified-access-control-r2` is re-run on the commit and reported with the round.

### 20.6 PR description — addition to §19.5

> **Round c1-r4 (verifier on c1-r3).** Pinned the last unpinned restore rule. A cascaded child whose row no longer reads
> back after its owner bind is named in `cascade_children_not_restored` as `NotApplied` (bind accepted) or `Refused` (bind
> refused). It is never counted restored: seeds S13 and S16 were fail-open and survived every test, and now bite (a
> fixture switch and two theory cases; no production change for it). Recorded for task 148: the child bind's PATCH has
> no `If-Match`, so it can upsert a row deleted mid-restore (LOW; the fix is `If-Match: *`, which the new refused case
> already covers).
>
> **Owner round 14 item 3.** `container_ownership_unreadable` (new extension `containerOwnershipState`) and
> `resume_creator_unavailable` (new `creatorState: refused`) classify a read Dataverse REFUSES (401/403, and a 400, by
> the cascade reads' one rule, `AssignCascadeChildOwners.IsRefusedRead`) as an administrator's job, not the same
> caller's retry; 503/429 stay retryable. The wizard shows a non-retryable copy for each, naming the service's
> permission. Guide §7a updated. 15 server + 6 client tests; seeds K1–K10 and CL6–CL10 bite. No new reason code.
>
> **Publish size** (measured by the round c1-r3 verifier, CLAUDE.md §10 hazards three and four). Fresh detached worktrees
> at short paths; `dotnet publish -c Release`; PowerShell `Compress-Archive -CompressionLevel Optimal` over
> `deploy/api-publish/*`, PDBs included. Base is the work-branch merge base `6b243f092`: **45.66 MB** (47,874,537 bytes).
> Branch `3062d960f`: **45.69 MB** (47,913,382 bytes). **Delta +0.04 MB** (+38,845 bytes). File counts equal at 212 = 212;
> ≤60 MB. The final head (round 14's C# strings and one predicate) is measured by the main session at
> integration. No package changed in the whole task, so there is no new CVE.

### 20.7 Owner round 14 item 3 — a read Dataverse REFUSES is an administrator's job, on every provisioning read

**The decision** (BINDING, `session27-owner-decisions-and-research.md` round 14 item 3, work branch commit `9d88da386`):
"`container_ownership_unreadable` and `resume_creator_unavailable` (creatorState unreadable) classify **401/403 as
Refused** (an administrator must act), as the cascade reads already do; 503/429 stay retryable." It answers the
observation carried since c1-r2 (§18.4). The main session relayed it mid-round in a worktree note, `NOTE-FROM-MAIN.md`.
That note was not committed (its own instruction) and was checked against the work branch before acting. The note
asked for:

- the retry hint and reason classification updated;
- the client copy, where the client branches on it;
- tests (401 and 403 each Refused; 503/429 still retryable), with seeds;
- the decision recorded as owner round 14.

**One rule, in one place.** `AssignCascadeChildOwners.IsRefusedRead(Exception)` is the cascade reads' existing rule:
`HttpRequestException` with 400, 401 or 403. It was the body of the private `FailureOf`, which now calls it. The endpoint
reuses it; there is no second copy. It therefore also covers a **400** on these reads (see §20.5, an owner-reversible
interpretation of "as the cascade reads already do").

| Read | Before (c1-r3) | Now |
|---|---|---|
| Container check: `businessunits` / project / matter / work assignment by `sprk_containerid` | Any failure → `container_ownership_unreadable`, "the same caller may" (no state) | 400/401/403 → `containerOwnershipState: refused`; the detail says calling again repeats the refusal and an administrator checks the service's Read privilege on business units and on the three root tables. Anything else → `unreadable`, the same caller's retry (detail unchanged) |
| Resume: `createdby`'s systemuser read | Any failure → `creatorState: unreadable` | 400/401/403 → `refused`; else `unreadable`. It still stops the decision (no fall-through to the column) |
| Resume: the `sprk_createdbyperson` column read | 400 → `column-missing`; any other → `unreadable` | 400 → `column-missing` (unchanged, checked first); 401/403 → `refused`; else `unreadable` |
| Resume: the recorded person's systemuser read | Any failure → `unreadable` | 400/401/403 → `refused`; else `unreadable` |

A `refused` resume is HTTP 500, like `unreadable` and `column-missing` (an environment fault, not a state of the data).

- **Detail:** for the read that refused, "Dataverse refused the read, or the service's permission to read users / it".
- **Recovery:** "Calling again repeats this refusal until an administrator restores the service's Read privilege — on
  users (systemuser) and on this record's table — or its sign-in; then calling again repeats the check." It never says
  "the same caller may".
- **Unchanged:** nothing is written and nobody else is shared to (F8). The extensions are the same ones
  (`creatorState`, `creatorColumn`, `createdByState`, `creatorPersonState`).

**Client** (`provisioningService.ts`):

- `classifyProvisioningFailure` swaps in two deterministic states, as it already does for `cascadeChildState: refused`:
  - **`creatorState: refused` → `RESUME_CREATOR_REFUSED`:** `needs-administrator`, not retryable. "Securing the
    project could not be finished, because the person who created it could not be looked up. Nothing about the project
    changed; an administrator needs to check the service's permission to look them up first."
  - **`containerOwnershipState: refused` → `CONTAINER_OWNERSHIP_REFUSED`:** `not-started`, not retryable. "Securing the
    project did not start, because the document container already linked to it could not be checked. Nothing about the
    project changed; an administrator needs to look at the service's permission to check it first."
- `provisionSecureProject` reads `containerOwnershipState` from the problem body.
- `IProvisioningFailureExtensions` gains `containerOwnershipState`.
- Both copies follow task 068's rules: no code, table name or HTTP status, and no "try again".
- `unreadable` or an absent extension keeps the existing retryable copy, so a host on an older BFF sees no change.
- No new reason code: the client's 28-code inventory test is unchanged.

**Docs.** Guide §7a gives `container_ownership_unreadable` the `containerOwnershipState` split, and
`resume_creator_unavailable` gains `refused` (HTTP 500), with its recovery. `created-by-person-schema.md` ("Who reads
it") names `refused`.

**Tests — server** (`DataMutation.ExternalAccess`, 213 → 228):

- `ProvisionRecordedContainerTests.Provision_WhenTheContainerCheckIsRefusedOrFails_ClassifiesItByTheReadsStatus`, 5
  cases:
  - 401, 403 and 400 → `refused`: the detail says "Calling again repeats this refusal" and names the "Read privilege",
    never "the same caller may";
  - 503 and 429 → `unreadable`, "(the same caller may)", and the retry then succeeds.
  - Each case writes nothing.
- The existing non-HTTP-fault test now also asserts `containerOwnershipState: unreadable`.
- `ProvisionResumeCreatorPersonTests`:
  - `Resume_WhenTheColumnReadIsRefused_Refuses500Refused_NotARetry` (401, 403): `refused`, naming the column, and a
    second call is refused the same way.
  - `Resume_WhenCreatedBysReadFails_ClassifiesItByTheReadsStatus` (401, 403 → `refused`; 503, 429 → `unreadable`): no
    fall-through to the column; the transient cases' retry shares to `createdby`.
  - `Resume_WhenTheRecordedPersonsReadFails_ClassifiesItByTheReadsStatus` (the same four).
  - Every case asserts its recovery text and zero writes.
- The fixture gains `ContainerOwnershipReadFailsWith` and `SystemUserReadFailsWith`, both in the real client's
  `HttpRequestException` shape. `CreatorPersonReadFailsWith` already carried a status.

**Tests — client** (`Spaarke.UI.Components`, 108 → 114):

- `provisioningService.test.ts` gains `creatorState=refused` (not retryable, the permission copy, no retry advice) and a
  `containerOwnershipState` theory (`unreadable`, absent and `refused`).
- `CreateProjectWizard.provisioningRetry.test.tsx` renders both refused states and asserts no "Try securing again"
  button and no retry advice.

**Seeds — server** (scratchpad `task133/perturb_c1r4b.py`). Each seed was applied, the test project built, the whole
`DataMutation.ExternalAccess` set (228) run, and the original bytes restored and touched. The MD5 of both source files
matched before and after. The sweep was **run twice**: on the pre-format code and again on the final, `dotnet format`ted
code. Both runs gave the same result.

| # | Seeded violation | Result (228) | Fails |
|---|---|---|---|
| **K1** | container check: only a 400 refused, 401/403 transient | **BITES (2)** | container 401, 403 |
| **K2** | container check: never refused (the pre-round-14 behaviour) | **BITES (3)** | container 400, 401, 403 |
| **K3** | resume: no creator read ever refused (the pre-round-14 behaviour) | **BITES (6)** | every resume 401/403 case |
| K4 | `createdby`'s refused read reported `unreadable` | BITES (2) | `createdby` 401, 403 |
| K5 | the recorded person's refused read reported `unreadable` | BITES (2) | person 401, 403 |
| K6 | the column's refused read reported `unreadable` | BITES (2) | column 401, 403 |
| **K7** | over-broad: every HTTP status refused (503/429 no longer retryable) | **BITES (11)** | every 503/429/500 case, cascade's included |
| K8 | a refused resume answered 409, not 500 | BITES (6) | every resume 401/403 case |
| K9 | the refused resume recovery offers "(the same caller may)" | BITES (6) | every resume 401/403 case |
| K10 | the refused container detail offers "the same caller may" | BITES (3) | container 400, 401, 403 |

**Seeds — client** (scratchpad `task133/perturb_c1r4_client.py`). Each seed was applied to `provisioningService.ts`, the
8 suites (114) run, and the original bytes restored and touched. The MD5 matched before and after.

| # | Seeded violation | Result (114) | Fails |
|---|---|---|---|
| CL6 | `creatorState: refused` offered as a retry | BITES (2) | the classifier case + the rendered no-retry case |
| CL7 | `containerOwnershipState: refused` offered as a retry | BITES (2) | the same pair for the container |
| CL8 | `containerOwnershipState` never read from the problem body | BITES (1) | the fetch-level `refused` case |
| CL9 | `creatorState: refused` not classified (the code's default copy) | BITES (2) | classifier + rendered |
| CL10 | `containerOwnershipState: refused` not classified (the transient, retryable copy) | BITES (2) | classifier + rendered |

**15/15 bite.** The sweep across task 133's c1 series is now **54/54**:

| Round | Seeds |
|---|---|
| c1 | 20 |
| c1-r1 | 4 |
| c1-r2 | 9 |
| c1-r3 | 1 |
| c1-r4 (verifier part) | 5 |
| c1-r4 (round 14 part) | 15 |

**Not changed (outside round 14's named scope):** `creator_unresolved`. The caller's systemuser id comes from a resolver
that returns no HTTP status (`CallerSystemUserIdResolves`), so there is nothing to classify at the endpoint. It stays a
retryable refusal before any write.
