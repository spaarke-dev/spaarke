# Current Task State

> **Last Updated:** 2026-11-20 (checkpoint at 85% context). Session `ebilling-r1` (Claude Desktop) is the only writer to this worktree.
> Older detail: `notes/handoff-history/current-task-2026-11-20a.md` (the previous state, verbatim), plus `-2026-11-16a`, `-15a` and `-13a`.

## Owner mode

- "Continue with open work items that are available." Keep `notes/owner-testing-checklist.md` current.
- The owner works over Remote Control. The closing message says what was done and what failed, plus the decisions needed, as questions with reply choices.

## Quick recovery

- **Active task:** 130g, "Firm view: show the reviewer-confirmed notice prose as the firm-safe reason" (EB-D-259). The owner said "Build 130g first" on 2026-11-20.
- **Step:** 2 of 4. The POML is written (`tasks/130g-firm-view-notice-prose.poml`); the implementer is launched next. 130g is server only; the SPA renders the field in 134.
- **Uncommitted files:** none. The work tip is pushed.
- **Open owner questions:** none. The deploy of the Invoice Review page to dev (checklist M2) waits; the owner chose 130g first.

## 130g design notes (gathered so far; put these into the POML)

- **Source:** `sprk_invoicesubmission.sprk_firmnoticetext`. It holds JSON `{ v, source, kind, subject, prose, html, htmlOmitted }`, stored by 132 on the invoice's OWN submission (`InvoiceBlockedHold.ReadOwnSubmissionAsync`). The column is field-secured to the writers profile only, and the BFF reads it app-only.
- **Obligation** (132 notes, "Stored sent notice"):
  - Re-screen `prose` with `FirmNoticeProse.Check` before showing it.
  - NEVER render the stored `html`.
  - Show `prose` as plain text.
- **Where:**
  - Add one optional field to `FirmOutcome` (`Services/Finance/FirmFindingsProjection.cs:86`), e.g. `NoticeProse`, with `JsonIgnore WhenWritingNull`.
  - Set it only for the `Rejected` and `ReturnedToFirm` kinds, and only when the stored `kind` matches the invoice's outcome. Otherwise it is null.
  - The projection's callers are a closed set (`FirmFindingsProjectionCallerBoundaryGuardTests`): `FirmOutcomeEndpoints` (SPA) and `FirmNoticeComposer`. `EbillingFeedSource` and `InvoiceApprovalEndpoints` also reference the type.
  - The composer must not echo the field into a new notice.
  - The reviewer's internal reason and note stay out (EB-D-259).
- **Fail-safe:** a read fault on the stored text must not leak anything or render part of it. Choose either "omit and mark Degraded" or "omit and log a warning". Never show unscreened text.
- **SPA:** if the external-spa invoice view shows outcome items today, it renders `noticeProse` as plain text. 134 (SPA invoice detail) is next and may own that rendering; decide the split in the POML. spa-r4 gets a copy of any SPA change.
- **Reviews:** one Opus adversarial pass, then a uac-r2 review (firm-facing, external). ontology-r1 is not needed.
- **Rigor:** FULL. The index said STANDARD, but this touches `.cs` and firm-facing output. Sonnet @ high implementer.

## Next action

1. Write the 130g POML from the notes above, mark the index row 🔄, commit and push.
2. Launch the implementer in an isolated worktree on branch `task/ebilling-130g` from the work tip. The brief names the base sha and includes the no-python, no-stash and no `--no-verify` lines.
3. Then: the Opus review, the uac-r2 review, fixes, merge into work, checklist N/M, the index.

## Recently done (detail in the notes and git)

- **132ui ✅** merged into work 04718b3224: the Invoice Review Notify step, an Opus review and 2 fix rounds. ISS-041 #2930 filed (the reload case). Checklist M2 waits for the page deploy, which needs the owner's OK.
- **131 dev seed ✅** 2026-11-19 (EB-D-256), on dev BFF 5ae2804884, which contains master fe51619cfc. `/healthz/catalog` stays 503 because of pre-existing drift (#1987).
- **#2904 merged to master** fe51619cfc on 2026-11-18. ISS-032 and ISS-034 are Done. ISS-040 #2901 stays open until the SpaarkeMaster 1.3.2.0 release.

## Open work after 130g

- **Build:** 134 (SPA invoice detail and on-screen report; honour K6), 123, 124, 050b, 096, 117, 121 (after uac-r2 191).
- **Partly done:** 086; 164 (live `-Apply` needs the owner's go); 089 and 050a (owner questions in their notes).
- **Deploy close-outs:** 053, 079, 112, 136, 153. Then 160, 161/162 (live runs need the owner's OK), 163 UAT, 189, 190.
- **Index tidy:** the drift check lists 028, 040, 050a, 084d, 093, 113 and 161.

## Standing reminders

- **Live Dataverse writes or role changes:**
  - Need the owner's OK, typed in THIS session.
  - uac-r2 gets a notice before and after.
  - Private access findings go to uac-r2 by message only, never into the public repo.
- **Commands:**
  - Never run `python`, `python3` or bare `python`.
  - No `git stash`, no `--no-verify`.
  - Merge master PRs as merge commits.
  - Never run the whole `Sprk.Bff.Api.Tests` or the whole ArchTests; filter them.
- **Agents:**
  - At most 4, each with a named model.
  - A worktree agent starts on master, so its brief names the base sha.
  - Remove an agent worktree with `scripts/worktree/Remove-WorktreeSafe.ps1`. The 132ui agent worktree (`.claude/worktrees/agent-a15c660293effd9cb` in the main checkout) was still locked at the last check.
- **Clean-up:** a human should delete C:\wt131a and C:\wt131b.
