# CI gap: no client-side jest suite runs in CI (707 test files, 39 packages)

> **Filed**: 2026-08-27 by `record-header-and-notepad-r2`
> **Addressed to**: `ci-cd-unit-test-remediation-r1` (owner of `.github/workflows/**` since its 2026-08-27 reactivation)
> **Status**: finding + recommendation. **No workflow was modified and no test was fixed by this project.**
> **Why we found it**: routine `git merge origin/master` into `work/record-header-and-notepad-r2`. Local test run was red; CI was green. That gap is the finding.

---

## 1. The finding in one paragraph

**No GitHub Actions workflow in this repository runs jest.** Not the legacy `sdap-ci.yml`, not `ci-tier1-blocking.yml`, not `ci-tier2-advisory.yml`, not `nightly-health.yml`. There are **707 tracked client test files across 39 packages** that carry a jest `test` script, and CI executes **none** of them. The only client-side gates that exist are Prettier + ESLint. For contrast, the **923 server-side `.cs` test files do run** — in tier2 `Full Unit Tests`, ~27 min. So roughly half the repo's test surface is enforced and the other half is decorative.

The practical consequence, measured: `Spaarke.UI.Components` alone currently has **12–13 failing tests across 8–9 suites**, and at least one has been failing since **2026-06-11 (~11 weeks)**. A PR touching that library gets an all-green board today.

---

## 2. How this was verified (reproducible)

```bash
# 1. No workflow invokes jest or npm test. Returns ZERO matches.
grep -nE 'run:.*(jest|npm (run )?test)' .github/workflows/*.yml

# 2. The only client jobs are lint/format:
#    sdap-ci.yml:330   "Client Quality (Prettier + ESLint)"
#    sdap-ci.yml:332   continue-on-error: true   <-- informational since 2026-06-24
#    ci-tier2-advisory.yml:146,181  "Lint (ESLint + Prettier)"  (prettier --check + eslint)

# 3. Count the unenforced surface (tracked files only, no node_modules):
git ls-files 'src/client/*.test.ts' 'src/client/*.test.tsx' \
             'src/solutions/*.test.ts' 'src/solutions/*.test.tsx' | wc -l
# => 707

# 4. Reproduce the failures in the largest package:
cd src/client/shared/Spaarke.UI.Components && npx jest --ci --maxWorkers=2
# => ~127s wall clock, 3224 tests, 227 suites, 12-13 failing
```

Note item 4: **`test:ci` already exists** in that package (`jest --ci --coverage --maxWorkers=2`). The entry point was authored; nothing ever calls it. Several other packages have the same shape. This is a wiring gap, not a missing-capability gap.

---

## 3. Scope — 39 packages with a jest `test` script

Test-file counts (packages found at depth ≤3; the tracked total is 707):

| Package | Files | | Package | Files |
|---|---:|---|---|---:|
| `shared/Spaarke.UI.Components` | 228 | | `pcf/CommunicationConnections` | 5 |
| `solutions/SpaarkeAi` | 121 | | `shared/Spaarke.Notifications` | 5 |
| `shared/Spaarke.Compose.Components` | 87 | | `solutions/WorkspaceLayoutWizard` | 4 |
| `shared/Spaarke.AI.Widgets` | 42 | | `pcf/ScopeConfigEditor` | 4 |
| `shared/Spaarke.Communication.Components` | 33 | | `pcf/DocumentRelationshipViewer` | 4 |
| `shared/Spaarke.DailyBriefing.Components` | 23 | | `pcf/CommunicationConversationPanel` | 4 |
| `client/office-addins` | 21 | | `pcf/VisualHost` | 3 |
| `code-pages/SemanticSearch` | 20 | | `pcf/CommunicationAttachments` | 3 |
| `solutions/NavigatorPane` | 15 | | `pcf/CommunicationActions` | 3 |
| `shared/Spaarke.AI.Outputs` | 14 | | `pcf/RelatedDocumentCount` | 2 |
| `pcf/SemanticSearchControl` | 11 | | `pcf/RegardingResolver` | 2 |
| `solutions/Notepad` | 8 | | `solutions/DocumentUploadWizard` | 1 |
| `shared/Spaarke.Auth` | 8 | | `solutions/AllDocuments` | 1 |
| `solutions/SmartTodo` | 6 | | `shared/Spaarke.SdapClient` | 1 |
| `shared/Spaarke.Visuals` | 6 | | `shared/Spaarke.DocumentOperations` | 1 |
| `shared/Spaarke.SmartTodo.Components` | 6 | | `pcf/RecordHeader` | 1 |
| `code-pages/PlaybookBuilder` | 6 | | `pcf/MatterHeader` | 1 |
| | | | *(4 more PCFs)* | 1 each |

⚠️ **Only 1 of these 39 packages has actually been measured** (`Spaarke.UI.Components`, below). The other 38 are unknown — they may be clean, or worse. **Establishing that baseline is the first real work item**, and it should happen before anyone commits to a blocking gate.

---

## 4. Measured state of the one package we ran

`Spaarke.UI.Components`: **3,224 tests · 227 suites · ~127s** (`--ci --maxWorkers=2`) · **12–13 failing**.

**The count varies between identical runs (13 → 12).** That matters more than the absolute number: a subset is timing-flaky, and a flaky suite promoted straight to *blocking* will produce false reds and burn the credibility of the new tier system.

### Deterministic failures (real, reproducible)

| Suite | Failure | Root cause |
|---|---|---|
| `services/EntityCreationService.cascade` | `$select` expected 2 columns, got 3 | `_sprk_ai_search_index_value` was added to the BU lookup in **`7142e06da` (PR #380, 2026-06-11)**; the test was never updated. **Red ~11 weeks.** |
| `services/surfaceLaunchRegistry` | registry has +1 / +2 entries vs expectation | Registry entries added without updating the coverage assertions |
| `components/WorkspaceShell/buildDynamicWorkspaceConfig` | expected `480px`, got `100vh` | rowHeight/`contentSizing:"clamped"` precedence changed |
| `utils/todoScoreMappings` | sha256 mismatch on `todoScoring.ts` | **Judgment call, not a mechanical fix.** This is a deliberate lock pinning a composite scoring formula. The file changed; whether that was *intended* is the todo project's call, not CI's. Do not "fix" by re-stamping the hash without an owner. |

### Flaky / DOM-timing (membership varies run to run)

`ConversationView.forward` · `ConversationView.emailInFlow` · `FilePreview/RichFilePreview` · `CommunicationTimeline/TimelineComposeBox` · `SprkChat/citationsIntegration`

All are `waitFor` timeouts under jsdom. Recommend quarantine over repair as the first move.

**None of these belong to `record-header-and-notepad-r2`.** We verified both the sources and the test files are untouched by our branch, and the clearest one predates our merge-base entirely.

---

## 5. 🚨 The constraint that shapes any fix — read before designing

The **shadow window is open** (opened 2026-08-27 20:47 UTC; at the time of writing **3/20 agreeing PRs, 0.2/5 calendar days**). Per the banner in [`projects/INDEX.md`](../../INDEX.md):

- **`ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml` are FROZEN.** Editing any of them changes the configuration under observation and **restarts the 20-PR clock**.
- **`sdap-ci.yml` is scheduled for deletion** once the window closes — investment there has a short payback.
- **New workflow files are explicitly fair game** and are *not* covered by the freeze.

So the obvious-looking move — "add a jest step to tier2" — is the one move that is currently the most expensive. It would reset the evidence gate on retiring `sdap-ci.yml` and enabling branch protection.

---

## 6. Recommended sequencing

**Phase 1 — a new standalone workflow (no freeze impact).**
Add `.github/workflows/client-tests.yml` as a *new* file: matrix over the packages, path-filtered so it only fires on `src/client/**` / `src/solutions/**`, and **`continue-on-error: true` from day one**. This buys visibility immediately without touching a frozen file and without a red board on day one — which matters, because with 12 known failures a blocking gate would be red on merge.

**Phase 2 — baseline all 39 packages.** One run, record pass/fail/duration per package. This is the missing input for every decision below.

**Phase 3 — triage.** Fix the 3 mechanical assertion drifts; route `todoScoreMappings` to its owner as a judgment call; quarantine the flaky five with tracked issues rather than repairing them under time pressure.

**Phase 4 — promote, after the window closes.** Fold the green packages into tier2 (or tier1 for those that are both clean and fast), and flip off `continue-on-error`. Doing this *after* the freeze lifts costs nothing extra; doing it now costs the whole shadow window.

---

## 7. Open decisions — yours, not ours

1. **Budget/shape.** `Spaarke.UI.Components` alone is ~127s. All 39 packages serially would plausibly be 10–20 min. Matrix + path filtering + sharding is a design decision against tier budgets (tier2 lint is currently budgeted <60s).
2. **PCF vs shared libs in one workflow?** They have different jest configs and some PCFs need `@spaarke/ui-components` `dist/` built first (`ensure-dist-fresh.js` prebuild). May warrant separate jobs.
3. **Blocking vs advisory, per-package or fleet-wide.** A per-package allowlist that graduates packages into blocking as they go green is probably kinder than an all-or-nothing flip.
4. **`todoScoring.ts` hash lock** — needs the owning project to say whether the formula change was intended.
5. **Is advisory enough?** An advisory gate nobody reads is how the current state arose. Worth deciding up front what makes it load-bearing.

---

## 8. What this project did and did not do

**Did**: found the gap; verified no workflow runs jest; counted the surface (707 files / 39 packages); measured one package; traced 4 deterministic failures to root cause; confirmed none are ours; identified the shadow-window constraint.

**Did NOT**: modify any workflow; fix, skip, or quarantine any test; measure the other 38 packages.

**Our own stake**: `record-header-and-notepad-r2` added **20 test files (~700 tests)** to `Spaarke.UI.Components` plus a 96-test PCF suite. CI will not run any of them. They pass locally as of the 2026-08-27 master merge (597 shared-lib + 96 PCF), which is currently the only evidence that exists — see PR #843.
