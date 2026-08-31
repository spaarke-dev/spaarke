# Test diet report — `spaarkeai-compose-r8`

**Run date**: 2026-08-28 · **Branch**: `work/spaarkeai-compose-r8` · **Scope**: `origin/master...HEAD`

> ⚠️ **Run EARLY, deliberately.** `/test-diet` is normally a project-close gate (task 090) and the skill
> states it is *not applicable during active development*, because build-class scaffolding is still
> load-bearing while work continues. It was run now at the owner's request to answer a specific
> question — *are all these tests necessary and helpful?* — with data instead of impression. **Re-run at
> 090**; treat the DELETE column here as signal, not as an instruction.

## Scope

| | |
|---|---|
| Test files touched | **60** (26 added · 31 modified · 3 deleted) |
| Test methods in added files | **187** |

**Path distribution** — 7 of 8 ADR-038 KEEP paths are respected:

| Path | Files | KEEP path? |
|---|---|---|
| `integration/seam` | 33 | ✅ (since 2026-07-09, ADR-038 §2) |
| `unit/Sprk.Bff.Api.Tests` | 14 | ❌ **not a KEEP path** |
| `integration/contract` | 7 | ✅ |
| `integration/tenant` | 2 | ✅ |
| `integration/regression` | 2 | ✅ |
| `Spaarke.ArchTests` | 2 | ✅ (fitness functions — heuristic 0) |

## The 14 outside a KEEP path are mostly NOT this project's debt

| File | Status | Classification |
|---|---|---|
| `Mocks/InMemorySessionFileBlobGateway.cs` | A | **Not a test** — a test double. Out of scope for the classifier. |
| `Services/Ai/Sessions/SessionFileBlobStoreConfigurationTests.cs` | A | **AMBIGUOUS** — the only test file this project added outside a KEEP path. Name suggests configuration/wiring (B3 territory); needs a read before judgement. |
| `Services/Compose/ComposeEditBatchTests.cs` | **D** | ✅ already deleted by this project |
| `Services/Compose/ComposeEditTransactionTests.cs` | **D** | ✅ already deleted |
| `Services/Compose/ComposeEditValidatorTests.cs` | **D** | ✅ already deleted (FR-C04 retired `ComposeEditValidator`) |
| 9 further files | M | **PATH-VIOLATION-PROTECTED** — pre-existing files at a pre-existing wrong path. This project modified them; it did not put them there. Not its debt to pay. |

**Net: the project added ONE test file outside a KEEP path and deleted THREE.** That is the right
direction, and it is the opposite of the "lots of tests" impression.

## The real finding: a ban that exists and is not enforced

Mechanical scan of all 60 touched files against the enforceable bans:

| Ban | Pattern | Files |
|---|---|---|
| **B1/B2/B7** | `Mock<HttpMessageHandler>` — **explicitly banned by ADR-038** | **24** |
| B3 | `GetRequiredService` assertions (DI-registration tests) | 6 |
| B4 | `Throws<ArgumentNullException>` on ctor | **0** ✅ |
| B13 | names without a scenario (`Test1`, `_Works`) | **0** ✅ |

**Attribution of the 24**: **4 added by this project**, **20 pre-existing**. By path: 15 `integration/seam`,
5 `integration/contract`, 2 `integration/regression`, 2 `unit`.

⚠️ **Judgement required, not mechanical deletion.** The classifier flags `Mock<HttpMessageHandler>`
unconditionally, but a seam test that stubs an *outbound HTTP boundary* is not the same thing as a unit
test whose *subject* is the mocked handler. 15 of the 24 are seam tests, where that stub is plausibly the
sanctioned boundary. Deleting them on the heuristic alone would be exactly the "confident nonsense" the
skill's own heuristic-0 note warns about. **Classified AMBIGUOUS pending a read.**

### ADR-038 is documented but NOT enforced

Verified: **no ArchTest checks any of B1–B17.** The bans live in `docs/adr/ADR-038-testing-strategy.md`,
in `.claude/constraints/testing.md`, and in this skill's classifier — all of which are consulted by a
human or an agent that chooses to look. Nothing fails a build.

The consequence is visible above: a pattern the ADR names as banned appears in **24 touched files**, and
grew by 4 during this project, without anything objecting.

**Recommendation** — a source-scan ArchTest, the same shape as the `CallerIdentityGuardTests` /
`ServiceBusClientGuardTests` pattern that #839 armed. It costs ~40 lines and turns a document into a
forcing function.

Two design notes learned the hard way this week, both from #839:

1. **Do not use a bare count ceiling.** The ADR-010 ratchet went 153→155 while *seven* interfaces were
   added and five removed — the net number hid five additions. Use a **checked-in inventory of the
   accepted set**, so the failure names the file, and net-zero churn is impossible to hide.
2. **Arm it in the same PR that adds it.** `CredentialGuardTests` shipped red and CI reported green for
   six days because it was never added to the Tier-1 filter. A guard that is not armed is a file that
   looks like enforcement.

## Not classified per-method

187 added test methods were **not** individually read. The path check, the ban scan and the
added-vs-modified attribution are mechanical and complete; per-method MAINTAIN/SCAFFOLDING judgement is
not, and is deferred to the 090 run. Stated rather than implied, because a report that looks exhaustive
and is not is worse than one that says where it stopped.

## Commands

**None emitted.** Nothing here is safe to delete mechanically: the only clear SCAFFOLDING signal is the
`Mock<HttpMessageHandler>` bucket, and 15 of those 24 are seam tests where the stub may be legitimate.
Per the skill's binding contract — *ambiguity is honest, not biased toward DELETE*.

## Answer to the question that prompted this run

> *"Is all this work necessary and helpful? It seems we have a lot of tests."*

**Volume is not this project's problem — distribution is.** 187 methods across 26 files, one path
violation added, three scaffolding files removed. What the scan actually found is a **banned pattern
nothing enforces**, and a second problem coverage measurement had already exposed on the same day:

> The `usePendingRedline` anchorless suite had **29 tests, and 23 of them exercised the same population**
> — every pre-existing test omitted `origin`, so the live-anchorless path (the only one a user can hit
> since task 051) had **zero** coverage. That is the shape of the real waste: not too many tests, but many
> tests clustered on one path while a live path has none. **Test count hides that; branch coverage finds
> it.**
