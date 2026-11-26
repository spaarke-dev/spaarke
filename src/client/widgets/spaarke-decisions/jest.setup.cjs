require('@testing-library/jest-dom');

// ResizeObserver: jsdom does not implement it, and Fluent v9 MessageBar constructs one in a layout effect (same stand-in as the shared
// library's jest.setup.js). Guarded so the node-environment suites, which have no `window`, are untouched.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}

// Test control for the wizard's display chain: the widget always opens the shared wizard as the in-frame dialog first, so master-detail
// (the dialog's fallback) is reached only when the dialog fails. A test that wants master-detail sets `globalThis.__widgetTest.dialogFails`
// (`wizardHost(items, { dialogFails: true })` does); the dialog host then throws while rendering, which the widget's boundary catches.
// Reset before every test. A test file that mocks the host module itself (WizardDialog.chain.test.tsx) replaces this wrapper.
globalThis.__widgetTest = { dialogFails: false };
beforeEach(() => {
  globalThis.__widgetTest = { dialogFails: false };
});
jest.mock('@spaarke/ui-components/components/DecisionWizard/DecisionWizardHost', () => {
  const actual = jest.requireActual('@spaarke/ui-components/components/DecisionWizard/DecisionWizardHost');
  const React = require('react');
  return {
    ...actual,
    DecisionWizardHost: props => {
      if (!props.embedded && globalThis.__widgetTest && globalThis.__widgetTest.dialogFails) {
        throw new Error('test: the dialog failed while rendering');
      }
      return React.createElement(actual.DecisionWizardHost, props);
    },
  };
});
