/**
 * The jest stand-in for `@spaarke/ui-components` (test-mocks/spaarke-ui-components.js)
 * maps used exports to their REAL source and throws for anything else, so a
 * future test can never silently receive `undefined` (task 081 round 3, L6).
 */
import * as ui from '@spaarke/ui-components';
import { getXrm as realGetXrm } from '../../../shared/Spaarke.UI.Components/src/utils/xrmContext';

describe('@spaarke/ui-components jest stand-in', () => {
  it('maps getXrm to the real shared implementation', () => {
    expect(ui.getXrm).toBe(realGetXrm);
  });

  it('throws a clear error for an export that is not mapped', () => {
    expect(() => (ui as unknown as Record<string, unknown>).EmptyState).toThrow(/is not mapped in test-mocks/);
  });
});
