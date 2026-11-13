/**
 * jsdom does no layout: every element reports `offsetParent === null` and the body a 0x0 rect, which tabster (Fluent v9's
 * focus and modal manager) reads as display:none, so a dialog finds nothing focusable and its modalizer never activates
 * (no focus trap, no aria-hidden pass). These two geometry stubs give jsdom the geometry a browser has; nothing else is
 * changed. Used by the tests that must observe the real trap (`RecordTemplateChildDialogs`, the K3 send hold in
 * `RecordTemplateSections`). Call `installLayout()` in `beforeEach` and `uninstallLayout()` in `afterEach`.
 */
import { vi } from 'vitest';

const realOffsetParent = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetParent');

export function installLayout(): void {
  Object.defineProperty(HTMLElement.prototype, 'offsetParent', {
    configurable: true,
    get(this: HTMLElement) {
      return this.parentNode;
    },
  });
  vi.spyOn(document.body, 'getBoundingClientRect').mockReturnValue({
    x: 0,
    y: 0,
    top: 0,
    left: 0,
    right: 1024,
    bottom: 768,
    width: 1024,
    height: 768,
    toJSON: () => ({}),
  });
}

export function uninstallLayout(): void {
  if (realOffsetParent) Object.defineProperty(HTMLElement.prototype, 'offsetParent', realOffsetParent);
}
