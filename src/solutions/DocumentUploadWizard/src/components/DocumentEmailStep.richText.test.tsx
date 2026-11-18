/**
 * DocumentEmailStep - the email step opens in rich text (task 197, owner dev recheck 2026-11-18).
 *
 * The step's default body is plain text ("The following documents have been uploaded..."). The composer opens in rich text,
 * so the step converts it with the shared `plainTextToHtml` and passes `initialBodyFormat="HTML"`: same text, line breaks
 * kept, `<` and `&` in a file or record name escaped once.
 *
 * NOTE - DocumentUploadWizard's jest config is node-only and matches `*.test.ts` (see jest.config.js), so this DOM test is
 * authored against the standard jest + @testing-library/react API like LegalWorkspace's `FilePreviewDialog.emailHandlers`
 * test, and runs under a jsdom config (task 197 ran it under a throwaway one).
 */
import * as React from 'react';
import { act } from 'react';
import { createRoot } from 'react-dom/client';

// React's act() needs this flag outside a test renderer.
(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const composerProps: { current: Record<string, unknown> | null } = { current: null };

jest.mock('@spaarke/ui-components/components/EmailComposer', () => ({
  EmailComposer: React.forwardRef((props: Record<string, unknown>, _ref: unknown) => {
    composerProps.current = props;
    return <div data-testid="email-composer-stub" />;
  }),
  createXrmEmailComposeHandlers: () => ({}),
  describeSendFailure: () => ({ title: 'x', message: 'x' }),
  // The real helper (React-free, no Xrm), not a stub, so the test sees the real conversion.
  plainTextToHtml: jest.requireActual(
    '../../../../client/shared/Spaarke.UI.Components/src/components/EmailComposer/composeOnlyText'
  ).plainTextToHtml,
}));
jest.mock('@spaarke/ui-components/utils/xrmContext', () => ({ getXrm: jest.fn(() => undefined) }));

// Import AFTER the mocks.
import { DocumentEmailStep } from './DocumentEmailStep';

const authenticatedFetch = jest.fn();

describe('DocumentEmailStep opens the email in rich text (task 197)', () => {
  beforeEach(() => {
    composerProps.current = null;
  });

  it('passes the plain-text default as HTML with the HTML format: text and line breaks kept, names escaped once', () => {
    const host = document.createElement('div');
    act(() => {
      createRoot(host).render(
        <DocumentEmailStep
          uploadedFileNames={['Fees <draft>.docx', 'A & B.pdf']}
          parentEntityName="Acme & Sons"
          parentEntityType="sprk_matter"
          parentEntityId="m-1"
          authenticatedFetch={authenticatedFetch as never}
          bffBaseUrl="https://bff.example.com"
        />
      );
    });

    expect(composerProps.current?.initialBodyFormat).toBe('HTML');
    const body = composerProps.current?.initialBody as string;
    expect(
      body.startsWith('<p>The following documents have been uploaded to Matter &quot;Acme &amp; Sons&quot;:</p>')
    ).toBe(true);
    expect(body).toContain('<br>');
    expect(body).toContain('Fees &lt;draft&gt;.docx');
    expect(body).toContain('A &amp; B.pdf');
    expect(body).toContain('<p>Please review the uploaded documents at your earliest convenience.</p>');
    expect(body).not.toContain('<draft>');
    expect(body).not.toContain('&amp;amp;');
  });
});
