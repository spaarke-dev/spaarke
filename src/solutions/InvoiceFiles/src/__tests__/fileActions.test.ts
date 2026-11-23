import { REVOKE_AFTER_MS, downloadFile, openFile, type FileActionDeps, type TabLike } from '../services/fileActions';
import { failed, fetched, file, ok, problem, PDF_ID } from './testKit';
import type { ApiResult, FetchedFile } from '../services/invoiceFilesApi';

function setup(fetchResult: ApiResult<FetchedFile>, popupBlocked = false) {
  const events: string[] = [];
  const tab: TabLike = { close: jest.fn(() => events.push('close')), navigate: jest.fn(() => events.push('navigate')) };
  const later = jest.fn();
  const deps: FileActionDeps = {
    api: {
      fetchFile: jest.fn(async () => {
        events.push('fetch');
        return fetchResult;
      }),
    },
    openTab: jest.fn(() => {
      events.push('openTab');
      return popupBlocked ? null : tab;
    }),
    createObjectUrl: jest.fn(() => 'blob:mock'),
    revokeObjectUrl: jest.fn(),
    saveBlob: jest.fn(() => events.push('save')),
    later,
  };
  return { deps, tab, events, later };
}

describe('openFile', () => {
  it('opens the tab before the fetch (inside the click), fetches from the content route, then points the tab at the blob', async () => {
    const { deps, tab, events, later } = setup(ok(fetched()));

    const result = await openFile(file(), deps);

    expect(result).toEqual({ ok: true });
    expect(events).toEqual(['openTab', 'fetch', 'navigate']);
    expect(deps.api.fetchFile).toHaveBeenCalledWith(PDF_ID);
    expect(tab.navigate).toHaveBeenCalledWith('blob:mock');
    expect(later).toHaveBeenCalledWith(expect.any(Function), REVOKE_AFTER_MS);
    later.mock.calls[0][0]();
    expect(deps.revokeObjectUrl).toHaveBeenCalledWith('blob:mock');
  });

  it.each([
    [409, 'file_scan_pending', /scanned for malware/],
    [404, null, /not available/],
    [503, 'file_store_unavailable', /file store is unavailable/],
  ])('a %i from the content route closes the tab and says why', async (status, code, message) => {
    const { deps, tab } = setup(failed(problem(status, code)));

    const result = await openFile(file(), deps);

    expect(result).toMatchObject({ ok: false, message: expect.stringMatching(message) });
    expect(tab.close).toHaveBeenCalled();
    expect(tab.navigate).not.toHaveBeenCalled();
    expect(deps.createObjectUrl).not.toHaveBeenCalled();
  });

  it('a blocked pop-up is reported without fetching the bytes', async () => {
    const { deps } = setup(ok(fetched()), true);

    const result = await openFile(file(), deps);

    expect(result).toMatchObject({ ok: false, message: expect.stringMatching(/blocked the new tab/) });
    expect(deps.api.fetchFile).not.toHaveBeenCalled();
  });

  it('a file still in its scan is refused without a tab or a call', async () => {
    const { deps } = setup(ok(fetched()));

    const result = await openFile(file({ scanState: 'Pending' }), deps);

    expect(result).toMatchObject({ ok: false, message: expect.stringMatching(/scanned for malware/) });
    expect(deps.openTab).not.toHaveBeenCalled();
    expect(deps.api.fetchFile).not.toHaveBeenCalled();
  });
});

describe('downloadFile', () => {
  it('fetches from the content route and saves the bytes under the file name', async () => {
    const { deps } = setup(ok(fetched()));

    const result = await downloadFile(file({ name: 'INV-792345.pdf' }), deps);

    expect(result).toEqual({ ok: true });
    expect(deps.api.fetchFile).toHaveBeenCalledWith(PDF_ID);
    expect(deps.saveBlob).toHaveBeenCalledWith(expect.any(Blob), 'INV-792345.pdf');
  });

  it('a failure saves nothing and says why', async () => {
    const { deps } = setup(failed(problem(404, null)));

    const result = await downloadFile(file(), deps);

    expect(result).toMatchObject({ ok: false, message: expect.stringMatching(/not available/) });
    expect(deps.saveBlob).not.toHaveBeenCalled();
  });

  it('a file still in its scan is refused without a call', async () => {
    const { deps } = setup(ok(fetched()));

    const result = await downloadFile(file({ scanState: 'Pending' }), deps);

    expect(result.ok).toBe(false);
    expect(deps.api.fetchFile).not.toHaveBeenCalled();
  });
});
