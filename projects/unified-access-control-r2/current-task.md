# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.
> - **NOTE:** master is ahead of this work branch on project files. This copy is the session's state carrier. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-13 ~14:05Z (checkpoint #134, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-handoff-2026-11-09-198.md`, `private-handoff-2026-11-08.md`, `private-p1-2026-11-11.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). This session (`uac-r2`) is authoritative and reviews peers. Merges: `git merge-tree` first, then squash with `--match-head-commit` after Router AND Client lint are green; ONE merge watcher at a time. Live actions need the owner's go IN THIS SESSION. At most 2 agents at once (Opus), fresh short-path worktree each. |
| **Dev BFF** | **`0c06f464e3`** (deployed 2026-11-13 13:26Z on the owner's go "go BFF deploy 92d0ca09f7"; LOCAL merge of master `92d0ca09f7` + spike `d7369fecf1`, ledger 153, never pushed; worktree `C:\wtdep6`, keep until the next deploy; rollback snapshot of `9411f604bb` in `C:\wtdep6\deploy\rollback\wwwroot-20261113T132429Z`). Contains 197a (#2765), 197a-2 (#2783), 197b (#2760), fm #2768; NOT fm 052b/045d/045e. Pre-deploy: Release build clean, ArchTests 2544/0, no schema-script change since the live build, SecureChild -Verify PASS, settings gate OK (`DirectMessage:*` unset). Read-back: healthz/dataverse/ping 200, anon /api/me and /mcp 401. **30-min gate 13:26–13:56Z PASS:** 98 requests, 0 5xx, no external traffic. Exceptions are all pre-existing or benign: #2761 acme proof (6), #2142 Create_Task schema (2), invoice-indexing `sprk_document` fault (4; pre-existing since 01:28Z, evidence added to #2351), one aborted request (cancellations at 13:49:17), one email-to-task operation naming a nonexistent systemuser (data, 13:54). After-notices sent to all six peers; spa-r4 restarts E2E part 2; word-add-in OK (Outlook checks 3–7 to be repeated by the owner). `C:\wtdep5` removed. |
| **Running** | Nothing. #2765 (197a) and #2783 (197a-2) merged and now deployed. Stale-owner K filed as **#2781**. #2722 / #2728 close after a live check of the unfile path (owner browser check, not blocking). fm 045f (regarding-URL, owner-approved) part A diff is coming for review. |
| **Next action** | (1) Review fm 045f part A when it arrives. (2) **D-2 DirectMessage settings:** 054b -Verify PASS and DI-026 PASS on dev. Waiting on spa-r4's E2E to finish (they message), then the owner's go HERE. Then message fm the time and SHA for 054c. (3) **#2761 is NOT our code:** dev's identity-link job also walks the ACME stamp's org (spaarke-acme), where the dev BFF identity has no seat. provisioning-r1 owns that registry row and rejects option 1 (stamp isolation) and warns about option 2 (T237). Option 3 (the job walks only the environments its BFF serves) is recommended; it is waiting on the owner (leave open vs fix here). (4) Peer diffs: ebilling 084d-2a + fix1 PASS; 084d-2b PASS (K-a/K-b accepted, K-c is their call); #2782 (firm NFR-01) is theirs. fm 045e (#2772 refuse) not yet received. |

## Peer reviews done 2026-11-13 (all PASS)
- fm #2768 (022b/c) merged d8b53ec0c3. 052b PASS (contact owner = CustomerBusinessUnitId default team, after the secure decision; secure-by-ownership pins pass); merged on fm's branch b18d4fc394. 045d PASS (H1 option (a), all six tables; the order test is named). 054b dev -Apply/-Verify PASS. DI-026 PASS.
- ebilling 084c-r2 PASS; 084f role apply → SecureChild -Verify PASS; 084d design OK with conditions.
- Owner decided #2772 (H2) "Refuse it" in fm's session → fm 045e.
- Finding for the owner: dev team "Spaarke Demo" holds System Administrator (widens who can write the field-secured columns). No change made.

## 197a / 197b decisions (main session, 2026-11-12)
- **197a-1 (#2722):** unfiled non-Direct communication → creator's BU team, EXCEPT keep the former owner when leaving a Secure/Restricted/Secure-team parent, OR the message itself is Secure-unit-owned / `sprk_issecure` / Restricted, OR `sprk_createdbyperson` is missing (never fall back to `createdby`), OR any fact read fails. Facts are carried so the first decision AND `SettleOwnerAsync` agree; the regarding-only path settles too.
- **197a-2 (#2728):** premise wrong (the column is already in `DocumentLinkFields`); the root cause is the #2468 demotion in `RecordOwnershipResolver.ResolveOwnerOnlyAsync`. Option (a): skip the demotion only when the communication's owner is POSITIVELY the Direct team. K (file an issue): a document can keep a stale non-secure owner after a #2722 move.
- **197b (#2760):** option B with bound (i') = positive unfiled-Direct recognition (thread typed Direct, no typed regarding, not the master thread, stamped pair valid), any owner; grantee in pair; exactly NoRoot. Hidden wins; transient = no grant. The 191c engine keeps a pair member's share on exactly NoRoot (a recognition fault carries the row). GateDecisionRecordSharing uses the stored flag only; one unit-name source (`ExternalAccess:ExternalTeamShare:BusinessUnitName` retired; not set on dev, no slots).
- **#2764 (191i blocker):** the engine still revokes on a transient Undecided; nothing retries user-owned DMs. Decide before 191c writes go on; configure the Direct team (D-2) first.

## Issues filed 2026-11-12
- **#2761 (ours, task 141 code):** the identity store's binding-column readability proof (`DataverseContactIdentityStore.WebApiColumnReadPermissionSource`) faults on WhoAmI (403 0x80072560) every 5 min on dev since 2026-11-02; the SDK-source proof of the same columns succeeds. Fail closed: an unbound contact's first sign-in may be refused. spa-r4 warned (watch-item for the owner's E2E).
- **#2764:** see above.

## Peer state (all by message)
- **spa-r4:** SPA live on dev ~23:00Z (master 2ef4a17915, run 43324493710; rollback = re-run on b083d62183). Gate 23:00–23:30Z PASS, 0 external traffic. Re-read the external routes after the owner's E2E (they will tell me).
- **fm:** #2733 (054) merged, not on dev. Peer OK for the next deploy that includes it is conditional: `DirectMessage:*` stay unset until 054b `-Verify` exits 0 + DI-026 check; 054b `-Apply`/054c after the deploy on the owner's go in fm's session. **022b/022c** reviewed PASS (+ seed ArchTest `FieldMappingSeederAccessColumnTests`), merged on fm's work branch `74556c46df`; reaches master in fm's next PR. Ruling: access columns (`AccessColumnsNeverMapped`) are never mapping targets; K9 "Restricted-ness decided at write time".
- **mcp-r1:** spike still `d7369fecf1` (dev deploys = master + spike only; never anything from `work/spaarkeai-mcp-bff-api-r1`; ping before any dev deploy). 021 (A10 on /mcp) auth PASS with 3 test asks (GET/DELETE refused, app token without idtyp → ScopeMissing, v1 issuer); RFC 9728 → recommend amending FR-02 (owner's call in mcp-r1). #2763 (#2762 auth-failure log) PASS; merges on their owner's go.
- **ebilling:** 084g (EB-D-221) PASS with one K (concurrent checks on one HttpContext; ask for a comment). Deploys with their task 112 (BEFORE/AFTER notices; then I re-run SecureChild `-Verify`). Condition-2 invariant check runs after each import; they message only on FAIL.
- **ontology:** 179/181 are client-only (their task 185). **word-add-in:** pre-cleared 350ca3da66..2ef4a17915.

## 191 remaining
- **191i** (dev migration + live gate; each write an owner go): `Set-ExternalCollaboratorRole.ps1 -Apply` → `Set-RecordCreatorPersonSchema.ps1 -Apply` → access-marker dry run → `-BffOmitsMarkersFromCallerCreates -Apply` → `-Verify` → `CustomerBusinessUnitId` before 191e WritesEnabled → placement job report-only → 191c settings (after #2764 is decided) → 191g one-marker gate (then the 5-case probe if it fails; template private in scratchpad c191g\probe-paste.txt) → AC-3 gate. When `ExternalAccess:FlaggedOfficeEdit:Enabled` goes on, tell word-add-in-r1.
- **191j**: docs + issues (provisioning follow-up for the field-security scripts; main-session `.claude/constraints/auth.md` naming `AppOnlyExternalVisibility`).

## Open items for the owner
- TrackingFieldTrio 1.0.63 checks on dev (Email members on PRJ-000046, on a To Do, and as a user without Write+Share; the bell; To Do empty-state wording).
- UAT U-1..U-13; 193 PCF import on dev; D-2 steps 2–6; D-10; 186 follow-up; quality alerts #1821, #1906 (mention only). Office checks on the dev build.

## Scratch
- Worktrees: `C:\wtdep4` (keep), `C:\wt197a`, `C:\wt197b` (remove after #2760 merges), `C:\wt198s` (branch has no PR; leave).
- App Insights for the dev BFF: `spe-insights-dev-67e2xz` (rg `spe-infrastructure-westus2`). Query through `az rest` POST to `https://api.applicationinsights.io/v1/apps/<appId>/query` with a JSON body file (az.cmd mangles `>`/`<` in `--analytics-query`); `first`/`last` are reserved column names.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
