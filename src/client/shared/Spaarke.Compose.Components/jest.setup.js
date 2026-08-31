// RTL custom matchers (toBeInTheDocument, toBeDisabled, …) for the co-located
// widget tests. Mirrors the sibling Spaarke.UI.Components setup.
require('@testing-library/jest-dom');

// jsdom ships no ResizeObserver. Fluent v9's MessageBar reflow hook
// (`useMessageBarReflow`, activated by MessageBarActions — DEF-15's dismiss
// affordance) attaches one on mount and throws "win.ResizeObserver is not a
// constructor" without this. A no-op stub is sufficient: tests assert DOM
// presence, not observed reflow.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}

// ---------------------------------------------------------------------------
// React Testing Library async budget — why this exists and why it is SEPARATE
// from jest's `testTimeout` (2026-08-31)
// ---------------------------------------------------------------------------
// Symptom: `compose-client-gate` failed on EVERY master commit with ~20 failures
// in ComposeWorkspace.redline-from-ledger.test.tsx, all of the form
// "Unable to find role=\"textbox\"" — the TipTap editor had not mounted yet.
// The same file passes locally: 24/24 in ~10s. In CI that suite takes ~105s.
//
// Cause: RTL's `findBy*` / `waitFor` carry their OWN timeout, which jest's
// `testTimeout` does NOT govern. jest.config.js already raises `testTimeout` to
// 30s (see the note there), and that was necessary but NOT sufficient: it
// extends the budget for the WHOLE test while each individual `findByRole`
// still gave up after its own, much smaller budget — 1s by RTL default, or the
// 5s some call sites passed explicitly. Under `--maxWorkers=2` contention the
// editor mount routinely crosses that inner budget, so the wait expired while
// the outer test still had ~25s of headroom left. Two timeouts, one of which
// was still sized for a fast unloaded laptop.
//
// This is the second half of the fix that PR #908 started. Raising only the
// outer budget looked like it should work, which is exactly why the gate stayed
// red afterwards and was easy to keep reading as "flaky".
//
// Why configure it globally rather than per call: it makes ONE knob instead of
// one per assertion, and an explicit per-call `{ timeout }` OVERRIDES this
// value — so scattered explicit budgets silently defeat it. The explicit 5000s
// in redline-from-ledger were removed for that reason; they now inherit this.
//
// Why 15s: the editor mounts in well under a second locally and the whole
// contended suite averages ~4.4s per test, so 15s absorbs CI contention with
// room to spare while still failing a genuine never-resolving mount rather than
// hanging until jest's 30s outer timeout. If a wait needs more than 15s that is
// a defect in the component or the test, not a reason to raise this again.
const { configure } = require('@testing-library/react');
configure({ asyncUtilTimeout: 15000 });
