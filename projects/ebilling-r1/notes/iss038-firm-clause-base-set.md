# ISS-038 — firm clause of a record rule uses both rule sets' flags

**Status:** fixed on `fix/iss-038-firm-clause-base-set` (local; not pushed). **Ruling:** per-record visibility uses both rule sets' flags (see uac-r2 thread).

## What changed

A record-scoped rule set holds every changed rule of one record (and firm). It is created once, with the "visible to firms" flag of the first rule changed there. `RuleSetService.GetFirmClauseAsync` used to decide a record rule's clause by that record set's flag alone. It now also requires the base rule's own rule set to be visible.

| Piece | Where |
|---|---|
| Helper | `RuleSetClauseVisibility.VisibleToFirms(Entity ruleSet, IReadOnlyCollection<Entity?> baseRuleSets)` in `Services/Finance/RuleSetService.cs`, beside `ForFirm`. |
| Caller | `ReadFirmClauseAsync` passes the helper's answer to `RuleSetClauseVisibility.ForFirm`. `ForFirm` is still the single place that shows or hides clause text. |
| Base lookup | `BaseRuleSetsOfRecordRuleAsync` and `RuleSetOfVersionAsync` (private, same file): base version, then Billing policy, then rule set. |
| Snapshot reader | `StoredProcedureSnapshot.RecordRuleBaseVersionIds` (`Services/Finance/Submission/ProcedureSnapshot.cs`), read from `resolutions[].rules[].recordRule.basePolicyVersionId`. |
| Guard | `tests/Spaarke.ArchTests/RuleSetVisibleToFirmsReaderGuardTests.cs` |
| Tests | `tests/integration/regression/Ebilling/Iss038_RecordRuleFirmClauseBaseSetTests.cs` |

### The helper's rule

- **A set that is not record-scoped:** decided by its own flag, as before. A missing flag is off. No base is read.
- **A record-scoped set:** visible only when all of these hold:
  - the record set's flag is on;
  - at least one base rule set was found;
  - every base rule set was read, is not itself record-scoped, and has its flag on.
- **Fails closed:** a missing slot, an unreadable slot (`Guid.Empty`), or a base version, policy or rule set that cannot be read means not visible.
- **The base's Procedure type:** the base read (`RuleSetOfVersionAsync`) also reads the base rule set's Procedure type, the same Internal Standard rule `FirmFindingsProjection.CitableVersionsAsync` applies to a Procedure's rule set. A base whose Procedure is Internal Standard, or whose type cannot be read, counts as an unreadable base: not visible. A base with no Procedure link (a legacy set) is decided by its flag.
- **Trace:** when the base is missing, unreadable or not usable, `ReadFirmClauseAsync` writes one Information entry carrying only the invoice id and the policy version id (structured parameters; no names, no clause text, no firm data).

## Base-rule source: the snapshot

The base comes from the invoice's own `procedureSnapshot`, the record-rule slot written at evaluation (`RecordRuleSlot.BasePolicyVersionId`). That is the basis the invoice was actually judged on, and `GetFirmClauseAsync` already reads it and never re-resolves (task 083). Reading the base from the snapshot keeps one authority for "what judged this invoice".

The two alternatives were rejected:

- **The live override row** (`ProcedureResolver` / `sprk_procedureassignment`). It can change or be removed after the invoice was judged, so it would be a re-resolution.
- **The record rule version's `sprk_basedonversion`.** It names the same base policy, but it is not the judged basis.

Every version of the base policy leads to the same policy and therefore the same rule set, so the flag answer does not depend on which base version the slot holds. When a record rule appears on several dates with different base versions, all of them must be visible.

Rows stored before 082d have no snapshot and already show no clause, so this fix adds no new "no snapshot" case.

## Guard: `RuleSetVisibleToFirmsReaderGuardTests`

The guard scans `src/server/**/*.cs` with line comments removed. It finds every occurrence of the column name, in any casing and also in the Web API `_value` form, and attributes each one to its enclosing member. The guard fails when:

- a (file, member) pair names the column but is not in the reviewed allow-list;
- a listed pair names the column a different number of times than its entry says;
- a listed pair no longer names the column (a stale entry).

**Controls:**

- Must-fire:
  - planted readers in an unlisted member (typed read, mixed case, OData `$select`, column add);
  - an extra read inside a reviewed member;
  - a stale entry.
- Must-not-fire:
  - comments;
  - a longer column name;
  - the helper call;
  - a reviewed shape.
- Real-tree control: `ReadFirmClauseAsync` names the column nowhere, and `VisibleToFirms` does.

The maintenance procedure is in the file's remarks.

**Fail-before:** with the old decision line restored, the scan names `RuleSetService.cs (ReadFirmClauseAsync)` as an unreviewed reader, and the real-tree control fails.

### Readers found, all reviewed (none decides a record set's content by its flag alone)

| File | Member | Count | Kind | Why it is not a record-content decision |
|---|---|---|---|---|
| RuleSetService.cs | `VisibleToFirms` | 2 | Decision | The helper itself. |
| RuleSetService.cs | `RuleSetColumns` | 1 | Select | Column list only. |
| RuleSetService.cs | `PublishedResult` | 1 | Internal admin read | The set comes from `LoadRuleSetAsync`, which refuses record-scoped sets. The response goes to an internal admin. |
| RuleSetService.cs | `Summarize` | 1 | Internal admin read | The sets come from `ListAsync` (record-scoped sets filtered out) or from `LoadRuleSetAsync`. |
| RuleSetService.cs | `SetVisibilityAsync` | 1 | Write | Non-record sets only. |
| RuleSetService.cs | `NewProcedureRuleSet` | 1 | Write | Initial flag of a Procedure's set. |
| ProcedureAssignmentService.cs | `LoadRuleAsync` | 2 | Select + seed read | Reads the base set (record-scoped sets are refused) to seed the record set's flag. |
| ProcedureAssignmentService.cs | `RecordRuleSetCreateAsync` | 1 | Write | The record set's initial flag. |
| ProcedureService.cs | `UpdateAsync` | 1 | Write | A type change resets the flag. |

The brief's line numbers map to `PublishedResult` (~2385), the old `ReadFirmClauseAsync` read (~3145, now the helper) and `Summarize` (~3455). No `IsVisibleToFirms` helper exists on this base, only `ProcedureService.InitialVisibleToFirms`, which computes a type default and reads no column. The only reader fixed is `ReadFirmClauseAsync`.

## Tests (regression, KEEP path)

All tests are in `Iss038_RecordRuleFirmClauseBaseSetTests`.

- **Must-fire, firm clause.** Runs the real `ChangeRuleAsync` twice on one record and firm: first a rule whose base set is visible, then a rule whose base set is hidden. Both land in one record set, and the set's flag is on. `GetFirmClauseAsync` on the second rule returns nothing: no quote, no section reference and no label. The first rule still shows.
- **Must-fire, projection.** The same data run through the real `FirmFindingsProjection` with the real clause reader. Only the first rule is cited, and the output JSON carries none of the second rule's clause text.
- **Missing base.** The base version is gone, the base policy is gone, the base rule set is gone, or the snapshot has no slot. Each case shows nothing; a readable visible base is the control.
- **Base Procedure type.** A base whose Procedure is Internal Standard shows nothing, in the clause reader and in the real projection output (no citation, no clause text, no rule set or Procedure name). A base whose Procedure type is missing, whose type read faults, or whose Procedure row is absent shows nothing (a Guideline base is the control). One Information entry names the invoice and the version ids only.
- **Both sets visible.** The clause shows with the neutral label. With the record set hidden and the base visible, nothing shows.
- **Non-record set.** Decided by its own flag, as before; only the version itself is read, with no base lookup.
- **Helper and snapshot reader.** Truth table, fail-closed cases, and per-version base ids. A damaged slot reads as `Guid.Empty` and the snapshot still reads.

Test changes outside the new files:

- `RuleSetServiceTests.GetFirmClause_ForARecordRule_CarriesTheNeutralLabel` now seeds a visible base and its slot. Without a base the clause now fails closed, which is the fix.
- A new shared builder, `RuleSetServiceTests.FindingsWithRecordRules`, is used by the regression file.
- `ProcedureAssignmentServiceTests.World`/`CapturingLogger` and `FirmFindingsProjectionTests.World`/`ListLogger` changed from `private` to `internal`, so the regression file can drive them.

**Fail-before (base Procedure type):** with the base's Procedure check switched off, 5 of the 6 new cases fail (the Internal Standard and unreadable-type cases, the projection case, the fault case and the trace case); the Guideline control passes either way.

**Fail-before (guard string literals):** with the guard's previous line-comment strip restored, the 7 new URL-string controls fail.

**Tenant-style isolation:** no tenant-boundary test fits, because the defect is inside one record set of one firm. The per-rule isolation is what the must-fire test shows: two rules in the same set are decided by their own bases.

**Fail-before:** with the decision line reverted to the record set's flag, 3 of 13 regression tests fail: both must-fire tests and the missing-base test. The others test the helper directly, positive paths or unchanged behaviour.

## Read-only query (not run)

This query counts record-scoped Billing rule sets that are visible to firms and hold a record rule whose base rule set is not visible. The tables and columns come from the code:

- `sprk_ruleset`: `sprk_scope`, where Record = 100000002 and Firm record = 100000003; `sprk_visibletofirms`; `sprk_domain`, where Billing = 100000000; and the core lookups `sprk_matter`, `sprk_project` and `sprk_workassignment`.
- `sprk_policy`: `sprk_ruleset`.
- `sprk_policyversion`: `sprk_policy`, and `sprk_basedonversion`, which on a record rule's version names the base version it was made from.

A set counts as record-scoped when it has a record scope or any core lookup, matching `RecordRuleSet.IsRecordScoped`.

```xml
<fetch aggregate="true">
  <entity name="sprk_ruleset">
    <attribute name="sprk_rulesetid" alias="record_sets_affected" aggregate="countcolumn" distinct="true" />
    <filter type="and">
      <condition attribute="sprk_domain" operator="eq" value="100000000" />
      <condition attribute="sprk_visibletofirms" operator="eq" value="1" />
      <filter type="or">
        <condition attribute="sprk_scope" operator="in">
          <value>100000002</value>
          <value>100000003</value>
        </condition>
        <condition attribute="sprk_matter" operator="not-null" />
        <condition attribute="sprk_project" operator="not-null" />
        <condition attribute="sprk_workassignment" operator="not-null" />
      </filter>
    </filter>
    <link-entity name="sprk_policy" from="sprk_ruleset" to="sprk_rulesetid" link-type="inner" alias="recordrule">
      <link-entity name="sprk_policyversion" from="sprk_policy" to="sprk_policyid" link-type="inner" alias="recordversion">
        <link-entity name="sprk_policyversion" from="sprk_policyversionid" to="sprk_basedonversion" link-type="inner" alias="baseversion">
          <link-entity name="sprk_policy" from="sprk_policyid" to="sprk_policy" link-type="inner" alias="basepolicy">
            <link-entity name="sprk_ruleset" from="sprk_rulesetid" to="sprk_ruleset" link-type="inner" alias="baseset">
              <filter type="or">
                <condition attribute="sprk_visibletofirms" operator="eq" value="0" />
                <condition attribute="sprk_visibletofirms" operator="null" />
              </filter>
            </link-entity>
          </link-entity>
        </link-entity>
      </link-entity>
    </link-entity>
  </entity>
</fetch>
```

To list the sets instead of counting them, drop `aggregate="true"` and the `countcolumn` attribute and add `<attribute name="sprk_rulesetid" />`.

The inner joins skip record versions whose base version, policy or rule set no longer exists. The code now hides those as well (fail closed), so they are not exposure.

The count is an upper bound: it does not filter the base policy's `statecode`, and it counts record rules that no invoice applied. It also does not check the base set's Procedure type, which the code now does. It says how many record sets to look at, not how many clauses a firm saw.

## Known limits (K)

- **K1: a stale record-set flag can still hide a clause.** The record set's flag is still a copy of the first rule's base flag. If that first rule's base was hidden, a later rule whose base is visible stays hidden. This fails closed and is not a disclosure. Changing it would mean deciding by the base flags alone, which is a separate owner decision.
- **K2: Internal Standard check for record rules (closed).** A record set has no Procedure link, so `FirmFindingsProjection.CitableVersionsAsync` cannot apply its Internal Standard check to it. The base read now checks the base set's Procedure type: an Internal Standard base is not shown, and a base whose type cannot be read is not shown. See uac-r2 thread for background. Regression: `Iss038_RecordRuleFirmClauseBaseSetTests` (clause reader, projection output, fault, missing type, one Information trace entry).
- **K3: what the guard does not see.**
  - It works per member, not per statement.
  - It ignores `//` inside regular, verbatim, interpolated and raw string literals (a URL string naming the column is reported), and keeps verbatim and raw string state across lines. It still does not see block comments, column names built at run time, or an allow-listed const alias.
  - New callers of `Summarize` and `PublishedResult` are not seen as new readers: their "never record-scoped" reasoning rests on today's callers (`ListAsync` and `LoadRuleSetAsync`).
  - It does not see readers outside `src/server`. The RuleReview code page reads the flag only through the internal admin rule-set routes.
- **K4: extra reads per record rule.** Each record rule's clause read now makes up to three extra reads per distinct base version (version, policy, rule set). There is normally one base, and the projection reads each version once per invoice.

## Gates

- `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror`: 0 warnings, 0 errors.
- Filtered unit tests (`RuleSet | FirmClause | FirmFindings | ProcedureAssignment | ProcedureResolver | ExternalInvoice | Iss038`): 1817 passed, 0 failed (after the base Procedure type check).
- Filtered ArchTests (`Finance | FirmFindings | RuleSet | RuleSetVisibleToFirmsReader`): 66 passed, 0 failed; the guard alone 21 passed.
- Adjacent filters run as well:
  - ArchTests `Procedure | Snapshot | Findings | Billing | CurrentVersion | RecordScope | Census`: 293 passed.
  - Unit `ProcedureSnapshot | ProcedureChangeBanner | InvoiceSubmission`: 1542 passed.
- Publish-size delta: no new file in the BFF; delta not measured. The change sits inside existing files: `RuleSetService.cs` and `ProcedureSnapshot.cs`.
