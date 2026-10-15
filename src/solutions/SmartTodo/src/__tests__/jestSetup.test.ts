/**
 * Guards the Jest config wiring (#1417). jest.config.cjs once used
 * `setupFilesAfterEach` (not a Jest option), so jest.setup.cjs silently never
 * loaded. These assertions can only hold if the setup file actually ran.
 */
describe('jest.setup.cjs is loaded', () => {
  it('registers the jest-dom matchers', () => {
    expect(typeof (expect as unknown as Record<string, unknown>).extend).toBe('function');
    const el = document.createElement('div');
    document.body.appendChild(el);
    // toBeInTheDocument only exists once @testing-library/jest-dom is loaded by the setup file.
    expect(el).toBeInTheDocument();
    el.remove();
  });

  it('polyfills window.matchMedia (jsdom has none)', () => {
    expect(typeof window.matchMedia).toBe('function');
    expect(window.matchMedia('(min-width: 1px)').matches).toBe(false);
  });

  it('polyfills ResizeObserver (jsdom has none)', () => {
    expect(typeof globalThis.ResizeObserver).toBe('function');
  });
});
