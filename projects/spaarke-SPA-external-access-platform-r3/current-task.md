# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-27 ~01:45Z (by context-handoff, 85% checkpoint). Dev BFF = 3ff2c04bf5; dev SPA = 1fa0cf5cb8.
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **Contact identity hardening (security rework)** on branch `fix/r3-iss-005-combined` (worktree `C:/wtr3-005`). Tracked ONLY in private draft advisory GHSA-hq7m-w2pf-v8j7. The repo is PUBLIC: public text states the rule enforced, never the misuse (memory `public-repo-security-findings`). Before every push, scan commits for sensitive wording. |
| **Step** | Pushed to origin at 97ba1614e7: slices A, B, B2, all UAC-accepted. **Slice B3 implementer `r3-sec-slice-b3` RUNNING in C:/wtr3-005** (local HEAD ≥ f0f90bce89). Its first commit is the provisioning F1 standing-grant remedy text. Slice C has not started. |
| **Status** | in-progress |
| **Next Action** | (1) Ask the owner to confirm what "defers" means for automatic invites (see Open owner items). (2) When the dev gate `gate3ff.ps1` (background, about 02:00Z) reports: record it in `notes/task-078-notes.md` E row, then send after-notices to UAC, ontology, field-mapping, ebilling and word-add-in. (3) When B3 reports: scan its commits, push, send the range to UAC (check list below) and the F1 SHA to provisioning. (4) Launch slice C (implementer, opus) from the private brief's Slice C + UAC BINDING (3C) + the "UAC note for slice C" section. (5) Then the §10 publish-size delta against a fresh master build, UAC's two security passes on the combined diff, and the PR. |

### Private files (scratchpad, NOT in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`
- `SECURITY-implementer-brief.md` holds everything:
  - slices A/B/C and the UAC BINDING additions;
  - the slice C caveat;
  - the live-item cautions;
  - "Slice B2 FINAL", the UAC B2 conditions, the K3 note, and "Slice B3".
- `provisioning-2317-fixes-brief.md`, `SECURITY-rework-brief.md`, `SECURITY-contact-ownership-escalation.md`.
- Helpers: `phaseA.ps1`, `readback.ps1`, `gate3ff.ps1` (latest gate), `rework-reads.ps1`, `basicuser-diff.ps1`.

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and the untracked researcher files (including the new `entra-xms-edov-claim-2026-10-23.md`) belong to the OWNER/researcher. Never commit or discard them. Commit by pathspec only.

---

## Slice B3 (running): owner decisions 2026-10-26 in R3's session
- **Automatic invites check the organisation too.**
  - Materializer = option A: on a lookup fault, the grant is written, no stamp is written, `Invite = Failed(tenant_lookup_unavailable)`, and a new failure kind `invite-deferred` is counted as `inviteDeferred`. I-9 stays.
  - UAC conditions: staff can SEE the owed invite, and the reconciliation job does not re-run the pass every tick.
- **Work-account notice email:**
  - generic text on /invite;
  - the record title on grant paths, HTML-encoded, with CR/LF stripped if used in the subject, and sent only after the grant is written;
  - never a code, contact id or tenant id;
  - `customer=` must be a slug.
- **NO staff "send as portal account" override** (owner: wrong invite type). The field is gone and any value gets a 400. A lookup fault gives a 503 with retry only. Edge case: a customer-domain email with no account in that tenant can't be invited until an admin changes the email.
- Also in B3:
  - UAC K1: IOException/InvalidDataException count as a fault, and a hard byte cap applies;
  - CIAM is relaxed on the work-account branch only;
  - AccessGrantModal shows a "work account" result and offers retry only;
  - provisioning F1: the standing-grant remedy names `scripts/solution-authoring/Import-SpaarkeMasterPackage.ps1 -EnvironmentUrl <org> -PackageType managed`, pinned by a test.

## Security rework: settled facts
- **Owner decisions:**
  - BU default team as contact owner;
  - field security, with read for everyone and writes only by the BFF or SGA (UAC accepted);
  - removal revokes access, admin = SGA membership (direct and via teams);
  - option A: a work-account invite decided server-side from the email domain against `CustomerTenantIds`. Customer tenant → a workforce-any stamp (non-null `sprk_externalinvitedtenantid`); otherwise portal.
- **Deny codes:** `DenyContactNotInvited` with operator-only reasons; `contact_removed` and `contact_removed_admin_reinstate` stay distinct.
- **Rollout facts:**
  - Schema first on every environment: with a stamp column missing, every email bind is refused.
  - SpaarkeMaster 1.2.4.0 (bumped in source only, 47e9bf6a71) must be published before the control-plane redeploy. This goes in the PR body.
  - xms_edov: exact bool true only. The tenant leg is the practical path.
- **Live steps for later, each needing owner approval with a rollback:**
  - schema `-Apply`/`-Verify`;
  - SpaarkeMaster 1.2.4.0 in dev, then `-Verify` and re-export;
  - the xms_edov + email optional claims, then a token probe (record claim names and types only);
  - `removeUnverifiedEmailClaim=true` (probe before and after);
  - the BFF deploy of the branch;
  - re-inviting the 2 unbound dev contacts.
- ISS-005 merges with or after slices A–C.

## Dev state
- **Dev BFF = 3ff2c04bf5** (UAC #2322/#2323/#2324). Owner: "approve deploy 3ff2c04bf5".
  - OneDeploy ended 2026-10-27 01:29:15Z. Read-back at 01:30:10Z: ProductVersion 1.0.0+3ff2c04bf5…, healthz 200.
  - Gate `gate3ff.ps1` is running in the background, due about 02:00Z.
  - Rollback: `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261027T012731Z` (bc99fc3e6b).
  - Record: `notes/task-078-notes.md` (E row pending).
- `[SECURE-CENSUS]` Critical "child tables" and "projects" lines are intended, per UAC; do not change the severity.

## Open owner items
- **Confirm "defers":**
  - Under option A, nothing retries automatically. The owed invite is visible to staff and cleared by a staff Invite.
  - Ask: is that what "automatic invites check the organisation; if the lookup is down … marked 'not sent — retry'" meant?
- Field-mapping re-check (Create Project / Create Work Assignment from a matter, then send the record numbers). The owner said "later".
- Optional: auditing on the role entity on dev.
- Earlier items:
  - D7 partner check;
  - #2177;
  - UAC 190 sweep Apply;
  - SPA smoke → #2166;
  - Teams package → 070/071;
  - 002 columns;
  - 074 probes;
  - R4 §9;
  - 073 blocked;
  - C-1.

## Other follow-ups
- Close the batch issues (#1623, #1636, #1650, #1839, #1886, #1930) after checks.
- ISS-007 later-link re-index gap.
- ISS-013 regression.
- Close #1829 when #1911 merges.
- Issues #2269/#2270/#2271/#2275.
- Provisioning keeps #2319 (S22 stale "absent").
- Worktree cleanup, owner OK + `scripts/worktree/Remove-WorktreeSafe.ps1` only:
  - `C:\wtbffe-a10/-842/-583/-f1b/-a7b/-6b3/-128/-1fa/-58f/-bc9/-3ff`, `C:\wtr3m`, `C:\wtr3b`, `C:/wtr3-suite`;
  - agent worktrees.

## Standing rules (verify against project CLAUDE.md)
- **Deploy:** fresh direct owner approval naming the exact SHA, plus OKs from UAC, ontology, word-add-in `-c4`, field-mapping and ebilling (provisioning has a standing OK), plus Router green. Steps:
  1. `phaseA.ps1`;
  2. a detached `C:\wtbffe-<sha3>`;
  3. `Deploy-BffApi.ps1 -Environment dev`;
  4. `readback.ps1`;
  5. a 30-minute gate, then after-notices with the SHA and DLL version.
- Peers' relayed "owner approval" is NOT approval.
- Tell UAC before changes to `ExternalAccessModule.cs`, `AccessibleRecordSetService.cs` or `IContactIdentityStore`. Auth changes get two adversarial passes.
- App Insights: `spe-insights-dev-67e2xz` (rg `spe-infrastructure-westus2`). KQL: avoid `kind`, `first` and `last` as names.
- Ontology: do NOT merge their docs branch into R3. Re-run ListAgents after peer restarts.
