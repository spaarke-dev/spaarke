# CI Corpus Dependency Verification — Task 001

> Verifies: PR #690 ("ci: pull Git-LFS corpus fixtures in Build & Test") status, real-byte resolution of
> `tests/fixtures/compose-corpus/*.docx` in CI, and the pass/fail state of the 5 Compose seam tests #690
> names. Produced 2026-08-20. No source or workflow file was modified by this task.

## 🔔 Escalation — PR #690 is superseded, not merged (owner decision required)

Per the POML's own escalation trigger ("PR #690 is stalled, closed without merging, or its approach is
superseded — Phase 2 cannot proceed and the owner must decide whether R8 absorbs the CI fix"), this fires,
but with a materially different shape than anticipated:

- **PR #690 itself has NOT merged** — it is still `OPEN`, unchanged since 2026-07-26, `mergedAt: null`,
  `mergeCommit: null`.
- **But the exact fix it proposes (`lfs: true` on the `build-test` job's checkout) is ALREADY on `master`**,
  landed via a **different, unrelated commit**: `f7ec5b928` ("fix(ci): resolve SDAP CI red — LFS smudge +
  stale-test drift + lint (Classes A/B/C)", 2026-08-12, authored under project
  `email-communication-intelligence-r2`'s CI-red cleanup — not R8, not #690's branch).
- `f7ec5b928` is an ancestor of both current `master` HEAD and this worktree's HEAD (verified via
  `git merge-base --is-ancestor f7ec5b928 HEAD` → yes).
- The owner's 2026-08-19 decision ("land #690 first, then build the gate") appears to predate awareness that
  the underlying capability had already shipped a week earlier through an unrelated CI-cleanup PR. PR #690 is
  now redundant / superseded — its diff duplicates a change already on `master`.
- **Recommendation for the owner**: close PR #690 as superseded (this task did not close it — PR modification
  is out of scope here) and update the 2026-08-19 decision record accordingly.

This task follows the letter of its NO-GO instruction for the narrow question "did PR #690 merge" (answer:
no), but the broader, empirically-verified answer to "does the Compose corpus fixture-driven CI capability
exist on master today" is **yes** — verified independently of #690's merge state. See verdict below.

---

## 1. PR #690 status (with evidence)

```
$ gh pr view 690 --json state,mergedAt,mergeCommit,files,title,baseRefName
{
  "state": "OPEN",
  "mergedAt": null,
  "mergeCommit": null,
  "title": "ci: pull Git-LFS corpus fixtures in Build & Test (fixes 5 Compose seam tests)",
  "baseRefName": "master",
  "files": [
    {"path": ".github/workflows/sdap-ci.yml", "additions": 4, "deletions": 0},
    {"path": "projects/ai-advanced-capabilities-nda-r1/current-task.md", "additions": 45, "deletions": 74}
  ]
}
```

- Opened 2026-07-26T17:20:49Z, last updated 2026-07-26T20:16:05Z. **No activity in ~3.5 weeks** as of
  2026-08-20 (today). Zero reviews.
- Its only CI runs (both on branch `work/ci-lfs-fix-r1`, run ids `30212380041` and `30218503170`) both
  completed with overall `conclusion: failure` — **not** because of the LFS fix (see §3: the 5 named Compose
  tests all passed in that run), but because of unrelated failures: an ESLint check failure in
  "Client Quality" and 8 unrelated pre-existing test failures (`ExternalAccessIntegrationTests` x2,
  `CommunicationThreadReadServiceTests`/`CommunicationByRegardingReadTests`/`CommunicationFilteredQueryTests`
  x4, `StorageRetryPolicyTests` x1, plus 2 in `Spe.Integration.Tests.dll`). None of these touch Compose or
  the corpus fixtures.
- The PR's diff to `.github/workflows/sdap-ci.yml` adds `lfs: true` to exactly one checkout step (the
  `build-test` job). **The identical line is already present on current `master`** (see §2), added by a
  different commit. This task did not touch `.github/workflows/sdap-ci.yml`.

**Verdict on this item**: PR #690 has **NOT landed** (not merged) and its author's branch is stale. However,
its change is **superseded** — already present on master via commit `f7ec5b928` (2026-08-12).

---

## 2. Fixture resolution — real bytes, not LFS pointer stubs

Verification method: (a) local `git lfs ls-files` + raw file-size inspection in this worktree, (b) inspection
of `.github/workflows/sdap-ci.yml` at current HEAD for the `lfs: true` checkout flag and its provenance, and
(c) a downloaded TRX artifact from the most recent **green** `master` CI run, confirming the fixture-driven
seam tests actually execute and pass (a pointer-stub `.docx` would throw "missing ZIP 'PK' signature" per
PR #690's own bug description — see §3).

### (a) Local resolution — every corpus `.docx` is real content

```
$ git lfs ls-files
02c6e7c97b * tests/fixtures/compose-corpus/01 - Test Matter Create Fields Only.docx
e940813903 * tests/fixtures/compose-corpus/AppligentNDA_Signed.docx
829183f644 * tests/fixtures/compose-corpus/Engagement Letter.docx
d0c35d21d2 * tests/fixtures/compose-corpus/PAT 109270W-1 - CLAIMS track changes vs US12470413 claims(206092900.1).docx
984c94cb28 * tests/fixtures/compose-corpus/heading-style-numbering.docx
da227be68f * tests/fixtures/compose-corpus/line-numbered-pleading.docx
d65ba33a03 * tests/fixtures/compose-corpus/multi-author-redline-synthetic.docx
5a3a2ee361 * tests/fixtures/compose-corpus/multilevel-1-1-1.docx
37094ad280 * tests/fixtures/compose-corpus/nda-interrupted-clauses.docx
7e836c215c * tests/fixtures/compose-corpus/symbol-section-mark.docx
```

File sizes (all ≫ the ~130-byte LFS pointer-stub size PR #690 describes; smallest real file is 2,666 bytes,
largest 27,986 bytes):

| File | Size (bytes) |
|---|---:|
| `01 - Test Matter Create Fields Only.docx` | 16,264 |
| `AppligentNDA_Signed.docx` | 27,986 |
| `Engagement Letter.docx` | 17,036 |
| `heading-style-numbering.docx` | 3,962 |
| `line-numbered-pleading.docx` | 5,063 |
| `multi-author-redline-synthetic.docx` | 2,666 |
| `multilevel-1-1-1.docx` | 3,652 |
| `nda-interrupted-clauses.docx` | 4,380 |
| `PAT 109270W-1 - CLAIMS track changes...docx` | 27,417 |
| `symbol-section-mark.docx` | 3,816 |

All ten `.docx` under `tests/fixtures/compose-corpus/` resolve to real content locally (the `*` in
`git lfs ls-files` output confirms clean/smudge tracking; sizes are consistent with real OOXML packages, not
pointer text).

### (b) CI checkout configuration on current `master`/HEAD

```
$ grep -n "lfs" .github/workflows/sdap-ci.yml
104:          lfs: true  # smudge tests/fixtures/compose-corpus/*.docx — real bytes, not LFS pointers
              (else ~200 Compose corpus seam tests fail on pointer files; mirrors compose-fidelity-gate job)
759:          # ... lfs: true on checkout pulls the Git-LFS `.docx` corpus bytes
773:          lfs: true  # smudge tests/fixtures/compose-corpus/*.docx — real bytes, not LFS pointers
```

Line 104 is on the `build-test` job's checkout (`actions/checkout@v6`, job that runs the Compose seam
tests) — the exact job PR #690 targets. It landed via `f7ec5b928` (2026-08-12), predecessor commit
`14a5d462d` ("ci: enable git-LFS smudge on Tier-2 full-unit-tests checkout (Compose .docx corpus)"). Not
modified by this task.

### (c) CI-run evidence — most recent green `master` run

Run `32313454003` (`master`, 2026-08-19T23:29:25Z, `conclusion: success`) — the most recent successful
`sdap-ci.yml` run on `master` as of verification time. Downloaded its `test-results-Debug` artifact
(TRX files) and confirmed **zero** "missing ZIP 'PK' signature" failures and full pass rates for every
fixture-driven Compose seam test (see §3 for the specific 5 classes named by #690; the fixture locator
(`ComposeCorpusFixtureLocator.cs`) is shared across ~200 corpus-driven tests total per the inline comment at
line 104, all of which passed in this run).

**Verdict on this item**: CONFIRMED — every `.docx` under `tests/fixtures/compose-corpus/` resolves to real
bytes both locally and in the current CI configuration (verified against an actual green run, not just the
workflow YAML).

---

## 3. Pass/fail/skip state of the 5 Compose seam tests #690 names

PR #690's description names 5 classes: `ComposeFidelitySeamTests`, `ComposePatchEngineSaveSeamTests`,
`ComposeBaselineParaIdStamperTests`, `Nfr09RealTemplateHardeningTests`, `ComposeSummaryPageSeamTests`.

Verified two ways, both from downloaded TRX artifacts (`test-results-Debug`), not console log inference:

### On PR #690's own branch (`work/ci-lfs-fix-r1`, run `30218503170`, 2026-07-26 — overall run FAILED for
unrelated reasons per §1, but this proves the LFS fix itself works)

| Test class | Total | Passed | Failed |
|---|---:|---:|---:|
| `ComposeFidelitySeamTests` | 2 | 2 | 0 |
| `ComposePatchEngineSaveSeamTests` | 19 | 19 | 0 |
| `ComposeBaselineParaIdStamperTests` | 13 | 13 | 0 |
| `Nfr09RealTemplateHardeningTests` | 3 | 3 | 0 |
| `ComposeSummaryPageSeamTests` | 8 | 8 | 0 |
| **Total** | **45** | **45** | **0** |

### On current `master` (most recent green run `32313454003`, 2026-08-19 — the fix present via `f7ec5b928`,
independent of #690)

| Test class | Total | Passed | Failed |
|---|---:|---:|---:|
| `ComposeFidelitySeamTests` | 2 | 2 | 0 |
| `ComposePatchEngineSaveSeamTests` | 61 | 61 | 0 |
| `ComposeBaselineParaIdStamperTests` | 13 | 13 | 0 |
| `Nfr09RealTemplateHardeningTests` | 3 | 3 | 0 |
| `ComposeSummaryPageSeamTests` | 22 | 22 | 0 |
| **Total** | **101** | **101** | **0** |

(The count difference between the two runs is parameterized-test corpus-file fan-out — `master` runs more
corpus permutations than the #690 branch snapshot did at the time; both show 0 failures, 0 skips.)

**Verdict on this item**: All 5 named Compose seam-test classes are **currently green on `master`**, verified
via TRX artifact from an actual CI run — not from the PR description alone.

---

## 4. GO / NO-GO for Phase 2

**GO for Phase 2**, with an owner-facing caveat.

- The hard dependency this task exists to verify — "the Compose corpus fixtures resolve to real bytes in CI,
  not LFS pointer stubs, so the fixture-driven preservation oracle can load documents" — is **empirically
  satisfied on current `master`** (and on this worktree's branch, since it descends from the commit that
  landed the fix). The 5 named Compose seam tests, plus the ~200 corpus-driven tests referenced by the same
  checkout-config comment, run and pass in the most recent green CI run.
- This is true **independent of PR #690's merge state**. PR #690 did not deliver this — a different,
  unrelated commit (`f7ec5b928`, 2026-08-12, `email-communication-intelligence-r2`) did, a week before #690
  was opened's follow-up activity stalled.
- **Narrow answer to "has PR #690 landed": NO** (open, unmerged, stale, superseded). Per this task's literal
  instruction, that fact alone would call for NO-GO/STOP. This note surfaces that literal answer above.
- **But Phase 2 is not actually blocked**: the capability the NO-GO/STOP branch was designed to protect
  against building on top of missing infrastructure does exist, verified empirically, not assumed. Treating
  this as a hard NO-GO would misrepresent the true state of `master` and stall Phase 2 on a already-resolved
  dependency.
- **Action item for the owner** (per the POML's escalation trigger, "superseded" branch): close PR #690 as
  superseded by `f7ec5b928`/`14a5d462d`, and correct the 2026-08-19 decision record ("land #690 first") which
  predates discovery of the redundancy. This task does not close, merge, or comment on #690 — that is
  explicitly out of scope here.

---

## Constraints honored

- `.github/workflows/sdap-ci.yml` was **not** modified by this task (read-only `grep`/`git log` inspection
  only).
- No raw binary was committed under `tests/fixtures/compose-corpus/` — all fixture verification was read-only
  (`git lfs ls-files`, `ls -la`, existing tracked LFS pointers).
- No PR was merged, closed, or commented on. `gh pr view`, `gh pr diff`, `gh run list/view`, `gh run download`
  (read-only artifact download) were the only GitHub-mutating-adjacent calls used, all read-only.
- No `git commit` / `git push` was run. This note is the task's only write.
