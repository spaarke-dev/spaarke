/**
 * OntologyAdminWorkspaceWidget - host wiring (tasks 100 and 103; D-22, FR-57, FR-59): the Rules and Classification areas are each
 * built over the session's authenticated fetch and BFF base URL and handed to the tab as its `rules` and `classification` slots. The
 * tab and the areas are mocked at the module boundary - their behaviour is covered in `@spaarke/ui-components`
 * (`OntologyAdmin/Rules/__tests__`, `OntologyAdmin/Classification/__tests__`).
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';

const authenticatedFetch = jest.fn();
const createOntologyRulesService = jest.fn((opts: unknown) => ({ opts }));
const createClassificationService = jest.fn((opts: unknown) => ({ opts }));
const createOntologyAuthoringService = jest.fn((opts: unknown) => ({ authoring: opts }));
const searchLookupTarget = jest.fn((_target: string, _query: string) =>
  Promise.resolve([{ id: 'm-1', name: 'Acme v Beta' }])
);
let tabProps: Record<string, unknown> | undefined;
let rulesAreaProps: Record<string, unknown> | undefined;
let classificationAreaProps: Record<string, unknown> | undefined;

jest.mock('@spaarke/ui-components', () => ({
  __esModule: true,
  OntologyAdminTab: (props: { rules?: React.ReactNode; classification?: React.ReactNode }) => {
    tabProps = props as Record<string, unknown>;
    return React.createElement('div', { 'data-testid': 'mock-tab' }, props.rules, props.classification);
  },
  RulesArea: (props: Record<string, unknown>) => {
    rulesAreaProps = props;
    return React.createElement('div', { 'data-testid': 'mock-rules-area' });
  },
  ClassificationArea: (props: Record<string, unknown>) => {
    classificationAreaProps = props;
    return React.createElement('div', { 'data-testid': 'mock-classification' });
  },
  createOntologyRulesService: (opts: unknown) => createOntologyRulesService(opts),
  createClassificationService: (opts: unknown) => createClassificationService(opts),
  createOntologyAuthoringService: (opts: unknown) => createOntologyAuthoringService(opts),
  searchLookupTarget: (target: string, query: string) => searchLookupTarget(target, query),
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
    rulesAreaProps = undefined;
    classificationAreaProps = undefined;
  });

  it("builds the Rules service over the session fetch and BFF base url, and passes the area as the tab's rules slot", () => {
    render(<OntologyAdminWorkspaceWidget {...props} />);

    expect(createOntologyRulesService).toHaveBeenCalledWith({
      fetch: authenticatedFetch,
      baseUrl: 'https://bff.example',
    });
    expect(screen.getByTestId('mock-tab')).toContainElement(screen.getByTestId('mock-rules-area'));
    expect(rulesAreaProps?.service).toEqual({ opts: { fetch: authenticatedFetch, baseUrl: 'https://bff.example' } });
  });

  it("builds the rule wizard's authoring service over the same session and hands it to the Rules area", () => {
    render(<OntologyAdminWorkspaceWidget {...props} />);

    expect(createOntologyAuthoringService).toHaveBeenCalledWith({
      fetch: authenticatedFetch,
      baseUrl: 'https://bff.example',
    });
    expect(rulesAreaProps?.authoringService).toEqual({
      authoring: { fetch: authenticatedFetch, baseUrl: 'https://bff.example' },
    });
  });

  it('hands the Rules area a matter picker for rules about sprk_matter, searching through the shared lookup search (task 102b)', async () => {
    render(<OntologyAdminWorkspaceWidget {...props} />);

    const picker = rulesAreaProps?.matterPicker as { subject: string; search: (query: string) => Promise<unknown> };
    expect(picker.subject).toBe('sprk_matter');
    await expect(picker.search('acme')).resolves.toEqual([{ id: 'm-1', name: 'Acme v Beta' }]);
    expect(searchLookupTarget).toHaveBeenCalledWith('sprk_matter', 'acme');
  });

  it("fills the tab's classification slot with the area, over a service built from the session fetch and BFF base url", () => {
    render(<OntologyAdminWorkspaceWidget {...props} />);

    expect(screen.getByTestId('mock-classification')).toBeInTheDocument();
    expect(tabProps?.classification).toBeDefined();
    expect(createClassificationService).toHaveBeenCalledWith({
      fetch: authenticatedFetch,
      baseUrl: 'https://bff.example',
    });
    expect(classificationAreaProps?.service).toEqual({
      opts: { fetch: authenticatedFetch, baseUrl: 'https://bff.example' },
    });
  });

  it('builds each service once across re-renders with the same session', () => {
    const { rerender } = render(<OntologyAdminWorkspaceWidget {...props} />);
    rerender(<OntologyAdminWorkspaceWidget {...props} />);

    expect(createOntologyRulesService).toHaveBeenCalledTimes(1);
    expect(createClassificationService).toHaveBeenCalledTimes(1);
    expect(createOntologyAuthoringService).toHaveBeenCalledTimes(1);
  });
});
