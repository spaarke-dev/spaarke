// DocumentLibrary's upload dialog - the path real users take to upload a project document - shows the BFF's own
// ProblemDetails detail for a refused upload (spaarke-SPA-external-access-platform-r3 task 042 review, FR-18, #1566).
// The old mapping said "a file named X already exists" for EVERY 409, including the owner-refusal 409
// ("Record owner unresolved"), and threw away the detail of a 503 / 413 / 502. The real dialog and the real
// bff-client `problemMessage` run; only the network seam (bffApiCall) is substituted, answering with an ApiError
// whose message is the raw ProblemDetails body, exactly as bff-client builds it.
import * as React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessLevel, ApiError } from '../src/types';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

import { DocumentLibrary } from '../src/components/DocumentLibrary';

const PROJECT = 'p-042';
const SLOW = { timeout: 60000 };

/** Documents list answers an empty list; the upload POST fails with the scripted ApiError. */
function failUploadWith(status: number, body: string): void {
  bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    if (options?.method === 'POST') throw new ApiError(status, body);
    if (path.endsWith('/documents')) return { value: [] };
    throw new Error(`unexpected path ${path}`);
  });
}

const problemBody = (status: number, detail: string): string => JSON.stringify({ status, title: 'refused', detail });

async function attemptUpload(fileName: string): Promise<void> {
  render(
    <FluentProvider theme={webLightTheme}>
      <DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />
    </FluentProvider>
  );
  fireEvent.click(await screen.findByRole('button', { name: /Upload Document/ }));
  const input = await waitFor(() => {
    const el = document.querySelector('input[type="file"]');
    if (!el) throw new Error('no file input yet');
    return el as HTMLInputElement;
  });
  fireEvent.change(input, { target: { files: [new File(['%PDF-1.4 test'], fileName, { type: 'application/pdf' })] } });
  fireEvent.click(await screen.findByRole('button', { name: 'Upload' }));
}

beforeEach(() => {
  bffApiCall.mockReset();
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

describe('UploadDialog failure messages', () => {
  it(
    'an owner-refusal 409 shows the server detail - it is NOT reported as "already exists"',
    async () => {
      const detail =
        'The document cannot be filed because no owning team could be resolved for this project. Please contact the project team.';
      failUploadWith(409, problemBody(409, detail));
      await attemptUpload('brief.pdf');

      expect(await screen.findByText(detail)).toBeTruthy();
      expect(document.body.textContent).not.toMatch(/already exists/);
    },
    SLOW
  );

  it(
    'the collision 409 still names the file (the server detail does)',
    async () => {
      const detail =
        "A file named 'dup.pdf' already exists in this project. Nothing was uploaded or changed - rename the file and try again.";
      failUploadWith(409, problemBody(409, detail));
      await attemptUpload('dup.pdf');

      expect(await screen.findByText(detail)).toBeTruthy();
    },
    SLOW
  );

  it.each([
    [503, 'The project could not be read just now. Please try again shortly.'],
    [413, 'The file is larger than the upload limit.'],
    [502, 'The document could not be stored. Please try again.'],
    [400, 'A non-empty file is required'],
  ])(
    'a %s shows the server detail, not the generic "check the file" text',
    async (status, detail) => {
      failUploadWith(status, problemBody(status, detail));
      await attemptUpload('a.pdf');

      expect(await screen.findByText(detail)).toBeTruthy();
      expect(document.body.textContent).not.toMatch(/Failed to upload the document/);
    },
    SLOW
  );

  it.each([
    [409, /"a\.pdf" could not be uploaded because of a conflict/],
    [413, /The file is too large to upload\./],
    [422, /This project has no storage configured/],
    [403, /Your access level does not permit uploading documents/],
    [503, /Please try again later\./],
    [400, /Failed to upload the document\. Please check the file and try again\./],
  ])(
    'with no ProblemDetails detail a %s falls back to its own text',
    async (status, message) => {
      failUploadWith(status, '');
      await attemptUpload('a.pdf');

      expect(await screen.findByText(message)).toBeTruthy();
    },
    SLOW
  );
});
