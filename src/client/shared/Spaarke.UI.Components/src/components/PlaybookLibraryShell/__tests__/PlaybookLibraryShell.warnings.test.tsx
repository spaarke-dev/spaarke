/**
 * PlaybookLibraryShell.warnings.test.tsx
 *
 * field-mapping-server-write-path-r1 task 028 (task 043 review): the Playbook Library shell shows the `warnings` of the
 * `/api/ai/analysis/create` 201 body (e.g. no fields were copied from the document) before it hands over, and shows
 * nothing extra when the body carries none. The create route stays `/api/ai/analysis/create`.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { PlaybookLibraryShell } from '../PlaybookLibraryShell';
import type { IDataService } from '../../../types/serviceInterfaces';

jest.mock('../../Playbook/playbookService', () => ({
  loadAllData: jest.fn().mockResolvedValue({
    playbooks: [{ id: 'pb-1', name: 'Contract review', description: 'Reviews a contract' }],
    actions: [],
    skills: [],
    knowledge: [],
    tools: [],
  }),
  loadPlaybookScopes: jest
    .fn()
    .mockResolvedValue({ actionIds: ['act-1'], skillIds: [], knowledgeIds: [], toolIds: [] }),
}));

// The card grid is not under test: a stub that lets the test pick the one playbook.
jest.mock('../../Playbook/PlaybookCardGrid', () => ({
  PlaybookCardGrid: (props: { playbooks: { id: string; name: string }[]; onSelect: (p: unknown) => void }) => (
    <button type="button" onClick={() => props.onSelect(props.playbooks[0])}>
      Pick playbook
    </button>
  ),
}));

const WARNING = 'No fields were copied from the document.';

function fetchReturning(body: unknown): jest.Mock {
  return jest.fn().mockResolvedValue({ ok: true, status: 201, json: async () => body, text: async () => '' });
}

async function renderAndRun(authFetch: jest.Mock, onComplete?: jest.Mock) {
  renderWithProviders(
    <PlaybookLibraryShell
      entityType="sprk_document"
      entityId="doc-1"
      dataService={{} as IDataService}
      authenticatedFetch={authFetch}
      bffBaseUrl="https://bff.example.com"
      onComplete={onComplete}
    />
  );
  fireEvent.click(await screen.findByText('Pick playbook'));
  const run = await screen.findByRole('button', { name: 'Run Analysis' });
  await waitFor(() => expect(run).toBeEnabled());
  fireEvent.click(run);
}

describe('PlaybookLibraryShell - warnings on the create response', () => {
  it('shows the warning of a 201 body that carries one, and hands over only when the user continues', async () => {
    const authFetch = fetchReturning({ analysisId: 'analysis-1', warnings: [WARNING] });
    const onComplete = jest.fn();

    await renderAndRun(authFetch, onComplete);

    expect(await screen.findByText(WARNING)).toBeInTheDocument();
    expect(authFetch.mock.calls[0][0]).toBe('https://bff.example.com/api/ai/analysis/create');
    expect(onComplete).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));

    expect(onComplete).toHaveBeenCalledWith({ analysisId: 'analysis-1' });
  });

  it('shows nothing and hands over at once for a body with no warnings member', async () => {
    const authFetch = fetchReturning({ analysisId: 'analysis-1' });
    const onComplete = jest.fn();

    await renderAndRun(authFetch, onComplete);

    await waitFor(() => expect(onComplete).toHaveBeenCalledWith({ analysisId: 'analysis-1' }));
    expect(screen.queryByTestId('playbook-library-create-warnings')).not.toBeInTheDocument();
    expect(screen.queryByText(WARNING)).not.toBeInTheDocument();
  });

  it('shows nothing for an empty warnings array', async () => {
    const authFetch = fetchReturning({ analysisId: 'analysis-1', warnings: [] });
    const onComplete = jest.fn();

    await renderAndRun(authFetch, onComplete);

    await waitFor(() => expect(onComplete).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('playbook-library-create-warnings')).not.toBeInTheDocument();
  });
});
