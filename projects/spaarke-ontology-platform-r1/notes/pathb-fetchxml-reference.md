# Path B — the hand-verified FetchXML reference (task 021)

> **Date**: 2026-10-04 (UTC) · **Env**: `spaarkedev1` (pac auth profile `SPAARKE DEV 1`, user ralph.schroeder@spaarke.com)
> **Executor**: `pac org fetch --xmlFile` (PAC CLI 1.46.1) — read-only; no row written anywhere.
> **Policy**: `sprk_policy` `4d204810-…` POL-COMMIT-BUDGET / version `42b3e716-…` — re-read after this task:
> `sprk_enabled = No`, untouched.

This query was written **by hand first**, run against the task 005 seed, and only then was the compiler built to
reproduce it. The compiler's output for the deployed rule body is asserted **byte-identical** to this text
(`PredicateCompilerTests.Compile_LivePathBBody_EqualsHandVerifiedReferenceByteForByte`), with the clock pinned
to the instant this ran — so "the compiled query" and "the query that ran" are the same string.

Machine-readable copy (what the test reads): [`tests/fixtures/signals/pathb-existence.reference.fetchxml`](../../../tests/fixtures/signals/pathb-existence.reference.fetchxml).
Rule body it compiles from, copied verbatim from the live `sprk_policyversion.sprk_rulebody`:
[`tests/fixtures/signals/pathb-existence.rulebody.json`](../../../tests/fixtures/signals/pathb-existence.rulebody.json).

## 1. The reference query (run at 2026-10-04T02:15Z, window anchor `now-30d` = 2026-09-04T02:15:00Z)

> **Updated after the Step 9.5 review (F6):** the reference now carries `<order attribute="sprk_matterid" />`
> (Microsoft's paging guidance for `distinct` queries). The updated text below was **re-run on spaarkedev1
> 2026-10-04** with `pac org fetch` and returned the **same 4 rows** — ordering does not change membership.

```xml
<fetch distinct="true">
  <entity name="sprk_matter">
    <attribute name="sprk_matterid" />
    <order attribute="sprk_matterid" />
    <link-entity name="sprk_communication" from="sprk_regardingmatter" to="sprk_matterid" link-type="inner" alias="c0">
      <filter type="and">
        <condition attribute="sprk_triagecategory" operator="in">
          <value>8b62dd84-1fbc-f111-aaaf-3833c5e9614d</value>
          <value>8d62dd84-1fbc-f111-aaaf-3833c5e9614d</value>
        </condition>
        <condition attribute="sprk_receiveddate" operator="ge" value="2026-09-04T02:15:00Z" />
        <condition attribute="sprk_reviewoutcome" operator="ne" value="100000003" />
      </filter>
    </link-entity>
    <link-entity name="sprk_budgetrevision" from="sprk_matter" to="sprk_matterid" link-type="outer" alias="c1">
      <filter type="and">
        <condition attribute="sprk_revisedon" operator="ge" value="2026-09-04T02:15:00Z" />
      </filter>
    </link-entity>
    <filter type="and">
      <condition entityname="c1" attribute="sprk_budgetrevisionid" operator="null" />
    </filter>
  </entity>
</fetch>
```

- **ONE query, both conjuncts** (CM-3). No second query, no C# join, no variable passed between clauses.
- `exists` = INNER `link-entity` with its filter inside the link (the `DataversePrecedentBoard.cs:182-190` shape).
- `notExists` = OUTER `link-entity` with the **window inside the link** (ON clause) + a root `null` test on the
  linked primary key. This is the anti-join; no in-repo template existed.
- No `modifiedon` anywhere (FR-07).

## 2. Result on real data

| Matter | GUID | In result? | Why |
|---|---|---|---|
| **REAL-2026-123456.01** (positive) | `2444af6d-e1f2-f011-8406-7ced8d1dc988` | ✅ **returned** | comm `f00b6389` Fee/rate change, 2026-10-03T12:00Z, File; zero revisions |
| **REAL-2026-123456.02** (NC1) | `b68299c6-bafb-f011-8407-7c1e520aa4df` | ❌ **not returned** | comm `9848e2b0` qualifies, but revision `e4e5dbe6` at 2026-10-03T14:00Z is in-window |
| **ONTOLOGY-DEV-SEED-005-NC2** (NC2) | `d14d79f4-8fbf-f111-aaaf-0022482913fc` | ❌ **not returned** | zero communications |
| PAT-477689 — Targeted Protein Degradation Patent Application | `dc091784-fd63-f111-ab0c-7ced8ddc4a05` | ✅ returned | 3 comms classified Scope/budget change, 2026-09-29, review outcome Update; no revision |
| EMPL-307998 — Intellectual Asset Management Lifecycle Optimization | `9f6f23e4-9183-f111-8076-7ced8ddc4a05` | ✅ returned | comm `3449bd41` Scope/budget change, 2026-09-29, Update; no revision |
| Form D - 2023 — Anaqua Investments Group OCP Capote 2023 LLC | `accb692e-647c-f111-ab0e-7ced8ddc4a05` | ✅ returned | comm `4cded5d5` Scope/budget change, 2026-09-29, Route; no revision |

**Actual result set (4 rows)**: `2444af6d…`, `9f6f23e4…`, `dc091784…`, `accb692e…`.

### The three extra matters are correct, not a leak — independently cross-checked

The query returns **four** matters, not one. That is not "only the positive matter", so it was checked
independently through a **different engine** (Dataverse SQL/TDS via MCP `read_query`, not FetchXML): every
communication in the org whose `sprk_triagecategory` is Fee/rate change or Scope/budget change belongs to one of
exactly five matters (`.01`, `.02`, PAT-477689, EMPL-307998, Form D - 2023). All seven such communications were
received 2026-09-29 → 2026-10-03 (inside the window), none is Dismiss, and the org's only `sprk_budgetrevision`
is NC1's. So the correct answer is precisely `{.01, PAT-477689, EMPL-307998, Form D - 2023}` — what the query
returned.

⚠️ **This is the first reading the seed could not give.** Those three are **pre-existing dev communications
classified by the live enrichment path on 2026-09-29**, not rows authored to make the predicate pass. It is
weak evidence for design §9's top risk ("the cross-source rule fires on nothing real") because it is still dev
data, but it is the first time the predicate has fired on data nobody constructed for it. Not acted on here — no
writer exists (task 030) and the policy stays disabled.

## 3. Discriminating probes — each one would catch a specific broken anti-join

All run the same way (read-only, same anchor `2026-09-04T02:15:00Z` unless stated). Files are reproduced from
the scratchpad by the variants described.

| # | Probe | Expect if correct | Actual | Catches |
|---|---|---|---|---|
| V1 | The reference (§1) | `.01` in, NC1 + NC2 out | 4 rows (§2) ✅ | — |
| V2 | `exists` clause only | `.01` **and** `.02` (+ the 3 others) | 5 rows incl. `.02` ✅ | proves NC1 is excluded **only** by the notExists conjunct |
| V3 | V1 with the revision window moved to `ge 2026-10-03T15:00:00Z` (after NC1's revision) | NC1 **comes back** | 5 rows incl. `.02` ✅ | proves the window is in the **join** (ON), not the WHERE |
| V4 | **Deliberately broken**: revision window in the root WHERE filter instead of the link | — | **0 rows** — returns NOTHING, `.01` lost | shows the failure mode the compiler is built to avoid: it reads as a quiet, working predicate |
| V5 | **Empty source table**: `exists sprk_signal` (table has **0 rows**, verified) + notExists revision | nothing | **0 rows** ✅ | an inverted join would return all 62 matters |
| V6 | **Empty notExists source**: notExists `sprk_signal` (0 rows) + exists comm | exactly the exists set | 5 rows = V2's set ✅ | neither everything (62) nor nothing |
| V7 | notExists `sprk_budgetrevision` only | 61 of 62 (all but NC1) | **61 rows**; NC1 absent, `.01` and NC2 present ✅ | anti-join is neither identity nor empty |

V5/V6 used `sprk_signal` because it is a **genuinely** empty table (0 rows). After the task 006 finding (§3c) the
compiler now **refuses** `sprk_signal` as a clause entity (the writer does not read it at verified Global depth),
so the compiled empty-source proof (C5/C6 below) empties the source **by filter** on Path B's own tables instead.

## 3b. The COMPILER's output, executed verbatim on real data (2026-10-04T03:38:48Z)

The production `PredicateCompiler` (real `TimeProvider.System`, real `RuleBodySchemaValidator`) was run from a
scratch .NET 10 file-based app (`#:project …/Sprk.Bff.Api.csproj`), each compiled string written to a file and
executed **unmodified** with `pac org fetch --xmlFile`. C1 compiled from the rule body fixture, which is the live
`sprk_rulebody` string copied verbatim (re-read via MCP 2026-10-04 and compared).

C1, the actual query that ran (identical to §1 except the anchor, which is the real clock):

```xml
<fetch distinct="true">
  <entity name="sprk_matter">
    <attribute name="sprk_matterid" />
    <link-entity name="sprk_communication" from="sprk_regardingmatter" to="sprk_matterid" link-type="inner" alias="c0">
      <filter type="and">
        <condition attribute="sprk_triagecategory" operator="in">
          <value>8b62dd84-1fbc-f111-aaaf-3833c5e9614d</value>
          <value>8d62dd84-1fbc-f111-aaaf-3833c5e9614d</value>
        </condition>
        <condition attribute="sprk_receiveddate" operator="ge" value="2026-09-04T03:38:48Z" />
        <condition attribute="sprk_reviewoutcome" operator="ne" value="100000003" />
      </filter>
    </link-entity>
    <link-entity name="sprk_budgetrevision" from="sprk_matter" to="sprk_matterid" link-type="outer" alias="c1">
      <filter type="and">
        <condition attribute="sprk_revisedon" operator="ge" value="2026-09-04T03:38:48Z" />
      </filter>
    </link-entity>
    <filter type="and">
      <condition entityname="c1" attribute="sprk_budgetrevisionid" operator="null" />
    </filter>
  </entity>
</fetch>
```

| # | Compiled from | Result | Verdict |
|---|---|---|---|
| C1 | live Path B body | **4 rows**: `2444af6d` (.01), `dc091784` (PAT-477689), `9f6f23e4` (EMPL-307998), `accb692e` (Form D - 2023) | ✅ positive in; NC1 + NC2 out; = §2 |
| C2 | Path B, `subjectId` = `.01` | **1 row**: `2444af6d` | ✅ fires for the positive matter |
| C3 | Path B, `subjectId` = NC1 | **0 rows** | ✅ |
| C4 | Path B, `subjectId` = NC2 | **0 rows** | ✅ |
| C5 | Path B with the **exists source emptied** (category id = a fresh random GUID, no row carries it) | **0 rows** | ✅ **empty source → nothing, not everything** |
| C6 | Path B with the **notExists source emptied** (`sprk_budgetrevisionid` = a fresh random GUID) | **5 rows** = C7's set exactly (incl. NC1) | ✅ neither everything (62) nor nothing |
| C7 | `exists` clause only | 5 rows: `.01`, `.02`, `9f6f23e4`, `dc091784`, `accb692e` | ✅ reference set for C6 |
| C8 | Path B with the revision window `>= now` (NC1's revision falls outside it) | 5 rows incl. **NC1** | ✅ the compiled window sits in the **join**, not the WHERE |

**Not done, stated plainly.** These ran as the operator (`ralph.schroeder@spaarke.com`), **not** impersonating the
writer principal `3121bf1b-…`: `pac org fetch` cannot set `MSCRMCallerID`, and the live seam test that can
(`SIGNALS_LIVE_CALLER_ID`) could not obtain a token in-process on this workstation (`DefaultAzureCredential`
→ `ExternalTokenManagement Authentication Requested but not configured correctly. 003`). Per
`notes/security-roles.md` §9 the writer reads both Path B tables (`sprk_communication`, `sprk_budgetrevision`) and
the subject (`sprk_matter`) at **Global** depth, so the result set **should** be identical under the writer. That
is an inference from the privilege table, not an observation; the seam test is the way to observe it.

**Re-run after the Step 9.5 fixes (2026-10-04T04:17:52Z).** The compiler was rebuilt with the review fixes
(`<order>`, verified joins, `\z` anchors, …) and C1–C8 were re-emitted and re-executed verbatim. The emitted C1 now
carries `<order attribute="sprk_matterid" />` (the guard aborted the run if it did not, after one stale cached
build was caught and discarded). **Every result set is identical to the table above** (C1 4 rows, C2 1, C3 0,
C4 0, C5 0, C6 = C7 = 5, C8 5 incl. NC1). `pac` prints rows in its own order, so the `<order>` is not
observable in its text output. It affects paging stability only, not membership.

## 3c. Read depth fails closed (task 006 finding, folded in 2026-10-04)

Dataverse trims every query to the rows the caller can read. Under the writer principal, a `notExists` over a
table read at **Basic** (own-rows) depth sees an effectively empty table and is **true for every matter** — the
evaluator would assert something it never checked (§0.3), producing Signals rather than an error; an `exists`
over such a table is silently false. Both read as a working predicate. The compiler therefore refuses any subject
or clause entity outside `PredicateCompiler.EvaluatorGlobalReadableEntities` = the nine tables §9 lists at Global
depth (`sprk_matter`, `sprk_communication`, `sprk_budget`, `sprk_budgetrevision`, `sprk_invoice`, `sprk_project`,
`sprk_event`, `sprk_memo`, `sprk_todo`). **That list mirrors role configuration and can drift from it** (§9's own
finding: the writer's reads come from the root BU default team, so a team-role edit changes them with no code
change). **Requirement carried to task 030**: verify the evaluating principal's *live* read depth for the subject
and every clause entity before evaluating, and fail closed per policy when it is not Global.

### Two Dataverse semantics established empirically before writing the compiler

| Question | Probe | Result | Consequence in the compiler |
|---|---|---|---|
| Does FetchXML `ne` drop null rows (SQL `<>` semantics)? | `sprk_reviewoutcome ne 100000003` on `.01` | **No** — returned 4 rows: the `File` one **plus 3 with null** review outcome | `<>` compiles to plain `ne`. Unreviewed communications count as "not Dismiss" — the recall-favouring reading (decision 13). No extra `or null` branch needed |
| Is a `Z`-suffixed datetime honored as UTC, or re-read in the caller's zone? (SQL shows `sprk_receiveddate` user-local: 08:00 for 12:00Z) | `ge 2026-10-03T11:30:00Z` vs `ge …12:30:00Z` on comm `f00b6389` (12:00Z) | 11:30Z **includes**, 12:30Z **excludes** | Windows are emitted as absolute UTC with `Z`. Honoured as UTC — an EDT reading would have excluded at 11:30Z |

## 4. Query count and shape (step 7 — the evaluator runs this per policy)

- **Count: 1 query per policy per evaluation pass** — not per matter. The nightly pass (task 031) runs one query
  per enabled Existence policy and gets every qualifying subject back. The per-matter event triggers (task 032)
  run the same query narrowed by `Compile(body, subjectId)`, which adds one root `sprk_matterid eq` condition —
  still one query.
- **Shape: 1 root entity + N `link-entity` (N = clauses; Path B = 2: 1 inner, 1 outer)**, `distinct="true"`,
  one projected column. `distinct` is required: an inner join to K qualifying communications would otherwise
  repeat the matter K times (PAT-477689 has three).
- **Paging is the evaluator's concern.** A `distinct` result above one page (5,000) needs the paging cookie; the
  62-matter dev org is far below it. Task 031 must page — the live seam test asserts `MoreRecords == false` so it
  fails loudly rather than silently truncating if the seed ever grows past a page.
- **Latency**: each `pac org fetch` round trip (connect + query) took a few seconds end to end; server time was
  not separable from PAC's connection setup and is not claimed here.

## 4b. Placement Justification + publish size (root CLAUDE.md §10, `bff-extensions.md` §A)

**Placement: in the BFF, in the existing `Services/Signals/` namespace + `AddSignalsModule()`.** It is pure,
synchronous, I/O-free domain code (rule-body JSON → FetchXML string) with no host of its own, so ADR-052's
host question does not arise: it runs inside whichever workload calls it (the ADR-036 nightly job, task 031; the
ADR-004 event handlers, task 032), both already placed in the BFF by spec FR-12/FR-13. No endpoint, no package,
no background work, no AI types (ADR-013). ADR-010: concrete singletons, no interface (one consumer).
§11 justification is the POML's own `<justification>` (three candidate seams checked; none accepts a predicate).

**Publish size** — three fresh detached worktrees at short paths, `dotnet publish -c Release`, zipped with
PowerShell `Compress-Archive` over the publish folder (the `Deploy-BffApi.ps1` method), PDBs included:

| Side | Path | Ref | Files | Zip bytes | MB |
|---|---|---|---|---|---|
| master | `C:\wt21m` | `62277d50a` (origin/master, fetched) | 212 | 47,875,571 | 45.66 |
| branch HEAD | `C:\wt21h` | `3ac193382` | 212 | 47,885,176 | 45.67 |
| HEAD + task 021 | `C:\wt21t` | `3ac193382` + the two changed `src` files | 212 | 47,893,365 | 45.67 |

**This task's delta: +8,189 bytes (+0.008 MB).** Branch-including-task vs master: +17,794 bytes (+0.017 MB).
File counts equal on all three sides (212). Ceiling 60 MB — not approached. `dotnet list package --vulnerable
--include-transitive`: *"no vulnerable packages"*; `Sprk.Bff.Api.csproj` unchanged.

## 5. Deviations from the POML (step 9)

| POML says | Did | Why |
|---|---|---|
| Step 4: "the window as a **relative** date filter" | Window resolves at **compile time** to an absolute UTC `ge` value from `TimeProvider` | FetchXML's relative operator `last-x-days` is a **different predicate**: it truncates to start-of-day in the *calling user's* time zone and caps at "now". The body says `{">=": "now-30d"}`; `ge (now − 30d)` is that, exactly. The window is still relative to *now* — it is recomputed on every compile |
| Output `tests/integration/seam/SignalPredicateTests.cs` | `tests/integration/seam/Signals/SignalPredicateTests.cs` | `tests/integration/seam/README.md`: "New seam tests add files under `seam/{Module}/`"; sibling `PolicyScopeResolverSeamTests` is already in `seam/Signals/`. Same compiled glob, same KEEP category |
| "Given the generated FetchXML, compared with the hand-verified reference **in notes/**" | The reference lives here **and** as a byte-identical fixture under `tests/fixtures/signals/`, which the test reads | A test reading `projects/**` breaks when the project is archived; `tests/fixtures/` is the repo's durable shared-fixture location (cf. `compose-citation-parity`) |
| Step 6 "integration tests over the seeded rows" | Live seam tests are **opt-in** (`SIGNALS_LIVE_DATAVERSE_URL`), skip-via-return otherwise | CI has no Dataverse; the repo's established live-test convention (`SpeAdmin/LiveIntegrationFixture`, `Phase2EndToEndTests.LiveMode_*`). The real-data proof of record is §2–§3 above, run with the exact compiled text |
| — (scope added mid-task) | Compiler refuses subject/clause entities outside the writer's verified Global-read set (§3c) | Coordinator relayed the task 006 finding (`notes/security-roles.md` §9) and asked for it here if it fits. It fits: the compiler is the one point every rule body passes through, and failing closed at compile time is cheaper than discovering a vacuous `notExists` in production. The live-depth check is carried to task 030 |
| Golden test for an empty-**table** source (`sprk_signal`) | Dropped; the empty-source proof is C5/C6 (emptied **by filter**) + V5/V6 (hand-written, genuinely empty table) | The read-depth gate (row above) now refuses `sprk_signal`, so the compiler can no longer emit that query. Nothing weaker is claimed: C5 is the compiler's own output returning nothing over an empty source |
| Booleans | Emitted as `1`/`0` | **Not exercised on real data** — no boolean filter exists in Path B. First rule that filters a boolean must verify it |
| — | `RuleBodySchemaValidator` registered in `AddSignalsModule()` | The compiler depends on it (validate before compile, so `bind`/`$` cannot re-enter through the compiler); task 020 had not registered it. One singleton shared with task 022's future save-time refusal |

## 6. Step 9.5 review (code-review + adr-check, independent agent, 2026-10-04) — disposition

Verdict: pass with findings; **no Critical**, one High; **no §6.5 ADR exception or amendment needed**.

| # | Sev | Finding | Disposition |
|---|---|---|---|
| F1 | High | `path` checked only as a name: a wrong-but-valid lookup (e.g. `sprk_communication.sprk_regardingbudget`) never matches, so `notExists` is true for every matter | **Fixed.** `PredicateCompiler.VerifiedJoins`: closed `(subject, clause entity) → path` list, each entry confirmed against the live schema (MCP `describe`, 2026-10-04): `sprk_communication.sprk_regardingmatter → sprk_matter`, `sprk_budgetrevision.sprk_matter → sprk_matter`. Anything else is refused. Live seam test checks each entry is a Lookup targeting the subject |
| F2 | Med | `$` anchors match before a trailing `\n`, so `"modifiedon\n"` could pass the FR-07 check | **Fixed.** All three regexes anchored with `\z`; the relative-date guard catches `now` + anything non-letter; tests for trailing-newline names and tokens |
| F3 | Med | Seam test used the wall clock; the seed ages out of the window on ~2026-11-02 and would read as a regression | **Fixed.** `FakeTimeProvider` pinned to the reference anchor 2026-10-04T02:15Z |
| F4 | Med | Live seam harness has never actually run | **Accepted as stated, not fixable here.** Recorded as "written, NOT yet run" in the test's own remarks and the completion note. The test now prints the principal it ran as. Observing it is carried to task 030 (runs as the writer in the app) |
| F5 | Med | Unit test at a non-KEEP path | **Fixed for 021**: moved to `tests/unit/domain/Signals/PredicateCompilerTests.cs`. **Not moved**: task 020's sibling `tests/unit/domain/Signals/RuleBodySchemaValidatorTests.cs` (another task's file) — flagged for the coordinator |
| F6 | Med | `distinct` without `<order>` pages inconsistently | **Fixed.** `<order attribute="{subject}id"/>` emitted; golden fixture updated **and re-run on real data** (same 4 rows) |
| F7 | Med | `seam/Signals/**` not adjudicated as a seam sub-category | **Fixed.** Sub-category paragraph added to `tests/integration/seam/README.md` |
| F8 | Med | Allow-listed names / `{name}id` convention / read depth not paired with real schema | **Fixed in the harness**: `CompilerAllowLists_MatchTheLiveSchemaAndTheCallersReadDepth` checks every verified join, every allow-listed primary key, and Global read depth for the calling principal. Same caveat as F4: written, not yet run. Run-time depth check still carried to task 030 |
| F9 | Low-Med | Refusal message asserted a fact the compiler never read (§0.3) | **Fixed.** Now "is not in the verified Global-read allow-list … (security-roles.md §9)" |
| F10 | Low | Column-level security outside the read-depth gate | **Logged for task 030** (a secured column read as null could make a `notExists` vacuous) |
| F11 | Low | No cap on clauses (Dataverse link-entity limit 15) | **Fixed.** `MaxClauses = 15`, refused at compile with a clear message; tested |
| F12 | Low | `now` applied to a text column in a test | **Fixed in the test** (uses `sprk_sentat`). Column-type assumption noted: relative tokens are meant for datetime columns; `Z`-as-UTC verified on `sprk_receiveddate` only |
| F13 | Low | Booleans `1`/`0` unverified on real data | **Logged.** Emitted form now has a test; the first rule that filters a boolean must verify it live |
| F14 | Low | Raw numeric text (`1e3`) | **Rejected (no change).** Such values fail loudly in Dataverse, not silently; schema already types them as numbers |
| F15 | Low | XML-invalid characters threw `ArgumentException` | **Fixed.** Wrapped into `PredicateCompilationException`; tested |
| F16 | Low | Duplicate JSON keys could make validator and compiler read different objects | **Fixed.** Compiler parses first with `AllowDuplicateProperties = false` and refuses; tested |
| F17 | Low | Weak exception-only assertions; missing `when`/boolean/newline cases | **Fixed.** `.WithMessage` on every refusal; tests added for `$` in `when`, a positive `when`, booleans, trailing newlines |
| F18 | Low | "64/64" counted live tests that returned without running | **Fixed in reporting.** Counts now state executed vs returned-without-running |
| F19 | Low | Seam variants were hand copies of the deployed body | **Fixed.** Variants derived from the body read live via `ReadRuleBodyAsync` |
| F20 | Low | Inactive (statecode) rows not excluded | **Logged for rule authors**: the body governs; a rule wanting active-only must say `"statecode": 0` (`when` or clause filter) |
| F21 | Low | `IReadOnlySet` backed by mutable `HashSet` | **Fixed.** `FrozenSet` / `FrozenDictionary` |
| F22 | Sugg | `?? throw` on non-nullable ctor params | **Fixed.** `ArgumentNullException.ThrowIfNull` |
| F23 | Low | Rule-body GUIDs emitted verbatim (ADR-044) | **Rejected (no change).** FetchXML tolerates case/braces; nothing breaks; canonicalization belongs to save-time (task 022) if wanted |
| F24 | Sugg | Golden depends on XLinq formatting | **Accepted.** Test remarks now state that any shape change must be re-run on real data, not re-baselined — done for F6 |
