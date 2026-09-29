# NFR-02 — corrected root cause: it was never the date picker's weight

> **Investigated**: 2026-08-26, main session, after the owner challenged the premise
> **Supersedes the framing in**: [`033-recordheader-pcf-bundle-nfr02.md`](033-recordheader-pcf-bundle-nfr02.md)
> **Status**: proposed fix measured + statically verified; **needs a runtime smoke test before trusting**

---

## The claim that was wrong

Task 033 reported: *"FR-06 mandates a Fluent date picker and v9 ships none natively, so the requirement
and the ceiling are in genuine tension."* I relayed that.

**Both halves were wrong.**

1. **Fluent v9 does ship a date picker.** `@fluentui/react-datepicker-compat` **is** the official
   Fluent v9 DatePicker, published by Microsoft under `@fluentui` and documented as the v9 component.
   The only true statement is narrower: it is not re-exported from the `@fluentui/react-components`
   umbrella package. "Not in the umbrella" is not "does not exist."
2. **The 285 KB was not the picker's weight.** It is overwhelmingly *duplicated Fluent v9 internals* —
   code the Power Apps host already loads.

## Actual root cause

`pcf-scripts` builds its externals from exactly one regex
(`platformLibrariesHandler.js:66`):

```js
map["@fluentui/react-components"] = supportedLib.libAlias;   // -> FluentUIReactv940
getFilterCallback(/^(?:@fluentui\/react-(?:components$|components\/))/, supportedLib.libAlias)
```

It externalizes **only** the umbrella. But `react-datepicker-compat` imports its dependencies by their
*individual* package names:

| package it imports | unminified `lib/` | also inside the umbrella? |
|---|---|---|
| `@fluentui/react-utilities` | 561 KB | yes |
| `@fluentui/react-positioning` | 500 KB | yes |
| `@fluentui/react-motion-components-preview` | 331 KB | yes |
| `@fluentui/react-tabster` | 306 KB | yes |
| `@fluentui/react-motion` | 261 KB | yes |
| `@fluentui/react-popover` | 213 KB | yes |
| `@fluentui/react-field` | 138 KB | yes |
| `@fluentui/react-input` | 127 KB | yes |
| `@fluentui/react-portal` | 87 KB | yes |
| `@fluentui/react-calendar-compat` | 857 KB | **no** — genuinely ours to carry |
| `@fluentui/react-datepicker-compat` | 126 KB | **no** — genuinely ours to carry |

None of the "yes" rows match the regex, so webpack bundles a second private copy of Fluent internals
the host is already serving. That is the 285 KB.

Note both compat packages already declare `sideEffects: false`, so this was never a tree-shaking
failure — the code was reachable and correctly retained. It simply should not have been ours to ship.

## Measured fix

Extend the custom `webpack.config.js` externals to map the granular packages onto the same platform
alias:

| configuration | `bundle.js` | vs 250 KB ceiling |
|---|---|---|
| as task 033 shipped it | 377,680 B | ❌ +51% |
| externalize **all** granular deps | 202,424 B | ✅ but **runtime-unsafe** — see below |
| externalize the **safe subset** | **210,765 B** | ✅ **84% of ceiling** |

That is −166,915 B (−44%) with the date picker fully intact.

## Two failed attempts, and what they establish

**A build that succeeds proves nothing here.** Webpack trusts an externals map without checking the
global actually provides those symbols.

1. **Externalizing everything built cleanly at 202,424 B — and would have crashed on mount.**
   Resolving the umbrella's *runtime* export surface (1,206 exports) showed **10 of 25 required
   symbols absent**: `useControllableState`, `useFluent_unstable`, `getRTLSafeKey`,
   `getReactElementRef`, `useOnClickOutside`, `useOnScrollOutside`, and the four motion atoms
   (`Fade`, `Slide`, `fadeAtom`, `slideAtom`). Each would be `undefined` when the picker mounts.
   → `react-utilities`, `react-shared-contexts` and `react-motion-components-preview` **must stay bundled**.

2. **Setting `externals` in the custom config replaces pcf-scripts' array rather than merging it.**
   Omitting `@fluentui/react-components` from your own map silently de-externalizes the umbrella,
   which then drags `@griffel/react`'s `module`-field source and reproduces
   [`pcf-build-scaffold.md` gotcha #1](../../../../.claude/patterns/pcf/pcf-build-scaffold.md)
   (`Can't resolve 'react/jsx-runtime'`). **Always re-declare the umbrella in your own map.**

For the safe subset, all **12** symbols imported from those packages were verified present on the
runtime export surface: **0 missing**.

## Residual risk — why this is not yet "done"

Verification was against the **locally installed** `@fluentui/react-components@9.73.2`. The runtime
global is the *platform's* build: the manifest declares `9.46.2`, and
`PlatformLibraryVersions.json` maps 9.0.0–9.68.0 → alias `FluentUIReactv940`. A symbol present in
9.73.2 is not guaranteed present in the platform's build.

**This requires a smoke test on a real form before it is trusted** — load a record, open the date
picker, confirm the calendar renders and a date commits, and check the console. That is cheap, and it
is the only thing that closes the gap.

## Consequence for the decision

The earlier options were built on a false premise and should be discarded:

- ~~Raise the NFR-02 ceiling~~ — unnecessary; the overage was avoidable duplication, not real weight.
- ~~Replace the picker with `<input type="date">`~~ — would have downgraded a tested renderer to
  compensate for a build-config defect.

**Fix the externals, keep the picker, keep the ceiling.** No spec amendment, no ADR amendment, no
renderer downgrade.

## Worth propagating beyond R2

This affects **every** Spaarke PCF that consumes a Fluent v9 package outside the umbrella. The
externals gap is in `pcf-scripts`, not in our code. Once the smoke test passes, the safe-subset
externals block belongs in `pcf-build-scaffold.md` as a documented recipe alongside the existing
bundle-optimization triad — the triad as written does not cover this case.

Also worth adding, found by task 033: **the manifest entry module must define exactly one export** —
a second fails `[pcf-1023]`.
