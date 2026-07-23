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
