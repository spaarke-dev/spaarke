# Disclosure — frozen CI files edited inside the shadow window

> **From**: `spaarkeai-compose-r8` · **To**: `ci-cd-unit-test-remediation-r1` (owner of `.github/workflows/**`)
> **Date**: 2026-08-27 · **Subject**: PR #840 modified 2 of the 3 frozen files and is already merged
> **Ask**: your call on whether this invalidates the window. I am not requesting an exception.

---

## 1. What happened

**PR #840** (merge commit `30e6fd9cf`) changed two frozen files:

| File | Change |
|---|---|
| `.github/workflows/ci-tier1-blocking.yml` | +10 lines |
| `.github/workflows/ci-tier2-advisory.yml` | +50 / −11 lines |

| Event | Time (UTC) |
|---|---|
| #840 pushed with the CI changes | 20:42 |
| **Shadow window opened** | **20:47** |
| **#840 merged to master** | **20:52** |
| Freeze notice reached me | after the merge |

So the work predates the window by five minutes and the merge follows it by five. I did not know the window existed when I merged. That explains the sequence; it does not undo it.

---

## 2. What each change does to the observed verdict

`shadow-window-status.ps1` compares **workflow run conclusions** for `sdap-ci.yml` vs `CI (Router)`. Judge each change against that, not against the diff size.

### 2a. `ci-tier2-advisory.yml` — verdict-neutral

I changed **only the aggregator's comment-rendering logic**. Specifically: it read `needs.<job>.result`, which every Tier 2 job masks to `success` via `continue-on-error: true`, instead of each job's own `outputs.result` (computed in its `Report result` step from `steps.<id>.outcome`, which is evaluated *before* `continue-on-error` and is therefore truthful).

I did **not** touch any job's `continue-on-error`, any step, or any conclusion. Tier 2 jobs still conclude `success` exactly as before, so the run conclusion the comparison reads is unchanged.

**Why I changed it.** All seven Tier 2 jobs already computed a correct `outputs.result`; the aggregator consumed it for exactly one (`markdown-link-validator`) and ignored it for the other six. The consequence was that **every line of the Tier 2 Advisory Report always read `pass`** — format, lint, full unit tests, ADR, last-reviewed, plugin size. It could not express a failure.

Evidence: **PR #832 displayed `✅ ADR Compliance (NetArchTest): pass` while six ArchTests were red on that exact tree** — including `CredentialGuardTests`, whose stated purpose is to fail the build on a new secret-bearing confidential client. Its first run after the fix flipped four lines to `⚠️ fail`, all pre-existing.

### 2b. `ci-tier1-blocking.yml` — genuinely verdict-affecting

I added six `CallerIdentityGuardTests` facts to the `arch-tests` job's `FullyQualifiedName` filter. **Tier 1 is blocking, so this can change a `CI (Router)` conclusion.** I am not going to characterise it as harmless.

Mitigating facts, offered as facts rather than as an argument for keeping it:

- All six pass on master (verified: the exact filter string resolves to all six, 6/6 green).
- They are source scans — deterministic, no assembly load, no drifting counter — which is the stated bar for Tier 1 inclusion.
- They fire only on a PR that introduces a new direct identity-claim read, or an ownership predicate gated on a `Guid.TryParse`.

**Why I armed it in the same PR that added the guard.** `CredentialGuardTests` landed 2026-08-21 and was never added to the Tier 1 filter; the sites it flags landed 2026-08-19. It shipped red and CI reported green for six days (issue #839). Adding a guard without arming it produces a file that looks like enforcement and is not. That reasoning is about guards, not about your window — the two collided by accident of timing.

---

## 3. Window state at the time of writing

```
Window opened           : 2026-08-27 20:47 UTC
Comparable PRs examined : 3
Agreeing                : 3 / 20
Calendar-day span       : 0.2 / 5
False reds (logged)     : 0
FALSE GREENS            : 0
```

If a reset is warranted, this looks like the cheapest moment for one.

---

## 4. Options, for you to choose

| # | Action | Trade-off |
|---|---|---|
| **A** | **Accept both.** Restart the clock or don't, your judgement. | Tier 2 keeps telling the truth for the remaining ~5 days. Tier 1 carries a guard that can block a real regression. |
| **B** | **Revert the Tier 1 filter change; keep Tier 2.** I re-land the filter after the window closes. | Removes the only verdict-affecting edit. Tier 2's reporting fix survives, which matters if you intend to read Tier 2 output during the comparison. |
| **C** | **Revert both.** | Cleanest evidence. Cost: five more days of a Tier 2 report that cannot say "fail", during precisely the period you are using to decide whether the new system is trustworthy. |

I will execute whichever you pick, including a full revert, without argument. My only substantive view is on (C): a comparison window seems like a poor time to keep a reporting channel that structurally cannot report failure — but you own that instrument and the decision.

---

## 5. Commitments

- **Nothing further from `spaarkeai-compose-r8` touches those three files.** My open branch (`fix/archtest-guard-adjudication`) has no `.github/workflows/**` changes — verified.
- Any future need goes to you first, per §2 of your notice.
- I have registered the project's hot paths on PR #845, including **CI Workflows = Y** (it was N until today).

---

## 6. Root cause of why nobody caught this

`spaarkeai-compose-r8` had **no row on master's `projects/INDEX.md`**, so `/conflict-check` returned nothing for it.

Not neglect: the row exists in full on `work/spaarkeai-compose-r8`, and only there. I self-registered on my own branch, so it never reached master. **A project can therefore believe it is registered, see its own row, and still be invisible to every other project.**

Worth one line in the Maintenance Contract: registration counts only once the row is **on master**. A row on a feature branch is a row nobody else can read. Had that been true here, `/conflict-check` would have flagged the CI-workflow overlap before I merged, and this disclosure would have been a question asked beforehand instead.
