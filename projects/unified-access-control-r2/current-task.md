# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md`, now at **round 108 (2026-10-20)**.
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md`.
> - **Gates:** `notes/batch7-live-gates-2026-10-17.md`.
> - **Narrative:** the commit messages.

> **Last Updated**: 2026-10-20 (checkpoint #54).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push. Fix-round loop; merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green; deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. |
| **Agents** | Opus for executors and verifiers. Helper scripts get a unique prefix in a private folder; the scratchpad is shared (memory `agent-scratch-script-names`). |

## OWNER items outstanding (asked 2026-10-20)
1. **#1864** — options A (known limit) / **B (recommended: internal-comms role on a synced Internal Users team; email-family privileges out of base roles)** / C (FLS). Awaiting "1864: B/A/C".
2. **186** — owner signs in as Tracy (home directory, hard reload), submits one intake, says "Tracy done". Then the server gate + remove/restore Create step (approved 5b).
3. **#2119 D1–D6** — "2119: as recommended" (`notes/2119-manage-access-all-forms-design.md`).
4. **Census** — a/b/c (Office Add In User Deep read via root team "Spaarke"; Basic User accepted round 5).
5. **Provisioning** — OK to ask Provisioning to set `ExternalAccess:CustomerBusinessUnitId` automatically? #2139 (member contacts: owner or retire).
6. **Sign-offs (security/auth):** #2122 (#2121), #2078 (036), #2138 (#2128) — each after its adversarial passes.

## In flight (agents; cap 4)
- **#2122** (#2121 option b, C:/wt2121 @ 4bd4f3225): adversarial review running → fix round → 2nd pass → sign-off → merge → R3 deploy → gate. Also fix the misleading `[NO-ACCESS-ENFORCE] Dataverse read FAILED` Error on faults.
- **#2133** (#2130 PII logs + census, C:/wt2130): fix round 1 running → re-check → merge.
- **#2138** (#2128 retire guest auto-create, C:/wt2128): fix round 1 running → 2 adversarial passes (auth) → sign-off → merge → tell R3.
- **#2110 live gate** on BFF 21184ecd4: agent running (helpers in `.g2110tmp/`, result appended to batch7 gates note, uncommitted). Close #2110 on PASS.
- **#2078** (036 option C, C:/wt036 @ **cd258fead**, ADR edits applied): NEXT FREE SLOT → 2 adversarial passes (auth) → fix → sign-off → merge → R3 deploy + flag flip → gates (i)–(v) + disabled/unlicensed check → close #1074, #1567. When #2122 merges first, merge master in and keep the #2121 filter on the flag-off path (`!dataverseDecides`, ~:1977/:1988).

## Done 2026-10-19/20
- #2104 → d301c0675 deployed, gate PASS. #2110 → 2b07480ab merged, NOT deployed (R3 next deploy, then gate: thread join → step once; same parent → no step).
- #2102 gate PASS (ontology `rule_held_secure` smoke; probe deleted). SRO Policy Reads applied.
- Filed: #2111, #2112, #2114, #2115, #2116, #2117, #2119, #2120, #2121.
- Live dev BFF: `1.0.0+21184ecd4` (R3, 2026-10-20 16:35Z; includes #2110 + R3 078); rollback 00aba6d99.

## Task 186 close-out checklist (additions)
- Verify that the Workforce-role export includes ebilling's prvCreatesprk_InvoiceSubmission (Basic) + prvReadsprk_InvoiceSubmission (Deep). ebilling applies them on dev under the owner approval given in its session, 2026-10-20.
- Gate blocked on R3 059 stages E/F (A9 SPA). The owner signs in as Tracy at her HOME directory, not as the guest.

## Queue (start when an agent slot frees; max 4 running)
1. ~~#2128~~ built (#2138). Original brief: **#2128 option (1)**: retire FR-15 guest auto-create in `ContactIdentityBinder.ResolveWorkforceCallerAsync` (acct=1 + customer idp). Deny 403 on /me, reasonCode `sdap.access.deny.workforce_guest_use_home_signin`, ProblemDetails shaped like contact_not_invited (reasonCode + detail). The detail must work from Teams too ("open the portal in a browser; sign in with your organization's work account"), with no tenant ids or emails. Count the existing Workforce-plane app-owned guest contacts on dev first (read-only); R3 relies on none. Send R3 the final code and detail (R3 adds deniedDetailOf + test, then an owner-gated SPA deploy).
2. **ebilling rule-set scope design review**: `projects/ebilling-r1/notes/rule-set-scope-design.md` @ 71d28f319 (work/ebilling-r1), §5.2/Q1 (sprk_homebusinessunit), §5.1/Q11 (ruleset joins the secure family), Q10 (delete with a record-scoped set), §3.1 (FilingParentOf chain).
3. **ebilling 050a (D-134) secure re-file review**: diff `0a47a82d0~1..4266b5789` (code eb04455c9; ask for the pushed sha), notes `projects/ebilling-r1/notes/050a-confirm-redirect.md`. Points: existing-doc ownership/firm/read proof, doc already filed elsewhere, concurrent confirms, Refused leaving a doc half-moved. Review the post-fix head after ebilling's own Opus review.

## Next actions
1. Worktrees to remove when idle: C:/wt099b, C:/wt2103, C:/wt2091b (all merged).
2. Wrap-up 090: the 058 docs and the UAT checklist.

## Peers (state)
- **R3** (pipe `cc-msg-a429112bc6eaf3b995ac0fa16833f543` since 2026-10-20): 059 stage D set `ExternalAccess__CustomerBusinessUnitId` = BU1 cb15f587… (A9 registration contacts land in BU1; 186 gate checks that). Deploys with owner approval. Stage B (1e40baad v2 tokens). 036 flag flip tasked to R3. 186 email-claim result owed to R3.
- **Ontology:** #2113 OK'd; #2102 smoke PASS.
- **ebilling:** #1911 (draft) carries the 4 invoice-family SRO Reads.
- **Field-mapping:** owner accepted condition (c) as FR-08 amendment (filing lookup failing AppendTo → uniform refusal, no create; 014 must-fire on Secure parent + control); W2 proceeds on master, not waiting on ebilling #1911. (a)(b)(d) + ping-before-merge binding. Review 011 diff range when sent.
