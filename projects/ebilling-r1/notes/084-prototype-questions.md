# 084: data-shape questions from spaarke-prototype (2026-10-21)

The prototype for the 084 UI (O-14) has not started yet: spaarke-prototype will start it when the owner asks. Its likely home is a new surface in `spaarke-prototype/projects/ebilling.r1`. Task 084 (and 089 where noted) answers each question below in its DTO notes, so the prototype can use mock data of the right shape.

**Procedures list**
- a. Per row: Procedure id and name, Area, Type, and the version in force today (number, effective from/to). Does the row carry every matching source (All records / Firm / Manual d0 / Manual d1 plus which parent), or only the winning source with its level?
- b. Is an Excluded Procedure still returned (mode Excluded, reason, who and when, at which distance)? The UI shows it struck through.
- c. Pinned: the pinned version and the newer version it skips ("pinned to v1, v2 available").
- d. Blocking gaps (two d1 parents with different pins, I-P1 overlaps, O-22 "no procedure"): a row flag, or a separate gaps array?
- e. Firm picker: which firms are offered? `sprk_billingfirm` is the default. Are firms with invoices on the record added?

**Rules in force**
- f. Per rule: code, name, type, a readable parameter summary, the source Procedure Version, level and distance, firm scope, and marks:
  - displaced, and by which rule;
  - excluded, with the reason and an optional firm;
  - changed, with the record rule id and the based-on version;
  - stale;
  - base retired.

  One marks array, or separate booleans?
- g. Is the date parameter the invoice work date? Is the default today?

**Overrides**
- h. Removal preview: is there a read for it (the open In Review invoices that get the banner, and the findings that close per D-170)? Is it in 084 or 089?
- i. Change rule: does 089 expose the typed parameter schema per rule type (D-27 templates), or does the editor start only from the base rule's current parameters?
- j. The pin, exclude and change request shapes on 089's routes: send their location to spaarke-prototype when they land.
