// R3 ISS-011 (#1886, owner decision 2026-10-14): a document with NO file is still listed, but its Download and Versions
// actions are hidden, so a partner never clicks into a 409 "No file attached". The BFF says so with `hasFile: false` on the
// document row; a row without the member (an older BFF) is treated as having a file. The real DocumentLibrary and Fluent
// grid run; only the network seam (bffApiCall) is substituted.
import * as React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessLevel } from '../src/types';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

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

async function renderLibrary(): Promise<void> {
  render(
    <FluentProvider theme={webLightTheme}>
      <DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />
    </FluentProvider>
  );
}

const downloadOf = (name: string): HTMLElement | null => screen.queryByRole('button', { name: `Download ${name}` });
const versionsOf = (name: string): HTMLElement | null =>
  screen.queryByRole('button', { name: `View version history for ${name}` });

beforeEach(() => {
  bffApiCall.mockReset();
});

describe('DocumentLibrary Download / Versions by hasFile', () => {
  it(
    'a hasFile:false row is listed with neither Download nor Versions',
    async () => {
      listReturns([{ sprk_documentid: 'd-1', sprk_name: 'NoFile.pdf', hasFile: false }]);
      await renderLibrary();

      expect(await screen.findByText('NoFile.pdf')).toBeTruthy();
      expect(downloadOf('NoFile.pdf')).toBeNull();
      expect(versionsOf('NoFile.pdf')).toBeNull();
    },
    SLOW
  );

  it(
    'a hasFile:true row shows Download and Versions',
    async () => {
      listReturns([{ sprk_documentid: 'd-2', sprk_name: 'WithFile.pdf', hasFile: true }]);
      await renderLibrary();

      expect(await screen.findByText('WithFile.pdf')).toBeTruthy();
      expect(downloadOf('WithFile.pdf')).toBeTruthy();
      expect(versionsOf('WithFile.pdf')).toBeTruthy();
    },
    SLOW
  );

  it(
    'a row with no hasFile member (an older BFF) shows Download and Versions',
    async () => {
      listReturns([{ sprk_documentid: 'd-3', sprk_name: 'OldBff.pdf' }]);
      await renderLibrary();

      expect(await screen.findByText('OldBff.pdf')).toBeTruthy();
      expect(downloadOf('OldBff.pdf')).toBeTruthy();
      expect(versionsOf('OldBff.pdf')).toBeTruthy();
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
      await renderLibrary();

      expect(await screen.findByText('Hasnt.pdf')).toBeTruthy();
      expect(downloadOf('Has.pdf')).toBeTruthy();
      expect(versionsOf('Has.pdf')).toBeTruthy();
      expect(downloadOf('Hasnt.pdf')).toBeNull();
      expect(versionsOf('Hasnt.pdf')).toBeNull();
    },
    SLOW
  );
});
