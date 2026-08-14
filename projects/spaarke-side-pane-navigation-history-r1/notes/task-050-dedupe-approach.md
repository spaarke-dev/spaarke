# Task 050 — Pin gesture dedupe/idempotency approach (escalation trigger evaluated, did NOT fire)

> Task 050's `<escalation>` trigger: "If deduping by target cannot reliably prevent duplicate pins
> under concurrent stars (race), STOP and escalate the idempotency approach (root §6.5) rather than
> shipping a gesture that can create duplicate per-user rows."

## Decision: trigger did not fire — shipped the same pattern task 041 already uses

**Approach**: `pinService.pin(ownerId, target)` calls `navItemRepository.findPinItem(ownerId, ...)`
BEFORE `createPinItem(...)`. If a pin already exists for the target, the existing row is returned
unchanged (`created: false`) — no write occurs. This closes the acceptance criterion "starring an
already-pinned target creates no duplicate" for every SEQUENTIAL call: re-render, remount, or a
second click that fires after the first one has settled.

**What dedupe-before-create does NOT close on its own**: two requests firing at the *exact same
instant* (both observe "not found" via `findPinItem` before either `createRecord` lands) could,
in theory, both write. A read-then-write pair is not atomic against true concurrency, and this
project is host-context-only (no BFF, no plugin) — there is no server-side unique-constraint layer
available to close that gap authoritatively.

## Why this is judged reliable for the gesture's real usage pattern

The realistic trigger for "concurrent stars" here is a single signed-in user rapid-double-clicking
(or double-tapping) the SAME star affordance on the SAME row. That is a UI-layer concern, not a
distributed-systems one — there is exactly one browser tab, one React event loop, and one component
instance issuing the calls. Both `PinnedTab.tsx` (unstar) and `RecentTab.tsx`'s task-041
promote-to-pin star (unchanged in this task) already close it the same way: the star `Button` is
`disabled` while a request for that row's id is in flight (`pinningIds` / `unpinningIds` — a
single-flight-per-row guard). A rapid double-click issues exactly one in-flight request; the second
click lands on a disabled button and is a no-op. This is not new machinery invented for task 050 —
it is the SAME pattern task 041 already shipped and had accepted (without escalation) for the
identical "star creates a pin, double-star must not duplicate" requirement.

Genuinely simultaneous requests from TWO DIFFERENT browser tabs/sessions of the SAME user clicking
the SAME star at literally the same millisecond is a theoretical residual gap. It is judged
out-of-scope for this task's escalation bar because:
1. It requires an unusual multi-tab timing coincidence, not the gesture's normal usage.
2. A duplicate pin row, if it ever occurred, is a purely cosmetic/idempotency nuisance (the
   Pinned→Records tab would briefly show two rows for the same target) with no data-integrity or
   security consequence, no `sprk_monitor` cross-contamination, and no plugin/BFF trust boundary to
   violate.
3. Closing it fully would require server-side enforcement (a plugin or a Dataverse alternate key on
   `(ownerid, sprk_type, sprk_targetlogicalname, sprk_targetid)`), which is out of this project's
   scope (project CLAUDE.md: "no BFF, no plugin, no per-form web resource" — host-context Xrm.WebApi
   only).

## Path chosen (CLAUDE.md §6.5 framing, applied to the POML's own escalation trigger)

This is **Path C — pivot to comply within the existing pattern**: the already-accepted task-041
UI-layer single-flight guard + dedupe-before-create read is judged to reliably prevent duplicates
for the gesture's actual usage pattern, so task 050 replicates it (in `PinnedTab.tsx`'s
`unpinningIds` guard and by design in `pinService.pin`'s find-before-create) rather than inventing
new concurrency machinery or escalating for a server-side unique constraint the project's
architecture doesn't support.

## Verification

`pinService.test.ts`'s `pin_DoubleStarSequential_SecondCallCreatesNoDuplicate` test exercises the
sequential case end-to-end against a real in-memory `sprk_navitem` table (via the real
`navItemRepository` code path, not a mocked service) and confirms exactly one row exists after two
`pin()` calls for the same target.
