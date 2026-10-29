# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md` (round 136 is the latest: customer BU = top level for customer records; 135: Unsecure keeps all access).
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md`.
> - **Last deploy + gate:** 136568cf70 on dev 2026-10-29 02:23Z (R3; SpaarkeMaster 1.2.4.0 published from a4b6459da3); gate PASS (a, b, d), filed #2431 (Redis lease, pre-existing). Previous: e56a3a51d4.
> - **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-28 (checkpoint #72).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 wrap-up rules. Merge only when Router + "Client lint (type-aware ratchet)" are green; one merge job at a time; `--squash --match-head-commit`, no `--delete-branch`, after `git merge-tree`. Deploy via R3 (the owner's go is given in R3's session). A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. Public repo: no live exploit mechanism in issues, PRs, commits or committed notes (task 197: details chat only, neutral wording). |
| **Agents** | Opus for executors and verifiers; Sonnet high for scoped re-checks and mapping. Every model is stated. Helper scripts use unique prefixes in private temp folders (`uacmerge/uacg/`). Cap 4 concurrent. |
| **Peers** | R3 `spaarke-wt-spa-external-access-platform-08`, ebilling `spaarke-wt-ebilling-r1-5b`, word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4`, field-mapping `spaarke-wt-field-mapping-server-write-pa-be`, provisioning `spaarke-wt-customer-provisioning-orchest-2f`, ontology `spaarke-wt-spaarke-ontology-platform-r1-79`. |
| **Gate tooling** | App Insights `spe-insights-dev-67e2xz` (RG `spe-infrastructure-westus2`). BFF `spaarke-bff-dev.azurewebsites.net`. Script: temp `uacmerge/uacg/uacg_gate.sh`. |

## Next actions (mine)
1. **Task 194b (#2339):** C:/wt194, `fix/uac-194b-ownership-primary`, head **7f25e18ecc** (R4b-4 68ffc79cf0 + R4b-4b: dead check removed; the guard refuses any other write of `$DeclaredRoleFiles`; Phase 7a needs Secure Record first; guard 48/48, Pester 4/4 on 5.7.1 and 6.2.0). R4b-4c **c4fd7175cd** (ontology K2 guard forms, Pester pinned 5.7.1). Ontology re-check CLEAN. **Dev applied** (owner go 10-29): both teams live in BU1, -Verify PASS (restricted 16685363, config 2dae7e6e); field-mapping re-owned 57 rows (their owner go). Provisioning takes it as **H7c (#2429)**; point the guide's two 'not part of provisioning yet' notes at #2429. **Gate agent RUNNING** (master merge, build, ArchTests, filtered unit, Pester, publish delta).
   - Next: guide → #2429 → PR → merge → deploy via R3 → report-only run + ontology memo-LIKE proof → owner go for writes → close. Check the first CI run of the Pester job. Remove C:/wt194 (check junctions).
2. **Task 193 (#2338, one "Add person"):** C:/wt193, `fix/uac-193-add-person`, head c4ed79080b (G3e). Branch notes `notes/task-193-add-person-notes.md` supersede the project copy.
   - Security pass A findings F-1..F-4 (paired end never retried; system paths record Declined; SPE failure → untrue pairedGrantEnded; client abort leaves the grant) and the server part of pass B (F1 read failure wording, F5 would_lower counts lapsed rows, F3 ExpiryDate on ShareRecordWithUserRequest) → **G4a RUNNING**.
   - **G4b to launch** (client/docs): the client wording, the share note respecting veto/suppression, the client sending expiryDate, the guide's adoption text corrected (owner: a duplicate access → removing any part removes all; a manual revoke of Assigned-To access holds until re-added or the Assigned To is re-set), the K3 duplicate-name test, the would_lower wording.
   - Then: fix-diff re-check → PR (owner behaviour table) → merge → owner go for `Deploy-ExternalRecordAccessPairedSystemUser.ps1` → deploy via R3 → TrackingFieldTrio (1.0.60) import via Import-SolutionScoped.ps1 → gate. After merge, tell ontology (K-1 conflict with #2418). Owner Q (optional): turn R5 writes on.
3. **Task 197 (owner 10-28: fix now, no tracking):** C:/wt197, `fix/uac-197-stored-match-and-session-owner`. A+B 1e86c8b107, parent-session 5ce954ae8b, C **422767bc05** (stored provenance v2: ids and reason codes only).
   - D decisions sent: hide unknown/unreadable (fail closed); one shared batched viewer resolver in `Spaarke.Communication.Components` generalized from the CommunicationConnections PCF resolver; review state unchanged by hiding (test); no resolver → no candidate cards. D1 **650a53a4a0** (shared resolver `viewerRecordNames.ts`); **D2 RUNNING**, then D3–D4.
   - Then E (dev report-only data script to v2), F (search-trim guard + only `SerializeProvenance` writes `sprk_associationprovenance`; invoice search allow-listed), G (gates) → 2 security passes → PR (neutral) → merge → deploy via R3 → owner go for the dev clean-up -Apply.
4. **R3:** 136568cf70 deployed and gated PASS (night window: no Office/invite traffic; watch the next daytime window). 1.2.4.0 read back.
5. **Task 195 (#2352 (i)+(ii)):** after 194b merges. **Task 196:** 196a after 193 merges; 196b after 194b (input: restore the pre-secure author on Unsecure, `notes/task-196-unsecure-keeps-access.md`).
6. **#2382:** merged (bafe310ca4); dev roles live, -Verify PASS. Ask the owner to retry UAT check 5, then close. #2396 needs a role decision.
7. **#2292** (membership junction never populated): fix when there is capacity.

## Peer reviews owed (UAC is the gate)
- **ebilling RecordFamilyEdges proposal** (4beed693da): verdict SENT. Accepted 21 rows. The invoice ↔ work-assignment row is kept, and ebilling adds a write-time refusal plus a test that pins the container 409. submission.sprk_invoice and .sprk_reportdocument are baselined as outputs, and ebilling puts the submission's core lookups first. **UAC follow-up #2427:** an `OutputLookups` set in RecordOwnershipResolver, landing before `OrdinaryMovesWritesEnabled` is turned on (after 194b merges); send the sha to ebilling.
- **ebilling 094 round 5** (97c793670b): READY after the F3 withdraw-fault test + blank-key fix (queued their side); re-check that diff only. 098 round 5 READY. 095 OK. Standing: no Procedure-table role privilege without UAC review; D-185.
- **ontology #2418** (Assign Task email + bell): NOT READY (F-1..F-4, F1b); wait for the fix head after the owner answers ontology's policy questions; re-check the diff only. #2422 (D-198) APPROVED; #2414 APPROVED; #2409 merged. Filed #2423.
- **field-mapping:** §13 (#2399) accepted. After 194b merges: run RecordFamilyEdgesGuardTests and 037's pins; check the copied default lands before the ownership step in `OwnedChildWrite.CreateAsync`. 071 starts after 194b.
- **word-add-in task 130:** two security passes when the PR arrives.

## Owner items outstanding
1. Task 191: F1 and S4 block T191b onward. Task 190: Apply + the Restricted call on the 91 unmatched archives.
2. Decisions: #2343, #2245, #2250, #2260, K5/#2270 (via R3), #2262, #2396; the dev arrangement for root-homed users.
3. Optional live checks: D7 partner document read; #2177; #2161; T191 MDA/Copilot/grid.
4. Carried: #2163 environment go; 186 intake checks 3/5b/6; 036 gates (ii)–(vi); #2153 sign-off.

## Recently filed
#2423 (send log names the sender address); peers: #2410, #2411, #2413, #2415, #2424-#2426.
