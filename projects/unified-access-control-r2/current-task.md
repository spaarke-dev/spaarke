# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md`, now at **round 104 (2026-10-19)**.
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md`.
> - **Gates:** `notes/batch7-live-gates-2026-10-17.md`.
> - **Narrative:** the commit messages.

> **Last Updated**: 2026-10-19 (checkpoint #52).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push. Fix-round loop; merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green; deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. |
| **Agents** | Opus for executors and verifiers. Helper scripts get a unique prefix in a private folder; the scratchpad is shared (memory `agent-scratch-script-names`). |

## OWNER items outstanding (asked 2026-10-19)
1. **A Secure dev matter:** make one, or name one. The #2102 gate steps 2 and 4a are blocked: dev has 0 Secure matters. Ontology's `rule_held_secure` smoke waits on a probe UAC holds for it.
2. **"Strip the SharePoint four"** from the SRO role on dev. Remove those 4 by name ONLY; never run the §5.4 "everything outside the file" loop until ebilling #1911 merges (4 invoice-family Reads are not yet in master's config). An approval relayed by ebilling was NOT accepted; it must come in this session.
3. **036: C / B / A.** Explained in full with the recommendation (C: app-only named-principal for every systemuser, so no OBO). Pending.
4. **Delete Q1–Q5** (re-sent 2026-10-19): the named role A/B, the SPE recycle bin vs permanent, keeping empty dedicated containers, a DR for each delete, and Signals cited by kept DRs.
5. **186 guest sign-in gate:** the owner is available. Needed from them: a NEW unlicensed guest whose home is bc3aa7f4 (manual invite, dev only), its sign-in name, and "OK to remove and restore Create" on the Workforce role's sprk_servicerequest Create (step 3).
   - Preconditions verified: the SPA is tenant-pinned (R3 ebc74b331); `WorkforceIdentity__CustomerTenantIds__1` holds bc3aa7f4; the Workforce role has the servicerequest privileges.
   - SPA: `https://green-dune-0c4f1221e.7.azurestaticapps.net`. Steps: 186 design §6.
   - Watch the `email` claim after R3 stage B (`removeUnverifiedEmailClaim`); without it, registration answers `registration_email_missing`.

## Done 2026-10-19
- **SRO role (round 104):** prvReadsprk_Policy and prvReadsprk_PolicyVersion added on spaarkedev1; VERIFY PASS, 32/32 tables, 40 privileges. Ontology re-probed: stable over 3 polls.
- **#2104 → `d301c0675`, DEPLOYED** (R3 00aba6d99, 21:11Z). It covers the no-email revoke 500 (#2103) and the internal user's role deletion (#2105), plus three-state container answers. **Server gate RUNNING** (agent, `gate2104/`). Filed: #2111 (email PII in logs), #2112 (group permissions), #2116 (50-page cap), #2117 (systemuser-oid trust).
- **#2110 → `2b07480ab`, merged, NOT deployed** (sent to R3 for the next deploy). Thread join and reply-into-thread run the after-re-file step; same-parent skip; guarded thread reconcile. Ontology PASS. Follow-ups are on #2115.
- **#2102 (#2084): DEPLOYED** (R3 283466d40). Gate PARTIAL: literals, must-not-fire, ordinary publish and App Insights PASS; the Secure steps are blocked (item 1).
- **Ontology #2113** (team-owned communication → DR re-own): UAC OK at 09ba17258. Filed #2114 (stale DR after an owner-only change).
- Live dev BFF: `1.0.0+00aba6d99`; rollback 283466d40.

## Open PRs (UAC)
| PR | Issue | Worktree / branch | State |
|---|---|---|---|
| **#2078** (draft) | #1074, #1567 (036) | C:/wt036 `feat/uac-036-fr20-flag-gated-swap` @ e7a8392bb | Held for owner item 3.<br>- **Next round:** pass-1 K-items (guide §12.8 bullet vi; soften "exactly what the MDA allows"; a 15 s linked timeout on app-only calls; probe comments :11 and :192-196), plus Path C if chosen.<br>- **ADR edits:** drafted in `notes/036-adr-edits-owner-a-f.md`; the main session applies them.<br>- **Then:** pass 2, merge, R3 deploy, then the flag flip in R3's session and gates (i)–(v); close #1074 and #1567. |

## Next actions
1. On the #2104 gate result: commit the notes, close #2103 and #2105 if PASS, and report the guest permission shape (does `grantedToV2.user.id` equal the oid?).
2. After R3 deploys #2110: run the server gate (thread join → step once; same parent → no step).
3. When the owner names or makes a Secure matter: re-run #2102 steps 2 and 4a, holding the Secure probe for ontology, then delete it after ontology's reply.
4. Worktrees to remove when idle: C:/wt099b, C:/wt2103, C:/wt2091b (all merged).
5. Wrap-up 090: the 058 docs and the UAT checklist (include 099c Expiration, the #2076 note and the CIAM UAT PASS from round 102).

## Peers (state)
- **R3:** deploys go through R3 with owner approval. Stage B in progress (1e40baad → v2 tokens; BFF accepts both since 283466d40). Every candidate includes 077. The 036 flag flip is tasked to R3.
- **Ontology:** 042b on master (#2109); 042c compatible. #2113 OK'd. Waiting for a Secure matter for the `rule_held_secure` smoke. Their next docs→master sync must keep `ParentsOf(attrs, table)`.
- **ebilling:** #1911 (draft) carries the 4 invoice-family SRO Reads (D-86). #2095 re-check PASS; it closes on their merge and deploy. D-120 is folded into the delete design.
- **Provisioning:** #2027 needs the owner to set it on dev; then UAC runs a read-only check.
