/**
 * useAttachmentPreview (#2967) — the preview / open logic shared by the CommunicationAttachments PCF and
 * EmailReadingAttachments: selection, prev/next browse, open in web / desktop, desktop failure, copy link.
 */
import { act, renderHook } from '@testing-library/react';
import { useAttachmentPreview, type IUseAttachmentPreviewOptions } from '../useAttachmentPreview';
import type { IAttachmentItem } from '../../../logic/attachments';

jest.mock('@spaarke/auth', () => ({ authenticatedFetch: jest.fn() }));

const item = (id: string | undefined, name: string): IAttachmentItem => ({ documentId: id, name }) as IAttachmentItem;
const ITEMS = [item('d1', 'a.pdf'), item(undefined, 'no-doc.txt'), item('d2', 'b.docx')];

function setup(over: Partial<IUseAttachmentPreviewOptions> = {}) {
  const apiService = {
    getPreviewUrl: jest.fn().mockResolvedValue('https://preview'),
    getOpenLinks: jest.fn().mockResolvedValue({ webUrl: 'https://sp/web' }),
  };
  const opts: IUseAttachmentPreviewOptions = {
    items: ITEMS,
    apiService: apiService as unknown as IUseAttachmentPreviewOptions['apiService'],
    apiBaseUrl: 'https://bff',
    openInDesktopApp: jest.fn().mockResolvedValue(undefined),
    openInDesktopFailedMessage: 'desktop failed',
    logTag: 'Test',
    onOpenRecord: jest.fn(),
    openExternal: jest.fn(),
    onOpenDesktopError: jest.fn(),
    ...over,
  };
  const hook = renderHook(() => useAttachmentPreview(opts));
  return { hook, opts, apiService };
}

const flush = () =>
  act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });

describe('useAttachmentPreview', () => {
  it('has no dialog until a row with a document is activated; a row without one is ignored', () => {
    const { hook } = setup();
    expect(hook.result.current.dialogProps).toBeNull();
    act(() => hook.result.current.handleRowActivate(ITEMS[1]));
    expect(hook.result.current.dialogProps).toBeNull();
    act(() => hook.result.current.handleRowActivate(ITEMS[0]));
    expect(hook.result.current.dialogProps).toMatchObject({ documentId: 'd1', documentName: 'a.pdf', currentIndex: 0 });
  });

  it('browses only items that have a document and stays in range', () => {
    const { hook } = setup();
    act(() => hook.result.current.handleRowActivate(ITEMS[0]));
    expect(hook.result.current.dialogProps?.navigationTotal).toBe(2);
    act(() => hook.result.current.dialogProps?.onNavigate(1));
    expect(hook.result.current.dialogProps?.documentId).toBe('d2');
    act(() => hook.result.current.dialogProps?.onNavigate(2));
    expect(hook.result.current.dialogProps?.documentId).toBe('d2');
    act(() => hook.result.current.dialogProps?.onClose());
    expect(hook.result.current.dialogProps).toBeNull();
  });

  it('opens the web URL through the host opener and the record through onOpenRecord', async () => {
    const { hook, opts } = setup();
    act(() => hook.result.current.handleRowActivate(ITEMS[0]));
    act(() => hook.result.current.dialogProps?.onOpenFile('web'));
    await flush();
    expect(opts.openExternal).toHaveBeenCalledWith('https://sp/web');
    expect(opts.openInDesktopApp).not.toHaveBeenCalled();
    act(() => hook.result.current.dialogProps?.onOpenRecord());
    expect(opts.onOpenRecord).toHaveBeenCalledWith('d1');
  });

  it('downloads through the BFF for "desktop" with the host-only base URL and file name', async () => {
    const { hook, opts } = setup();
    act(() => hook.result.current.handleRowActivate(ITEMS[0]));
    act(() => hook.result.current.dialogProps?.onOpenFile('desktop'));
    await flush();
    expect(opts.openInDesktopApp).toHaveBeenCalledWith(
      expect.objectContaining({ bffBaseUrl: 'https://bff', documentId: 'd1', fallbackFileName: 'a.pdf' })
    );
    expect(opts.onOpenDesktopError).toHaveBeenLastCalledWith(null);
  });

  it('reports a desktop failure, and closes the dialog only when asked to', async () => {
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const failing = jest.fn().mockRejectedValue(new Error('boom'));
    const keep = setup({ openInDesktopApp: failing });
    act(() => keep.hook.result.current.handleRowActivate(ITEMS[0]));
    act(() => keep.hook.result.current.dialogProps?.onOpenFile('desktop'));
    await flush();
    expect(keep.opts.onOpenDesktopError).toHaveBeenLastCalledWith('desktop failed');
    expect(keep.hook.result.current.dialogProps).not.toBeNull();

    const close = setup({ openInDesktopApp: failing, closeOnOpenDesktopError: true });
    act(() => close.hook.result.current.handleRowActivate(ITEMS[0]));
    act(() => close.hook.result.current.dialogProps?.onOpenFile('desktop'));
    await flush();
    expect(close.hook.result.current.dialogProps).toBeNull();
  });

  it('treats an empty open-links response as a desktop failure', async () => {
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const { hook, opts, apiService } = setup();
    apiService.getOpenLinks.mockResolvedValue(null);
    act(() => hook.result.current.handleRowActivate(ITEMS[0]));
    act(() => hook.result.current.dialogProps?.onOpenFile('desktop'));
    await flush();
    expect(opts.openInDesktopApp).not.toHaveBeenCalled();
    expect(opts.onOpenDesktopError).toHaveBeenLastCalledWith('desktop failed');
  });

  it('copies the web link to the clipboard', async () => {
    const writeText = jest.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const { hook } = setup();
    act(() => hook.result.current.handleRowActivate(ITEMS[0]));
    act(() => hook.result.current.dialogProps?.onCopyLink());
    await flush();
    expect(writeText).toHaveBeenCalledWith('https://sp/web');
  });
});
