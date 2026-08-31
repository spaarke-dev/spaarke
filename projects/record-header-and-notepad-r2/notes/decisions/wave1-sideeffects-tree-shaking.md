# Decision — `sideEffects: false` on `@spaarke/ui-components` (NFR-02 blocker)

> **Found**: 2026-08-25, main session, Wave-1 end verification (tasks 010–014, 020)
> **Status**: ✅ applied · **cross-cutting** — affects all 9 deep-import PCFs + code pages
> **Consumed by**: task **015** (barrel), task **033** (new PCF), every per-wave NFR-02 measurement

---

## The problem, measured

Task 010 added `@fluentui/react-datepicker-compat` for `DateField` (FR-06 requires a Fluent date
picker; Fluent v9 ships none natively, so this is the official compat package — the need is real).

Before letting task 015 add `DateField` to the `fields/` barrel, I measured what that would cost.
`MatterHeader` imports the **barrel** (`@spaarke/ui-components/dist/components/RecordHeader`), and it
never renders a date field — so in theory the renderer should tree-shake away.

It did not:

| Configuration | `bundle.js` | vs NFR-02 (250 KB) |
|---|---|---|
| Baseline — no `DateField` in barrel, no `sideEffects` flag | **64,422 B** | ✅ |
| `DateField` in barrel, no `sideEffects` flag | **353,250 B** | ❌ **BREAKS** — 345 KB, +448% |
| `DateField` in barrel **+ `sideEffects: false`** | **61,519 B** | ✅ — *below baseline* |

**Adding one unused renderer to the barrel would have blown the bundle ceiling by 38%.**

## Root cause

`package.json` had **no `sideEffects` field**. Webpack must then assume every module in the package
may have import-time side effects, so it cannot drop unreachable modules — only unused *exports*.
The barrel re-export kept `DateField` → `@fluentui/react-datepicker-compat` alive in the graph.

The PCF `webpack.config.js` already sets `optimization.sideEffects: true`, but that only means
*"respect the package's `sideEffects` field."* With no field to respect, it is inert. The
bundle-optimization triad was doing less than it appeared to.

## Why the fix is safe (verified, not assumed)

`sideEffects: false` asserts no module has import-time side effects. Scanned shipped source:

- CSS / asset imports: **0**
- Bare side-effect imports (`import 'x'`): only inside `__tests__/`, which `tsc` excludes from `dist/`
  (verified: `find dist -name "*.test.js"` → 0)
- Module-scope `window.` / `globalThis.` / `document.` assignment: only inside `__tests__/`
- Module-scope registration calls (`initializeIcons`, `registerIcons`, `configure`): **0**

Consumer smoke tests after the change:

| Consumer | Result |
|---|---|
| `MatterHeader` | builds green, **61,519 B** (was 64,422 — *improved*), 7/7 tests pass |
| `RegardingResolver` | builds green, **61,280 B** |
| Shared lib | `tsc` exit 0; RecordHeader suites **141/141** |

## Scope note — this is deliberately outside R2's declared scope

R2 is a client-side PCF project and this edits a shared-library `package.json` consumed by nine PCFs
and the code pages. It is applied anyway because:

1. **NFR-02 is unmeetable without it.** Task 015's whole job is adding renderers to that barrel.
2. It is a **one-line metadata correction**, not a behaviour change — the library never had
   import-time side effects; the manifest simply failed to say so.
3. It **improves every consumer** measured, and regresses none.

Flagged rather than absorbed silently. If a future module *does* need import-time side effects, it
must be listed explicitly (`"sideEffects": ["./dist/thatModule.js"]`) rather than removing the flag.

## Binding consequence for later tasks

- **Task 015** — safe to export all renderers from the barrel, but MUST re-measure the PCF bundle
  after wiring it; do not assume this doc's number still holds.
- **Task 033** — the new `RecordHeader` PCF imports the barrel and *will* render dates, so the
  datepicker legitimately lands in its bundle. Budget for it: the delta above (~292 KB unshaken)
  is what the picker costs when actually reachable. If 033's bundle approaches 250 KB, the fallback
  is a deep-path import of `DateField` only, loaded lazily per resolved layout.
- **Every Phase-5 wave** — NFR-02 says measure per wave. This finding is exactly why.
