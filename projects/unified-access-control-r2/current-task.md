# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.
> - **NOTE:** master is ahead of this work branch on project files. This copy is the session's state carrier. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-13 ~01:00Z (checkpoint #129, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-handoff-2026-11-09-198.md`, `private-handoff-2026-11-08.md`, `private-p1-2026-11-11.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). This session (`uac-r2`) is authoritative and reviews peers. Merges: `git merge-tree` first, then squash with `--match-head-commit` after Router AND Client lint are green; ONE merge watcher at a time. Live actions need the owner's go IN THIS SESSION. At most 2 agents at once (Opus), fresh short-path worktree each. |
| **Dev BFF** | **`9411f604bb`** (deployed 2026-11-13 02:49Z on the owner's go "go BFF deploy e9208abc5e + spike d7369fecf1"; LOCAL merge of origin/master `e9208abc5e` + spike `d7369fecf1`, never pushed; worktree `C:\wtdep5`, keep until the next deploy; rollback snapshot of `4e64670ea0` in `C:\wtdep5\deploy\rollback\wwwroot-20261113T024655Z`). Contains fm 054 (`d878e78a90`), c53452bb0c, 350ca3da66. Does NOT contain #2760 (197b, merged 3366f7f532) or fm #2768. 30-min gate 02:49–03:19Z PASS: 95 requests, 0 5xx, external routes all 2xx. Pre-existing faults (since 11-03) got fresh evidence on #1638 and #2142. After-notices sent to all six peers. |
| **Running** | **PR #2765** (197a) at head `51ae98a20b`, ready. Round 90 decisions implemented: F1-3 a user-owned row keeps its owner; F1-2 content rows follow via the Direct step's move; F1-1 filed as **#2773**. Tests: 462/462 touched, ArchTests 2532. merge-tree vs master is clean. Owner columns checked: `ReadWholeRowAsync` uses ColumnSet(true). Next: merge watcher (`scratchpad\merge-watch-2765.ps1`). Agent `ac83f626f40c96eaa` done (its 3 temp dirs removed). |
| **Next action** | (1) When #2765 merges: remove `C:\wt197a` (Remove-WorktreeSafe), then launch **197a-2** (#2728, option a: skip the #2468 demotion only when the communication's owner is POSITIVELY the Direct team) as a fresh Opus implementer in a new short-path worktree, and file the stale-owner K issue (a document can keep a stale non-secure owner after a #2722 move). (2) **D-2 DirectMessage settings:** 054b -Verify PASS and DI-026 PASS on dev. Waiting on spa-r4's E2E to finish (they message), then the owner's go HERE. Then message fm the time and SHA for 054c. (3) Fix **#2761** (agent slot). (4) Pending peer diffs: ebilling 084d build (design OK'd with conditions: app-only by-id load, fault → 503, collision → asked); fm 045e (#2772 refuse, after 045d). |

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
