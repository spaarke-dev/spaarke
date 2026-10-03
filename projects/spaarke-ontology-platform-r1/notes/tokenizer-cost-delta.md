# Task 082 — `WellFormedTokenPattern` measured query-count delta (ADR-045)

**Status**: measured, not assumed, per ADR-045 and the task 082 constraints.

## 1. Why a measurement, not an estimate, is possible here

`IdentifierReverseLookupRung.EvaluateAsync` issues **exactly one Dataverse reverse-lookup query per core
record type in the catalog roster** (`roster.Count`, currently the 7 core types — matter, project, invoice,
work assignment, budget, service request, report card), batched via one `In`-filter per number field, **not
one query per candidate token**. The gate is binary:

- **Zero candidate tokens extracted** (well-formed or bare-numeric) → the roster is never read → **0 queries**.
- **≥1 candidate token extracted** → the roster is read and one batched query issued per roster entry → **7
  queries** (with the current 7-type core roster), regardless of how many distinct tokens the message
  contains.

So the cost of loosening `WellFormedTokenPattern` reduces to one countable question: **over a representative
sample of communications, how many additional messages now extract at least one well-formed token that did
not before?** That count, multiplied by the roster size (7), is the query-count delta. This is measured
below over a curated representative corpus — not inferred from the regex shape alone.

## 2. The change

**Before** (original):
```
\b[A-Za-z]{2,}[-.][A-Za-z0-9][A-Za-z0-9.\-]*[A-Za-z0-9]\b
```
Required the alpha prefix *immediately* adjacent to the `-`/`.` separator — no whitespace tolerated anywhere.

**After** (`IdentifierReverseLookupRung.cs`):
```
\b(?:[A-Za-z]{3,}[ \t][A-Za-z]|[A-Za-z]{2,})[ \t]*[-.][ \t]*(?=[A-Za-z0-9]*\d)[A-Za-z0-9][A-Za-z0-9.\-]*[A-Za-z0-9]\b
```
Three deliberate, narrow changes:

1. **Whitespace tolerance around the separator.** The `-`/`.` separator may now have optional whitespace on
   both sides.
2. **A one-letter second prefix word, ONLY when it is exactly one letter.** This is what makes the live
   matter number `"Form D - 2023"` (prefix words `Form` + `D`) tokenize: `Form` (3+ letters) + a single space
   + the single letter `D`. The restriction to *exactly one letter* is load-bearing, not cosmetic — see §3.1,
   "the bug this caught," below.
3. **Digit-in-body requirement.** The text following the separator must contain at least one digit. Every
   real record number in this catalog is numeric-bodied (`123`, `002`, `10001.01`, `441482`, …); this is also
   what keeps ordinary hyphenated English compounds out even though the separator now tolerates whitespace.

Without change (3), whitespace tolerance alone would admit ordinary prose like `"regroup - the plan"` —
exactly what the negative acceptance criterion forbids. Change (3) is not optional scope creep; it is
required to hold that criterion while shipping changes (1)–(2).

### 3.1 The bug this caught — a first draft of (2) was ambiguous

The first draft allowed the second prefix word to be **any length**, not just one letter:
```
\b[A-Za-z]{2,}(?:[ \t]+[A-Za-z]+)?[ \t]*[-.][ \t]*(?=[A-Za-z0-9]*\d)[A-Za-z0-9][A-Za-z0-9.\-]*[A-Za-z0-9]\b
```
Running the full `tests/unit/Sprk.Bff.Api.Tests` suite with this draft **failed a pre-existing passing test**
(`IdentifierReverseLookupRungTests.Fr12_NewRecordFraming_ReferencedIdentifier_CappedSubThreshold_NotAutoFiledAlone`).
The subject `"This is a new litigation matter related to MAT-123"` tokenized as `"to MAT-123"` instead of
`"MAT-123"`: regex matching scans left-to-right and commits to the first position where a match is possible,
so the connector word `"to"` was accepted as prefix-word-1 and the *real* 3-letter prefix `"MAT"` was absorbed
as prefix-word-2 — producing a token value that no longer equals the stored field value `"MAT-123"`, so the
exact-match reverse lookup silently resolved to nothing.

**The fix**: restrict the second word to *exactly one letter*. A single letter can never stand alone as a
prefix word (the existing `{2,}` floor on a standalone prefix exists for precisely that reason), so it can
only ever be the genuine second half of a compound name like `"Form D"` — it can never be "stolen" by an
unrelated connector the way a 2+ letter word (`"MAT"`, `"Co"`, …) can. Re-running the full suite after the
fix: no regressions, plus the regression case is now pinned permanently as its own corpus row (see §3 below)
and its own assertion in `IdentifierReverseLookupRungTests.cs`.

This is reported here because catching it is itself part of the measurement discipline the task calls for —
a first draft that LOOKED like a safe characterization of "Form D" silently broke an existing deterministic
match on a build-and-test cycle. Shipping without running the full suite would have missed it.

## 3. Methodology

A 35-message representative corpus of communication subjects was constructed spanning:

- **Controls** (8) — subjects that already tokenized under the old pattern (`MAT-123`, `INV-002`,
  `PRJT.10001.01`, `WRK-55`, `BDGT-9012`, `SVCR-77`, `RPTC-3001`, and the `REAL-2026-123456.02` P1
  substring-guard regression case) — must remain unaffected.
- **Repair target** (3) — the task's repro (`"Form D - 2023"`, a subject-line variant, and a second
  same-shape example `"Case A - 4521 pending"`).
- **Regression guard** (1) — `"This is a new litigation matter related to MAT-123"`, the exact subject that
  caught the ambiguity bug in §3.1. Already matched before this task (tight `MAT-123`, no spaces); pinned here
  so the ambiguity class cannot silently return.
- **Genuinely new matches** (3) — shapes the whitespace tolerance admits beyond the repro itself
  (`"Acme Corp - 88 invoice"`, `"Team sync - 2pm start"`, `"Standup - 9am tomorrow"` — each resolves via its
  SECOND word directly against the separator, e.g. `"Corp - 88"`, since the first word doesn't qualify for
  either prefix form).
- **Ordinary hyphenated English compounds** (7) — `"Follow-up"`, `"state-of-the-art"`, `"Well-known"`,
  `"catch-up"`, `"Good-to-go"`, `"Sign-off"`, `"Up-to-date"` — all **already matched the OLD pattern** (which
  had no digit requirement at all), and are now correctly excluded.
- **Other ordinary prose** (5), **dates** (2, via the unaffected bare-numeric fallback), **bare-numeric
  controls** (2, unaffected), and **plain conversational subjects with no tokens** (4).

Both the frozen pre-task-082 pattern (reproduced above) and the shipped pattern were run against every corpus
message (`System.Text.RegularExpressions`, same options: `Compiled | CultureInvariant`, no `IgnoreCase` —
matching production exactly), together with the unchanged bare-numeric fallback (`\b\d{4,}\b`) and the P1
substring-guard rule (a bare-numeric run that is a substring of an already-extracted well-formed token is not
independently extracted). For each message: triggers-a-query = `(well-formed match) OR (bare-numeric match not
absorbed by a well-formed match)`.

The same corpus is executed as a durable xUnit theory through the rung's public surface —
`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/WellFormedTokenPatternCostDeltaTests.cs` — asserting the
exact AFTER query count (0 or 7) per message, plus an aggregate pin (133) so a future uncoordinated change to
this pattern is caught rather than silently drifting ADR-045's cost profile. The BEFORE state cannot be
re-executed from this codebase (the fix is already applied), so it is recorded here rather than as a
permanent test.

## 4. Measured result

| | Messages triggering ≥1 query | Total Dataverse queries (× 7) | Avg queries / message (n = 35) |
|---|---|---|---|
| **BEFORE** (original pattern) | 23 / 35 | 161 | 4.600 |
| **AFTER** (task 082 pattern) | 19 / 35 | 133 | 3.800 |
| **Delta** | **−4 messages** | **−28 queries** | **−0.800** |

**The cost profile improved, not regressed.** This is below the escalation trigger ("more than doubling")
by a wide margin — no escalation required.

### Why the net is negative, not positive

The repro itself is **query-count neutral**: `"Form D - 2023"` already issued 7 queries **before** this fix,
via the unrelated bare-numeric fallback matching the trailing year `"2023"` (4+ digits) standalone — it simply
resolved to no matter, because an exact-match lookup for `"2023"` never equals the stored field value
`"Form D - 2023"`. This matches the original diagnosis in `notes/mvp-technical-spec.md` §17.5(a): *"the
bare-numeric fallback … extracts `"2023"`, which reverse-looks-up to no matter."* After this fix, the SAME
query is now productive — `ExplicitReference` fires — rather than being a new cost. The regression-guard row
(`"… related to MAT-123"`) is query-count neutral for the same reason it existed before this task at all: the
tight `MAT-123` substring already matched under the OLD pattern.

The real movement:

- **+3 messages / +21 queries** — genuinely new matches the whitespace tolerance admits beyond the repro
  (`Acme Corp - 88 invoice`, `Team sync - 2pm start`, `Standup - 9am tomorrow`). Accepted: these are an
  over-match-by-design cost (the rung's own docstring: *"Precision comes from the EXACT reverse lookup, not
  this pattern — an over-match simply resolves to no record"*), not a misfile risk, since the reverse lookup
  still requires an exact match against a real record's number field.
- **−7 messages / −49 queries** — ordinary hyphenated English compounds that the OLD pattern (no digit
  requirement) was **already** matching and querying, now correctly excluded by the digit-in-body requirement
  added to hold the "no arbitrary prose" criterion.

Net: +21 − 49 = **−28 queries** over this corpus.

## 5. Caveat on generalizing this number

This is a measurement over a **curated representative corpus**, not a sample of this tenant's live traffic
(reading live communications was out of scope for this task — no deploy, no Dataverse data/metadata changes).
The *sign* of the result (net decrease, driven by removing a larger pre-existing false-positive class than is
added) is expected to hold directionally because ordinary hyphenated compounds are common in business
correspondence and real multi-word space-bearing record numbers are comparatively rare — but the exact
magnitude will differ against real traffic. The durable regression test
(`WellFormedTokenPatternCostDeltaTests.RepresentativeCorpus_IssuesExpectedQueryCount` +
`Corpus_AggregateQueryCount_MatchesTheMeasuredAfterTotal`) pins today's AFTER behavior so any future change to
this pattern is required to re-measure rather than assume, per ADR-045.

## 6. Scope not taken

- Did not generalize the second prefix word beyond **exactly one letter**. A second word of 2+ letters (e.g.
  a hypothetical `"Smith Co - 2024"`) reintroduces the §3.1 ambiguity class (an unrelated connector word
  swallowing a real standalone prefix) and is out of scope; the task's repro only needs the one-letter case.
- Did not generalize the prefix beyond **one** extra word at all. A 3+-word matter name with embedded spaces
  (not seen in the task's repro or the current data model's examples) is out of scope.
- Did not touch `AutoFileGate` or the 0.85 auto-file threshold, nor the `WellFormedConfidence` (0.90) /
  `BareNumericConfidence` (0.65) constants — this task changes only the tokenizer's acceptance shape, never
  the confidence ladder (project constraint: auto-file stays deterministic, never reachable by the AI rung
  alone).
