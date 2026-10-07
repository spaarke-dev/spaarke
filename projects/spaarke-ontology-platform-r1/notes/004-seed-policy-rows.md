# Task 004 — seed the first Policy + PolicyVersion rows: record

> **Date**: 2026-10-03 · **Status**: complete · **Env**: `spaarkedev1` · solution `OntologyPlatformSolution`

## Rows created (reversible — delete these two GUIDs to undo the seed)

| Table | GUID | Key fields |
|---|---|---|
| `sprk_policy` | `4d204810-61bf-f111-aaaf-0022482913fc` | `sprk_name`=Budget Variance Policy · `sprk_policycode`=POL-COMMIT-BUDGET · `sprk_enabled`=**No** · `sprk_priority`=500 · `sprk_lane`=Decide (100000000) · `sprk_subjecttype`=Matter (100000000) · `sprk_countstowardsuppression`=Yes · `sprk_currentversion`→ the version row below |
| `sprk_policyversion` | `42b3e716-61bf-f111-aaaf-0022482913fc` | `sprk_name`=POL-COMMIT-BUDGET v1 · `sprk_policy`→ the policy row above · `sprk_versionnumber`=1 · `sprk_ruletype`=Existence (100000002) · `sprk_inforcefrom`=2026-10-03 · `sprk_inforceto`=null (in force) · `sprk_authoredby`=Ralph Schroeder (`e44317a0-bb2e-f111-88b5-7ced8d1dc988`) |

No `sprk_signal`, `sprk_decisionrecord` or `sprk_budgetrevision` rows were created — out of scope for this task
(task 005 owns dev-data + negative controls; task 041 owns the append-only negative test, per
`notes/ontology-architecture-feedback.md` §8.3).

**sprk_enabled confirmed No** on read-back (`sprk_enabledname: "No"`) — the policy is inert until a reviewer
turns it on, per CLAUDE.md §2's trap and spec FR-10.

## sprk_rulebody (validated)

```json
{
  "type": "Existence",
  "subject": "sprk_matter",
  "when": {},
  "all": [
    {
      "exists": "sprk_communication",
      "path": "sprk_regardingmatter",
      "filter": {
        "sprk_triagecategory": ["8b62dd84-1fbc-f111-aaaf-3833c5e9614d", "8d62dd84-1fbc-f111-aaaf-3833c5e9614d"],
        "sprk_receiveddate": { ">=": "now-30d" },
        "sprk_reviewoutcome": { "<>": 100000003 }
      }
    },
    {
      "notExists": "sprk_budgetrevision",
      "path": "sprk_matter",
      "filter": { "sprk_revisedon": { ">=": "now-30d" } }
    }
  ]
}
```

Two independent ANDed clauses (`all`), no `bind`/`$`-reference between them. Validated with Python
`jsonschema` against the shipped
`src/server/api/Sprk.Bff.Api/Services/Signals/Schemas/existence-rule.schema.json` **before** writing it to
Dataverse, and re-validated against the string read back from the created row afterward — both passed. The
two triage-category ids are `Fee / rate change` (`8b62dd84-...`) and `Scope / budget change` (`8d62dd84-...`),
confirmed live via `mvp-technical-spec.md` §10.1 and a direct `sprk_triagecategory` query. `sprk_reviewoutcome
<> 100000003` excludes `Dismiss`, matching mvp-technical-spec.md §11.4/§3.4a exactly.

## sprk_shortheadline / sprk_messagetemplate / sprk_proposedaction

- **Headline**: "Fee or scope change with no budget revision in 30 days" — states only what the two clauses
  test, nothing more.
- **Message template**: "A commitment with financial consequence was raised in the last 30 days and the
  budget has not been revised in that period." — this is `mvp-technical-spec.md` §11.4's own corrected
  message verbatim (the spec document that explicitly rewrote the first-draft message because it asserted a
  budget *comparison* the predicate never computed — §0.3's own worst bug, per CLAUDE.md §3.1). It is a static
  sentence with **zero field placeholders**, so acceptance criterion 3 ("every referenced field is one the
  body actually reads") holds vacuously, and criterion 4 (no budget-comparison claim) holds by inspection —
  "has not been revised" is an existence statement, not a comparison of amounts.
- **Proposed action**: "send-budget-inquiry: notify the matter's billing/finance contact that a budget
  revision may be needed before proceeding." The `send-budget-inquiry` slug is `mvp-technical-spec.md`
  §3.3/§11.4's own identifier; no `sprk_analysisaction` row exists under that name yet (queried — 50 existing
  rows, none matching), so this stays a text proposal, reviewable by a human, not a bound lookup.
  `sprk_policyversion.sprk_proposedaction` is plain `NVARCHAR(400)`, not a lookup — the lookup-to-action field
  (`sprk_decisionrecord.sprk_action` → `sprk_analysisaction`) belongs to a different table and a later stage
  (what was actually done), confirmed by `notes/001-schema-verification.md`.

## One deviation from this POML's literal text — and why

Step 1 says literally: *"subjecttype set to the communication subject."* I set
**`sprk_policy.sprk_subjecttype` = `Matter` (100000000)`, not `Communication`.** Reasoning:

1. The Existence rule body's own `"subject"` field is `"sprk_matter"` — both in the shipped example
   (`mvp-technical-spec.md` §3.4a) and in the project's own corrected predicate (§11.4: `subject: sprk_matter`,
   `all: [exists sprk_communication …, notExists sprk_budgetrevision …]`). The rule evaluates **per matter**,
   checking whether a qualifying communication exists and no budget revision exists, for that matter.
2. §11.5 names two different placements for the same underlying data: (a) **communication**-subject — a
   column/badge inside existing reconciliation grids, described as "on-ramp", and (b) **matter**-subject — a
   new worklist, described as **"the differentiated one"**. Task 004's own goal is the Path B policy that
   produces that differentiated worklist signal, not the on-ramp grid badge.
3. Setting `sprk_subjecttype = Communication` while the rule body's own `"subject": "sprk_matter"` would leave
   the policy's declared subject type inconsistent with what its rule body actually evaluates against — the
   kind of mismatch §0.3 exists to catch in message text, and the same principle applies to this column.

Per the POML's `<steps mode="directional">` contract, step wording is guidance subordinate to the `<goal>` +
`<acceptance-criteria>` + the project's own spec; none of the four acceptance criteria name `sprk_subjecttype`,
and the deviation is recorded here per step 9 rather than silently diverging.

## Step 6 — "enable the ten classifierguidance rows" — already true, verified, no write made

Queried `sprk_triagecategory` directly before writing anything: **all 10 rows already have
`sprk_enabled = true`** and a populated `sprk_classifierguidance` value (confirmed row-by-row, including
`Fee / rate change`, `Scope / budget change`, and the `Unclassified` abstention row). This matches
`mvp-technical-spec.md` §10.1/§16.4's "all ten enabled" note — the 2026-09-29 disabled-by-default trap this
step guards against had already been fixed by an earlier session. No `update_record` call was made against
`sprk_triagecategory`; acceptance criterion 5 is satisfied by the existing state, confirmed by direct query
rather than assumed from the notes.

## What was deliberately left undone

- No `PublishAllXml` or any tenant-wide publish was run (hard rule for this task; the two created rows are API
  writes, immediately live for API consumers without a publish).
- No `sprk_signal`, `sprk_decisionrecord`, `sprk_budgetrevision`, or dev `sprk_matter`/`sprk_communication`
  rows were created — that is task 005's scope.
- No code changes — this task is pure data, confirmed against root CLAUDE.md §3 (no `.claude/` writes needed)
  and ADR-002 (no plugins; nothing here is a plugin).

---

## v2 re-seed (task 009) - 2026-10-07

**Env**: `spaarkedev1`. Preconditions: 0 `sprk_signal` rows (counted before writing). Task 024 had not merged, so the
quiet-window knob is omitted (the 14-day default applies); v2's body is v1's byte-for-byte as read back.

**Validation (step 2)**: `PolicyVersionValidator.ValidateForSave("Existence", <v1 body>, <v2 template>)` run from a
scratch console project against a clean worktree at HEAD `4ddcb27fb` (task 024's uncommitted edits excluded):
`IsValid=True Reason= Errors=[]`. Scratch worktree removed afterwards.

**Rows written (all read back)**

| Row | GUID | Read-back values |
|---|---|---|
| `sprk_policyversion` v2 (new) | `e51906ea-62c2-f111-a05a-7c1e520a989f` | name `POL-COMMIT-BUDGET v2` · versionnumber 2 · ruletype Existence (100000002) · inforcefrom `2026-10-07T08:00:00` (local display; written `2026-10-07T12:00:00Z`) · inforceto null · authoredby Ralph Schroeder · shortheadline and proposedaction copied from v1 · template: "A communication on this matter was classified as a fee or scope change in the last 30 days, and no budget revision was recorded in that time." (literal, no tokens, no witness values) · decisionplan `{"actions":["send-budget-inquiry","revise-budget","approve-variance"],"nextSteps":[]}` |
| `sprk_policy` POL-COMMIT-BUDGET | `4d204810-61bf-f111-aaaf-0022482913fc` | name "Classified fee or scope change, with no budget revision since" · shortname "Fee or scope change, no budget revision" · worktype Approve or rebudget (100000001) · currentversion = v2 · enabled **No** · priority 500 (both unchanged) · lane Decide · subjecttype Matter |
| `sprk_policyversion` v1 (stamp only) | `42b3e716-61bf-f111-aaaf-0022482913fc` | inforceto `2026-10-07T08:00:00` = v2 inforcefrom; body, type, template unchanged (identical to the 004 values) |

**For task 036**: the catalog must contain exactly the codes `send-budget-inquiry`, `revise-budget`, `approve-variance`,
resolved in that order. The spec does not fix the JSON shape of `sprk_decisionplan`; this seed uses
`{"actions":[...codes in order...],"nextSteps":[]}`. If 036 chooses a different shape, a new version is needed
(plans are immutable after publish, FR-49) - cheap now with 0 Signals.

**Deviations**: none from the POML.
