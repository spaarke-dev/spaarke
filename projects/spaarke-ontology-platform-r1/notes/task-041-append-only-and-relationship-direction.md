# Task 041: append-only by privilege, and the 1-to-N direction (notes)

Date 2026-10-14 · rigor FULL (declared) · model Sonnet 5.5 @ high · environment spaarkedev1 only · D-138 approval.
Evidence lives in `notes/security-roles.md` section 11. Replay: `task-041-live-test.sh`, `task-041-share-probe.sh`.

## Outcome

Every acceptance criterion passes. The escalation trigger ("a non-admin CAN update a Decision Record") did **not** fire:
Chelsea Friez (Spaarke roles only) was refused on update, delete and deactivate (HTTP 403 `0x80040220`, missing
`prvWritesprk_DecisionRecord` / `prvDeletesprk_DecisionRecord`), including on a row she owns and on a row shared to her
with Write+Delete. The relationship is 1:N with the FK `sprk_signal.sprk_decisionrecord`; the Decision Record has no
lookup to a Signal; three Signals resolved by one decision read back as three rows from the Signal side, as Chelsea.

## uac-r2 coordination (access-control rule)

Fetched `origin/work/unified-access-control-r2` = `b04b5b37f` (2026-10-14). Read:
- `config/secure-record-owner-role.json`, last commit `0615f97817` (task 039 entry for `sprk_decisionrecord`, Read only,
  states "no human role holds Write or Delete, so mirrored share rights are inert").
- `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildLineage.cs`, last commit `0615f97817` (`sprk_decisionrecord`
  keyed on matter/project/work assignment; `sprk_signal` includes the `sprk_decisionrecord` lookup).
- Present and unread beyond their listing: `scripts/Set-SecureRecordOwnerRolePrivileges.ps1`, `CoreAncestorResolver.cs`,
  `RecordOwnershipResolver.cs`.

Nothing in them grants Write or Delete on the table. This task tested their "inert" claim (section 11.4) and it holds.
No uac-r2 file was edited and no role or privilege was changed.

## Deviations from the POML

1. **Impersonation, not a Chelsea sign-in.** No Chelsea credential exists and none may be created, so the `az` operator
   token with `MSCRMCallerID` was used. Dataverse enforces the impersonated user's privileges (same method as tasks 006
   and 008). It proves the privilege model, not the Console UI path.
2. **Subgrid proven at the query level.** No `sprk_decisionrecord` form has a subgrid today (read-only Name/Owner forms,
   security-roles section 10.5), and adding one is form design the POML does not ask for. Criterion 3 was checked with
   the relationship navigation read and the equivalent FetchXML, both as Chelsea.
3. **TASK-INDEX.md and the POML `<status>` were not edited.** The caller owns them (instruction for this run). Both must
   be set to completed together, or `scripts/check-task-status-drift.ps1` fails the next push.
4. **No new automated test.** The task changes no behaviour. The only repo-side evidence is a read of the role XML
   (section 11.6). A role-XML source scan that fails on Write/Delete for `sprk_decisionrecord` would be a cheap guard
   (root CLAUDE.md section 16), but the POML does not ask for one; see "Observations".

## Known limits

- **K-a (by design)**: a Decision Record cannot be deactivated (Write is needed). Recorded in security-roles 11.3.
- **K-b (accepted, security-roles section 1)**: System Administrator and System Customizer hold every privilege
  (System Customizer includes `# github-actions-spe-infrastructure`, section 10.3). Detection is auditing, on for
  `sprk_decisionrecord`.
- **K-c**: the test cannot cover a future role that gains Write. 11.6 is a point-in-time union.
- **K-d**: `task-041-live-test.sh` needs Create on Decision Records for the test user; task 049 (D-62) removes it.

## Observations (not fixed; outside this task)

- Step 4b of the first script draft used a bound `SetState` that does not exist on the Web API (404). Removed; the
  deactivate case is covered by the `statecode` PATCH.
- The repo-side append-only invariant has no automated guard. If the owner wants one, it is a source-scan test over
  `src/dataverse/solutions/SpaarkeMaster/Roles/*.xml` (must-fire: a role with `prvWritesprk_DecisionRecord`;
  must-not-fire: today's roles) paired with the live role read, per ADR-038. Suggest filing it with the NFR-11 work; not
  built here.

## Cleanup

Zero `zz-041` rows remain in `sprk_signals` and `sprk_decisionrecords` (query 2026-10-14T17:10:10Z; security-roles 11.7).
