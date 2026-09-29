# Task 108 — ISS-018 (#995): an unsecure must not report a failed share read as a clean sweep

> **Status**: code complete and reviewed at `d0845724d`; **verification now complete** — ArchTests,
> the criterion-4 perturbation, and the publish-size A/B were all re-run against the committed code on
> 2026-09-21 and passed (§8). The POML's `<status>` element is left for the owning session to flip.
> **Diff**: 5 files, +477 / −42. No production behaviour outside `/unsecure-project` is touched.

---

## 1. What shipped

`UnsecureProjectEndpoint.RevokeAllSharesAsync` enumerated a record's POA shares with the **soft** read
`IDataverseRecordShareService.GetPrincipalAccessAsync`. That read answers an **empty list** when it
fails, so "this record has no shares" and "the shares could not be read" arrived indistinguishable.
Nothing was revoked, the endpoint answered `200` with `sharesRevoked: 0`, and an unsecure could leave
every share in place silently.

The change:

- Enumeration moved to the **strict** read `GetPrincipalAccessOrThrowAsync` (complete answer or
  exception — built by task 063 for exactly this class of defect), with a **soft fallback** so partial
  progress is still made when the strict read refuses.
- New `private readonly record struct ShareSweep(int Revoked, bool Complete)`; enumeration extracted
  into `EnumerateSharesAsync` so the sweep reads *enumerate → revoke → report*.
- `UnsecureProjectResponse` gains **`bool? SweepComplete`** — `true` every share enumerated and
  removed, `false` a sweep ran but could not account for every row, **`null` no sweep was attempted**.
- Tests: three fixture knobs, a `LogCapture` `ILoggerProvider`, and 6 new tests (suite 13 → **19**).

## 2. The premise, verified against source before obeying it

The POML's premise held, with one correction worth recording. It says the existing `catch` "cannot fire
for the failure it names". Strictly, that catch was **not wholly dead** — `SendGetAsync` or
`ReadFromJsonAsync` can still throw on transport failure or malformed JSON. What it could not fire for
were the *status-code* and *unreadable-object-type-code* failures, which the soft read swallows
internally (`DataverseWebApiService.cs:1222-1245`: empty on unreadable type code, on non-success status,
and on null rows). The defect stands; its blast radius is narrower than "the catch is unreachable".

The POML's cited line range `:301-340` was exact.

## 3. Decisions taken (POML step 1 required this to be recorded)

### 3.1 Partial progress, not refusal — and why this was *not* an escalation

The POML arms an escalation trigger if the partial-progress question "turns out to be a product
question". It is not one, and the reason is structural rather than preferential: by the time the sweep
runs, ownership has **already moved and been read back** (`:164-220`). The record is reachable
regardless. Refusing the sweep outright would leave **every** share in place; sweeping what can be
enumerated removes some stale access and reports the shortfall. There is no trade-off to put to an
owner. Both review gates independently agreed. GitHub #995's own text had reached the same design
("the strict read plus a reported partial-failure count") before this task read it.

### 3.2 `sprk_issecure` is still cleared on an incomplete sweep

Per ADR-003, Secure suppresses the **derived-member and org-expansion** terms; it does **not** suppress
explicit grants or Dataverse's own answer. A surviving POA row *is* Dataverse's answer. So holding the
flag back buys **no protection against the surviving share**, while manufacturing the half-applied
"no longer isolated yet still reads as secure" state the endpoint already treats as a defect.

Verified against ADR text rather than paraphrase, by me and independently by the adr-check gate:
`.claude/adr/ADR-003-authorization-seams.md:39-40`, the full term list at
`docs/adr/ADR-003-lean-authorization-seams.md:148-163` ("Secure … suppresses **terms 3 and 4**"), and
the implementation at `AccessibleRecordSetService.cs:237-256`.

**Caveat the first draft omitted** (adr-check W1): clearing the flag is *not free* — it also drives
container placement (`SecureContainerDecision.cs:60-78`). A secure record gets its own container; a
non-secure one may fall back to the owning BU's **shared** container, and SPE permissions are
additive-only, so that is not retractable. That consequence is intended for a completed unsecure and is
**accepted** for an incomplete one, because the alternative is the half-applied state above. The doc
comment was narrowed from "buys exactly no protection" to "no protection *against the surviving share*"
and now states the container consequence.

### 3.3 `bool?` rather than `bool`, and no default

See §4 — this was the fix for the Critical review finding, not an initial design choice.

## 4. Step 9.5 — both gates, and the Critical defect they caught

Two independent Opus reviews (`code-review`, `adr-check`). **Both found the same Critical defect, which
this task had introduced.**

**C1 / V1 — `SweepComplete` reinstated ISS-018 on the retry path.** The idempotent early-return
(`:132-143`) constructed the response with four positional arguments, so `SweepComplete` silently took
its `= true` default — but that path enumerates *nothing*. The damaging sequence is the operator's
natural remediation:

1. Incomplete sweep → flag cleared (§3.2) → `200 {sharesRevoked: N, sweepComplete: false}`; shares survive.
2. Operator reads `sweepComplete: false` and **retries**.
3. `sprk_issecure` is now `false` → early return → `200 {sharesRevoked: 0, sweepComplete: true}` — a
   *clean sweep of zero* over a record whose shares are still there.

That is verbatim what this task's own new test calls "THIS is the defect". Root cause: **a fail-OPEN
default on a fail-CLOSED field.** I had reasoned the `true` default was "truthful — no sweep was
needed", which is correct for a never-secure record and **wrong** for the retry, the case that matters.

**Fix (path C):** the default was **deleted** so the compiler forces every construction site to declare
completeness, and the field became `bool?` so "no sweep attempted" (`null`) is distinct from "ran but
incomplete" (`false`). Fixing the cause, not the instance. A test now pins the two-call retry sequence.

Other findings applied:

| # | Finding | Disposition |
|---|---|---|
| W2 | The 500 `detail` prose asserted "shares revoked" while the extension said `sweepComplete:false` | Fixed — prose is now conditional on `sweep.Complete`; stale comment corrected |
| W3 | `when (!ct.IsCancellationRequested)` tests the **token's state, not the exception's identity**, so a real read failure coinciding with a client disconnect was swallowed; it had also removed the half-applied-state diagnostic | **Both filters removed.** Review overturned my decision (d) and was right |
| W4 | Log assertions matched only `Warning` + a guid; provisioning writes such lines too | Fixed — both anchored on `[UNSECURE]` |
| S2 | `RevokeAllSharesAsync` had two responsibilities and subtle definite-assignment | Fixed — `EnumerateSharesAsync` extracted |
| S4 | `sharesRevoked` not asserted against `Revokes.Count`; no test on the idempotent path | Both added |
| W3 (adr) | `reasonCode`/`traceId` vs ADR-019's `errorCode`/`correlationId` | **Pre-existing**, recorded as task-097 S6/S7. Not introduced here; no action |
| W2 (adr) | ADR-019 has no partial-success guidance (200 + body flag) | Path B candidate; does not block |

Code-review verified the four new tests **by mutation** — each kills a mutant no other test kills — and
reported **0 AI code smells**.

## 5. CLAUDE.md §11 — `LogCapture`

- **Existing?** Not "none". `CapturingLoggerProvider`
  (`tests/integration/Sprk.Bff.Api.IntegrationTests/PlaybookByNameDeprecationTestFixture.cs:267`) is the
  same shape. The repo-wide census is **seven** ad-hoc log-capture doubles.
- **Extension?** No. That one lives in a **different assembly** with no project reference, and the
  same-assembly helpers (`CapturingLogger`, `ListLogger<T>`) are `private sealed` `ILogger<T>` doubles
  nested in test classes — unreachable from another file and structurally unable to capture from an
  endpoint inside `WebApplicationFactory`. Verified by the code-review gate.
- **Cost of doing nothing?** Concrete: **half of ISS-018 was that the warning could never be written.**
  Asserting only the response would leave the operator-signal half unverified.

**Deferred follow-up (not done here):** hoist one provider into `tests/integration/Shared/` — already
globbed into this assembly at `Sprk.Bff.Api.Tests.csproj:87` — and delete the duplicates. Both gates
rated this a non-blocking *placement* suggestion; doing it mid-task would have been discretionary scope
during an unstable build.

## 6. §6.5 path A — regression-test placement

`tests/CLAUDE.md` says every bug fix gets `tests/integration/regression/Issue{N}_*Tests.cs`. No
`Issue995_*Tests.cs` was added. Taking **path A (documented project-scoped exception)**: regression
coverage for #995 lives in `SecureProjectShareTests.cs`, because the POML's step 3 directs tests to "the
unsecure endpoint's existing suite", `tests/integration/data-mutation/**` **is** a KEEP path (ADR-038 §2
path #3 — writes/transactions, compiled via `Sprk.Bff.Api.Tests.csproj:188`), and splitting one
endpoint's coverage across two KEEP paths fragments it. Surfaced rather than left silent, per §6.5.

## 7. Verification — with the scope of each measurement stated

| Check | Result | Scope |
|---|---|---|
| Build (5 projects) | 0 warnings / 0 errors | **post-review code** (`d0845724d`) |
| Affected suite | **19 / 19 passed** | **post-review code** (`d0845724d`) |
| ArchTests | **323 / 323 passed** | ✅ **post-review code** (`d0845724d`), re-run 2026-09-21 |
| Perturbation P1 (criterion 4) | GREEN 19/19 → RED **Failed 2 / Passed 17 / Total 19**, exactly `Unsecure_WhenTheShareReadCannotBeCompleted_ReportsAnIncompleteSweep` + `Unsecure_RetriedAfterAnIncompleteSweep_DoesNotThenClaimACleanSweep` → restored byte-identical (`git diff --stat` empty) → re-confirmed 19/19 | ✅ **post-review code** (`d0845724d`), re-run 2026-09-21 |
| Publish size | pre-task `b42d6471e` 45.56 MB vs post-task `d0845724d` 45.56 MB (47,774,597 vs 47,775,951 bytes); **delta +1,354 bytes (+0.0013 MB)**; 215 files both sides; PDBs included (4 each); `Compress-Archive -CompressionLevel Optimal`, fresh short-path worktrees (`C:\wt108a`, `C:\wt108b`), zipped into the worktree root (not `C:\` itself, which refused the zip write) | ✅ **post-review code**, measured 2026-09-21 |
| CVE (`--vulnerable --include-transitive`) | no vulnerable packages | current |
| Conflict sync (Step 10.6) | master 0 commits since merge-base; touched none of my files | current |
| Drift baseline | 116 POMLs / 116 rows, rc=0 | current |

**Publish-size attribution correction.** The naive branch-vs-master figure is **+0.10 MB**, but that is
the *whole project's* divergence across ~476 files. Re-publishing `wt108b` at the same commit with the
two source files reverted isolates this task at **0.00 MB**. Reporting +0.10 as task 108's contribution
would have repeated the 2026-09-02 attribution error in miniature. The 2026-09-21 pre-task-vs-post-task
A/B (`b42d6471e` vs `d0845724d`, §10) directly re-confirms this: **+0.0013 MB**, effectively zero.

## 8. Verification status (updated 2026-09-21 — all three gaps closed)

All three measurements this section previously flagged as pre-review-only have now been re-run against
the **committed, post-review code** at `d0845724d` (parent `b42d6471e`), from a healthy machine, per the
§10 follow-up:

- **ArchTests**: 323/323 passed, matching the prediction exactly. No route was added (unsecure-project
  already existed), no DI change, no package change — the number held.
- **Criterion-4 perturbation**: run GREEN (19/19) on the committed code, then `UnsecureProjectEndpoint.cs`'s
  `EnumerateSharesAsync` had its strict call (`GetPrincipalAccessOrThrowAsync`) swapped for the soft call
  (`GetPrincipalAccessAsync`) at the single call site — the minimal revert to "soft enumeration". Re-run:
  **Failed 2 / Passed 17 / Total 19**, and the two failures were exactly the two predicted tests, not two
  others (the "no shares can be enumerated at all" test and every other test in the suite still passed,
  because they either fail the soft read too via `SoftShareReadSucceeds=false` — same outcome either way
  — or exercise the happy path, unaffected by which read is called). The file was then restored from a
  pre-edit byte copy; `git diff --stat` on the file showed **zero changes**; the suite was re-run and
  confirmed **19/19** again.
- **Publish size**: two fresh worktrees at short paths (`C:\wt108a` = `b42d6471e`, `C:\wt108b` =
  `d0845724d`), published and zipped identically. 215 files both sides (equal — trustworthy). Delta
  **+1,354 bytes (+0.0013 MB)**, essentially zero, consistent with §7's earlier finding. Both worktrees
  removed via `git worktree remove` after the measurement (no node_modules junction was present — the
  worktrees only ever ran `dotnet publish`, no npm install).

Criterion 4 (build green; affected suites and ArchTests green; publish size reported against a fresh
master and ≤60 MB) and criterion 5's build/test wording are now **fully met** against the committed code.
This task can be marked verified. The corrected SHA label: the notes previously cited a post-review
commit `b9a57aecf`, which **does not exist in this repository** (`git cat-file -t b9a57aecf` returns "Not
a valid object name") — it was a laptop working SHA that never survived past the laptop described in the
old §8. The real, committed post-review commit is **`d0845724d`** (parent `b42d6471e`), used throughout
this section and §7.

## 9. Lessons

1. **A fail-open default on a safety field is the bug.** `SweepComplete = true` looked harmless and
   reinstated the exact defect the field existed to remove. Deleting the default — making the compiler
   ask — was the real fix.
2. **Write the expected result into the command.** A `--no-build` run after a *failed* build reported
   `Failed: 0` over stale binaries. It was caught **only** because the command said "expect 19" and
   returned 17. Without that, unverified code would have been recorded as green.
3. **Never trust an exit code you piped.** `dotnet … | tail` and `cmd; echo $?` both report the *last*
   command's status. Two apparent successes this session were `tail`/`echo` exiting 0.
4. **A broken build is not a result in either direction** — this project's existing rule, re-earned.
   Six distinct error codes, zero of them about the code.
5. **When a build breaks after a clean, restore and rebuild plainly.** My `rm -rf obj bin` started the
   cascade, and each "fix" added a variable: `-p:ProduceReferenceAssembly=false` *moved* the failure
   from `obj/**/ref/` to `bin/**/` by changing where consumers look, and re-fingerprinted the graph so
   MSBuild gutted `Spaarke.Core` and `Sprk.Bff.Api` entirely. Several of the ten failures were mine.
6. **A double is only evidence to the extent it refuses what Dataverse would refuse.** The fixture's
   strict read delegated to the soft read and could never throw, and `RevokeAccessAsync` could never
   fail — so the production change *and* acceptance criterion 3 were both unobservable before the knobs
   existed. Criterion 3 would have passed without testing anything.

## 10. Follow-ups

- ~~Re-run ArchTests, the criterion-4 perturbation, and the publish measurement~~ **DONE 2026-09-21**,
  against the committed code `d0845724d` (parent `b42d6471e`), from a healthy machine. Results in §8:
  ArchTests 323/323; perturbation GREEN 19/19 → RED Failed 2/Passed 17/Total 19 (exactly the two
  predicted tests) → restored byte-identical → 19/19 again; publish delta +0.0013 MB, 215 files both
  sides. All three measurements this row used to gate are closed; no outstanding re-run remains.
- Hoist one log-capture provider into `tests/integration/Shared/` and delete the duplicates (§5).
- ADR-019 partial-success guidance (200 + body flag) — path B candidate (§4 W2-adr).
- No test asserts `sweepComplete` on the 500 flag-not-cleared ProblemDetails; that extension member is
  untested surface. Would need a fixture knob to fail the `sprk_issecure` update.
