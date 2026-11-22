/**
 * R4 task 023c review (F1): a document's name on the Project page opens the Document record.
 *
 * The Documents list is the shared grid, which always draws a document's name as a link. The Project page used to pass no
 * opener, so the name was a link that did nothing. It now goes through the shell's `useOpenRecord` (the record template,
 * task 013), as a name does inside a record's Documents section. Each must-fire has a must-not-fire pair.
 *
 * Rendered for real: the page, `DocumentLibrary` and the shared grid. Substituted: the network seam (`bffApiCall`), the
 * project read, `/me`, the grid's Dataverse client and the shell's opener (a spy: the modal it shows is task 013's).
 */
import * as React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { bffApiCall, openRecord } = vi.hoisted(() => ({ bffApiCall: vi.fn(), openRecord: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

vi.mock('../src/api/web-api-client', async () => {
  const actual = await vi.importActual<typeof import('../src/api/web-api-client')>('../src/api/web-api-client');
  return {
    ...actual,
    getProjectById: vi.fn().mockResolvedValue({
      sprk_projectid: 'p-open',
      sprk_name: 'Project Open',
      sprk_referencenumber: 'PRJ-OPEN',
      sprk_status: 1,
      sprk_issecure: false,
    }),
  };
});

vi.mock('../src/services/gridDataverseClient', async () => (await import('./documentsGridHarness')).gridClientModule());

vi.mock('../src/components/detail/RecordOpener', () => ({ useOpenRecord: () => openRecord }));

vi.mock('../src/hooks/useExternalContext', () => ({
  useExternalContext: () => ({
    context: {
      contactId: 'c-open',
      email: 'partner@firm.example',
      projects: [{ projectId: 'p-open', accessLevel: 'Collaborate' }],
    },
    isLoading: false,
    error: null,
  }),
}));

import { ProjectPage } from '../src/pages/ProjectPage';
import { documentRow } from './documentsGridHarness';

const SLOW = 60000;
const DOC_ID = '00000000-0000-4000-8000-000000000901';

function listHasOneDocument(extra: Record<string, unknown> = {}): void {
  bffApiCall.mockImplementation(async (path: string) => {
    if (/\/projects\/p-open\/documents$/.test(path)) {
      return {
        value: [{ sprk_documentid: DOC_ID, sprk_name: 'Brief.pdf', createdon: '2026-01-05T10:00:00Z', ...extra }],
        canCreate: true,
      };
    }
    throw new Error(`unexpected request ${path}`);
  });
}

function renderProject(): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={['/project/p-open']}>
        <Routes>
          <Route path="/project/:id" element={<ProjectPage />} />
        </Routes>
      </MemoryRouter>
    </FluentProvider>
  );
}

beforeEach(() => {
  bffApiCall.mockReset();
  openRecord.mockReset();
});

describe('Project page: a document name opens the Document record', () => {
  it(
    'a click on the name asks the shell to open that sprk_document, returning focus to the name',
    async () => {
      listHasOneDocument();
      renderProject();

      const link = within(await documentRow('Brief.pdf')).getByRole('button', { name: 'Brief.pdf' });
      link.focus();
      fireEvent.click(link);

      expect(openRecord).toHaveBeenCalledTimes(1);
      expect(openRecord).toHaveBeenCalledWith(
        { entityName: 'sprk_document', recordId: DOC_ID },
        { returnFocusTo: link }
      );
    },
    SLOW
  );

  it(
    'a document with no file is opened too (its record shows Information only)',
    async () => {
      listHasOneDocument({ hasFile: false });
      renderProject();

      fireEvent.click(within(await documentRow('Brief.pdf')).getByRole('button', { name: 'Brief.pdf' }));

      expect(openRecord).toHaveBeenCalledTimes(1);
      expect(openRecord.mock.calls[0][0]).toEqual({ entityName: 'sprk_document', recordId: DOC_ID });
    },
    SLOW
  );

  it(
    'must-not-fire pair: showing the list opens nothing, and a click on a row action does not open the record',
    async () => {
      listHasOneDocument();
      renderProject();

      const row = await documentRow('Brief.pdf');
      expect(openRecord).not.toHaveBeenCalled();
      fireEvent.click(within(row).getByRole('button', { name: 'Version history' }));
      expect(openRecord).not.toHaveBeenCalled();
    },
    SLOW
  );
});
