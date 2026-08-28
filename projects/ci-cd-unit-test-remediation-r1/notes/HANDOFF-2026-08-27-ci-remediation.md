# Handoff — CI/CD Remediation, 2026-08-27

> **Last Updated**: 2026-08-27 (by `context-handoff`)
> **Recovery**: read "Quick Recovery" first, then "Do not re-derive these".
> **Full narrative + evidence**: <https://claude.ai/code/artifact/d84bbd8d-5b6e-45ca-8de9-f5a39fe660ae>

This is **ad-hoc direct repair, not a tracked project** — an explicit owner decision. It is filed here because the remaining work *is* this project's tasks 071 / 075 / 077, not because the project was revived. `ci-cd-unit-test-remediation-r1` remains dormant.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Work** | CI/CD remediation — master was red, projects reporting breakage |
| **Status** | **All six PRs merged.** master green except one documented owner-action item (below). |
| **Next Action** | Start the **shadow-observation window** (step 2 below). Nothing else should land until it completes. |
| **Branch when saved** | `docs/sidecar-42a-amendment` (PR #837, docs-only) |

> **Updated 2026-08-27 16:2x** — #833, #835, #836 merged. Verified on master: tier1 carries exactly its 8 jobs with **no** `build-test`; tier2 carries `full-unit-tests`; `Deploy Office Add-ins` flipped red → green. One workflow is still red on master and it is **not** a regression — see "Remaining red" below.

### Remaining red (owner action, not a code defect)

`Build Provisioning Sidecar` fails on master at the `Azure login (OIDC)` step:

```
AADSTS700213: No matching federated identity record found for presented
assertion subject 'repo:spaarke-dev/spaarke:ref:refs/heads/master'
```

Everything #836 fixed passes — build, Trivy, size (212 MB compressed). The image simply is not published. It was green on the PR because the publish leg is gated `if: github.event_name != 'pull_request'`.

The ACR itself **is** provisioned (`sprkcontrolplanedevacr.azurecr.io`, var populated 2026-08-21) — the workflow's comment claiming otherwise was stale and is corrected in #837. The one missing piece is the federated credential. Fix: `docs/guides/GITHUB-ACTIONS-AZURE-OIDC-SETUP.md`, five subjects, one `az` loop.

Deliberately **not** papered over with a `vars.SIDECAR_ACR_LOGIN_SERVER != ''` skip — that goes green while silently never publishing.

### Owner's north star (binding — drove several decisions)

1. Fix it **right the first time** — no short-term rework.
2. CI must **not hold up high-frequency master builds and pushes**.
3. **No constant reds.**
4. **No full-time CI/CD manager** — must be low-maintenance.

---

## State

### Merged
| PR | What |
|---|---|
| #828 | tenant-isolation gate green (I1 regression, I5 comment-blind scan, I2 waiver) |
| #829 | retry classifier — measure determinism instead of assuming it |
| #830 | trivy pin, npm convention, manifest trigger, nightly graph-parity gating |

| #833 | 3 gates → tier1; full suite → tier2. Verified on master post-merge. |
| #835 | build `@spaarke/auth` before add-ins + trigger path. `Deploy Office Add-ins` now green on master. |
| #836 | sidecar on .NET 10 LTS + measured size gate. |

### Open
| PR | Branch | What |
|---|---|---|
| **#837** | `docs/sidecar-42a-amendment` | docs-only. design.md §4.2a → v3.7 (base image + measured 212 MB) + corrects the stale ACR comment. |

### Open issue
- **#827** — thread tenant through the job spine (RecordMatchService I2 waiver expires when `spaarke-records-index` goes multi-tenant). Prerequisite for shared tenancy, not a follow-up.

### master workflow health (post-merge, 2026-08-27)
```
SDAP CI                     success     (was 60% failure rate)
CI (Router)                 success
nightly-health              success     (9/9 jobs — first fully green night)
Deploy Office Add-ins       success     <- flipped by #835
Build Provisioning Sidecar  failure     <- build/Trivy/size all PASS; only the
                                           publish leg fails, on the missing
                                           OIDC federated credential. Owner
                                           action; see "Remaining red" above.
```

---

## Next actions, in order

1. ~~**Merge #833, #835, #836.**~~ ✅ **Done 2026-08-27.** master runs verified afterwards; results in the health block above.
2. **Shadow window — 20 agreeing code PRs, floor ≥5 calendar days.** Run tier1 and `sdap-ci.yml` in parallel and confirm the verdicts agree.

   **Binding, per `spec.md` MUST Rules + "Shadow-window exit criterion".** Amended 2026-08-27 (Path B, owner-approved) from the original *"14 days of new-tier stability"*. Calendar time is a proxy for evidence and a poor one — it advances whether or not anything is being tested, while every server-code PR pays `sdap-ci.yml`'s **~40-minute** duplicate leg for the window's full duration. Counting agreeing PRs ties the exit to the thing actually being measured.

   > Two earlier revisions of this handoff were wrong here: the first said "a few days" (understated the then-binding 14-day MUST); the second said "14 days" (correct at the time, superseded the same day by the amendment). Current criterion is the one stated above.

   **Counts toward the 20** only if the PR is a **code PR** (`docs_only == false`; on docs-only PRs tier1/tier2 are correctly skipped so there is no verdict to compare) **and** both systems reached a terminal conclusion (cancelled/superseded runs don't count).

   | `sdap-ci` | tier1 | Meaning |
   |---|---|---|
   | red | **green** | **FALSE GREEN — disqualifying.** Reset the count to zero and diagnose. |
   | green | **red** | False red. Doesn't disqualify, but log it and understand it before branch protection. |
   | agree | agree | Counts. |

   **≥5 calendar-day floor** so a single high-volume day can't satisfy it — some failure modes only appear across day boundaries.

   Related rule, NOT amended: `spec.md` — MUST NOT restore the `Release` matrix before Phase 2 deletion has merged AND the surviving suite is green ≥7 days. Different clock.

      **Check progress**: `pwsh scripts/ci/shadow-window-status.ps1`. Read-only, holds no state — every
   number is derived live from the GitHub API, so it cannot drift and needs no upkeep. No cron, no
   workflow, no tracking file. Its `-Since` default is load-bearing: it starts the window after PR #841
   (the last CI-config change). Counting earlier would score the pre-remediation period, when `sdap-ci`
   itself was failing ~60% of runs — that reads as a wall of "false greens" that are nothing of the
   sort, and the window could never close. Verified: 5 such rows dated 08-18..08-26.

   **Cannot be compressed below the floor**; see "the port broke twice" below for the evidence base.
3. **Delete `sdap-ci.yml`** (tasks 071 / 075 / 077). Removes the duplicate *and* its ~30 min leg.
4. **Enable branch protection** with `CI / Router` as the single required check. **OWNER DECISION — do not do unprompted.** This is what makes the north star hold; without it everything drifts red again. Sequence last: flipping it while a flaky gate remains blocks the team rather than annoying them.

### Still owed to the owner
- ~~**`design.md` §4.2a amendment**~~ ✅ **Written 2026-08-27 — PR #837.** One correction to what this handoff originally said: it filed the amendment as **Path A** (project-scoped exception). That was wrong. Path A means the guidance is still correct in general and we have a narrow reason to deviate — but `powershell:7.4-mariner` is not a valid-but-inconvenient choice, it is **end-of-life** (MCR publishes a `7.4-mariner-2.0-EOL` tag beside the pinned one). The design text was factually wrong, so it was written as **Path B (amendment)**. Filing it as an exception would have left a dead image standing as the documented default. Heuristic worth keeping: *Path A when the rule still holds and you are the special case; Path B when the rule itself has stopped being true.*
- **Five Azure OIDC federated credentials** — owner action, cannot be done from here. Runbook shipped at [`docs/guides/GITHUB-ACTIONS-AZURE-OIDC-SETUP.md`](../../../docs/guides/GITHUB-ACTIONS-AZURE-OIDC-SETUP.md). Only remaining red workflow after #835/#836 merge (`publish-provisioning-arm-artifacts`).

---

## Do not re-derive these (hard-won, cost real time)

**Empty string ≠ unset in GitHub Actions env.** Overriding a workflow-level var to `''` at job level makes it *present-but-empty*, which is worse than either state. Three consistent data points on tier1's `APPLICATIONINSIGHTS_CONNECTION_STRING`:
```
absent (tier2, sdap-ci)        full suite passes, ~19 min
present + valid (tier1 run 1)  tests run, process never exits -> 6h timeout, reads `cancelled`
present + empty (tier1 run 2)  275 startup failures
```

**The full test suite belongs in tier2, never tier1.** tier1's contract is p95 ≤ 3 min blocking. `build-test` is ~28 min. Porting it there was my mistake and caused both failures above. tier2's `full-unit-tests` already ran the same suite; it absorbed the two-pass classifier instead. **Do not move it back.**

**Coverage collection is the leading suspect for the 6h hang.** sdap-ci's `build-test` used `--collect:"XPlat Code Coverage"`; tier2's job does not and completes in 19 min with the same work. The collector hooks process shutdown. Coverage is observation-never-a-gate per ADR-038 and nightly-health already has a coverage job — deliberately not re-added.

**A `dotnet test --filter` term can match nothing and read as success.** Verified real: `AuthorizationIntegrationTests.Authorization_ChecksDifferentPolicies_PerEndpoint` matches no test in the current assembly (skipped, then removed). The classifier now emits `retry_methods` and the verdict step fails INCONCLUSIVE if any expected method did not execute. **Do not remove that guard** — it is the difference between "retried and passed" and "silently never ran".

**Theory names break `--filter`.** A TRX `testName` is `Class.Method(endpoint: "/api/x")`; the parens/quotes/comma break parsing. Names are truncated at the first `(`.

**PowerShell base images all sit on dying .NET.** 7.4→.NET 8 (EOL 2026-11-10), 7.5→.NET 9 (STS), 7.6→.NET 10 LTS but **no base image published**. Hence #836 inverts it: base on `dotnet/runtime:10.0-azurelinux3.0`, install pwsh 7.6.5 as a pinned dotnet tool. **Do not "simplify" this back to a `powershell:*` base** — that was evaluated and rejected; it buys weeks and guarantees a repeat.

**Sidecar size: 436 MB uncompressed = 212 MB compressed**, ceiling 250 MB. The old gate compared uncompressed against a 350 MB proxy and failed a compliant image. It now measures `docker save | gzip`.

**Docker is a deliberate outlier, not sloppiness.** One Dockerfile in the repo. DS-1b audited ~29 shell-out collaborators; exactly one (Exchange `ApplicationAccessPolicy`) has no .NET equivalent — no Graph API for it or its successor. The container is a quarantine. The one alternative never evaluated is in-process PowerShell hosting (`Microsoft.PowerShell.SDK`); **recommended against** — a vendor module that hangs inside the provisioning worker is worse than a connection-refused from a container.

---

## Two things I got wrong (so they are not repeated)

1. **Ported `build-test` into tier1** — blew its stated 3-min budget by ~10× and duplicated tier2. Corrected in #833's second commit; net result is *fewer* jobs than before the port started.
2. **Called F4 "one deterministic test failure" and praised the classifier** in the original assessment. Neither was true — every flagged test passed locally, and the classifier defined "deterministic" as "not on the allowlist". Corrected in the artifact; findings F1, F4, F6 carry correction notes.

---

## Gotchas about the process itself

- **The port broke twice on environment differences invisible in the diff**, despite being byte-identical copies. That is the entire argument for the shadow window in step 2.
- **A finished fix sat unmerged for six hours** while master was red (#828's underlying commit `8e8f90b97`). No branch protection = no back-pressure to land CI fixes. This is the concrete case for step 4.
- **I1 was a regression, not an unfixed bug** — commit `1834b77bc` fixed it on 2026-08-16 and five docs still record it as fixed. The code regressed and no gate caught it, because the gate that would have was already red for other reasons.
