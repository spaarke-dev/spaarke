/**
 * Task 133 slice A: Work Assignment exists in the add-in ONLY for the Invoice view's core-record picker. The Save
 * picker and its "+ New" form keep exactly Matter, Project and Invoice (Invoice leaves Save only in slice C).
 *
 * Harness mirrors `SaveFlow.filingAccess.test.tsx` (real SaveFlow + real RelatedToPicker).
 */
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import { fetchRelatedCandidates } from '../../services/communicationSuggestionsService';
import { ALL_ENTITY_TYPES } from '../../hooks/useEntitySearch';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));
jest.mock('../../services/communicationSuggestionsService', () => ({
  fetchRelatedCandidates: jest.fn(),
}));

const TEST_TIMEOUT_MS = 60000;
const WAIT = { timeout: 10000 };

beforeEach(() => {
  global.fetch = jest.fn(async () => ({ ok: true, status: 200, json: async () => ({}) })) as unknown as typeof fetch;
  sessionStorage.clear();
  (fetchRelatedCandidates as jest.Mock).mockResolvedValue([]);
});

function pillNames(): string[] {
  const group = screen.getByRole('radiogroup', { name: 'Related to record type' });
  return within(group)
    .getAllByRole('radio')
    .map(r => r.textContent ?? '');
}

describe('Save keeps its target types (task 133)', () => {
  it(
    'Outlook Save offers Matter, Project and Invoice — and no Work Assignment, in the picker or "+ New"',
    async () => {
      render(
        <FluentProvider theme={webLightTheme}>
          <SaveFlow
            hostType="outlook"
            itemId="AAMkAGI2TG93AAA="
            internetMessageId="<abc@contoso.com>"
            itemName="Invoice 4711"
            getAccessToken={jest.fn().mockResolvedValue('token')}
            apiBaseUrl="https://bff"
          />
        </FluentProvider>
      );

      await screen.findByLabelText('Search Matter records', {}, WAIT);
      expect(pillNames()).toEqual(['Matter', 'Project', 'Invoice']);

      await userEvent.click(screen.getByRole('button', { name: 'New' }));
      expect(await screen.findByText('Create New Record', {}, WAIT)).toBeInTheDocument();
      expect(pillNames()).toEqual(['Matter', 'Project', 'Invoice']);
      expect(screen.queryByText(/Work ?Assignment/)).toBeNull();
    },
    TEST_TIMEOUT_MS
  );

  it('ALL_ENTITY_TYPES (the generic picker default) does not include WorkAssignment', () => {
    expect(ALL_ENTITY_TYPES).not.toContain('WorkAssignment');
  });
});
