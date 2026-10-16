/**
 * WorklistWorkspaceWidget - host wiring tests (task 059): the Signal read route client over the session fetch, the aggregate
 * card opening the Email Review tab through the pane bus, and the lookup only existing when Xrm does. The shared WorklistWidget and
 * the aggregate card are mocked at the module boundary - their behaviour is covered in their own packages.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';

const authenticatedFetch = jest.fn();
const dispatch = jest.fn();
let xrm: unknown;
let widgetProps: Record<string, unknown> | undefined;
let cardProps: Record<string, unknown> | undefined;
const createWorklistService = jest.fn((opts: unknown) => ({ opts }));

jest.mock('@spaarke/ui-components', () => ({
  __esModule: true,
  XrmDataverseClient: class {},
  getXrm: () => xrm,
  createWorklistService: (opts: unknown) => createWorklistService(opts),
  searchContactsAsLookup: jest.fn(),
  searchOrganizationsAsLookup: jest.fn(),
  WorklistWidget: (props: Record<string, unknown>) => {
    widgetProps = props;
    return React.createElement('div', { 'data-testid': 'mock-worklist' }, props.aggregateRow as React.ReactNode);
  },
}));
jest.mock('@spaarke/communication-components', () => ({
  __esModule: true,
  ReconciliationAggregateCard: (props: Record<string, unknown>) => {
    cardProps = props;
    return React.createElement(
      'button',
      { 'data-testid': 'mock-card', onClick: props.onOpen as () => void },
      'Open Email Review'
    );
  },
}));
jest.mock('../../../providers/useAiSession', () => ({
  __esModule: true,
  useAiSession: () => ({ authenticatedFetch, bffBaseUrl: 'https://bff.example' }),
}));
jest.mock('../../../events/useDispatchPaneEvent', () => ({
  __esModule: true,
  useOptionalDispatchPaneEvent: () => dispatch,
}));

import { WorklistWorkspaceWidget } from '../WorklistWorkspaceWidget';

const props = {} as unknown as React.ComponentProps<typeof WorklistWorkspaceWidget>;

describe('WorklistWorkspaceWidget', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    widgetProps = undefined;
    cardProps = undefined;
    xrm = { WebApi: { retrieveMultipleRecords: jest.fn() } };
  });

  it('reads the route over the session fetch and BFF base url', () => {
    render(<WorklistWorkspaceWidget {...props} />);
    expect(createWorklistService).toHaveBeenCalledWith({ fetch: authenticatedFetch, baseUrl: 'https://bff.example' });
    expect(widgetProps?.service).toEqual({ opts: { fetch: authenticatedFetch, baseUrl: 'https://bff.example' } });
  });

  it('hands the shared widget the wizard lookup and the aggregate card, and no narrative', () => {
    render(<WorklistWorkspaceWidget {...props} />);
    expect(typeof widgetProps?.lookup).toBe('function');
    expect(screen.getByTestId('mock-card')).toBeInTheDocument();
    expect(cardProps?.dataverseClient).toBeDefined();
    expect(widgetProps?.narrative).toBeUndefined();
  });

  it('"Open Email Review" opens the reconciliation tab on the workspace channel', () => {
    render(<WorklistWorkspaceWidget {...props} />);
    fireEvent.click(screen.getByTestId('mock-card'));
    expect(dispatch).toHaveBeenCalledWith('workspace', {
      type: 'widget_load',
      widgetType: 'communications-reconciliation',
    });
  });

  it('without Xrm there is no lookup and the card gets no client (it reports Missing), but the worklist still renders', () => {
    xrm = undefined;
    render(<WorklistWorkspaceWidget {...props} />);
    expect(screen.getByTestId('mock-worklist')).toBeInTheDocument();
    expect(widgetProps?.lookup).toBeUndefined();
    expect(cardProps?.dataverseClient).toBeUndefined();
  });
});
