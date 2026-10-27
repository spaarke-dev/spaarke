import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import * as api from '..';
import type { InvoiceAttachmentRole, MarkableAttachment } from '..';

const { InvoiceAttachmentMarking, defaultInvoiceMarking, validateInvoiceMarking } = api;

const pdf = (id: string, extra: Partial<MarkableAttachment> = {}): MarkableAttachment => ({
  id,
  fileName: `${id}.pdf`,
  contentType: 'application/pdf',
  ...extra,
});
const doc = (id: string, extra: Partial<MarkableAttachment> = {}): MarkableAttachment => ({
  id,
  fileName: `${id}.docx`,
  ...extra,
});

describe('validateInvoiceMarking', () => {
  const none: Record<string, InvoiceAttachmentRole> = { a: 'supporting' };
  it.each(['new', 'correction'] as const)('%s: needs exactly one invoice', mode => {
    expect(validateInvoiceMarking(none, mode)).toEqual({ ok: false, reason: 'no-invoice' });
    expect(validateInvoiceMarking({ a: 'invoice', b: 'invoice' }, mode)).toEqual({
      ok: false,
      reason: 'more-than-one-invoice',
    });
    expect(validateInvoiceMarking({ a: 'invoice', b: 'skip' }, mode)).toEqual({ ok: true });
  });
  it.each(['new', 'correction'] as const)('%s: non-PDF invoice is not allowed', mode => {
    expect(validateInvoiceMarking({ a: 'invoice' }, mode, [doc('a')])).toEqual({
      ok: false,
      reason: 'invoice-not-allowed',
    });
    expect(validateInvoiceMarking({ a: 'invoice' }, mode, [pdf('a')])).toEqual({ ok: true });
  });
  it('supporting-only: no invoice allowed, none is fine', () => {
    expect(validateInvoiceMarking({ a: 'invoice' }, 'supporting-only')).toEqual({
      ok: false,
      reason: 'invoice-not-allowed',
    });
    expect(validateInvoiceMarking(none, 'supporting-only')).toEqual({ ok: true });
    expect(validateInvoiceMarking({}, 'supporting-only')).toEqual({ ok: true });
  });
  it.each(['new', 'correction', 'supporting-only'] as const)(
    '%s: a disabled attachment may only be skip',
    mode => {
      const locked = pdf('x', { disabledReason: 'Over 20 MB' });
      const base: Record<string, InvoiceAttachmentRole> =
        mode === 'supporting-only' ? { a: 'supporting' } : { a: 'invoice' };
      expect(validateInvoiceMarking({ ...base, x: 'supporting' }, mode, [pdf('a'), locked])).toEqual({
        ok: false,
        reason: 'disabled-attachment-marked',
      });
      expect(validateInvoiceMarking({ ...base, x: 'skip' }, mode, [pdf('a'), locked])).toEqual({ ok: true });
    }
  );
});

describe('defaultInvoiceMarking', () => {
  it('pre-marks exactly one suggestedInvoice, others supporting', () => {
    expect(defaultInvoiceMarking([pdf('a'), pdf('b', { suggestedInvoice: true }), doc('c')], 'new')).toEqual({
      a: 'supporting',
      b: 'invoice',
      c: 'supporting',
    });
  });
  it('with none suggested, pre-marks the only PDF', () => {
    expect(defaultInvoiceMarking([doc('a'), pdf('b')], 'correction')).toEqual({ a: 'supporting', b: 'invoice' });
  });
  it('detects PDF by file name when no content type', () => {
    expect(defaultInvoiceMarking([{ id: 'a', fileName: 'Inv.PDF' }], 'new')).toEqual({ a: 'invoice' });
  });
  it('two candidates pre-mark none', () => {
    expect(defaultInvoiceMarking([pdf('a'), pdf('b')], 'new')).toEqual({ a: 'supporting', b: 'supporting' });
    expect(
      defaultInvoiceMarking([pdf('a', { suggestedInvoice: true }), pdf('b', { suggestedInvoice: true })], 'new')
    ).toEqual({ a: 'supporting', b: 'supporting' });
  });
  it('a non-PDF suggestion is not a candidate', () => {
    expect(defaultInvoiceMarking([doc('a', { suggestedInvoice: true }), pdf('b')], 'new')).toEqual({
      a: 'supporting',
      b: 'invoice',
    });
  });
  it('disabledReason attachment defaults to skip and is never the invoice', () => {
    expect(defaultInvoiceMarking([pdf('a', { disabledReason: 'Too large' }), pdf('b')], 'new')).toEqual({
      a: 'skip',
      b: 'invoice',
    });
  });
  it('supporting-only pre-marks no invoice', () => {
    expect(defaultInvoiceMarking([pdf('a', { suggestedInvoice: true })], 'supporting-only')).toEqual({
      a: 'supporting',
    });
  });
  it('empty list gives empty map', () => {
    expect(defaultInvoiceMarking([], 'new')).toEqual({});
  });
});

describe('InvoiceAttachmentMarking', () => {
  const setup = (
    attachments: MarkableAttachment[],
    value: Record<string, InvoiceAttachmentRole>,
    mode: 'new' | 'correction' | 'supporting-only' = 'new',
    disabled?: boolean,
    theme = webLightTheme
  ) => {
    const onChange = jest.fn();
    render(
      <FluentProvider theme={theme}>
        <InvoiceAttachmentMarking
          attachments={attachments}
          value={value}
          onChange={onChange}
          mode={mode}
          disabled={disabled}
        />
      </FluentProvider>
    );
    return onChange;
  };

  it('calls onChange with the full next map', () => {
    const onChange = setup([pdf('a'), pdf('b')], { a: 'supporting', b: 'supporting' });
    fireEvent.click(screen.getAllByLabelText('Invoice')[1]);
    expect(onChange).toHaveBeenCalledWith({ a: 'supporting', b: 'invoice' });
    fireEvent.click(screen.getAllByLabelText("Don't save")[0]);
    expect(onChange).toHaveBeenLastCalledWith({ a: 'skip', b: 'supporting' });
  });

  it('offers no Invoice option in supporting-only mode', () => {
    setup([pdf('a')], { a: 'supporting' }, 'supporting-only');
    expect(screen.queryByLabelText('Invoice')).toBeNull();
    expect(screen.getByLabelText('Supporting')).toBeTruthy();
  });

  it('offers no Invoice option for a non-PDF', () => {
    setup([doc('a'), pdf('b')], { a: 'supporting', b: 'supporting' });
    expect(screen.getAllByLabelText('Invoice')).toHaveLength(1);
  });

  it('shows the reason and locks a disabledReason attachment', () => {
    const onChange = setup([pdf('a', { disabledReason: 'Over the size limit' })], { a: 'supporting' });
    expect(screen.getByText('Over the size limit')).toBeTruthy();
    expect(screen.queryByLabelText('Invoice')).toBeNull();
    const skip = screen.getByLabelText("Don't save") as HTMLInputElement;
    expect(skip.checked).toBe(true);
    expect((screen.getByLabelText('Supporting') as HTMLInputElement).disabled).toBe(true);
    fireEvent.click(screen.getByLabelText('Supporting'));
    expect(onChange).not.toHaveBeenCalled();
  });

  it('disabled disables every choice', () => {
    const onChange = setup([pdf('a'), doc('b')], { a: 'invoice', b: 'supporting' }, 'new', true);
    const radios = screen.getAllByRole('radio') as HTMLInputElement[];
    expect(radios.length).toBe(5);
    radios.forEach(r => expect(r.disabled).toBe(true));
    fireEvent.click(radios[1]);
    expect(onChange).not.toHaveBeenCalled();
  });

  it('renders in dark mode', () => {
    setup([pdf('a')], { a: 'invoice' }, 'new', false, webDarkTheme);
    expect((screen.getByLabelText('Invoice') as HTMLInputElement).checked).toBe(true);
  });
});

describe('subpath and isolation', () => {
  const pkgRoot = path.resolve(__dirname, '../../../..');
  const folder = path.resolve(__dirname, '..');

  it('package.json exports the file subpath and it resolves to the exact agreed surface', () => {
    const pkg = JSON.parse(fs.readFileSync(path.join(pkgRoot, 'package.json'), 'utf8'));
    const target = pkg.exports['./invoice-attachment-marking'];
    expect(target).toBe('./src/components/InvoiceAttachmentMarking/index.ts');
    expect(fs.existsSync(path.join(pkgRoot, target))).toBe(true);
    expect(Object.keys(api).sort()).toEqual(
      ['InvoiceAttachmentMarking', 'defaultInvoiceMarking', 'validateInvoiceMarking'].sort()
    );
  });

  it('no file in the folder imports the barrel, Xrm, Office, fetch or a service module', () => {
    const files = fs.readdirSync(folder).filter(f => /\.tsx?$/.test(f));
    expect(files.length).toBeGreaterThanOrEqual(3);
    for (const f of files) {
      const src = fs.readFileSync(path.join(folder, f), 'utf8');
      const imports = [...src.matchAll(/(?:from|import)\s+['"]([^'"]+)['"]/g)].map(m => m[1]);
      for (const spec of imports) {
        const ok = spec === 'react' || spec === '@fluentui/react-components' || spec.startsWith('./');
        expect({ f, spec, ok }).toEqual({ f, spec, ok: true });
      }
      expect(src).not.toMatch(/\bXrm\b|\bOffice\.|\bfetch\s*\(|authenticatedFetch|React\.useId/);
    }
  });
});
