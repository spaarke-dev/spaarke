# Task 002 — deviations and corrections

> **Task**: 002 `SharedPackageCensusTests` — a 16th shared package fails the build
> **Date**: 2026-09-04 · **Outcome**: completed, no escalation fired

---

## 1. An acceptance criterion rested on a false premise, and was replaced rather than waived

Task 002 stated four times — in `<background>`, in a `<constraint source="spec">`, in an
`<acceptance-criteria>` and in `<notes>` — that **`Spaarke.LegalWorkspace` has no `package.json`**, and
used that as the *reason* for keying the census on directory presence.

**It has one.** All 15 of 15 shared packages carry a manifest (measured 2026-09-04; see
[`task-001-deviations.md`](task-001-deviations.md) §1).

This makes criterion 4 — *"Given `Spaarke.LegalWorkspace` has no `package.json`, it is still counted —
proving the census is keyed on directory presence"* — **unsatisfiable as written**. Its premise is false,
so satisfying it would prove nothing.

**What was done, and why not the alternatives:**

| Option | Rejected because |
|---|---|
| Mark the criterion met anyway | It is not met. The premise is false; asserting it would put a false statement in the completion record — the exact defect class this project exists to remove. |
| Waive the criterion | The criterion's *intent* is sound and load-bearing: prove the census is keyed on the directory, not on the manifest. Waiving it would drop a real check because its stated example turned out wrong. |
| Re-key the census on `package.json` | The instruction to key on directory presence is still **correct**, just for a different reason (below). Changing the design to match a broken rationale is backwards. |
| **Replace the criterion with a true-premise equivalent** ✅ | Keeps the intent, discards only the false example. |

`Census_CountsADirectoryWithNoPackageJson` creates a **real temporary directory** under
`src/client/shared/` with no manifest, asserts the enumerator returns it, and removes it in a `finally`
plus a post-assert. A real directory rather than an in-memory stand-in because the claim under test is
about the enumerator's behaviour *on disk* — an in-memory version would assert the design rather than test
it.

**The design decision survives its broken justification.** Directory-keying is right because ADR-012
enumerates *directories*, and because a package arriving without a manifest — or with one added a commit
later — would otherwise be invisible for as long as it takes someone to notice by hand. That reasoning
never depended on LegalWorkspace.

## 2. `SourceScan` was **extended**, not forked — and this is not the escalation the POML anticipated

The POML carries an escalation trigger: *"If reusing SourceScan turns out to be impossible for
directory-level scanning, STOP and escalate rather than forking it."*

`SourceScan` exposed only `ServerSourceFiles()` and `TestSourceFiles()` — no directory enumerator — so on
a literal reading the trigger looked live. **It is not**, and the trigger's own wording is why: it fires
on *forking*, and there is a third option between "reuse as-is" and "fork".

`SourceScan` gained `SharedClientPackageDirectories()`, alongside the existing two. This follows a
precedent already in the file: `TestSourceFiles()` was itself **added later**, for
`Adr038TestBanGuardTests`, and documents that reasoning in its own XML comment. Extending the shared
primitive is what the ADR-038 reuse rule asks for and what CLAUDE.md §11 asks for; a private walker inside
the census would have resolved the repo root a second way, and `ResolveRepoRoot()` is precisely the
fragile part a fork would duplicate.

No escalation raised. Recorded here because a reader comparing the trigger to the diff should see the
reasoning, not have to reconstruct it.

## 3. The application allow-list was given teeth rather than being carried as a dead list

The constraint requires an explicit allow-list for four application-scoped `@spaarke/*` names. Measured:
**none of the four lives under `src/client/shared/`**, so a directory-keyed census can never encounter
them — the list as specified would filter nothing.

Rather than carry four unreachable filter entries, the list is **asserted**:
`NoApplicationScopedPackageHasCreptIntoTheSharedTree` fails if any of them ever appears as a shared
package. That converts documentation into a live check of a real architectural event (an application
being mistaken for a library), and satisfies the criterion — the four names cannot trigger a census
failure — by construction rather than by subtraction.

One correction inside the list: **`@spaarke/pcf-shared` is not a published package at all.** It appears
exactly once in the repository, as a usage example inside a doc comment at
`src/client/pcf/shared/index.ts`. It is retained in the list with that fact written down, so a future
reader who greps the name finds the note rather than concluding a shared package went missing.

## 4. Minimum-reason-length check — noted against the "no thresholds" constraint

`EveryCensusEntryIsExplained` rejects a reason shorter than 40 characters. This is a **minimum-substance
check on prose**, directly precedented by `CredentialCensusTests.EveryCensusEntryIsExplained` (which uses
60). It is flagged here because the project's standing constraint bans thresholds — that ban is on
**count-proxies for judgment questions** (test count, duplication %, file size), which is a different
thing from requiring that a written justification actually be written. Recorded so the distinction is
deliberate and visible rather than assumed.

---

## Verification

| Criterion | Result |
|---|---|
| Passes on exactly the 15 enumerated packages | ✅ 8/8 census tests green |
| A synthetic 16th directory fails | ✅ and **verified empirically** — a real `Spaarke.Interloper.Empirical/` on disk produced the failure, then was removed and the tree confirmed clean |
| The failure message names all three questions + the amendment requirement | ✅ asserted by content in the negative control, not merely by "it threw" |
| ~~LegalWorkspace has no package.json, still counted~~ | ⚠️ **premise false** — replaced by `Census_CountsADirectoryWithNoPackageJson` over a real manifest-free directory (§1) |
| Negative: the four application names do not trigger a failure | ✅ structurally impossible (none is in the scanned tree) **and** asserted |
| Negative: no DI resolution, `SourceScan` not forked | ✅ 0 `GetRequiredService`/`ServiceCollection`/`Moq`; 0 local directory walkers; 5 `SourceScan.` calls |
| `dotnet build Spaarke.sln` green | ✅ 0 errors, 5 pre-existing CA2024 warnings (unchanged from the pre-task baseline) |
| ArchTests project passes | ✅ **199/199** — note ArchTests is *not* in `Spaarke.sln`, so this is a separate run, not implied by the solution build |

**Escalation triggers**: neither fired. The directory count matched ADR-012's enumeration exactly (15), and
`SourceScan` was extended rather than forked (§2).
