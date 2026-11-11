import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { InvoiceFilesPanel } from '../components/InvoiceFilesPanel';
import type { FileActionDeps, TabLike } from '../services/fileActions';
import type { ApiResult, InvoiceFile } from '../services/invoiceFilesApi';
import { DOC_ID, INVOICE_ID, PDF_ID, PENDING_ID, SUPPORT_ID, failed, fetched, file, ok, problem } from './testKit';

function files(): InvoiceFile[] {
  return [
    file(),
    file({ fileId: SUPPORT_ID, name: 'timesheet.txt', purpose: 'Supporting', contentType: 'text/plain', sizeBytes: 800, sourceEmail: null }),
    file({ fileId: PENDING_ID, name: 'scanning.pdf', purpose: 'Supporting', scanState: 'Pending', sourceEmail: null }),
    file({ fileId: 'c', name: 'ledger.xlsx', purpose: 'Supporting', contentType: 'application/vnd.ms-excel', sourceEmail: null }),
  ];
}

function deps(): { deps: FileActionDeps; tab: TabLike } {
  const tab: TabLike = { close: jest.fn(), navigate: jest.fn() };
  return {
    tab,
    deps: {
      api: { fetchFile: jest.fn(async () => ok(fetched())) },
      openTab: jest.fn(() => tab),
      createObjectUrl: jest.fn(() => 'blob:mock'),
      revokeObjectUrl: jest.fn(),
      saveBlob: jest.fn(),
      later: jest.fn(),
    },
  };
}

function renderPanel(
  listResult: ApiResult<InvoiceFile[]> | Promise<ApiResult<InvoiceFile[]>>,
  opts: { theme?: typeof webLightTheme; actionDeps?: FileActionDeps; openDocument?: jest.Mock } = {}
) {
  const listFiles = jest.fn(() => Promise.resolve(listResult));
  const d = opts.actionDeps ?? deps().deps;
  const openDocument = opts.openDocument ?? jest.fn();
  const view = render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <InvoiceFilesPanel invoiceId={INVOICE_ID} api={{ listFiles }} actionDeps={d} openDocument={openDocument} />
    </FluentProvider>
  );
  return { ...view, listFiles, openDocument, actionDeps: d };
}

describe('InvoiceFilesPanel states', () => {
  it('shows a spinner while the list loads, then the list', async () => {
    let release!: (r: ApiResult<InvoiceFile[]>) => void;
    const pending = new Promise<ApiResult<InvoiceFile[]>>(resolve => {
      release = resolve;
    });
    const { listFiles } = renderPanel(pending);

    expect(screen.getByTestId('invoicefiles-loading')).toBeInTheDocument();
    release(ok(files()));
    expect(await screen.findByTestId('invoicefiles-list')).toBeInTheDocument();
    expect(listFiles).toHaveBeenCalledWith(INVOICE_ID);
  });

  it('lists every file in the order given, with its type, size and status', async () => {
    renderPanel(ok(files()));
    await screen.findByTestId('invoicefiles-list');

    const rows = screen.getAllByTestId(/^invoicefile-row-/);
    expect(rows.map(r => r.getAttribute('data-testid'))).toEqual([
      `invoicefile-row-${PDF_ID}`,
      `invoicefile-row-${SUPPORT_ID}`,
      `invoicefile-row-${PENDING_ID}`,
      'invoicefile-row-c',
    ]);
    const first = within(rows[0]);
    expect(first.getByText('INV-792345.pdf')).toBeInTheDocument();
    expect(first.getByText('Invoice PDF')).toBeInTheDocument();
    expect(first.getByText('121 KB')).toBeInTheDocument();
    expect(first.getByText('Ready')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Supporting')).toBeInTheDocument();
  });

  it('an empty list says "No files"', async () => {
    renderPanel(ok([]));
    const empty = await screen.findByTestId('invoicefiles-empty');
    expect(empty).toHaveTextContent('No files');
  });

  it('a failed list shows its reason and Retry loads it again', async () => {
    const listFiles = jest
      .fn()
      .mockResolvedValueOnce(failed(problem(503, 'file_store_unavailable')))
      .mockResolvedValueOnce(ok(files()));
    render(
      <FluentProvider theme={webLightTheme}>
        <InvoiceFilesPanel invoiceId={INVOICE_ID} api={{ listFiles }} actionDeps={deps().deps} openDocument={jest.fn()} />
      </FluentProvider>
    );

    const error = await screen.findByTestId('invoicefiles-error');
    expect(error).toHaveTextContent(/temporarily unavailable/);
    fireEvent.click(within(error).getByRole('button', { name: 'Retry' }));

    expect(await screen.findByTestId('invoicefiles-list')).toBeInTheDocument();
    expect(listFiles).toHaveBeenCalledTimes(2);
  });

  it('the uniform 404 reads as no access, never as an empty list', async () => {
    renderPanel(failed(problem(404, null)));
    const error = await screen.findByTestId('invoicefiles-error');
    expect(error).toHaveTextContent(/may not have access/);
    expect(screen.queryByTestId('invoicefiles-empty')).not.toBeInTheDocument();
  });
});

describe('InvoiceFilesPanel actions', () => {
  it('Open reads the file from the content route and shows it in a new tab', async () => {
    const { actionDeps } = renderPanel(ok(files()));
    await screen.findByTestId('invoicefiles-list');

    fireEvent.click(screen.getByTestId(`invoicefile-open-${PDF_ID}`));

    await waitFor(() => expect(actionDeps.openTab).toHaveBeenCalled());
    await waitFor(() => expect(actionDeps.api.fetchFile).toHaveBeenCalledWith(PDF_ID));
    await waitFor(() => expect(actionDeps.createObjectUrl).toHaveBeenCalled());
  });

  it('Download reads the file from the content route and saves it under its name', async () => {
    const { actionDeps } = renderPanel(ok(files()));
    await screen.findByTestId('invoicefiles-list');

    fireEvent.click(screen.getByTestId(`invoicefile-download-${SUPPORT_ID}`));

    await waitFor(() => expect(actionDeps.api.fetchFile).toHaveBeenCalledWith(SUPPORT_ID));
    await waitFor(() => expect(actionDeps.saveBlob).toHaveBeenCalledWith(expect.any(Blob), 'timesheet.txt'));
  });

  it('a failed Open shows the reason above the list and keeps the list', async () => {
    const d = deps();
    (d.deps.api.fetchFile as jest.Mock).mockResolvedValue(failed(problem(404, null)));
    renderPanel(ok(files()), { actionDeps: d.deps });
    await screen.findByTestId('invoicefiles-list');

    fireEvent.click(screen.getByTestId(`invoicefile-open-${PDF_ID}`));

    expect(await screen.findByTestId('invoicefiles-action-error')).toHaveTextContent(/not available/);
    expect(screen.getAllByTestId(/^invoicefile-row-/)).toHaveLength(4);
    expect(d.tab.close).toHaveBeenCalled();
  });

  it('a file still in its scan shows "Scan in progress" and its buttons are disabled', async () => {
    renderPanel(ok(files()));
    await screen.findByTestId('invoicefiles-list');

    expect(screen.getByTestId(`invoicefile-pending-${PENDING_ID}`)).toHaveTextContent('Scan in progress');
    expect(screen.getByTestId(`invoicefile-open-${PENDING_ID}`)).toBeDisabled();
    expect(screen.getByTestId(`invoicefile-download-${PENDING_ID}`)).toBeDisabled();
    expect(screen.getByTestId(`invoicefile-open-${PDF_ID}`)).toBeEnabled();
  });

  it('a type a tab cannot show has Download only', async () => {
    renderPanel(ok(files()));
    await screen.findByTestId('invoicefiles-list');

    expect(screen.queryByTestId('invoicefile-open-c')).not.toBeInTheDocument();
    expect(screen.getByTestId('invoicefile-download-c')).toBeInTheDocument();
  });
});

describe('InvoiceFilesPanel source email', () => {
  it('the source email link opens that Document', async () => {
    const { openDocument } = renderPanel(ok(files()));
    await screen.findByTestId('invoicefiles-list');

    const link = screen.getByTestId(`invoicefile-source-${PDF_ID}`);
    expect(link).toHaveTextContent('Invoice 792345 from Acme.eml');
    fireEvent.click(link);

    expect(openDocument).toHaveBeenCalledWith(DOC_ID);
  });

  it('a file with no source email shows no link', async () => {
    renderPanel(ok(files()));
    await screen.findByTestId('invoicefiles-list');

    expect(screen.queryByTestId(`invoicefile-source-${SUPPORT_ID}`)).not.toBeInTheDocument();
  });
});

describe('InvoiceFilesPanel dark mode', () => {
  it.each([
    ['light', webLightTheme],
    ['dark', webDarkTheme],
  ])('renders the list in the %s theme', async (_name, theme) => {
    renderPanel(ok(files()), { theme });
    expect(await screen.findByTestId('invoicefiles-list')).toBeInTheDocument();
    expect(screen.getByText('INV-792345.pdf')).toBeInTheDocument();
  });

  it('the host FluentProvider decides the colours: the panel and the page carry no colour literal (control: the scan sees one)', () => {
    const colour = /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/;
    const read = (rel: string) => fs.readFileSync(path.join(__dirname, '..', rel), 'utf8');

    expect(colour.test(read('components/InvoiceFilesPanel.tsx'))).toBe(false);
    expect(colour.test(read('main.tsx'))).toBe(false);
    expect(colour.test('color: #fff;')).toBe(true);
    expect(colour.test('background: rgb(0, 0, 0)')).toBe(true);
    expect(colour.test('color: tokens.colorNeutralForeground1')).toBe(false);
  });
});
