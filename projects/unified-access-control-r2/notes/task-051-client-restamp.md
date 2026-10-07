# Task 051 — RegardingResolver PCF: ancestor re-stamp on set / reparent / clear (FR-26)

> Client half of FR-26. Task **050** put the derivation contract in the shared service; task **052**
> mirrors it server-side. This task wires the **only set-regarding UI** to that contract.
> Control version **1.4.9 → 1.5.0**; presave web resource **1.2.0 → 1.3.0**.

---

## 1. The three transitions, and where each one lands

The stamp is what makes child access a **1-hop** lookup, so a missed transition is not a cosmetic bug:
a stale stamp is simultaneously an **over-grant** (the old parent's principals still reach the child)
and an **under-grant** (the new parent's principals do not).

| Transition | UPDATE mode (row exists) | CREATE mode (no row yet) |
|---|---|---|
| **set** | `applyRegardingSelection` → shared `buildRegardingSelectionPayload` → ONE `updateRecord` carrying target lookup + resolver fields + ancestor stamp | stamp published on `__sprk_regarding_pending__`; presave stages it onto form attributes → rides the INSERT |
| **reparent** | same call; the shared builder pre-clears the old ancestor and applies the new stamp **after** the clear | `clearLookups` published alongside `ancestorStamps`; presave applies **clears before sets** |
| **clear** | `clearRegarding` nulls the 12 catalog lookups **∪ the 4 core-ancestor lookups**, intersected with the host's real columns | n/a (nothing to clear on a new row) |

### The clear case, specifically

`clearRegarding` was the gap. The catalog covers three of the four core entities
(matter / project / work assignment) but **not** `sprk_servicerequest` (finding **F-050-3**). On a host
that carries `sprk_regardingservicerequest`, a catalog-only clear left that stamp standing, so a user who
"detached" a record kept it visible to the old ancestor's principals. Fixed by clearing the **union**,
resolved against discovered nav-props so we never emit a column the host lacks (SRFR-048 — writing an
absent property makes Dataverse reject the whole update).

**This gap is wider than F-050-1 implied** — see F-051-5 below. `sprk_todo` *does* carry
`sprk_regardingservicerequest`, so the union is load-bearing on the most common host, not an edge case.

---

## 2. What changed, file by file

### `handlers/ResolverWriteHandler.ts`

- `applyRegardingSelection` no longer assembles a payload. The local pre-clear loop and the direct
  `applyResolverFields` call are **deleted**; it now delegates wholesale to
  `buildRegardingSelectionPayload`. Per ADR-024 the handler holds **no ordering assumptions of its own** —
  the derive → pre-clear → set → stamp-last sequence is the shared service's contract
  (`notes/phase3-derivation-rules.md` §4).
- **Fail-closed (NFR-01)**: `status: 'error'` → return `success: false`, write nothing, stage nothing.
- `IResolverWriteResult` gained `ancestorStamps`, `ancestorStatus`, `unstampable`, `clearLookups`.
- `clearRegarding` gained the core-ancestor union (§1 above).

**Pre-clear catalog is the FULL `TODO_REGARDING_CATALOG`, not the `catalog` argument.** The argument is
the maker's `regardingTargets` allow-list and governs what a user may *select*; if it also governed the
clear set, narrowing that list would strand a previously-set lookup — and with it a stale stamp. Pinned by
`a maker-restricted catalog still pre-clears the FULL lookup set`.

**`clearLookups` is derived from the shared payload, not recomputed.** `nulledLookupColumns()` maps the
payload's null `@odata.bind` keys back through the host nav-props to column logical names (what form
attributes need). Recomputing the membership rule would be a second place to get it wrong; this way the
CREATE path cannot disagree with the UPDATE path about what a reparent clears.

### `RegardingResolverApp.tsx`

- Manual-pick CREATE bridge publishes `ancestorStamps` + `clearLookups`.
- **Subgrid auto-detect CREATE path (new Phase 2d).** This path never went through
  `applyRegardingSelection` — it hand-writes form attributes — so it derived nothing. Dataverse's
  relationship mapping populates one `sprk_regarding{X}` lookup, but when X is a CHILD record that lookup
  is a *relationship, not an access edge*; the child inherited nothing. Phase 2d calls the shared
  `deriveCoreAncestorStamps` and stages the result. It runs inside the existing `Promise.all` so it does
  not disturb SRFR-057's two-phase fast-save design; the Phase-1 baseline bridge carries
  `ancestorStamps: []` so an early save under-grants rather than over-grants.
- Derivation failure there is `console.error` + no stamp — we must never block or crash the host save
  (FR-24), and inventing an unverified stamp is worse than not writing one.

### `sprk_todo_regarding_presave.js` (v1.3.0)

- Stages `clearLookups` (nulls) **then** the chosen lookup **then** `ancestorStamps`.
  **Order is load-bearing**: the chosen lookup is frequently itself a core-ancestor column (a user picking
  a Matter directly), so clearing after setting would null the very access edge just written. Pinned by
  `clears are applied BEFORE sets`.
- A stamp whose column is not on the form is `console.error` naming the column; a *clear* for an absent
  column stays `console.warn`. Asymmetric on purpose — see §4.
- Backward compatible: a v1.2.0-shaped payload (no new keys) still stages the legacy fields.

---

## 3. Verification actually performed

| Check | Command | Result |
|---|---|---|
| Unit suite | `npx jest` in `src/client/pcf/RegardingResolver` (also `--clearCache` + `--runInBand`) | **105 passed / 105**, 3 suites — was **0 runnable** at HEAD, see F-051-1 |
| Production bundle | `npm run build:prod` | `webpack compiled successfully`, `bundle.js 70.7 KiB` |
| Lint | `npm run lint` | `Succeeded` |
| Version bump | 4 locations + footer date | manifest / solution manifest / `solution.xml` / `index.ts` `CONTROL_VERSION` all `1.5.0`; `BUILD_DATE` `2026-09-04` |

`Solution/Controls/.../bundle.js` + `styles.css` were refreshed from `out/` so the checked-in solution
artifacts match the 1.5.0 manifests. **Not deployed** (per task instruction).

### Mutation testing — the tests are not vacuous

Each mutation was applied, the suite run, and the mutation reverted:

| Seeded defect | Caught by |
|---|---|
| `clearRegarding` drops the core-ancestor union | `FR-26 CLEAR — a core lookup outside the catalog is nulled too` |
| Pre-clear uses the maker-restricted catalog | `a maker-restricted catalog still pre-clears the FULL lookup set` |
| `ancestorStamps` never populated | 3 tests (SET + both CREATE cases) |
| Presave never stages stamps | 4 tests |
| Presave applies clears **after** sets | `clears are applied BEFORE sets` |
| App bridge drops `ancestorStamps` | `the derived stamp reaches the seam so it can ride the INSERT` |
| Auto-detect Phase 2d derives but never stages onto the form | `a pre-populated CHILD lookup derives and stages its CORE ancestor` |

---

### Step 9.5 quality gates

**`adr-check`** — 0 violations, 8 ADRs compliant, 6 warnings (4 pre-existing / out of scope).
ADR-024 net **strengthened** (the handler shrank; SET and CLEAR now resolve nav-props through the same
shared helper, so they cannot disagree about which column a lookup is). ADR-012 compliant and **inside**
task 050's existing Path A exception — 051 adds zero new entity literals in executable code. ADR-028: no
auth surface at all. ADR-006: extending the web resource is the *sanctioned* category, not an exception —
`.claude/constraints/webresource.md` reserves web resources for form event handlers precisely because a
PCF cannot register `addOnSave`. ADR-021: no JSX, styles or colors added (verified by scanning added lines
only: 0 hex literals, 0 v8 `@fluentui/react` imports). Two items wanted writing down, both **Path A**, both
now recorded here: the auto-detect empty-clear assumption (§2) and the jest source-mapping (F-051-1).
One ADR-044 item was **Path C / fixed**: `setFormLookupValue` had a hand-rolled `replace(/[{}]/g,'')`;
now calls the canonical `cleanGuid`, which matters more than it did because that function now stages
access edges. The equivalent in the plain-JS web resource stays (no module system → cannot import it).

**`code-review`** — no Critical. Metrics: handler 371→544 lines / complexity 45→59; App 1637→1757 /
144→155; web resource 366→504 / 38→56. Growth is concentrated in documentation and one new responsibility
per file, not in branching. AI-smell scan: 0 catch-log-rethrow, 0 new single-implementation interfaces,
0 real TODO/HACK/FIXME. Two findings recorded below (F-051-6) and one **self-criticism**: 70% of the
handler's added lines are comment. The comments explain *why* (which ordering is load-bearing, and what
breaks if it inverts) rather than restating code, and this file's prior style is equally dense — but it is
at the edge, and a reviewer trimming it would not be wrong.

## 4. Findings

### F-051-1 🔴 — Both PCF test suites had been dead since the ADR-012 deep-import refactor

At `HEAD`, `npx jest` in this control ran **0 tests**: both suites failed to load.

- `ResolverWriteHandler.test.ts` and `RegardingResolverApp.test.tsx` both did
  `jest.mock('@spaarke/ui-components', …)` — the **root barrel**, a specifier the control stopped
  importing when it moved to deep `dist/…` paths. Every mock and every assertion in both files was inert.
- The deep `dist/*.js` files are ESM (`export async function …`), which Jest cannot parse, so the suites
  died at module load with `SyntaxError: Unexpected token 'export'`.
- `RegardingResolverApp.tsx` additionally imports `./generated/ManifestTypes`, a gitignored build artifact,
  so the App suite could not compile on a clean checkout.

Fixed in `jest.config.js`: map `@spaarke/ui-components/dist/*` → the shared **TS source**, map
`generated/ManifestTypes` → a stub, and force `react` / `react-dom` / `react/jsx-runtime` to the PCF's own
copy (the shared lib's `node_modules` carries React 19; without the mapping every Fluent render inside a
deep-imported shared component dies on `recentlyCreatedOwnerStacks`, a React-19 internal — the same
single-instance guarantee webpack gives at real PCF runtime per ADR-022). The App suite's mocks were
retargeted at the deep specifiers.

Mapping to source rather than a stub is deliberate: `ResolverWriteHandler.test.ts` now drives the **real**
task-050 derivation with only the `webApi` and `fetch` seams faked. A suite that mocks
`buildRegardingSelectionPayload` can assert the PCF *called something*; it cannot prove the stamp reaches
the payload, which is the entire claim of FR-26.

**A sibling PCF has the same shape.** `CommunicationConnections` — both
`__tests__/CommunicationConnectionsApp.test.tsx` and `__tests__/ConnectionsWriteHandler.test.ts` do
`jest.mock('@spaarke/ui-components')` while its source deep-imports `@spaarke/ui-components/dist/…`, and
its `jest.config.js` has **no** `dist` mapping — i.e. the same inert-mock + unparseable-ESM combination.
(`CommunicationConversationPanel` mocks the barrel too but *does* map `dist`, so it is probably fine.)
Not fixed here: different control, and other agents are active in this worktree. Worth a follow-up task —
the failure mode is silent, because a suite that cannot load reports "0 tests" rather than a red test.

This surface **is** watched: `.github/workflows/client-tests.yml` (nightly, advisory, not a PR gate) runs
jest per client package precisely to expose packages like this one. RegardingResolver moves from
"suite fails to load" to **105 green** in that baseline; `CommunicationConnections` should show up there
the same way.

### F-051-2 🟠 — `displayName` was never forwarded on the success path (SRFR-054 was inert)

`applyRegardingSelection` returned `displayName` on its **failure** branch only. `RegardingResolverApp`
reads `result.displayName ?? selection.recordName` for the CREATE bridge, so it always fell through to the
picker's Primary Name — which for `sprk_matter` is the *number*. That is precisely the defect SRFR-054 was
written to fix. Fixed here (same function, one line) since the CREATE bridge is this task's surface.

### F-051-6 🔴 — `clearRegarding` is correct but UNREACHABLE, and native form-clear bypasses all of it

Found during the Step 9.5 code-review pass. Two separate things, both about the clear transition:

**1. Nothing calls it.** `clearRegarding` is exported from the handler and has no caller anywhere in
`src/client/**` outside its own tests. The control renders no clear/detach/remove affordance, and the
shared `PolymorphicPicker` exposes no `onClear` / `clearable`. The sibling shared-lib
`buildTodoRegardingClear` likewise has no production caller (only a barrel re-export). So the clear path I
fixed is a **correct contract with no user-reachable entry point today**. The fix is still right — task 050
wired the shared clear, the tests pin it, and the moment anything calls it the stamp goes with the
regarding — but the *live* exposure this task actually closes is the **reparent** path, which is reachable.

**2. The reachable clear bypasses this code entirely — and that hole is still open.** 🔴 A user can clear
the regarding lookup natively on the Dataverse form. No PCF code runs. When the parent was a **core**
record the lookup cleared *is* the stamp, so that case self-heals. When the parent was a **child** record
(a Communication, say), clearing `sprk_regardingcommunication` leaves `sprk_regardingmatter` — the
denormalized stamp — **still set**. The record now shows no regarding while remaining fully visible to the
old ancestor's principals. That is precisely the over-grant FR-26 exists to prevent, reached by the most
obvious user gesture.

No client-side fix is possible: nothing of ours executes on a native field clear. It needs either a
Dataverse plugin on `sprk_regarding*` update (task 052's server-writer surface is the natural home) or
acceptance + documentation. **Out of scope here and not fixed** — flagging it because task 051 could
otherwise be read as "the clear transition is now covered", and it is not.

### F-051-3 🔴 — Form composition is a hard prerequisite, and I could not verify it

`formContext.getAttribute(name)` returns **null** for a column that is not on the form, so a core-ancestor
lookup missing from the CREATE form **cannot be staged** and the child is created unstamped. This is not
hypothetical: it is exactly how `sprk_regardingrecordurl` was silently skipped for two releases (SRFR-043).

**This session had no live Dataverse connection**, so I could not enumerate which
`sprk_regarding{core}` columns are present on the `sprk_todo` (or `sprk_communication` / `sprk_event`)
CREATE forms. What I did instead:

- The presave logs `console.error` naming the exact column when a stamp cannot be staged — deliberately
  louder than the `console.warn` used for cosmetic fields, because the failure mode is a *silent
  under-grant on a row that looks correct*.
- **No post-create update fallback was added**, per the task's explicit constraint.

**This is the task's `<escalation>` trigger condition, but I do not have a concrete gap to escalate** — I
have an unverifiable prerequisite. Owner action: during Phase 3 UAT, confirm that every core-ancestor
lookup the host carries is on its CREATE form (hidden is fine), for each host entity the resolver is bound
to. The second UI test in the POML (`CREATE-mode stamp rides the insert`) is the check.

### F-051-4 🟠 — the "host cannot store this ancestor" hole is now visible at the consumer

When derivation returns an ancestor whose lookup column the host lacks, `applyRegardingSelection` surfaces
it as `unstampable` and excludes it from `ancestorStamps` (so CREATE mode does not try to stage a column
that cannot exist), alongside the shared service's warn. Pinned by
`a derived ancestor the host cannot store is surfaced, not swallowed`.

The test drives this through a **constructed** narrow host rather than naming a real entity — see F-051-5
for why.

### F-051-5 🔴 — F-050-1 is FALSE: `sprk_todo` DOES have `sprk_regardingservicerequest`

`notes/phase3-derivation-rules.md` F-050-1 states that `sprk_todo` has 11 regarding lookups and no
service-request column, citing repo evidence rather than a metadata query (task 050 flagged this as an
unverified caveat at the time). **A sibling agent checked live metadata: the column exists.**
`CoreAncestorResolver.cs:90` and `:287` carry the same false claim server-side.

**No production code in this task depended on it.** Column presence is resolved from the host's discovered
nav-props on every write — never from an assumed list — which is precisely why a wrong belief did not
become a wrong behaviour. What it *did* corrupt:

- the test fixture, which omitted the column and asserted counts around that omission (**corrected**: the
  wide fixture now carries it; the narrow-host cases use a clearly-labelled constructed host so the suite
  never again encodes a schema claim it cannot verify);
- three docstring passages in `ResolverWriteHandler.ts` (**corrected**);
- the earlier draft of this note (**corrected**).

The correction makes the FR-26 clear-union **more** load-bearing, not less: `sprk_todo` is the most common
host, so before this task a reparent or clear on a To Do could leave a live `sprk_regardingservicerequest`
stamp — a real over-grant on the main path, not the edge case it was filed as.

**Follow-up for the owner**: F-050-1 in `notes/phase3-derivation-rules.md` and the two
`CoreAncestorResolver.cs` comments should be corrected at source. Not done here — `notes/` belongs to task
050 and `src/server/**` is owned by other agents in this worktree.

---

## 5. Deviations from the POML

| POML said | What I did |
|---|---|
| Step 3: "verify `clearRegarding` nulls ancestor stamps *via the shared service's clear payload*" | There is no host-generic shared clear builder to consume. `buildTodoRegardingClear` emits a null bind even when the nav-prop is absent (`navProp?.navPropName ?? target.lookupAttribute`), which would regress SRFR-048 on narrow hosts (`sprk_event` → "Invalid property" → the whole clear fails). Kept `clearRegarding` in the PCF and extended it using the shared `CORE_ANCESTOR_LOOKUPS` + `findHostNavPropForLookup` primitives. Consuming the shared constants is delegation, not a fork; task 050 owns the shared file and the POML forbids editing it. |
| Outputs list 4 files | Also changed: `jest.config.js` + a `ManifestTypes` stub (F-051-1, without which nothing runs), `RegardingResolverApp.test.tsx` (retargeted mocks + 2 FR-26 tests), `ControlManifest.Input.xml` / `Solution/*` (version bump), and the solution `bundle.js` / `styles.css` (kept in sync with the bumped manifests). Added `__tests__/regardingPresave.test.ts`. |
| Auto-detect path not mentioned | It needed the same treatment and had none — see §2, Phase 2d. |
| UI tests (3 defined) | **Not run** — no live environment in this session. Deferred to Phase 3 UAT; test 2 is also the F-051-3 check. |

---

## 6. Not verified in this session

- The three POML `<ui-tests>` (reparent re-stamp, CREATE-mode single-INSERT network trace, ADR-021 dark
  mode) — require a live org.
- Form composition per F-051-3.
- Live metadata confirmation of which core-ancestor columns each host entity actually carries (carried
  forward from task 050's honest caveat).
- Deployment — explicitly out of scope for this task.
