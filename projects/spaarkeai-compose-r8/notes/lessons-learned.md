# Lessons learned — `spaarkeai-compose-r8`

> Written at wrap-up, 2026-09-01. Kept to things that changed how the work was done, not a summary of it.

## 1. A passing test proves nothing until a mutation has made it fail

Used on every fix this project shipped, and it earned its keep repeatedly:

- **#698** — the negative control **falsified the test's own header**. I had claimed that dropping the
  style-numbering lookup shifts every deeper label. It does not: Word gives an un-incremented level its
  `start` value, so `1.1.1.` is unaffected and only the style-numbered paragraphs and their siblings'
  counter advance are damaged. The header was rewritten to say what the mutation actually showed, and one
  of the four tests was labelled decorative under it rather than left to imply coverage it did not have.
- **#777** — asking *what happens on the fail-open path* caught a near-miss: emitting the warning only
  inside the merge loop would have left the **total-loss** path (no baseline) silent, making the worst
  outcome the quietest and inverting ADR-049's never-silent rule.
- **090 wrap-up** — a seeded doc mutation exposed that the residual-loss parity check could not see the
  row I had just added (§4 below).

**The corollary that cost time twice**: assert the mutation is *in the file* and the build is *green*
before trusting a result. A stale binary reports the old behaviour as a pass. It happened once here, on a
`sed` that produced literal newlines inside a C# string; the test "passed" against a binary that never
compiled the change.

## 2. When an issue's premise is wrong, correcting it is most of the value

- **#696** named `/api/compose/project` **and** `/upload` as both running synchronous OOXML projection on
  the request body. Only `/project` does. Bounding `/upload` on that reasoning would have been a change
  made for a stated cause that was not true.
- **#699** reported a defect that had **already been fixed** by another project. The valuable work was
  finding what the fix silently depended on — a hand-written client mirror of a C# parser with no
  mechanism keeping the two in step.
- **#858**'s remaining client work looked like deleting a dead field. The field was the harmless half; the
  **pre-save container gate** next to it was actively refusing saves the server would have completed.

Reading the issue is not the same as reading the code the issue is about.

## 3. Deviating from an issue's prescription is fine — silently is not

**#781** specified "newest `modifiedon`" for canonical-row selection. `modifiedon` moves whenever a row is
touched, so two concurrent saves could pick **different** canonicals — reintroducing the split-brain the
unique key exists to prevent. Used oldest `createdon` instead (immutable, and the oldest row is the one
downstream records already point at), and the issue's suggestion was quoted alongside the reason for not
taking it. Same for declining to delete duplicate rows during a user's save.

## 4. A guard can pass because it cannot see the thing it is guarding

The residual-loss doc has a parity test whose stated job is to fail if the document and the renderer
disagree **in either direction**. I added a row to the document; the test stayed green. Three separate
reasons, each found by seeding a removal and watching nothing happen:

1. The measured-family table had no interior-section-break family — a structural gap, not inattention:
   every other family is a **run**, and `w:sectPr` lives in `w:pPr`, so it could not ride the existing hook.
2. The code was missing from the check's hard-coded `known` list.
3. Direction A scanned the **whole document**, so prose *discussing* a code satisfied it — including, on
   the second attempt, the sign-off amendment's own table.

Each fix made the next hole visible. **A green guard is evidence about the guard, not about the code**,
until you have watched it go red.

## 5. Hand-maintained counts in comments drift every single time

The provenance census header has now been wrong on four separate occasions — "nine" when there were
twelve, "eleven" when there were eight, "seven" when a machine count said six. The comment warning about
this drift was itself part of the drifting comment. Fixed by writing the `grep -c` recipe into the file
rather than another number.

## 6. Parity between two runtimes needs two mechanisms, not one

For the C#/TypeScript citation resolvers:

- A **shared behavioural corpus** catches drift in what the parsers *do* for shapes already covered — but
  cannot catch a shape nobody wrote a case for.
- A **source-level pin** on the vocabulary and enums catches a one-sided addition — but says nothing about
  behaviour.

Ported test cases, which is what existed before, are neither: two hand-kept copies of the same
expectations cannot detect drift between themselves.

## 7. An unfreeze trigger must be something you can observe yourself

Recorded mid-project and worth keeping. Our unfreeze condition on `ComposeService.cs` read *"their comment
on #858"* — while our own last comment there opened *"✅ DEFINITIVE — you are unblocked. Nothing here needs
a reply."* We asked for a signal and told the other party not to send it. Waiting on it would have blocked
us indefinitely. Replaced with an observable condition: a PR merging.

## 8. Close the issue when the work lands

Four issues completed during this project (#776, #777, #781, #696) were still open at wrap-up, discovered
only because the owner asked "are there any other deferred items?". The work was done, tested and
committed; the tracker said otherwise, which is the same as the work being invisible. Closing an issue is
part of finishing it, not paperwork after it.

## 9. What the owner's directives changed in practice

- *"No deferrals — whether there are 8 or 10 or 100 issues, we fix them all"* turned several "file a
  follow-up" moments into fixes. Where a deferral genuinely remained (the letter/roman corpus fixture, the
  repair script's live run), it is named as one with what it needs and who must act.
- *"Do not display messages that are not useful/actionable"* is why two retired warning codes had their
  **client copy removed**, not just their emission — and why the new `storage-failed` copy names the admin
  action instead of saying "contact an administrator" with nothing to ask for.
