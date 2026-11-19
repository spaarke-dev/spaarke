/**
 * DocumentEmailStep - the email step opens in rich text (task 197, owner dev recheck 2026-11-18).
 *
 * The step's default body is plain text ("The following documents have been uploaded..."). The composer opens in rich text,
 * so the step converts it with the shared `plainTextToHtml` and passes `initialBodyFormat="HTML"`: same text, line breaks
 * kept, `<` and `&` in a file or record name escaped once.
 *
 * Runs under this package's node jest config (CI: `client-tests.yml`, job `test`, package
 * `src/solutions/DocumentUploadWizard`): the step is rendered with `react-dom/server`, so no DOM is needed, and the
 * composer is a stub that records the props it was given. Moved from a never-run jsdom form in task 197 fix round 1.
 */
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const composerProps: { current: Record<string, unknown> | null } = { current: null };

jest.mock('@spaarke/ui-components/components/EmailComposer', () => ({
  EmailComposer: React.forwardRef((props: Record<string, unknown>, _ref: unknown) => {
    composerProps.current = props;
    return <div data-testid="email-composer-stub" />;
  }),
  createXrmEmailComposeHandlers: () => ({}),
  describeSendFailure: () => ({ title: 'x', message: 'x' }),
  // The real helper (React-free, no Xrm), not a stub, so the test sees the real conversion.
  plainTextToHtml: jest.requireActual('@spaarke/ui-components/components/EmailComposer/composeOnlyText').plainTextToHtml,
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
    renderToStaticMarkup(
      <DocumentEmailStep
        uploadedFileNames={['Fees <draft>.docx', 'A & B.pdf']}
        parentEntityName="Acme & Sons"
        parentEntityType="sprk_matter"
        parentEntityId="m-1"
        authenticatedFetch={authenticatedFetch as never}
        bffBaseUrl="https://bff.example.com"
      />
    );

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
