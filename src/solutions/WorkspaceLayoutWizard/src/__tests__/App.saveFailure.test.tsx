/**
 * The wizard's save-failure path, driven through the real `App` (not the `layoutSaveError` helper alone).
 *
 * `layoutSaveError.test.ts` pins the helper's status -> message mapping, but nothing there proves
 * `handleFinish`'s catch block still calls it and that WizardShell shows the result. If the catch were
 * changed to swallow the error (or to rethrow the raw ApiError), every helper test would stay green while
 * the user saw "Workspace Saved" or "HTTP 412". These tests click "Save Layout" with an
 * `authenticatedFetch` that THROWS the real `ApiError`, exactly as `@spaarke/auth` does for a non-OK
 * response, and assert the alert the user reads.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ApiError } from '@spaarke/auth';
import { App, LAYOUT_EDITED_ELSEWHERE_MESSAGE } from '../App';

/** A one-row, one-section layout, so saveAs lands on Arrange Sections with a prefilled, valid form. */
const SOURCE_SECTIONS_JSON = JSON.stringify({
  schemaVersion: 1,
  rows: [{ id: 'row-1', columns: '1fr', columnsSmall: '1fr', sections: ['documents'] }],
  scope: 'my',
});

function renderSaveAs(authenticatedFetch: jest.Mock) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <App
        mode="saveAs"
        layoutId={null}
        layoutTemplateId="single-column"
        sectionsJson={SOURCE_SECTIONS_JSON}
        sourceName="Quarterly"
        authenticatedFetch={authenticatedFetch as unknown as React.ComponentProps<typeof App>['authenticatedFetch']}
      />
    </FluentProvider>,
  );
}

let closeSpy: jest.Mock;

describe('WorkspaceLayoutWizard App - a failed save reaches the user', () => {
  beforeEach(() => {
    // A swallowed save error makes WizardShell close the dialog as if it succeeded: the code-page close path ends in
    // window.close(), which in jsdom tears the whole test environment down. Stub it and assert it is NOT reached.
    closeSpy = jest.fn();
    Object.defineProperty(window, 'close', { configurable: true, writable: true, value: closeSpy });
  });

  beforeAll(() => {
    // jsdom has no ResizeObserver; Fluent's focus/overflow machinery constructs one on mount.
    (window as unknown as { ResizeObserver: unknown }).ResizeObserver = class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    };
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('a thrown 412 shows the "edited in another tab" sentence in the wizard alert', async () => {
    const authenticatedFetch = jest.fn().mockRejectedValue(new ApiError('HTTP 412', 412, null));
    renderSaveAs(authenticatedFetch);

    fireEvent.click(await screen.findByRole('button', { name: 'Save Layout' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(LAYOUT_EDITED_ELSEWHERE_MESSAGE);
    // The save was attempted as a create (saveAs), and nothing reported success.
    expect(authenticatedFetch).toHaveBeenCalledWith(
      '/api/workspace/layouts',
      expect.objectContaining({ method: 'POST' }),
    );
    expect(screen.queryByText('Workspace Saved')).not.toBeInTheDocument();
    expect(closeSpy).not.toHaveBeenCalled();
  });

  it('a thrown 409 shows the workspace-limit message', async () => {
    const authenticatedFetch = jest.fn().mockRejectedValue(new ApiError('HTTP 409', 409, null));
    renderSaveAs(authenticatedFetch);

    fireEvent.click(await screen.findByRole('button', { name: 'Save Layout' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/Maximum 10 workspaces reached/));
    expect(screen.queryByText('Workspace Saved')).not.toBeInTheDocument();
    expect(closeSpy).not.toHaveBeenCalled();
  });
});
