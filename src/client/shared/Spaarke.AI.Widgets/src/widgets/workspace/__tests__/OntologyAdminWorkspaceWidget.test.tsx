/**
 * OntologyAdminWorkspaceWidget - host wiring (task 103): the Classification area is built from the session's authenticated fetch and
 * BFF base URL and handed to the tab shell as its `classification` slot. The shared tab and area are mocked at the module boundary;
 * their behaviour is covered in `@spaarke/ui-components`.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';

const authenticatedFetch = jest.fn();
let tabProps: Record<string, unknown> | undefined;
let areaProps: Record<string, unknown> | undefined;
const createClassificationService = jest.fn((opts: unknown) => ({ opts }));

jest.mock('@spaarke/ui-components', () => ({
  __esModule: true,
  createClassificationService: (opts: unknown) => createClassificationService(opts),
  OntologyAdminTab: (props: Record<string, unknown>) => {
    tabProps = props;
    return React.createElement('div', { 'data-testid': 'mock-tab' }, props.classification as React.ReactNode);
  },
  ClassificationArea: (props: Record<string, unknown>) => {
    areaProps = props;
    return React.createElement('div', { 'data-testid': 'mock-classification' });
  },
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
    tabProps = undefined;
    areaProps = undefined;
  });

  it('fills the tab\'s classification slot with the area, over a service built from the session fetch and BFF base url', () => {
    render(<OntologyAdminWorkspaceWidget {...props} />);

    expect(screen.getByTestId('mock-classification')).toBeInTheDocument();
    expect(tabProps?.classification).toBeDefined();
    expect(createClassificationService).toHaveBeenCalledWith({ fetch: authenticatedFetch, baseUrl: 'https://bff.example' });
    expect(areaProps?.service).toEqual({ opts: { fetch: authenticatedFetch, baseUrl: 'https://bff.example' } });
  });

  it('builds the service once across re-renders with the same session', () => {
    const { rerender } = render(<OntologyAdminWorkspaceWidget {...props} />);
    rerender(<OntologyAdminWorkspaceWidget {...props} />);

    expect(createClassificationService).toHaveBeenCalledTimes(1);
  });
});
