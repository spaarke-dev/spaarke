# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md`, now at **round 106 (2026-10-20)**.
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md`.
> - **Gates:** `notes/batch7-live-gates-2026-10-17.md`.
> - **Narrative:** the commit messages.

> **Last Updated**: 2026-10-20 (checkpoint #53).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push. Fix-round loop; merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green; deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. |
| **Agents** | Opus for executors and verifiers. Helper scripts get a unique prefix in a private folder; the scratchpad is shared (memory `agent-scratch-script-names`). |

## OWNER items outstanding (asked 2026-10-20)
1. **036: C** — critique confirms C (`notes/036-obo-resolution.md`). Awaiting "036: C".
2. **#2119** — "build #2119 read-only" (read-only inherited Manage Access on Document, then To Do/Event/Communication; fixes #2120 too) or a banner stop-gap first. Design: `notes/2119-manage-access-on-document.md`.
3. **Delete D2** — delete a matter's Decision Records with the matter? Currently no. (Q3 keep container; Q5 a deleted DR deletes its Signal — round 106.)
4. **186** — Tracy Cheatham guest gate whenever the owner runs it (guest Accepted). Then the remove/restore Create step (approved 5b).
5. **#2121 sign-off** — security code; merge only after owner sign-off.

## In flight
- **#2121** (F1 from the Secure-as-intended check, `notes/secure-access-as-intended-2026-10-20.md`): systemuser Term 1 lookup-derived membership not Secure-suppressed (SPA/Teams over-grant). Implementer running in **C:/wt2121** (`fix/uac-2121-secure-systemuser-membership`) → draft PR. Then one adversarial pass ×2 (security), owner sign-off, merge, R3 deploy, gate. **036's branch rewrites the same Term 1 block** — when 036 resumes, merge master into it and keep the #2121 filter on the flag-off path.
- Secure verdict (round 106 ask): contacts behave exactly as decided (round 2 item 3: named direct grants only); no "internal only" decision exists.

## Done 2026-10-19/20
- #2104 → d301c0675 deployed, gate PASS. #2110 → 2b07480ab merged, NOT deployed (R3 next deploy, then gate: thread join → step once; same parent → no step).
- #2102 gate PASS (ontology `rule_held_secure` smoke; probe deleted). SRO Policy Reads applied.
- Filed: #2111, #2112, #2114, #2115, #2116, #2117, #2119, #2120, #2121.
- Live dev BFF: `1.0.0+00aba6d99`; rollback 283466d40.

## Open PRs (UAC)
| PR | Issue | Worktree / branch | State |
|---|---|---|---|
| **#2078** (draft) | #1074, #1567 (036) | C:/wt036 `feat/uac-036-fr20-flag-gated-swap` @ e7a8392bb | Held for owner item 1. Next round: pass-1 K-items + Path C for all systemusers + critique conditions (404 re-ask, fault vs denial, no-OBO ArchTest, CA note) + ADR edits (`notes/036-adr-edits-owner-a-f.md`, B1 widened). |

## Next actions
1. On the #2121 implementer result: adversarial review (Opus, ×2 for security), fix rounds, then ask owner sign-off.
2. After R3 deploys #2110: run its server gate.
3. Worktrees to remove when idle: C:/wt099b, C:/wt2103, C:/wt2091b (all merged).
4. Wrap-up 090: the 058 docs and the UAT checklist.

## Peers (state)
- **R3:** deploys with owner approval. Stage B (1e40baad v2 tokens). 036 flag flip tasked to R3. 186 email-claim result owed to R3.
- **Ontology:** #2113 OK'd; #2102 smoke PASS.
- **ebilling:** #1911 (draft) carries the 4 invoice-family SRO Reads.
- **Field-mapping:** awaiting owner's FR-08 amendment decision; review 011/017 diffs when sent.
