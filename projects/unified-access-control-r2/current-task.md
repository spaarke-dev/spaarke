# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md`, now at **round 110 (2026-10-20)**.
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md` (§12 = ebilling D-146; supersedes §10/§11).
> - **D7 design:** `notes/2119-manage-access-all-forms-design.md` §6.
> - **Gates:** `notes/batch7-live-gates-2026-10-17.md`.
> - **Narrative:** the commit messages.

> **Last Updated**: 2026-10-20 (checkpoint #56).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push. Fix-round loop; merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green; one background merge job at a time; `--squash --match-head-commit`, no `--delete-branch`, after `git merge-tree`. Deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. |
| **Agents** | Opus for executors and verifiers. Helper scripts get a unique prefix in a private folder; the scratchpad is shared (memory `agent-scratch-script-names`). |
| **Dev live** | BFF `1.0.0+0b976628e` (#2122) since 20:36Z; rollback `deploy/rollback/wwwroot-20261020T203441Z` (= c3ec5d6e5), in R3's worktree. |
| **Merged today** | #2138 83d360ec8, #2133 c3ec5d6e5, #2122 0b976628e, #2078 (036) 4f5f22694 — 036 is NOT yet deployed. |

## OWNER items outstanding
1. **#1864**: "1864: B/A/C" (B recommended).
2. **#2119 D1–D6**: "2119: as recommended". (D7 accepted in round 110.)
3. **Census** a/b/c (a recommended). **Provisioning**: may `CustomerBusinessUnitId` be set automatically (yes recommended)? **#2139**: (2) recommended. **#2157**: (a) recommended.
4. **186**: the owner signs in as Tracy, submits one intake, and tells me first → check 3 + 5b remove/restore of `prvCreatesprk_ServiceRequest` (fd03f48f…) on role 9deaac9c…, at Basic. Check 6 needs a licensed internal sign-in.
5. **Sign-offs to come**: #2150 (#2143) and #2153 (#2152), each after its passes.
6. **036 flag flip**: the owner confirms directly to R3, after my canary result.

## In flight (agents; cap 4)
- **#2150 (#2143) round 4** (agent a89d69914b839a4f1, C:/wt2143): add the lineage-unchanged conjunct to the guard's `beforeBuSecure` arm (same gap as F1). Round 3 = c3483933b7 + 82224df95f (10/10 mutants killed).
  - Next: scoped re-check of the r3+r4 diff (fix diff + direct callers/callees) → owner sign-off → merge → R3 deploy → live gate. Tell ebilling the final exception name (`RecordOwnerUnsettledException` → 409 `record_owner_unsettled`) when merged.
  - Known limits (round 3, accepted): K4 counts only an owner this call wrote (others' correct write → accurate 409, retry-able); recovery after a held release ends with the parents' owner; `ResolveSecureBusinessUnitAsync` / owner-team queries keep NoLock (not parent facts).
- **ebilling 050a round 2 review** (agent a7ae539be21181008) → send the findings to ebilling, with the email-activity answer.
- **036 dev deploy (flag OFF)**: requested from R3. When live: flag-off sanity check → re-run the 034 negative canary against the deployed build → tell R3 the result → owner flips → gates (i)–(v) + disabled/unlicensed → close #1074, #1567. Then UAC builds D7 T7.

## Next after those
- **#2153 (#2152)** (C:/wt2152, draft, head 863326f0b): owner sign-off → merge → R3 deploy → gate.
- **D7 T7** (UAC, after 036 is live): one predicate (root grants ∧ own Access Permission ≠ Restricted ∧ not from internal-only/non-Open communication, via `_sprk_relatedcommunication_value` → ParentDocumentLookup ≤ 3 hops, cycles fail closed; reuse `ExternalCallerCommunicationGuard.ReadVisibleCommunicationIdsAsync`) wired on list, download, versions, preview, SPE routes and AI tools; secure `sprk_relatedcommunication` for BFF-only writes; I-14 + ADR amendment. ebilling 081.5/086.4 and word-add-in T8 depend on it.
- **word-add-in task 130 (T8)**: review its PR with two security passes when it arrives.
- **field-mapping 017**: review when sent.
- **ebilling 081/082/086/087 revision**: review when sent.

## Done 2026-10-20 (since checkpoint #55)
- #2121 option (b): merged (#2122) and deployed; live gate PASS (b48c0bd87); closed.
- #2130/#2131/#2128 live gates PASS (aa84f4e40); closed.
- Task 186 gate: Tracy registration PASS (47fded389); intake + 5b NOT-RUN.
- 036 merged after round 4 (fault → 503 `sdap.access.dataverse_unavailable`, not 403); ADR-028 A5 + ADR-034 A2 edits.
- D7 designed and accepted (§6; 6bd15c51e). D-145 (file under the record; internal-only → Restricted) and D-146 (rule sets / rate cards always unlinked, never deleted; ownership never downgraded) recorded (3fbf650fb).
- #2152 fixed in #2153 (MUST field exclusions always unioned; CacheVersions 3/7).
- Filed: #2151, #2152, #2154, #2156, #2157, #2158.

## Task 186 close-out checklist
- Export the Workforce role into SpaarkeMaster (package-scope.json:128-131 currently excludes it); verify it includes prvCreatesprk_InvoiceSubmission (Basic) and prvReadsprk_InvoiceSubmission (Deep).

## Peers (state)
| Peer | Pipe | State |
|---|---|---|
| R3 | `cc-msg-a429112bc6eaf3b995ac0fa16833f543` | Deploying 036 flag-off on request; flip waits for the canary + the owner. |
| ebilling | `cc-msg-83e4fded6c95d56e40d08aa57052bd98` | Accepted D-145/D-146; 081.5/086.4 depend on T7 (STOP trigger). Expect the 081/082/086/087 revision. Owes the override after-notice. |
| word-add-in | — | T8 = task 130; depends on T7. |
| field-mapping | `cc-msg-8bd2a30b7c3f35dc7941d072ffd53ebb` | Review 017 when sent. |
| Provisioning | `cc-msg-dc6f397f8149c1443ce9df2440ac85fc` | Awaiting the owner's answer on CustomerBusinessUnitId. |

## Cleanup / wrap-up
- Worktrees to remove when idle: C:/wt099b, C:/wt2103, C:/wt2091b, C:/wt2130, C:/wt2128, C:/wt036 (all merged).
- Wrap-up 090: the 058 docs and the UAT checklist.
