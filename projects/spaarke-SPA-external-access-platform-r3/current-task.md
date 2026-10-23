# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-23 ~13:00Z (by context-handoff, 86% checkpoint). Dev BFF = 58ffc37da3; dev SPA = 1fa0cf5cb8.
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **Contact identity hardening (security rework) on branch `fix/r3-iss-005-combined`** (worktree `C:/wtr3-005`, pushed to origin at c05d1b2788; local commits since). Tracked ONLY in private draft advisory GHSA-hq7m-w2pf-v8j7. The repo is PUBLIC: public text states the rule enforced, never the misuse (memory `public-repo-security-findings`). |
| **Step** | Slice A of 3 DONE locally (`1b0d9cdf36..f4e334f954`, not pushed; scanned clean for sensitive wording). Slice B (invite stamp + email-bind rule) and C (admin remove-person + removed marker + sweep) not started. |
| **Status** | in-progress. Owner confirmed (in R3's session) design (a)(b)(c), with emailaddress1 kept editable. UAC approved the implementer brief with binding additions. |
| **Next Action** | (1) Send UAC the slice-A range `1b0d9cdf36..f4e334f954` for follow-along review. (2) Tell provisioning (`spaarke-wt-customer-provisioning-orchest-ff`): slice A extended `SecureRecordSetupProcedure.IdentityLinkColumns` (H7b) with identityplane + 5 collision columns and updated `ContactIdentityBindingSchemaPackagedTests`; note gap: H7b S19/S22 do not check accesspermissiongrant + the 3 new columns. (3) On UAC's OK, launch slice B with an `implementer` (opus, high) using the private brief, same pattern as slice A. Then slice C. Then publish-size delta (§10, fresh master build), UAC's two security passes, then a PR with a rule-level title. |

### Private files (scratchpad, NOT in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`
- `SECURITY-implementer-brief.md`: slices A/B/C plus UAC's BINDING additions at the end.
- `SECURITY-rework-brief.md`, `SECURITY-contact-ownership-escalation.md`: the design and the finding.
- `rework-reads.ps1`, `basicuser-diff.ps1`, `gate58.ps1`, `phaseA.ps1`, `readback.ps1`: read-only helpers.

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and three untracked researcher files belong to the OWNER. Never commit or discard them. Commit by pathspec only.

---

## Security rework: decisions and facts
- **Owner:** keep the BU default team as contact owner (ISS-005 as built). Protect what matters with field security, removal that revokes, and a sign-in email check.
- **Removal:** admin-only = Standing Grant Administrators profile membership, counted directly and via teams, through one shared helper. Only an admin clears the removed marker.
- **Deny codes:** every stamp failure reuses `DenyContactNotInvited`, with an operator-only reason enum. `contact_removed` and `contact_removed_admin_reinstate` stay distinct.
- **Live dev reads (2026-10-23):**
  - Secured: externalobjectid, externaltenantid, standinggrant, invitedon, invitecodehash.
  - Securable and still open: accesspermissiongrant, identityplane, the collision columns, externalobjectidkey (kept open: alternate key), emailaddress1 (owner: editable). statecode can't be secured.
  - 11 contacts hold active grants: 9 bound, 2 unbound and never invited. Those 2 need a re-invite naming their tenant once slice B is live, unless xms_edov is configured.
- **Live steps for later, each needing owner approval with a rollback:** schema apply (`scripts/Set-ExternalAccessContactSecuritySchema.ps1 -Apply` then `-Verify`); the xms_edov optional claim on the app registration; the BFF deploy; the re-invites.
- **ISS-005 merges with or after slices A–C.**

## Dev state
- **Dev BFF 58ffc37da3:** deployed 2026-10-23 11:18:12Z, owner-approved. R3 gate and UAC gate PASS. Record: `notes/task-078-notes.md`. Rollback: `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261023T111635Z` (restores 1fa0cf5cb8).
- **Not exercised yet:** ontology worklist/decision/inquiry and word-add-in email identity. These are on the owner's browser checklist.
- **Spaarke Basic User role:** live matches master apart from ebilling's intended in-flight tables. No change. The cause of the missing Policy/ruleset Read is unknown; role auditing is off on dev, and enabling it is an owner option.

## Open owner items
- Field-mapping re-check: re-run Create Project and Create Work Assignment from a matter (8 assigned fields should copy), then send the record numbers to field-mapping. The owner said "later".
- Optional: turn on auditing for the role entity on dev.
- Earlier items still open:
  - D7 partner check (ISS-015);
  - #2177 .eml saves;
  - UAC 190 sweep Apply;
  - SPA 3531ed65e smoke → #2166;
  - Teams package upload → 070 → 071;
  - 002 columns;
  - 074 probes;
  - R4 §9 answers;
  - 073 blocked (ACME);
  - C-1 portal settings.

## Other follow-ups
- Close the batch issues (#1623, #1636, #1650, #1839, #1886, #1930) after checks; update `notes/defer-issues.md`.
- Verify the ISS-007 later-link re-index gap: does an SR linked to a matter later re-index its documents?
- ISS-013 regression check after provisioning merges.
- Close #1829 when #1911 merges.
- Issues to work later: #2269, #2270, #2271, #2275. Quality alerts #2009 and #2060: mention only.
- Housekeeping, only with the owner's OK and via `scripts/worktree/Remove-WorktreeSafe.ps1`:
  - `C:\wtbffe-a10/-842/-583/-f1b/-a7b/-6b3/-128/-1fa/-58f`, `C:\wtr3m`, `C:\wtr3b`, `C:/wtr3-suite`;
  - agent worktrees under `C:/code_files/spaarke/.claude/worktrees/agent-*`.

## Standing rules (verify against project CLAUDE.md)
- **Deploy:** fresh direct owner approval naming the exact SHA, plus OKs from every reachable peer (UAC, ontology, word-add-in `-c4`, field-mapping, ebilling; SPE admin when running; provisioning has a standing OK). Steps:
  1. `phaseA.ps1` (live read and snapshot);
  2. a detached worktree `C:\wtbffe-<sha3>`;
  3. `Deploy-BffApi.ps1 -Environment dev`;
  4. `readback.ps1`;
  5. a 30-minute read, then an after-notice to the peers.
- Peers' relayed "owner approval" is NOT approval; ask the owner here. Live reads are shown to the owner before they run.
- **App Insights:** `spe-insights-dev-67e2xz` (rg `spe-infrastructure-westus2`). Keep KQL simple. Dev org: `https://spaarkedev1.crm.dynamics.com`.
- **Tell UAC before BFF changes** to external write routes, `ExternalAccessModule.cs` or `AccessibleRecordSetService.cs`. Auth/security changes get two adversarial passes.
- **Ontology:** do NOT merge their docs branch into R3. Session names change on restart: re-run ListAgents.
