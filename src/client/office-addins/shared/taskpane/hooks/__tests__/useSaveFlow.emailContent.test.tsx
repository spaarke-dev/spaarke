/**
 * Task 116a (spaarkeai-word-add-in-r1): what the Outlook save SENDS for the email's content.
 *
 * The body and the selected attachments are read in the add-in at submit (a B2B guest's mailbox is out of the
 * server's Graph reach) and sent in the existing `email.body` / `email.attachments[].contentBase64` fields. The pane
 * supplies the reader through `EmailContentCaptureContext`; these tests supply it directly.
 *
 * Pins: the request carries the captured body/attachments/names and still the internetMessageId; the reader gets the
 * pane's attachments and the user's selection; a read failure sends NOTHING and surfaces as the save error; without
 * a reader the request is exactly the pre-116a shape (no body, no attachments — the server's Graph fallback).
 */
import React from 'react';
import { renderHook, act } from '@testing-library/react';
import { createHash } from 'crypto';
import { useSaveFlow, type SaveFlowContext } from '../useSaveFlow';
import { EmailContentCaptureContext, type CaptureEmailContent } from '../emailContentCaptureContext';
import { EmailCaptureError, type EmailContentCapture } from '../../services/emailContentCapture';
import type { AttachmentInfo } from '@shared/adapters/types';

jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

const mockFetch = jest.fn();
global.fetch = mockFetch;

Object.defineProperty(global.crypto, 'subtle', {
  configurable: true,
  value: {
    digest: jest.fn(async (_algorithm: string, data: ArrayLike<number>) => {
      const digest = createHash('sha256')
        .update(Buffer.from(Array.from(data)))
        .digest();
      const out = new Uint8Array(digest.length);
      out.set(digest);
      return out.buffer;
    }),
  },
});

const ATTACHMENTS: AttachmentInfo[] = [
  { id: 'att-1', name: 'Contract.pdf', contentType: 'application/pdf', size: 3, isInline: false },
  { id: 'att-2', name: 'Logo.png', contentType: 'image/png', size: 3, isInline: false },
];

function outlookContext(): SaveFlowContext {
  return {
    hostType: 'outlook',
    // Task 121: the Exchange item id and the RFC Message-ID are separate values (the pane used to send the item id as
    // internetMessageId).
    itemId: 'AAMkAGI2TG93AAA=',
    internetMessageId: '<msg-1@contoso.com>',
    itemName: 'Re: Filing',
    attachments: ATTACHMENTS,
    senderEmail: 'counsel@contoso.com',
  };
}

const CAPTURED: EmailContentCapture = {
  body: '<p>The email text</p>',
  isBodyHtml: true,
  attachments: [
    {
      attachmentId: 'att-1',
      fileName: 'Contract.pdf',
      size: 3,
      contentType: 'application/pdf',
      contentBase64: 'YWJj',
      isInline: false,
    },
  ],
  selectedAttachmentFileNames: ['Contract.pdf'],
  skipped: [],
};

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) {
      const body = { jobId: 'job-1', streamUrl: '/api/office/jobs/job-1/stream', status: 'Queued' };
      return { ok: true, status: 202, text: async () => JSON.stringify(body), json: async () => body };
    }
    const status = { jobId: 'job-1', status: 'Running', completedPhases: [] };
    return { ok: true, status: 200, text: async () => JSON.stringify(status), json: async () => status };
  });
});

const getAccessToken = jest.fn().mockResolvedValue('token');

function saveCalls() {
  return mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save'));
}

async function runSave(capture: CaptureEmailContent | undefined, selected: string[]) {
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <EmailContentCaptureContext.Provider value={capture}>{children}</EmailContentCaptureContext.Provider>
  );
  const { result } = renderHook(
    () => useSaveFlow({ getAccessToken, pollingIntervalMs: 60_000, apiBaseUrl: 'https://bff' }),
    { wrapper }
  );
  act(() => result.current.setSelectedAttachmentIds(new Set(selected)));
  await act(async () => {
    await result.current.startSave(outlookContext());
  });
  return result;
}

describe('useSaveFlow — email content read in the add-in (task 116a)', () => {
  it('sends the captured body, attachments and document names, and still the internetMessageId', async () => {
    const capture = jest.fn<ReturnType<CaptureEmailContent>, Parameters<CaptureEmailContent>>(async () => CAPTURED);

    await runSave(capture, ['att-1']);

    // The reader gets the pane's attachments and the user's selection — it decides what is read.
    expect(capture).toHaveBeenCalledTimes(1);
    const [atts, selected] = capture.mock.calls[0]!;
    expect(atts).toBe(ATTACHMENTS);
    expect(Array.from(selected)).toEqual(['att-1']);

    expect(saveCalls()).toHaveLength(1);
    const email = JSON.parse(String((saveCalls()[0] as [string, RequestInit])[1].body)).email;
    expect(email.body).toBe('<p>The email text</p>');
    expect(email.isBodyHtml).toBe(true);
    expect(email.attachments).toEqual(CAPTURED.attachments);
    expect(email.selectedAttachmentFileNames).toEqual(['Contract.pdf']);
    expect(email.internetMessageId).toBe('<msg-1@contoso.com>');
  });

  it('an UNTICKED attachment travels in email.attachments (the complete .eml) but not in selectedAttachmentFileNames', async () => {
    const logo = {
      attachmentId: 'att-2',
      fileName: 'Logo.png',
      size: 3,
      contentType: 'image/png',
      contentBase64: 'ZGVm',
      isInline: false,
    };
    const capture: CaptureEmailContent = async () => ({
      ...CAPTURED,
      attachments: [...CAPTURED.attachments, logo],
      selectedAttachmentFileNames: ['Contract.pdf'],
    });

    await runSave(capture, ['att-1']);

    const email = JSON.parse(String((saveCalls()[0] as [string, RequestInit])[1].body)).email;
    expect(email.attachments.map((a: { fileName: string }) => a.fileName)).toEqual(['Contract.pdf', 'Logo.png']);
    expect(email.attachments[1].contentBase64).toBe('ZGVm');
    expect(email.selectedAttachmentFileNames).toEqual(['Contract.pdf']);
  });

  it('a skipped attachment is not named for a document (the names come from what was actually sent)', async () => {
    const capture: CaptureEmailContent = async () => ({
      ...CAPTURED,
      attachments: [],
      selectedAttachmentFileNames: [],
      skipped: [
        { attachmentId: 'att-1', name: 'Contract.pdf', ticked: true, reason: 'too-large', message: 'too large' },
      ],
    });

    await runSave(capture, ['att-1']);

    const email = JSON.parse(String((saveCalls()[0] as [string, RequestInit])[1].body)).email;
    expect(email.selectedAttachmentFileNames).toEqual([]);
    expect(email.attachments).toEqual([]);
  });

  it('a read failure sends NOTHING and surfaces the reader message as the save error', async () => {
    const capture: CaptureEmailContent = async () => {
      throw new EmailCaptureError('Couldn\'t read the attachment "Contract.pdf", so nothing was saved.');
    };

    const result = await runSave(capture, ['att-1']);

    expect(saveCalls()).toHaveLength(0);
    expect(result.current.flowState).toBe('error');
    expect(result.current.error?.message).toBe('Couldn\'t read the attachment "Contract.pdf", so nothing was saved.');
  });

  it('a retry reads the email again (live, never a cached first read)', async () => {
    const capture = jest
      .fn<ReturnType<CaptureEmailContent>, Parameters<CaptureEmailContent>>()
      .mockRejectedValueOnce(new EmailCaptureError('transient'))
      .mockResolvedValueOnce(CAPTURED);

    const result = await runSave(capture, ['att-1']);
    expect(saveCalls()).toHaveLength(0);

    await act(async () => {
      result.current.retry();
    });
    await act(async () => {
      await Promise.resolve();
    });

    expect(capture).toHaveBeenCalledTimes(2);
    expect(saveCalls()).toHaveLength(1);
  });

  it('without a reader the request is the pre-116a shape: no body, no attachments, names from the selection', async () => {
    await runSave(undefined, ['att-2']);

    const email = JSON.parse(String((saveCalls()[0] as [string, RequestInit])[1].body)).email;
    expect(email.body).toBeUndefined();
    expect('attachments' in email).toBe(false);
    expect(email.selectedAttachmentFileNames).toEqual(['Logo.png']);
    expect(email.internetMessageId).toBe('<msg-1@contoso.com>');
  });

  it('the request log shows the content lengths, never the email text or file bytes', async () => {
    const log = jest.spyOn(console, 'log');
    await runSave(async () => CAPTURED, ['att-1']);

    const logged = log.mock.calls
      .filter(c => c[0] === '[SaveFlow] Sending request:')
      .map(c => String(c[1]))
      .join('\n');
    expect(logged).toContain('"body": "[21 chars]"');
    expect(logged).toContain('"contentBase64": "[4 chars]"');
    expect(logged).not.toContain('The email text');
    expect(logged).not.toContain('YWJj');
  });
});
