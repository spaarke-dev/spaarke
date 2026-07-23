# Cross-Satellite Seam-Fork Verification — Task AIR2-072

> **Task**: AIR2-072 (Phase H, gate G-R2-D hardening) · **Date**: 2026-07-10
> **Scope**: verify no satellite project (Compose r2, future Insights Engine refurbish) forked an
> AI-internal seam — core is the ONLY project that modifies `Services/Ai/` internals; satellites
> consume only through `Services/Ai/PublicContracts/` (spec FR-D-03, ADR-013, ADR-039).
> **Result: ZERO forks found. No STOP escalation required.**

---

## 0. Seam enumeration (step 0)

Per `notes/SEAM-STATUS.md` + `notes/seam-publication-ordering.md` (task 017), the seven published
R2 seam shapes under `Services/Ai/PublicContracts/`:

| Seam | Contract file |
|---|---|
| `ComposeDisposition` (+ `ComposeDispositionFrame`) | `ComposeDisposition.cs` |
| `OutcomeCard` | `OutcomeCard.cs` |
| `ContextEnvelope` | `ContextEnvelope.cs` |
| `GateDecisionV2` | `GateDecisionV2.cs` |
| `JobAwareCompletionState` | `JobAwareCompletionState.cs` |
| `MemoryItem` | `MemoryItem.cs` |
| `TraceEvent` | `TraceEvent.cs` |

AI-internal namespaces/types satellites MUST NOT reference directly (must go through the facade
instead): `Sprk.Bff.Api.Services.Ai.IOpenAiClient`, `Sprk.Bff.Api.Services.Ai.IPlaybookService`,
`Sprk.Bff.Api.Services.Ai.Nodes.*` (executors), `Sprk.Bff.Api.Services.Ai.PublicContracts.IConsumerRoutingService`
(the capability-invocation router — satellites dispatch through the session-dispatch seam, never
the router directly), `Sprk.Bff.Api.Services.Ai.Chat.TypedHandlerResumeExecutor`.

---

## 1. Grep sweep (step 1) — satellite worktrees checked

### `spaarkeai-compose-r2` (`C:/code_files/spaarke-wt-spaarkeai-compose-r2`, branch `work/spaarkeai-compose-r2`) — the only currently-active satellite

**(a) Divergent-copy check** — searched for a `class`/`record` declaration of any of the seven seam
type names anywhere under that worktree's `src/`:

```
grep -rnE "^\s*(public|internal)\s+(sealed\s+)?(record|class)\s+(ComposeDisposition|OutcomeCard|GateDecisionV2|JobAwareCompletionState|ContextEnvelope|MemoryItem|TraceEvent)\b" src/
```

Result — **every** declaration is under `Services/Ai/PublicContracts/`, zero elsewhere:

```
.../Services/Ai/PublicContracts/TraceEvent.cs:94:public sealed record TraceEvent
.../Services/Ai/PublicContracts/OutcomeCard.cs:56:public sealed record OutcomeCard
.../Services/Ai/PublicContracts/MemoryItem.cs:73:public sealed record MemoryItem
.../Services/Ai/PublicContracts/JobAwareCompletionState.cs:111:public sealed record JobAwareCompletionState
.../Services/Ai/PublicContracts/GateDecisionV2.cs:268:public sealed record GateDecisionV2
.../Services/Ai/PublicContracts/ContextEnvelope.cs:57:public sealed record ContextEnvelope
.../Services/Ai/PublicContracts/ComposeDisposition.cs:66:public static class ComposeDisposition
.../Services/Ai/PublicContracts/ComposeDisposition.cs:181:public sealed record ComposeDispositionFrame
```

**No divergent copy of a core seam DTO exists in the Compose r2 worktree.**

**(b) Forbidden-internal-reference check** — searched `Services/Compose/**` (Compose's own domain)
for the five forbidden AI-internal identifiers:

```
grep -nE "IOpenAiClient|IPlaybookService|Services\.Ai\.Nodes|IConsumerRoutingService|TypedHandlerResumeExecutor" src/server/api/Sprk.Bff.Api/Services/Compose/*.cs
```

Every hit is inside an XML doc comment (`/// <c>IOpenAiClient</c>...`) explicitly documenting that
the type does **not** depend on it (e.g. `AnnotationReanchorService.cs:29`, `ComposeEditBatch.cs:36`,
`IComposeService.cs:34/38`) — **zero actual `using`/field/constructor-parameter dependencies** on
any forbidden type. Confirmed separately for the two newest, currently-in-flight files
(`ComposePushSavePreview.cs`, `ComposePushSaveStatusStore.cs`, part of Compose task 055): the only
`using` from the AI domain is `Sprk.Bff.Api.Services.Ai.PublicContracts` (sanctioned).

**(c) File-identity check on `PublicContracts/*.cs` between the two worktrees** — a raw
`diff -rq` initially reported 11 files as "differing," but `diff -B -b` (ignoring blank
lines/whitespace) on the largest of these (`GateDecisionV2.cs`, 24937 vs 24381 bytes) showed **zero
content diff** — the byte-count gap is a CRLF-vs-LF line-ending artifact between the two Windows
worktree checkouts, not a real edit. Corroborated at the git level: `git diff work/spaarkeai-compose-r2
HEAD -- .../GateDecisionV2.cs` (core HEAD `931fef171` vs the compose-r2 branch tip) is **empty**, and
`git log` shows the last commit to touch each of these files on both branches is the same core-authored
commit (e.g. `79ddba20f` for `MemoryItem.cs`). Compose r2's own `git status --short` shows **zero
uncommitted changes** under `Services/Ai/PublicContracts/`. This is staleness (compose-r2 hasn't yet
merged core's most recent commits), not a fork.

### `ai-spaarke-insights-engine-r2` / `ai-spaarke-insights-engine-widgets-r1` (the two existing
Insights Engine worktrees — NOT the "future Insights refurbish" the POML anticipates)

Grepped both for all seven seam names + the five forbidden AI-internal identifiers. **Zero matches
on any of the seven R2 seam type names** in either worktree. The `IOpenAiClient`/`IPlaybookService`
hits that do appear (~100 files each) are each worktree's own **pre-existing, pre-R2** copies of
`Services/Ai/IOpenAiClient.cs`, `IPlaybookService.cs`, etc. — these worktrees' last commits
(2026-06-27 and 2026-06-24 respectively) **predate R2 seam publication** (tasks 010–016 landed
2026-07-08), so there is nothing from R2 for them to have forked. Per the POML's own framing, the
"future Insights refurbish" satellite does not exist yet as of this verification — this is a
scope note, not a violation.

**Conclusion for step 1: zero seam forks found in any worktree checked.**

---

## 2. Codify (step 2) — the check that re-runs at future gates

Two codified mechanisms, one already existing + reaffirmed here, one newly evidenced (grep recipe):

### 2a. NetArchTest rule (already exists, dependency-direction enforcement)

`tests/Spaarke.ArchTests/ADR013_ComposeFacadeTests.cs` (authored by Compose r2 task 025, reaffirmed
by its own task 081) asserts, via assembly reflection, that no type in
`Sprk.Bff.Api.Services.Compose` depends on `IOpenAiClient`, any `Services.Ai.Nodes.*` executor, or
`IConsumerRoutingService`. `tests/Spaarke.ArchTests/ADR013_AiBoundaryTests.cs` asserts the broader
CRUD-vs-AI-internal boundary repo-wide. Ran both, green:

```
$ dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj --filter "FullyQualifiedName~ADR013"
Passed ADR-013/NFR-05: Services/Compose/ must not depend on IOpenAiClient, a Nodes executor, or IConsumerRoutingService [406 ms]
Passed ADR-013/FR-C6: CRUD code must not depend on AI-internal types — use Services/Ai/PublicContracts facade [2 s]
Total tests: 2. Passed: 2.
```

These tests run in the shared `Sprk.Bff.Api` assembly, so they exercise **whatever is currently
merged of Compose's code** — they re-run on every future CI build and every future task-072-style
gate re-check for free. This satisfies "codified so it re-runs at future gates" for the
dependency-direction half of the requirement.

**Negative control — proves the check actually catches a violation (POML NEGATIVE acceptance
criterion):** seeded a scratch file
`Services/Compose/__Task072ScratchViolation.cs` injecting `IOpenAiClient` via constructor, re-ran
the filtered test:

```
Failed ADR-013/NFR-05: Services/Compose/ must not depend on IOpenAiClient, a Nodes executor, or IConsumerRoutingService [162 ms]
Error Message:
 ADR-013 / NFR-05 violation: a Sprk.Bff.Api.Services.Compose type depends on IOpenAiClient, ...
 Violating types: Sprk.Bff.Api.Services.Compose.Task072ScratchViolation
```

The check fails exactly as designed, naming the offending type. The scratch file was then deleted
and the suite re-confirmed green (2/2, `git status` shows zero residual diff under
`Services/Compose/` — nothing committed).

### 2b. Documented grep recipe (new — cross-worktree divergent-copy detection)

NetArchTest inspects one compiled assembly; it **cannot** see a separate satellite git worktree's
on-disk state before that code merges. The "no divergent DTO copy in a satellite worktree" half of
this task's goal is therefore a **grep recipe**, run against each active satellite worktree path
(see `projects/INDEX.md` for the current list), not a unit test:

```bash
# From core repo root, targeting a satellite worktree path $SATELLITE:
grep -rnE "^\s*(public|internal)\s+(sealed\s+)?(record|class)\s+(ComposeDisposition|OutcomeCard|GateDecisionV2|JobAwareCompletionState|ContextEnvelope|MemoryItem|TraceEvent)\b" "$SATELLITE/src/" \
  | grep -v "Services/Ai/PublicContracts/"
# Non-empty output = FORK — escalate per this task's <escalation> trigger, do not patch silently.

grep -rnE "IOpenAiClient|IPlaybookService|Services\.Ai\.Nodes\.|IConsumerRoutingService|TypedHandlerResumeExecutor" "$SATELLITE/src/server/api/Sprk.Bff.Api/Services/<satellite-domain>/"
# Any hit that is NOT inside an XML doc comment = a direct dependency violation.
```

Re-run this recipe at each future gate (G-R2-D re-checks, and whenever a new satellite project
starts consuming R2 seams) against every active satellite row in `projects/INDEX.md`.

---

## 3. Registry reconcile (step 3)

`projects/INDEX.md` was already updated by task 017 (2026-07-10) and requires no further edit:

- Core's row states: *"**CORE** (judgment + memory); **sole owner of `Services/Ai/` internals**."*
- Compose r2's row states: *"...does NOT modify `Services/Ai/` internals; no-fork rule enforced by
  core task 072."*

Both statements are confirmed accurate by this task's sweep (§1).

**`/conflict-check` would flag a fork**: the skill's file-level overlap detection
(`.claude/skills/conflict-check/SKILL.md`) diffs a branch's changed files against master and
against other open PRs. If Compose r2 ever committed a change touching any
`Services/Ai/PublicContracts/*.cs` file, or opened a PR touching `Services/Ai/**` (per the
Hot-Path Watchlist § BFF entry/DI row), the tool would surface it as an overlap against core's own
in-flight PR on the same path — the "Coordinate with PR owner" branch of its decision tree. Verified
today: Compose r2's own `git status --short` shows zero changes under `Services/Ai/**` (only
`Services/Compose/**` + its own new endpoint/test files), so there is currently nothing for
`/conflict-check` to flag — consistent with "no fork exists," not a gap in the check's coverage.

---

## 4. Acceptance-criteria disposition

| Criterion | Status | Evidence |
|---|---|---|
| Grep/NetArchTest check confirms satellites reference AI capability only via `PublicContracts/`, never AI-internal types | ✅ MET | §1(b) grep (doc-comment-only hits, zero real deps) + §2a NetArchTest green (2/2) |
| No satellite worktree contains a divergent copy of a core seam DTO | ✅ MET | §1(a) — all 7 seam declarations found ONLY under `PublicContracts/` in compose-r2; 0 matches in either insights-engine worktree |
| The check is codified so it re-runs at future gates | ✅ MET | §2a (existing `ADR013_ComposeFacadeTests.cs`/`ADR013_AiBoundaryTests.cs`, run in CI on every build) + §2b (documented grep recipe, this note) |
| `projects/INDEX.md` records core as sole `Services/Ai/` internal modifier; `/conflict-check` would flag a fork | ✅ MET | §3 — INDEX.md rows already correct (task 017); `/conflict-check` mechanism verified applicable |
| NEGATIVE: a deliberately-seeded satellite reference to an AI-internal type is caught | ✅ MET | §2a negative-control — seeded violation FAILED the test with the exact offending type name, then reverted + reconfirmed green |

**No STOP-level escalation raised.** Zero real seam-forks were found in any satellite worktree
checked. The `<escalation>` trigger in this task's POML ("if a real seam-fork is found... STOP") did
not fire.

---

## 5. Files touched by this task

- **Created**: this report (`projects/spaarke-ai-architecture-redesign-r2/notes/072-seam-fork-verification.md`).
- **Transient (created + deleted, not committed)**: `src/server/api/Sprk.Bff.Api/Services/Compose/__Task072ScratchViolation.cs`
  — the negative-control probe file. Confirmed removed; `git status` shows no residual diff.
- **No edits** to `projects/INDEX.md`, `notes/SEAM-STATUS.md`, `tests/Spaarke.ArchTests/*.cs`, or any
  satellite-worktree file — all pre-existing state was already correct; this task is verification-only
  per its own framing ("This is a verification-and-enforcement task, not a refactor").
