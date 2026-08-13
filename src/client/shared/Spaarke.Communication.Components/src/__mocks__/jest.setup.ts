/**
 * Jest setup — @spaarke/communication-components.
 *
 * Registers `@testing-library/jest-dom`'s custom matchers (`toBeInTheDocument`,
 * etc.) for every test file. Mirrors `@spaarke/daily-briefing-components`'s
 * `test/jest.setup.ts`.
 */
import '@testing-library/jest-dom';

// jsdom does not implement `Element.prototype.scrollIntoView` (it throws
// "Not implemented"). The reconciliation citation highlight (task 054) calls it
// to bring a resolved passage into view — a best-effort visual affordance. Stub
// it as a no-op so components that scroll-on-highlight render cleanly under test.
if (typeof Element !== 'undefined' && !('scrollIntoView' in Element.prototype)) {
  // eslint-disable-next-line @typescript-eslint/no-empty-function
  (Element.prototype as unknown as { scrollIntoView: () => void }).scrollIntoView = () => {};
} else if (typeof Element !== 'undefined') {
  jest.spyOn(Element.prototype, 'scrollIntoView').mockImplementation(() => {});
}
