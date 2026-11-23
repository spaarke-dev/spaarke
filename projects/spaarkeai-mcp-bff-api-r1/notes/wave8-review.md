# Wave 8 review (tasks 025, 032) — 2026-11-14

One Fable adversarial pass per task, a fix round, a scoped Sonnet re-check, then a small closing fix each. 032 is done. 025's code is done; AC 1–2 (CI run URL, the §6.5 Path-A exception) wait for the owner's choice of Tier 1 or Tier 2 for the `mcp-conformance` job (`notes/task-025-ci-job.md`).

## Fixed (F-class)

| Task | Finding | Fix commit |
|---|---|---|
| 025 | Plan result uncapped: about 100+ evidence lines exceed 64 KiB (F2, FR-05/FR-11) | `763f5fa180` (row bounded in the MCP tool only; `evidenceTotal` / `evidenceTruncated`; REST unchanged) |
| 025 | Bound edge cases: whitespace, nested arrays, invalid surrogate, non-Latin titles clipped at about 40 characters | `64daee5166` (+ `tests/unit/domain/Mcp/PlanEvidenceBoundTests.cs`) |
| 025 | CI job filter missed the csproj and the auth and middleware modules (F4) | `763f5fa180` |
| 025 | `run-conformance.sh` wrote `conformance-results/` into the repo (F4) | `763f5fa180` |
| 025 | Measurement "tests" asserted nothing (F3) | `763f5fa180` (`--measure` mode of the test-only host) |
| 025 | 200-item seed was best case; size tests did not assert success (F3) | `763f5fa180` |
| 025 | A SystemAdmin-policy sixth tool would pass the five-tool test (F3) | `763f5fa180` (`catalog.ToolsFor` asserted too) |
| 025 | Test token injector authenticated every route (F2, low, test-only) | `763f5fa180` |
| 032 | Double-click on Next recorded the decision before Confirm was seen (F2, NFR-06; Console too) | `714564464f` (`confirmGuardMs`, Console `ThreePaneShell.tsx`) |
| 032 | Late lookup answer wiped newer results (F1) | `714564464f` |
| 032 | Failed or one-letter search read "No results found" (F1) | `714564464f` |
| 032 | Master-detail rendered out of view in narrow frames (F1) | `714564464f` |
| 032 | Lane not re-read after close; impossible "read the list again" text (F1) | `714564464f` |
| 032 | "Open in Spaarke" missing (F1) | `714564464f` (evidence lines with `{table}:{guid}`; matter heading stays text — deviation in `decisions.md`) |
| 032 | Wizard never opened under StrictMode (F4, dev only) | `714564464f` |
| 032 | Widget jest could not load lexical without the shared lib's node_modules (F4, CI) | `714564464f` |
| 032 | A refused `openLink` was silent (F3) | `1737c6e838` |
| 032 (regression in its fix round) | A short-term browse was dropped on a parent re-render (RuleReview `AssignmentPanel`) | `1737c6e838` |

Also in `714564464f`: Escape closes through the discard check, "Showing N of M" for a bounded plan, real reason codes in tests, display mode restored on close.

## Known limits (K-class)

| Task | Limit | Owner / next |
|---|---|---|
| 025 | `/mcp` does not validate `Host`/`Origin` (dns-rebinding check baselined) | ISS-007 #2835, owner decision before 042 |
| 025 | 41 baseline entries (38 failures, 3 warnings), 36+ fixture-only; scenario-crash ids also excuse a crash | Owner: is that "green" for NFR-09? Re-baseline when prompts or completion are added |
| 025 | Plan option lists and rule description are not bounded (catalog worst case 28.7 KB; margin about 17 KB) | Known |
| 025 | Real Entra token validation is not exercised by the conformance host | 042 (live) |
| 032 | Bundle 1.54 MB (aim 1 MB, ceiling 3 MB); about 254 KB is the inquiry email editor | Owner: accept or lazy-load |
| 032 | Display chain: fullscreen refused → master-detail beside the lane | Owner confirm (design line is self-contradictory) |
| 032 | The last fallback opens the Console page, not the Work Item | Owner: Console launch parameter? |
| 032 | ui-tests need a local MCP Apps basic-host; not run | 042 (live) |
| 032 | Under StrictMode (dev only) the plan is read twice per open | Known |
| 032 | Focus-return can stay pending if the lane read never lands | Known |
