# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-25 (by `context-handoff`) · **Branch**: `work/spaarkeai-compose-r8`
> **Mode**: AUTONOMOUS parallel sub-agent execution — operator asked for no per-task confirmation.
> **Recovery**: read "Quick Recovery" first. Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Pushed + verified** | **055** `452f55a73` · **049** `7a0f9ab29` · **057** `1f62af7fd` |
| **Committed, NOT pushed, NOT verified** | **060** — `3cb96d974` + `79884ed53` + `0b0a08974` (3 commits ahead of origin) |
| **In flight** | **056** (objects carried) — a sub-agent is mid-edit in the main tree |
| **Next Action** | **Wait for 056.** Then: `dotnet build src/server/api/Sprk.Bff.Api/` → full `dotnet test tests/unit/Sprk.Bff.Api.Tests/` → verify **056 AND 060 together** → push → dispatch **052**. |

### ⛔ Do not do these two things

1. **Do not run `dotnet build`/`dotnet test` while 056 is mid-edit.** The tree currently fails on
   `TryCarryEmbeddedObjects` in `ComposeDocxProjectionBuilder.cs` — that is 056's half-written work, NOT a
   regression and NOT a cherry-pick problem.
2. **Do not push 060 as verified.** Its agent worked in a worktree branched from **master @ `845b4cdc9`**,
   so its "10,665 passed / 10,615 baseline" is a MASTER number and says nothing about this branch's
   ~11,192. The cherry-picks applied cleanly and the new code compiles; the suite has never run with 060's
   code against this branch's own tests.

### Leftover to clean up when 056 lands

`tests/integration/seam/Compose/ZzProbe2.cs` is untracked in the tree — looks like a 056 scratch probe.
Confirm and delete before committing; do not commit it.

---

## What this wave completed

| Task | What it did |
|---|---|
| **055** | Whole-document anchored placement. `comments[]` (the `flag-risks` intent's ENTIRE output) was 100% prose-anchored; now resolves deterministically and populates `AnchoredAnnotationAnchor.paraId` — a **sixth dark-machinery instance** (field shipped R3 FR-11 as PRIMARY, live consumer in `AnnotationReanchorService`, no writer). Decision: converge the RESOLUTION, keep the two SINKS separate — collapsing either costs Word `w:comment` export or ledger-key idempotency. |
| **049** | Word fields carried, SERVER half. Field row moved §2 → §3. Gate is STRUCTURAL not a keyword allow-list. Found and corrected a **stale renderer comment** ("the model does not carry bookmarks" — untrue since task 041), which is what allowed `REF`/`PAGEREF` to be carried LIVE instead of frozen. |
| **057** | Fields carried, CLIENT half — created because 049 was a **producer with no consumer** (`docxBridge.ts` never mapped a `field` atom into the posted model). A field is the first segment present in the run stream and ABSENT from the coordinate space. Also fixed a `getHTML()` round-trip defect that turned an atom's label into document content. |
| **060** | Durable tenant-partitioned session-file byte store. **Code-complete, unverified on this branch.** |

**Residual-loss list is now blocked on task 056 alone.** After it, §2 is footnote refs, endnote refs and
content controls — the three the owner said are not important (2026-08-25).

---

## 🔔 Task 060 — FIVE items needing OWNER decision (do NOT decide autonomously)

1. **The POML's own premise was partly wrong.** "Blob infra provisioned with managed-identity RBAC" is
   **false for two of three bicep stacks** — `customer.bicep` and `stacks/model1-shared.bicep` never pass
   `appServicePrincipalId`, so no role assignment is created. Where `model2-full` does pass it, it grants
   the **system-assigned** identity while the BFF pins a **UAMI** when `Graph:ManagedIdentity:ClientId` is
   set. **Nobody has checked which stack the live environments use. Until resolved, the store cannot
   authenticate in production.**
2. **Container choice** — `ai-chunks` used (provisioned everywhere, AI-domain, zero consumers). A dedicated
   `session-files` container is a one-line bicep change, deliberately left as the owner's call.
3. **ADR-015 retention/erasure undefined for this store** — taken as §6.5 **Path A**, bounded by a
   mechanical gate: `SessionFileStore:BlobEndpoint` ships EMPTY so the non-compliant state is unreachable
   until 062/063 land. Needs owner confirmation in `design.md` ADR Tensions.
4. **ADR-015 governed-stores table needs a row** — §6.5 **Path B**; text drafted in the agent report.
5. **`X-Tenant-Id` header fallback — highest-severity item surfaced.** Pre-existing and repo-wide, but this
   task promotes it from a 4-hour cache key to a **durable 90-day partition key**. Not changed (four
   handlers + the auth path); both write sites now log a Warning naming the header-derived tenant.

Also: 060 ships **disabled** by design, and **nothing ran against real Azure** — no storage account, no MI,
no RBAC. Every blob assertion is against an in-memory gateway mimicking Blob naming semantics.

---

## Remaining queue

| # | Task | Gate |
|---|---|---|
| **052** | Demote the text-search path | READY — but collides with 056 on `Services/Compose`; dispatch after |
| **053** | Bounded confirmable fallback | 052 |
| **061/062/063** | Track B lazy re-index · retention/TTL · erasure | 060 (all `parallel-safe: ✅`) |
| **070–073** | Track D decomposition | ready, but same files as Track A/C — sequence carefully |
| **074** ⛔ | Retire `ComposeShadowPatchEngine` | gate-confirm first |
| **045** | Residual list + ADR amendment | **056** — then owner sign-off on a 3-row list |
| **090** | Wrap-up | all |

---

## The parallelization judgment (repeat it, it worked)

The project sets a BLANKET `parallel-safe: false` on the whole Compose spine. That is too coarse. What
actually matters is **file and toolchain disjointness**, checked per pair:

- 055 (client/jest) ∥ 049 (server/dotnet) — no shared file, no shared build output. Ran clean.
- 060 — genuinely independent (`Services/Ai/Sessions/**`), but ALSO dotnet, so it went to an **isolated
  worktree** to avoid `bin`/`obj` contention. Cherry-picked back cleanly.
- 052 ∥ 056 — **both** touch `Services/Compose`. Do NOT run together.

**Main session reserves** `TASK-INDEX.md`, `current-task.md`, `.claude/**` and ALL git operations. Agents
own code only. This prevented every collision.

**Known cost of parallelism**: CPU contention causes timing flakes.
`SseStreamingIntegrationTests.Cancellation_NoLingeringBackgroundTask_AfterClientAbort` flaked once; it
passes in isolation in 178ms and no SSE file was touched. Re-run a suspicious failure in isolation before
calling it a regression.

---

## Verification discipline — this is what caught real problems

**Do not accept agent reports. Re-run and spot-check.** What that caught this wave:

- A **suite failure the agent did not have** (the SSE flake above).
- A **publish-size number 1.3 MB off** mine (agent 45.02 MB vs measured 43.72 MB compressed). Note: the
  RAW directory sum is ~137 MB — measuring that instead of the zipped size is a trap I fell into once.
- A **doc claim that was true of only one path** — `notes/049 §4` said bold/italic/underline survive; true
  server-side, false on a keystroke edit (an opaque atom declares `marks: ''`). Corrected in both the note
  and the published row.
- **049's own §7 disclosure** that its carry was unreachable from a keystroke edit — verified
  independently (`composeInlineAtom` appeared exactly once in the client bridge) and became task 057.

**Escalation ruling made this wave (precedent):** 057 fired its trigger #2 on the literal predicate
("attributes do not survive the round trip") but the reasoning behind the trigger did not apply — the
payload was in the server HTML and only needed DECLARING, the same four-line mechanism task 048 used for
`symFont`/`symChar`. Accepted the scope extension because it was minimal, self-contained, used the in-file
precedent, and was **surfaced with an offer to revert** rather than buried.

---

## Standing project constraints (unchanged)

- **Deploy prerequisite**: `Deploy-AnalysisAction.ps1` MUST run before ANY of Track C is observable. Dev
  stores the WHOLE mirror file in `sprk_inputschema` for `compose-draft-alternative` /
  `compose-compare-to-playbook`, so `GetDeclaredProperties` returns null and **051's `targetParaId` would
  not render either**. Deploy BFF + `sprk_spaarkeai` together (NFR-05).
- Publish ceiling 60 MB **compressed**; current 43.72 MB incl. PDBs. No new NuGet on Track A.
- **NEVER delete `docxBridge.ts`.**
- `/conflict-check` before every BFF PR. PR **#806** is open.
- Pre-existing CI red, not ours: **Compose Client Gate** (timeout flake since `7069717bd`) and **Trivy**
  (HIGH CVE on master). Verified identical on `dfa713cbf`, before this session's work.
- **C-4 still unmeasured against a real model response**: anchors add 3.50% at realistic payload size
  (40.4 KB, under the 128 KB cap); the over-cap case at the schema's declared maxima is pre-existing.
