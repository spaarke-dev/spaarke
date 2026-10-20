# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md`, now at **round 109 (2026-10-20)**.
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md` (§11 = ebilling D-137).
> - **Gates:** `notes/batch7-live-gates-2026-10-17.md`.
> - **Narrative:** the commit messages.

> **Last Updated**: 2026-10-20 (checkpoint #55).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push. Fix-round loop; merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green; one background merge job at a time; `--squash --match-head-commit`, no `--delete-branch`, after `git merge-tree`. Deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. |
| **Agents** | Opus for executors and verifiers. Helper scripts get a unique prefix in a private folder; the scratchpad is shared (memory `agent-scratch-script-names`). |
| **Dev live** | BFF `1.0.0+c3ec5d6e5` (18:12:51Z; rollback `deploy/rollback/wwwroot-20261020T180912Z` = 21184ecd4, in R3's worktree). SPA from master 28274e04e (has d4ea1b4a8, `workforce_guest_use_home_signin`); rollback tag `external-spa-dev-rollback-1908696a8`. |

## OWNER items outstanding
1. **#2122** (#2121 option b): two adversarial passes and the re-check are clean → awaiting **"2122: approved"**, then a green Router, then merge.
2. **#1864**: "1864: B/A/C" (B recommended).
3. **186**: the owner signs in as Tracy at her HOME directory (a guest sign-in now gets `workforce_guest_use_home_signin`), submits one intake, and says "Tracy done". Then the server gate and the remove/restore Create step (5b approved).
4. **#2119 D1–D6**: "2119: as recommended".
5. **Census**: a/b/c. **Provisioning**: may `CustomerBusinessUnitId` be set automatically? **#2139**: member contacts, owner or retire.
6. **Q1/Q10** (ebilling rule sets): relayed by ebilling and not yet confirmed by the owner directly → awaiting "Q1/Q10 confirmed".
7. **Sign-offs still to come**: #2078 (036) and #2143, each after its adversarial passes.

## In flight (agents; cap 4)
- **#2143 fix** (agent a743a53c95f234ff5, option A): `RecordOwnershipResolver.ReparentAsync`.
  - SDK conditional update (`IfRowVersionMatches` → `DBConcurrencyException`); re-read the version and parents after `applyChange`, then re-resolve; at most 3 retries; fail closed; compensation is conditional too.
  - Next: draft PR → 2 adversarial passes (security) → owner sign-off → merge → deploy → gate. Tell ebilling when it lands (050a re-file).
- **036 / #2078 round 4** (agent a5f1a15baba6d9e6e, C:/wt036): null/unknown → 503, not 403; route-census guard control; K3 doc; K6 token test; #2147/#2148 links.
  - Next: apply its "Round 4" ADR drafts (`036-adr-edits-owner-a-f.md`) to C:/wt036/.claude/adr in the main session → re-check the fix diff → owner sign-off.
  - Run the 034 negative canary (NFR-04 merge gate). Merge after #2122: merge master in and keep the #2121 filter on the flag-off `else` path, with per-id rights.
  - Then R3 deploys and flips the flag → gates (i)–(v) plus the disabled/unlicensed check → close #1074 and #1567.
- **#2130/#2128 live gates** (agent a19cc19d9ad468d00): it appends "## #2130 / #2128 live gates (2026-10-20)" to the batch7 gates note (uncommitted).
  - On PASS: commit the note and close #2130, #2131 and #2128.
  - B3 (a live guest refusal) may be NOT-RUN; the owner can see it by signing in as Tracy's guest.

## Done 2026-10-20
- **#2138** (#2128, guest auto-create retired) merged as 83d360ec8, after owner sign-off in round 109. The dev data step was a no-op: all 3 guest contacts have enabled systemusers.
- **#2133** (#2130, PII logs and census) merged as c3ec5d6e5 and deployed.
- **#2110**: gate PASS, closed.
- Filed: #2142, #2143, #2147, #2148.
- ADR-034 and ADR-028 036 edits applied in C:/wt036 (cd258fead, c2e8e9b2f8).
- Delete design §11 (D-137): a record's own rule set is deleted when uncited, otherwise unlinked (86cba5a00).
- ebilling `sprk_ebillingoverride` pre-notice answered: no UAC collision; not a census-guarded privilege. Asked ebilling for: fail-closed confirmation, the privilege in the 112 export, and a read-back after-notice.

## Task 186 close-out checklist
- Verify that the Workforce-role export includes prvCreatesprk_InvoiceSubmission (Basic) and prvReadsprk_InvoiceSubmission (Deep).

## Peers (state)
| Peer | Pipe | State |
|---|---|---|
| R3 | `cc-msg-a429112bc6eaf3b995ac0fa16833f543` | Deploys with owner approval. Next: #2122, then 036 + flag flip. 186 email-claim result is owed to R3. |
| ebilling | `cc-msg-83e4fded6c95d56e40d08aa57052bd98` | Expect: the 050a revision; the D-137 citing lookups; the snapshot-column diff (ProvisionProjectEndpoint, child-create) to review under (a)–(f); the rule-set design revision; the override after-notice. |
| field-mapping | `cc-msg-8bd2a30b7c3f35dc7941d072ffd53ebb` | 011 OK. Review 017 when sent. |
| Ontology | `cc-msg-7848627ab2b6296a8e63ee2cbf37cde6` | #2113 OK; #2102 PASS. |
| Provisioning | `cc-msg-dc6f397f8149c1443ce9df2440ac85fc` | Awaiting the owner's answer on CustomerBusinessUnitId. |

## Next actions
1. Process agent results as they land (#2143, 036 r4, the gates).
2. Merge #2122 on "2122: approved" + green checks → R3 deploy → live gate → close #2121.
3. Worktrees to remove when idle: C:/wt099b, C:/wt2103, C:/wt2091b, C:/wt2130, C:/wt2128 (all merged).
4. Wrap-up 090: the 058 docs and the UAT checklist.
