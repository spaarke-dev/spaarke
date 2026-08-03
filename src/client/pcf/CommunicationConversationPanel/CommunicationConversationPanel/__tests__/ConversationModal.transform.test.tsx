/**
 * ConversationModal transform-robust portal test (task 070 / FR-08).
 *
 * P5 replaced the hand-rolled `createPortal` + `position:fixed` overlay with
 * `<SprkModal size="md">`. The hand-roll existed to dodge a centering bug: a CSS
 * `transform` on an app-shell ancestor redefines the containing block for
 * `position:fixed`, top-anchoring a raw overlay inside the model-driven form.
 * This asserts the REPLACEMENT centers correctly — the Fluent Dialog portal
 * mounts OUTSIDE (above) the transformed subtree, so the transform cannot offset
 * it. Structural analogue of the P0 `SprkModal.test.tsx` transform test, in this
 * PCF's React-16 + `@testing-library/react` 12 environment.
 *
 * The heavy shared conversation widgets are stubbed — this is an ENVELOPE test
 * (the modal surface / portal), NOT a conversation-content test (those have their
 * own suites). `SprkModal` itself is the REAL shell, resolved via `pcfSafeShim`.
 */
import * as React from 'react';
import { render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { AuthenticatedFetchFn } from '@spaarke/ui-components';

// Stub the two-pane workspace + view so the test targets the SprkModal portal,
// not BFF-backed conversation content. `createXrmNavigationService` runs in a
// useMemo at mount, so it must be provided too. (require('react') inside keeps the
// factory clear of jest's out-of-scope-variable guard.)
jest.mock('@spaarke/ui-components', () => {
  const R = require('react');
  return {
    ConversationWorkspace: () => R.createElement('div', { 'data-testid': 'workspace-stub' }, 'workspace'),
    ConversationView: () => null,
    createXrmNavigationService: () => ({}),
  };
});

import { ConversationModal } from '../ConversationModal';

const noop = () => {};
const authFetch = jest.fn() as unknown as AuthenticatedFetchFn;

function renderModal() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <div style={{ transform: 'scale(0.9)' }} data-testid="xf">
        <ConversationModal
          open
          onClose={noop}
          entityType="sprk_matter"
          id="44444444-4444-4444-4444-444444444444"
          authenticatedFetch={authFetch}
          currentUserSystemUserId="11111111-1111-1111-1111-111111111111"
          threadNames={{}}
          onOpenRecord={noop}
        />
      </div>
    </FluentProvider>
  );
}

describe('ConversationModal — transform-robust portal (task 070 / FR-08)', () => {
  it('mounts the modal surface OUTSIDE a CSS-transformed ancestor', () => {
    const { container } = renderModal();
    const transformed = container.querySelector('[data-testid="xf"]') as HTMLElement;
    const dialog = screen.getByRole('dialog');
    // FR-08 invariant: the Fluent Dialog portal escapes the transformed subtree,
    // so a residual ancestor transform cannot offset the surface. This is the
    // exact failure the hand-rolled position:fixed overlay worked around — and the
    // reason P5 is a VALIDATION of the shell, not a rewrite.
    expect(transformed).not.toBeNull();
    expect(transformed).not.toContainElement(dialog);
  });

  it('renders the SprkModal chrome (Messages title + maximize/close) with the workspace in the body', () => {
    renderModal();
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Messages')).toBeInTheDocument();
    // Window controls composed inside the shell (ModalWindowControls) — the old
    // hand-rolled cluster is gone.
    expect(within(dialog).getByRole('button', { name: /maximize dialog/i })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /^close$/i })).toBeInTheDocument();
    // The workspace mounts inside the shell body (content-mount parity).
    expect(within(dialog).getByTestId('workspace-stub')).toBeInTheDocument();
  });
});
