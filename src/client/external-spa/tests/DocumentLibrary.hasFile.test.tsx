// R3 ISS-011 (#1886, owner decision 2026-10-14): a document with NO file is still listed, but its Download and Versions
// actions are hidden, so a partner never clicks into a 409 "No file attached". The BFF says so with `hasFile: false` on the
// document row; a row without the member (an older BFF) is treated as having a file. R4 task 023c (FR-15, FR-16): the list is
// the shared grid and Download / Version history are its row actions, hidden per row by `isVisible`, never disabled. The real
// DocumentLibrary and the real shared grid run; only the network seam (bffApiCall) and the grid's Dataverse client are
// substituted.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessLevel } from '../src/types';
import { documentRow, rowAction } from './documentsGridHarness';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});
vi.mock('../src/services/gridDataverseClient', async () => (await import('./documentsGridHarness')).gridClientModule());

import { DocumentLibrary } from '../src/components/DocumentLibrary';

const SLOW = { timeout: 60000 };
const PROJECT = 'p-iss-011';

type Row = { sprk_documentid: string; sprk_name: string; hasFile?: boolean };

function listReturns(rows: Row[]): void {
  bffApiCall.mockImplementation(async (path: string) => {
    if (path.endsWith('/documents')) return { value: rows };
    throw new Error(`unexpected path ${path}`);
  });
}

function renderLibrary(): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />
    </FluentProvider>
  );
}

beforeEach(() => {
  bffApiCall.mockReset();
});

describe('DocumentLibrary Download / Versions by hasFile', () => {
  it(
    'a hasFile:false row is listed with neither Download nor Versions',
    async () => {
      listReturns([{ sprk_documentid: 'd-1', sprk_name: 'NoFile.pdf', hasFile: false }]);
      renderLibrary();

      const row = await documentRow('NoFile.pdf');
      expect(rowAction(row, 'Download')).toBeNull();
      expect(rowAction(row, 'Version history')).toBeNull();
    },
    SLOW
  );

  it(
    'a hasFile:true row shows Download and Versions',
    async () => {
      listReturns([{ sprk_documentid: 'd-2', sprk_name: 'WithFile.pdf', hasFile: true }]);
      renderLibrary();

      const row = await documentRow('WithFile.pdf');
      expect(rowAction(row, 'Download')).toBeTruthy();
      expect(rowAction(row, 'Version history')).toBeTruthy();
    },
    SLOW
  );

  it(
    'a row with no hasFile member (an older BFF) shows Download and Versions',
    async () => {
      listReturns([{ sprk_documentid: 'd-3', sprk_name: 'OldBff.pdf' }]);
      renderLibrary();

      const row = await documentRow('OldBff.pdf');
      expect(rowAction(row, 'Download')).toBeTruthy();
      expect(rowAction(row, 'Version history')).toBeTruthy();
    },
    SLOW
  );

  it(
    'in one list only the file-less row loses its actions',
    async () => {
      listReturns([
        { sprk_documentid: 'd-4', sprk_name: 'Has.pdf', hasFile: true },
        { sprk_documentid: 'd-5', sprk_name: 'Hasnt.pdf', hasFile: false },
      ]);
      renderLibrary();

      const hasnt = await documentRow('Hasnt.pdf');
      const has = await documentRow('Has.pdf');
      expect(rowAction(has, 'Download')).toBeTruthy();
      expect(rowAction(has, 'Version history')).toBeTruthy();
      expect(rowAction(hasnt, 'Download')).toBeNull();
      expect(rowAction(hasnt, 'Version history')).toBeNull();
      // Hidden, not disabled: no disabled action button exists on the file-less row.
      expect(
        screen.queryAllByRole('button', { name: /Download|Version history/ }).filter(b => b.hasAttribute('disabled'))
      ).toHaveLength(0);
    },
    SLOW
  );
});
