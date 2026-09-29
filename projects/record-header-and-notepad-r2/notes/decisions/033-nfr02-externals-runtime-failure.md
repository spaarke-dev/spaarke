# NFR-02 — the granular-Fluent externals fix FAILS AT RUNTIME. Reverted.

> **Date**: 2026-08-26 · **Supersedes**: [`033-nfr02-root-cause-corrected.md`](033-nfr02-root-cause-corrected.md)
> **Found by**: owner, second UAT of v1.1.1 — *"have never seen a minified React error before in our PCF"*
> **Status**: reverted in v1.1.2. **NFR-02 is breached again and the decision is reopened.**

---

## What happened

v1.1.1 crashed on the Project form with:

```
Minified React error #31
args[]=object with keys {$$typeof, type, key, ref, props}
```

React error #31 is *"Objects are not valid as a React child"*, and the object described **is a React
element**. An element reaching a child position that React does not recognise as an element is the
signature of two copies of Fluent's rendering internals meeting.

## Cause — my change, and an unprecedented one

To bring the bundle under NFR-02 I added a custom `externals` block to `webpack.config.js` mapping
granular `@fluentui/*` packages onto the platform Fluent global. **RecordHeader was the only PCF in
the repo doing this** — verified across all 14 PCFs with a `webpack.config.js`; every other one
carries the standard triad and nothing more.

That split Fluent's internals across two copies:

| bundled with the control | provided by the host (`FluentUIReactv940`) |
|---|---|
| `react-utilities` (the `slot()` machinery) | `react-input` |
| `react-jsx-runtime` | `react-field` |
| `@griffel/react` | `react-popover`, `react-positioning`, `react-portal` |
| `react-calendar-compat`, `react-datepicker-compat` | `react-tabster`, `react-motion`, `react-theme` |

Fluent v9 renders through slot objects created by `react-utilities` and consumed by
`jsx-runtime`. When a slot crosses from the bundled copy into a host-provided component (or back),
the receiving side does not recognise it — React sees a raw object where a child belongs.

## Why v1.1.0 did not crash — the trap

v1.1.0 shipped the **same** externals block and rendered without error. That is what made the change
look safe.

It was masked: in v1.1.0 the metadata defect made every renderer fall back to plain `TextField`, so
**the DatePicker, Dropdown, Switch and LookupField never mounted**. The split was never exercised.
Fixing metadata in v1.1.1 mounted the real Fluent components for the first time — and the latent
runtime failure surfaced immediately.

Two defects masking each other is why the first UAT looked like "wrong data" rather than "broken
build".

## What this invalidates

I wrote, in the very note this supersedes: *"A build that succeeds proves nothing here — webpack
trusts an externals map without checking the global actually provides those symbols."* I then
verified 12 symbols statically against 9.68.0, found 0 missing, and shipped.

**Static symbol presence was the wrong test.** Every symbol *was* present. The failure is not a
missing export — it is two live copies of the same module graph, which no symbol check can detect.
Only mounting the components reveals it.

## Reverted — and the guard is now in the file

`webpack.config.js` is back to the standard triad, with an explicit ⛔ comment so the next person
does not re-derive the same "optimization".

| build | bundle | NFR-02 (250,000 B) |
|---|---|---|
| v1.1.1 with granular externals | 211,544 B | ✅ — but **crashes at runtime** |
| **v1.1.2 standard triad** | **378,457 B** | ❌ **+51%** |

Date picker confirmed still bundled (`fui-Calendar` × 94). PCF tests 43/43.

## NFR-02 is reopened — and option D is dead

The surviving options are the ones from before, minus the one I thought I had found:

- **A — raise the ceiling for this control.** NFR-02's 250 KB predates FR-06's requirement for a date
  picker. R2's own code is ~92 KB; the overage is one spec-mandated vendor component.
- **B — replace the picker** with a Fluent-token-styled `<Input type="date">`. Near-zero bytes, keeps
  the ceiling, uses the browser-native calendar. Changes FR-06's acceptance criterion and discards a
  renderer tasks 010/015 built and tested.
- **C — accept, time-boxed**, revisit post-UAT.
- ~~**D — externalize granular Fluent internals**~~ — **empirically dead.** Builds clean, passes static
  verification, crashes on mount.

## The lesson worth keeping

A PCF that deviates from the repo's established build pattern is carrying risk that no unit test,
type check, or bundle inspection will surface. The other 13 PCFs converged on the standard triad for
a reason. **Deviating needed a runtime smoke test before shipping, not after** — and I had already
written down why, then proceeded on the weaker evidence anyway.

Catalogued as a repo-level failure mode; see `.claude/FAILURE-MODES.md`.
