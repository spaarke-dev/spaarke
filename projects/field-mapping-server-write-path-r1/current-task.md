# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-22, after PR #2943 (task 055) merged into the work branch. Desktop session `field-mapping-r1`.
> Previous version, with the full 045–055 history: `notes/handoff-history/current-task-2026-11-22-pre-056.md`
> (older: `current-task-2026-11-12-pre-022c.md`). State only here; decisions in `notes/decisions.md`, standing rules in
> the project `CLAUDE.md`.

## Quick Recovery

| Field | Value |
|---|---|
| **Session / worktree** | `field-mapping-r1`, `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`, on `work/field-mapping-server-write-path-r1`. |
| **Work branch** | At 46a0e1c236 (merge of #2943): 16 commits ahead of master, 31 behind. Not on master: task 055 and the docs commits after #2916. Next master PR carries 055 (needs the owner's go). |
| **Master** | #2916 merged at 02bb05ba94 (2026-11-20): 052e/f/g, the #2919 fix, the 055/056 scope. Everything up to 054 is on master. |
| **Dev BFF (last known)** | c8ab2cba9e, live about 20:43Z 2026-11-20 (UAC-r2 deploy): carries #2919 and 052e/f/g. `RecordRepair:Pass:GoLive` unset everywhere. Direct setup and both `DirectMessage:*` settings are set on dev. U-11 re-run passed. 054c Stamp and Move are done on dev. |
| **Active task** | **056b** (build), FULL rigor, Opus tier, on `task/fm-056b` (base f1d35f053a: `PrivateContextColumns.cs` + UAC-r2's three clarifications in the note). 056a APPROVED by UAC-r2 at 51419e7768, owner answers in `notes/decisions.md` 2026-11-22, merged to the work branch b995d2302e. Four implementer agents running in worktrees, each branching from f1d35f053a: `task/fm-056b-send` (Opus: request field, `PrivateMessageSendGate`, sender-then-recipient checks, 403/503/409 codes, contract + data-mutation tests); `task/fm-056b-read` (Opus: `about` list + thread read, `privateContext` on messages resolved as the viewer, ArchTests for registries/allow-list/C5, seam tests); `task/fm-056b-schema` (Sonnet: `scripts/Set-PrivateContextSchema.ps1` with NoCascade/RemoveLink + Pester, field-security targets, `BffOwnedCommunicationColumns`, mapping must-fire); `task/fm-056b-client` (Sonnet: Send privately, private entries, chip, RTL). Next: merge each into `task/fm-056b` with `--no-ff`, resolve `CommunicationEndpoints.cs` overlap (send vs read handlers), gates, Opus adversarial review ×2 (security), UAC-r2 access-path review (branch + SHA). Live: schema script and field-security change need the owner's go per environment; no deploys from here. |
| **Task 055** | 🔄 Code complete, merged to the work branch via #2943 (46a0e1c236; the tree is identical to the reviewed 434958a0ca). UAC-r2 PASS and re-check PASS. Open: AC5, the live dev check. |
| **056b state (checkpoint)** | On `task/fm-056b` at 478011f5f3: schema (e50c97d72d), client (e259783438), send (c01370b9f4), read (593b0caa5e) all merged `--no-ff`, no conflicts. Event ids 5424–5427 (send gate). Not yet run on the merged tip: sln build, ArchTests, verify-suites, client jest. **Open fixes (in scope):** (F-a) `MembershipFieldDiscoveryService.DiscoverLookupsTargetingAsync` (:744) returns every lookup on the related table that targets the primary table; `MembershipResolverService.ResolveTransitiveAsync` (:1414, `includeRelated`) then reads matching rows app-only. Fix: drop `PrivateContextColumns.All` when the source table is `sprk_communication`; add the file to the allow-list in `tests/Spaarke.ArchTests/PrivateContextColumnGuardTests.cs`; must-fire test next to `tests/unit/domain/Membership/PrivateContextLookupDiscoveryTests.cs`. Tell UAC-r2 by message (rule wording only). (F-b) 055 defect: `CommunicationWorkspaceReadSeamTests.NoMembershipUnionRegression…` pins 6 ctor params; 055 (ab12e0901d) added `IGenericEntityService` + `IDataverseRecordShareService` for the 1:1 shape read (UAC-r2-reviewed). Update the pin with the reason; the banned-seam check stays. (F-c) Flaky 055 RTL test (`ConversationWorkspace.messages.test.tsx` F1 direct-modal: the dialog closes after a failed first send when the four conversation suites run together): the client agent is diagnosing it on `task/fm-056b-client-fix` from d7b85edb2d. **Deploy note:** after 056b the BFF thread read selects the reference columns, so the schema must exist before that BFF; `Set-ThreadFlagFieldSecurity.ps1 -Verify` fails on an environment until `Set-PrivateContextSchema.ps1 -Apply` has run there. |
| **Review pass 1 (2026-11-22)** | `task/fm-056b` at ba842009d0 (F-a, F-b, F-c fixed and merged). Opus review pass 1: NOT ready. **Two F1s, both in UAC-r2 files, sent to UAC-r2; UAC-r2 CONFIRMED both and agreed both rules. We make them in 056b as named hunks in its two files (it reviews them with the package). A: CitationLookups hunk + tests (i) a reference-only row has no parent, so it moves to the Direct team; (ii) regarding + reference has the regarding as its only parent, also for secure-if-any; (iii) unfile resolves to the Direct team. Added to the review1 agent. B: waits for the owner's answer here; on yes, RefileAsync hunk under UAC-r2 conditions 1–5 (one write; narrow; must-not-fire exact payload; filing-path guard with allow-list + bypass control incl. Office reconcile and chat tool; null-reference tests; premise in the note); on no, stop and go back to UAC-r2:** (A) the three reference columns are missing from `RecordOwnershipResolver.CitationLookups`, so whole-row parent derivation reads them as parents (owner step, unfile, secure-if-any); proposed fix: add them as citations. (B) Design C3 is false: the reader field-security profile gives every user Read, so readers of a FILED private message read the reference; proposed rule: filing clears the reference in `RefileAsync` (changes owner-approved Q5 "left as is", so it needs the owner's answer HERE). **In-scope fixes (implementer agent on `task/fm-056b-review1` from ba842009d0):** F2 thread read selects the live-only columns (gate on presence); F2 `ResolveParentLookup` drops citations; F3 guard `IsCitation` true per column (expected red until A lands); F3 real-gate seam tests; F4 the script (p0) message. Known limits: K2 refused private send leaves an empty pair thread; K1 echo after a row-create fault saves without the reference; K4 an unreadable Restricted flag with an internal pair passes; K4 the name stays visible until the reconcile drops a No-Access viewer's share. verify-suites on ba842009d0 running (log `scratchpad/v056b`). |
| **Review-1 fixes merged** | `task/fm-056b` at f07a17f2e0 (merge of `task/fm-056b-review1`: 094d387179 reads gate on columns, d88d8a35f2 push citations, 2490090029 CitationLookups hunk A + guard + tests (i)–(iii), bc8e197a25 30 gate-seam tests, f8ca3c6954 script). Agent's run: sln 0 warnings, ArchTests 2731/2731, filtered BFF 1574/1574, Pester 21/21. Open small items: the column-presence check caches only "present", so an environment without the schema makes one metadata call per thread read (add a short "missing" cache with the B round); design note Q2.2 wording (an unreadable flag with an internal pair passes). The earlier verify-suites run on ba842009d0 stalled (machine load; two Documents contract tests timed out) and was stopped; re-run on f07a17f2e0 in `scratchpad/v056b-r1`. |
| **Gates on f07a17f2e0 (all pass)** | sln 0 warnings; ArchTests 2731/2731 (fresh); verify-suites BFF 13,325 passed / 0 failed / 10 skipped; client jest 61 suites, 917/917, tsc 0, eslint 0 on 18 changed UI files (the PCF `ConversationModal.tsx` is not in that eslint run); publish delta vs master 0363ba96d2: +21,116 bytes, 40.63 MB, 191 = 191 files, Compress-Archive; CVE none. |
| **B — owner answered (2026-11-22)** | Owner: a private message always stays 1:1 and is never filed (supersedes Q5; clear-on-file dropped). Recorded in decisions.md and the design note (Q5 rewritten, U2 and Q2.2 wording fixed). Invite-only follow-up filed: DI-036 / #2977. Sent UAC-r2 the rule and the proposal: one refusal in `RecordOwnershipResolver.ReparentAsync` (every writer reaches it; absent columns pass, read fault refuses, stable code `sdap.communication.private.not_fileable` → 409); asked placement, authorship, and whether users can set a communication regarding directly. Waiting for the reply. |
| **056b never-filed build (2026-11-23)** | UAC-r2 APPROVED at 2967cd5bd8 (code 47c51ecb64; their re-run: 834/834 focused + 109/109 ArchTests). Merged into the work branch at 778bf422a2 (--no-ff; not pushed). Gates on 47c51ecb64: sln 0/0; ArchTests 2735/2735; suites 13,520 passed / 0 failed / 10 skipped; publish +30,150 B, 40.64 MB, 191=191 files; no CVEs. As-built, deploy order and known limits: notes/task-056-private-message-design.md §6. Accepted limits: a CLEAR of a filing link is refused (the out-of-band clear removes it); K1 G for rows handed in (E1 covers them); K4 Retryable; #2979; #2977. |
| **Next action** | Per environment, each step needing the owner's go in THIS session: (1) Set-PrivateContextSchema -Apply; (2) Set-ThreadFlagFieldSecurity -Apply then -Verify; (3) UAC-r2 CONDITION: a read-only count of sprk_communication rows with any sprk_privatecontext* value. It must be 0; send the count to UAC-r2; nonzero holds the deploy; (4) BFF deploy by UAC-r2 on the owner's exact-SHA go in BOTH sessions; (5) the 056 live check. Also 055: master PR (owner's go), AC5 live check. |

## Task 055: what remains

1. **Master PR**: the work branch goes to master through the project's normal PR (owner's go). Send UAC-r2 the master SHA when it lands.
2. **Dev deploy (UAC-r2 runs it)**:
   - Their sequence: build the candidate (master + mcp spike-v2 973776a5a6), test it, ping mcp-r1 and spa-r4.
   - **Approving the commit**: the owner names the exact SHA in BOTH sessions. UAC-r2's session is where the deploy starts; this session is where 055 is approved to go live. Neither acts on a go relayed from the other.
   - Then UAC-r2 deploys `sprk_communicationconversationpage` and `sprk_spaarkeai`, and runs its 30-minute gate.
   - Never deploy the BFF or those web resources from this session.
3. **AC5 live check on dev** (owner's go here): recipient stamped; owner is the Direct team; both participants read; a third user does not. Then mark 055 ✅ (POML `completed`, index ✅).
4. **CommunicationConversationPanel PCF**: its bundle changes; bump to 1.18.5 and `/pcf-deploy` at its next release (not needed for 055).
5. Known limits: `notes/task-055-notes.md` (candidate cost bound 200 / 4 queries; faulted rows left out with no marker; DI-035 #2936 client paging).

**Revert, never commit:** `src/client/pcf/CommunicationConversationPanel/package-lock.json`.

## Binding deploy order for Direct messages (other environments)

Each live step needs the owner's go; on dev every step is done (detail: `notes/task-054-notes.md` "Live runs").

1. BFF from master with the `DirectMessage:*` settings unset (piece 3 off).
2. 054b `Set-ThreadFlagFieldSecurity.ps1`: dry run → `-Apply` → `-Verify` (owner's go in THIS session).
3. Direct setup + the two `DirectMessage:*` settings (owner's go in UAC-r2's session), only after step 2's `-Verify` exits 0; DI-026 (#2701) check first.
4. 054c Stamp: dry run → `-Apply` → `-Verify`.
5. 054c Move (`-DirectShareJobLive`; the first run scoped to one message, L16).

After every SpaarkeMaster import where 054b is applied: `-Verify`, and `-Apply` if it fails. A message moves to the Direct team only with a valid stamped pair.

## Known limits carried

- **022:** K2 (the isolation owner is the single Secure team), K3 (the Project wizard's follow-on creates map in the browser; tasks 023/024), K4 = DI-024 #2648.
- **022b:** K1–K6 in `notes/task-022b-notes.md`.
- **054:** 054a L3, L6–L10; 054b K1–K5; 054c L11–L16 (the `GrantAccess` shape is not exercised live); #2728; #2722.
- **045e K8:** the push never writes a pair, against the `PushMayWrite` / FR-23 comments. It is wording only. Fix the predicate and comment the next time the file is touched; it gets no PR of its own.

## Open owner items

- Hand checks: 023 (Create Event wizard, live on dev); 021 after its deploy; the 076(c) re-run after UAC-r2 deploys 076c.
- The master PR for the work branch (055).
- Deleting the snapshot branches `…-049c`, `…-030b` and `…-054`.

## Later lanes

023–029 (wizard chain), 035, 036, 050/051, 053, 061–063, 090; DI-031 (#2874).
Cleanup: the merged agent worktrees still listed under `C:\code_files\spaarke\.claude\worktrees\` (task/fm-023, -052c, -052d, -052f, -052g, -052g-r2). Remove them only with `scripts/worktree/Remove-WorktreeSafe.ps1`.

## Coordination

- Peers:
  - `uac-r2` (desktop, `local_54acf7b0-9732-48a1-aa4c-2d840ded57e5`);
  - ebilling-r1, mcp-r1, spa-r4, provisioning-r1, ontology-r1, word-add-in.
- Access defects go to UAC-r2 by message only.
- UAC-r2's access files are not ours to edit. The list is in POML 056 and the project `CLAUDE.md`. For example `DirectThreadAccessService.cs`: we were allowed to edit one condition there (055), and that permission did not carry over to anything else.
- List UAC-r2's access-path files at the top of any diff we send them.
- Office-scope server test legs hit their 20-minute job cap across branches and master (#1474, #1718, #2767). The legs are not required checks. Read the test summary in the log before calling a red leg a failure.
- Nightly quality alerts open on master: #1906 (client tests), #2067 (flake hunt), #2866 (duplication sweep).
