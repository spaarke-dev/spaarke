/**
 * OntologyAdminWorkspaceWidget - host wiring (task 100; D-22, FR-57): the Rules area is built over the session's authenticated fetch
 * and BFF base URL and handed to the tab as its `rules` slot. The tab and the area are mocked at the module boundary - their
 * behaviour is covered in `@spaarke/ui-components` (`OntologyAdmin/Rules/__tests__`).
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';

const authenticatedFetch = jest.fn();
const createOntologyRulesService = jest.fn((opts: unknown) => ({ opts }));
let rulesAreaProps: Record<string, unknown> | undefined;

jest.mock('@spaarke/ui-components', () => ({
  __esModule: true,
  OntologyAdminTab: (props: { rules?: React.ReactNode }) => React.createElement('div', { 'data-testid': 'mock-tab' }, props.rules),
  RulesArea: (props: Record<string, unknown>) => {
    rulesAreaProps = props;
    return React.createElement('div', { 'data-testid': 'mock-rules-area' });
  },
  createOntologyRulesService: (opts: unknown) => createOntologyRulesService(opts),
}));
jest.mock('../../../providers/useAiSession', () => ({
  __esModule: true,
  useAiSession: () => ({ authenticatedFetch, bffBaseUrl: 'https://bff.example' }),
}));

import { OntologyAdminWorkspaceWidget } from '../OntologyAdminWorkspaceWidget';

const props = {} as unknown as React.ComponentProps<typeof OntologyAdminWorkspaceWidget>;

describe('OntologyAdminWorkspaceWidget', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    rulesAreaProps = undefined;
  });

  it('builds the Rules service over the session fetch and BFF base url, and passes the area as the tab\'s rules slot', () => {
    render(<OntologyAdminWorkspaceWidget {...props} />);

    expect(createOntologyRulesService).toHaveBeenCalledWith({ fetch: authenticatedFetch, baseUrl: 'https://bff.example' });
    expect(screen.getByTestId('mock-tab')).toContainElement(screen.getByTestId('mock-rules-area'));
    expect(rulesAreaProps?.service).toEqual({ opts: { fetch: authenticatedFetch, baseUrl: 'https://bff.example' } });
  });

  it('builds the service once across re-renders with the same session', () => {
    const { rerender } = render(<OntologyAdminWorkspaceWidget {...props} />);
    rerender(<OntologyAdminWorkspaceWidget {...props} />);

    expect(createOntologyRulesService).toHaveBeenCalledTimes(1);
  });
});
