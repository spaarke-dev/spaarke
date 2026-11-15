import '@testing-library/jest-dom';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewPage } from '../components/InvoiceReviewPage';
import { PROCEDURE_CHANGE_FALLBACK, procedureChangeView } from '../components/ProcedureChangeNotice';
import type { ProcedureChange } from '../services/invoiceReviewApi';
import { INVOICE_ID, line, mockApi, review, type MockApi } from './testKit';

// ebilling-r1 task 113b: the Invoice Review page shows the server's Procedure-change banner (task 113).

const CHANGED_TEXT = 'The procedures that apply to this invoice have changed; re-check to apply them.';

function renderPage(api: MockApi, theme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <InvoiceReviewPage invoiceId={INVOICE_ID} api={api as never} pollIntervalMs={60_000} />
    </FluentProvider>
  );
}

async function renderWith(procedureChange: ProcedureChange | null | undefined, theme = webLightTheme) {
  const api = mockApi(review([line(1), line(2)], procedureChange === undefined ? {} : { procedureChange }));
  renderPage(api, theme);
  await screen.findByTestId('review-title');
  return api;
}

describe('InvoiceReviewPage: Procedure-change banner (113b)', () => {
  it('must-fire: Changed shows the server sentence as a warning', async () => {
    await renderWith({ state: 'Changed', text: CHANGED_TEXT });
    const notice = screen.getByTestId('procedure-change-notice');
    expect(notice).toHaveTextContent(CHANGED_TEXT);
    expect(notice).toHaveAttribute('data-state', 'Changed');
    expect(procedureChangeView({ state: 'Changed', text: CHANGED_TEXT })?.intent).toBe('warning');
  });

  it('must-fire: CouldNotBeChecked shows as a warning, never as no change', async () => {
    await renderWith({ state: 'CouldNotBeChecked', text: 'Procedure changes could not be checked' });
    expect(screen.getByTestId('procedure-change-notice')).toHaveTextContent('Procedure changes could not be checked');
    expect(procedureChangeView({ state: 'CouldNotBeChecked' })?.intent).toBe('warning');
  });

  it('must-fire: NotYetChecked shows as information, titled so it reads as a sentence', async () => {
    await renderWith({ state: 'NotYetChecked', text: 'Not yet checked' });
    const notice = screen.getByTestId('procedure-change-notice');
    expect(notice).toHaveTextContent('Procedure changes');
    expect(notice).toHaveTextContent('Not yet checked');
    expect(procedureChangeView({ state: 'NotYetChecked' })?.intent).toBe('info');
  });

  it.each([
    ['Changed', PROCEDURE_CHANGE_FALLBACK.Changed],
    ['CouldNotBeChecked', PROCEDURE_CHANGE_FALLBACK.CouldNotBeChecked],
    ['NotYetChecked', PROCEDURE_CHANGE_FALLBACK.NotYetChecked],
  ])('a %s that arrives without its text uses the server sentence', async (state, expected) => {
    await renderWith({ state });
    expect(screen.getByTestId('procedure-change-notice')).toHaveTextContent(expected);
  });

  it('a blank text also falls back to the server sentence', () => {
    expect(procedureChangeView({ state: 'Changed', text: '   ' })?.text).toBe(PROCEDURE_CHANGE_FALLBACK.Changed);
  });

  it.each<[string, ProcedureChange | null | undefined]>([
    ['absent (the server did not judge it)', undefined],
    ['null', null],
    ['NotChanged', { state: 'NotChanged' }],
    ['a state this page does not know, with no text', { state: 'SomethingNew' }],
  ])('must-not-fire: %s shows no banner', async (_label, change) => {
    await renderWith(change);
    expect(screen.queryByTestId('procedure-change-notice')).not.toBeInTheDocument();
  });

  it('must-fire: a state this page does not know, with text, shows that text as information (never read as no change)', async () => {
    await renderWith({ state: 'SomethingNew', text: 'Some new text' });
    const notice = screen.getByTestId('procedure-change-notice');
    expect(notice).toHaveTextContent('Some new text');
    expect(procedureChangeView({ state: 'SomethingNew', text: 'Some new text' })?.intent).toBe('info');
  });

  it('must-not-fire: the banner blocks nothing; Accept all clean and the corrections stay enabled', async () => {
    await renderWith({ state: 'Changed', text: CHANGED_TEXT });
    expect(screen.getByTestId('correct-header')).toBeEnabled();
    expect(screen.getByTestId('accept-clean')).toBeEnabled();
    expect(screen.getByTestId('add-line')).toBeEnabled();
  });

  it('renders in the dark theme too', async () => {
    await renderWith({ state: 'Changed', text: CHANGED_TEXT }, webDarkTheme);
    expect(screen.getByTestId('procedure-change-notice')).toHaveTextContent(CHANGED_TEXT);
  });
});
