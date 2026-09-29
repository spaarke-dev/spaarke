# 034 — sparkle wiring: the existence gate, and the escalation it fired

> **Date**: 2026-08-26 · **Task**: 034 · **Shipped as**: RecordHeader v1.1.4
> **Status**: complete. Also closes [`ISSUE-recordheader-integration-test-stale.md`](../issues/ISSUE-recordheader-integration-test-stale.md).

---

## What shipped

The sparkle shows when the summary attribute **exists in entity metadata** — never on whether it
holds a value. An existing-but-empty column still renders the affordance, and the popover reads
*"No summary yet."* That is the **expected** state at ship time: the owner created
`sprk_recordsummary` on all six rollout entities and a separate project populates it.

When the attribute is genuinely absent, `RecordHeaderView` **omits the `aiSummary` prop entirely**
and `HeaderToolbar` renders no sparkle. Passing a fetch that resolves `null` would instead have
produced a sparkle whose popover is permanently empty — the dead affordance FR-17 rules out.

| metadata has it | value | sparkle | popover | in `$select` |
|---|---|---|---|---|
| yes (default field) | populated | shown | the text | yes |
| yes (default field) | `''` / `null` | shown | "No summary yet." | yes |
| yes (configured field) | populated | shown | the text | yes |
| **no** | n/a | **hidden** | n/a | **no** |

---

## The escalation — the POML's premise came from the red suite

Task 034 required the empty state to read *"No summary yet"* under testid
`sparkle-popover-empty`, citing `recordHeader.integration.test.tsx:422`.

**The shipped `AiSummaryPopover` had neither.** Its empty copy was *"No summary available for this
document."* and it carried **zero** `data-testid` attributes. The POML had inherited its premise
from the known-red suite — the same suite this task was assigned to fix.

The trigger says missing behavior lands in the shared lib and the popover must not be forked. Three
ways to satisfy that:

| option | blast radius | verdict |
|---|---|---|
| Change the default copy globally | **9 shipped surfaces** — VisualHost, SemanticSearchControl, DocumentCard, RichFilePreview, InsightSummaryCard, DocumentLibrary, MessageQuickView, CalendarVisual, MatterHeader | rejected |
| **Optional `emptyText` + testids** | **zero** — omitted by every existing caller, so their output is byte-identical | ✅ **chosen** |
| Accept the document copy | wrong words on a Matter or Project header | rejected |

Reversing it later is one line, which is why this did not block on the owner.

---

## Two deviations from the authored steps

### (a) The metadata request now names BOTH summary candidates

Step 1 as written could not work. `extractConfiguredAttributeNames` contributes a summary field
only when `layoutJson` names one explicitly; it cannot contribute the **default**, because task 031
deliberately passes `summaryField` through as `undefined` so the wiring site owns that decision.

That ordering deadlocks: the effective field comes from the resolved config → the config comes from
metadata → metadata is fetched **by name**.

`buildMetadataAttributeNames` breaks the cycle by requesting both candidates up front. The existence
check then simply reads whichever won.

**Why this was load-bearing rather than tidy:** `sprk_recordsummary` sits on **none** of the six
rollout entities' FORMS. Without this, the metadata payload would never contain it, the gate would
fail on every entity, and the sparkle would be invisible everywhere — with no error, no warning,
and nothing in the console to explain why. It would have looked exactly like "task 034 was never
done."

### (b) A metadata retry that cannot blank the header

The requested list is a union of form controls, every `layoutJson` name, and both summary
candidates — so it can legitimately contain a name the entity does not have. A maker typo in
`summaryField` is the expected case, and FR-17's negative path requires the header to survive it.

`Xrm.Utility.getEntityMetadata` is documented to **filter** on that argument, so an unknown name
should simply be absent from the result — which is precisely the signal the gate reads. But if a
host ever rejected instead, the two-argument form would take the whole header down: no metadata →
no resolved config → no fields. A blank form over one mistyped character is what NFR-10 forbids.

`useHeaderFormMetadata` now retries **unprojected** on rejection. Same shape as the no-`$select`
retry in `useRecordFieldValues` (FAILURE-MODES **G-12**) — the read path already learned this.

---

## `$select` safety (FR-23 / RS-1, third occurrence)

The summary column joins the `$select` **only after** the existence check passes. A `$select`
naming a column Dataverse does not recognise fails the **entire** retrieve with HTTP 400 and blanks
every cell — that is RS-1, which took the shipped Matter header down on every record.

Both branches are unit-tested against the pure `buildSelectFields`, not just through a render.

---

## FR-22a — one source of truth, enforced

The field name is imported from `@spaarke/ui-components/dist/hooks/toolbarLaunchDefaults`, never
re-declared. The v1.0.20 sparkle regression **was** two copies of that literal drifting apart. A
source-grep test now fails if any file under `control/` reintroduces the string.

`RECORD_SUMMARY_EMPTY_TEXT` was added beside `RECORDSUMMARY_FIELD` for the same reason: the PCF
suite and the shared integration suite both assert that copy, and declaring it twice is how the
next drift starts.

---

## The integration suite carried FIVE stale contracts, not one

[The issue note](../issues/ISSUE-recordheader-integration-test-stale.md) diagnosed one cause (the
removed `sparklePopoverOpen` API). Rewriting it surfaced four more — every one an assertion that
had drifted from shipped behavior, none a product defect:

| # | the suite asserted | shipped reality | how it was settled |
|---|---|---|---|
| 1 | `sparklePopoverOpen` / `sparklePopoverContent` from the hook | removed at v1.0.10; the consumer composes `aiSummary` | hook JSDoc still documented the removed API — **deleted**, replaced with the real pattern |
| 2 | badges read `@odata.count` | `Xrm.WebApi` strips it; `useRelatedCount` counts `entities.length` | the mock returned a fake count **beside an empty `entities: []`** — it was itself the mask the hook's header warns about |
| 3 | `pageInput.name` | `webresourceName` | the hook says so at its own call site |
| 4 | `pageInput.data` is an object | URL-encoded **string** | a comment 90 lines above the call site claimed "object" and "tests verify the object shape" — **corrected** |
| 5 | sparkle named "AI Summary" | **"View AI summary"** — the Button's own `aria-label` beats the Tooltip's | asserted as a regex so it survives either |

Per **AP-8**, each was checked against the source before the test was touched. In every case the
source was right and already documented its own correction — #2 and #4 in this same file, #3 at the
call site. The tests were believing stale **comments** over adjacent code.

**Result: 2/10 → 11/11.** Repo-wide known-red drops from **9 suites to 8**.

---

## Verification

| | |
|---|---|
| RecordHeader PCF suite | **72 / 72** (was 43 — +29) |
| `recordHeader.integration` | **11 / 11** (was 2 / 10) |
| All suites touching changed components | **21 / 22** — the one failure is `RichFilePreview`, **empirically confirmed identical at HEAD** via `git stash` |
| Bundle | **100,352 B** (was 99,068) — **40%** of the 250,000 ceiling |
| Version sync | 1.1.4 across all **5** ADR-020 locations |
| ADR-021 / NFR-05 / NFR-06 | grep-clean — no hex, no `@spaarke/auth`, no `fetch(` |

**Not verified: anything at runtime.** Three clean builds have shipped a broken control in this
project. The four `<ui-tests>` remain owner-executed at re-UAT.
