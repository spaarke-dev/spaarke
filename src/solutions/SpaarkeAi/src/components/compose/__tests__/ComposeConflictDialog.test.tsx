/**
 * ComposeConflictDialog.test.tsx — task 051 unit tests (POML acceptance criteria).
 *
 * Verifies the dialog contracts from the POML + FR-16:
 *   1. Dialog renders with FR-16 verbatim button labels.
 *   2. Closed state — not in DOM when open=false.
 *   3. Force-close click → onForceCloseOtherSession callback fires.
 *   4. Go-to-other click → onGoToOtherSession callback fires.
 *   5. Cancel click → onCancel callback fires.
 *   6. Document display name renders in the dialog body.
 *   7. Conflicting-session-opened-at timestamp renders when provided.
 *   8. Dialog gracefully omits timestamp line when timestamp invalid.
 *
 * Test category per ADR-038: **Component Tests** — assert COMPONENT BEHAVIOUR
 * (rendering, button labels, callback invocation), NOT implementation details.
 * No internal-state assertions, no DI tests, no ctor null-checks per ADR-038
 * ban list. No `Mock<HttpMessageHandler>` (this is a frontend component).
 *
 * Mock boundary: the component has zero external dependencies beyond
 * `@fluentui/react-components`. No mocks needed.
 *
 * @see src/solutions/SpaarkeAi/src/components/compose/ComposeConflictDialog.tsx
 * @see projects/spaarkeai-compose-r1/tasks/051-spe-multi-tab-conflict-ux.poml
 * @see projects/spaarkeai-compose-r1/spec.md FR-16
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { ComposeConflictDialog } from '../ComposeConflictDialog';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const FIXED_DISPLAY_NAME = 'Contract Draft v3.docx';
// 2026-06-29T14:30:00Z — locale-formatted by the component.
const FIXED_OPENED_AT = '2026-06-29T14:30:00Z';

function renderDialog(
  overrides: Partial<{
    open: boolean;
    // Use a sentinel so the test can pass `documentDisplayName: undefined`
    // to override the default. Plain `?:` semantics make `undefined` and
    // "key not present" indistinguishable via `??`.
    documentDisplayName: string | undefined;
    conflictingSessionOpenedAt: string | null;
    onGoToOtherSession: jest.Mock;
    onForceCloseOtherSession: jest.Mock;
    onCancel: jest.Mock;
  }> = {},
): {
  goToOther: jest.Mock;
  forceClose: jest.Mock;
  cancel: jest.Mock;
} {
  const goToOther = overrides.onGoToOtherSession ?? jest.fn();
  const forceClose = overrides.onForceCloseOtherSession ?? jest.fn();
  const cancel = overrides.onCancel ?? jest.fn();

  const displayName =
    'documentDisplayName' in overrides
      ? overrides.documentDisplayName
      : FIXED_DISPLAY_NAME;

  render(
    <FluentProvider theme={webLightTheme}>
      <ComposeConflictDialog
        open={overrides.open ?? true}
        documentDisplayName={displayName}
        conflictingSessionOpenedAt={
          overrides.conflictingSessionOpenedAt === undefined
            ? FIXED_OPENED_AT
            : overrides.conflictingSessionOpenedAt
        }
        onGoToOtherSession={goToOther}
        onForceCloseOtherSession={forceClose}
        onCancel={cancel}
      />
    </FluentProvider>,
  );
  return { goToOther, forceClose, cancel };
}

// ---------------------------------------------------------------------------
// Tests — POML acceptance criteria + FR-16
// ---------------------------------------------------------------------------

describe('ComposeConflictDialog', () => {
  describe('Component Renders (FR-16 verbatim labels)', () => {
    it('renders dialog with title and document name when open', () => {
      renderDialog();

      // Title (DialogTitle is the accessible name of the dialog).
      expect(
        screen.getByRole('alertdialog', {
          name: /this document is open in another compose session/i,
        }),
      ).toBeInTheDocument();

      // Document display name appears in the body.
      expect(screen.getByText(FIXED_DISPLAY_NAME)).toBeInTheDocument();
    });

    it('renders "Force-close other session and open here" button (FR-16 verbatim)', () => {
      renderDialog();

      const button = screen.getByRole('button', {
        // FR-16 verbatim — exact text match
        name: 'Force-close other session and open here',
      });
      expect(button).toBeInTheDocument();
      expect(button).toBeEnabled();
    });

    it('renders "Go to that session" button (FR-16 verbatim)', () => {
      renderDialog();

      const button = screen.getByRole('button', {
        name: 'Go to that session',
      });
      expect(button).toBeInTheDocument();
      expect(button).toBeEnabled();
    });

    it('renders "Cancel — close this tab" button (third-option escape hatch)', () => {
      renderDialog();

      // Use a regex to be lenient on the em-dash character in the rendering.
      const button = screen.getByRole('button', {
        name: /cancel.*close this tab/i,
      });
      expect(button).toBeInTheDocument();
    });
  });

  describe('Closed state', () => {
    it('does NOT render the dialog when open=false', () => {
      renderDialog({ open: false });

      expect(
        screen.queryByRole('alertdialog', {
          name: /this document is open in another compose session/i,
        }),
      ).not.toBeInTheDocument();
      // Buttons should not be present either.
      expect(
        screen.queryByRole('button', {
          name: 'Force-close other session and open here',
        }),
      ).not.toBeInTheDocument();
    });
  });

  describe('Button click handlers', () => {
    it('invokes onForceCloseOtherSession when Force-close button clicked', async () => {
      const user = userEvent.setup();
      const { forceClose, goToOther, cancel } = renderDialog();

      await user.click(
        screen.getByRole('button', {
          name: 'Force-close other session and open here',
        }),
      );

      expect(forceClose).toHaveBeenCalledTimes(1);
      expect(goToOther).not.toHaveBeenCalled();
      expect(cancel).not.toHaveBeenCalled();
    });

    it('invokes onGoToOtherSession when Go-to-other button clicked', async () => {
      const user = userEvent.setup();
      const { goToOther, forceClose, cancel } = renderDialog();

      await user.click(
        screen.getByRole('button', { name: 'Go to that session' }),
      );

      expect(goToOther).toHaveBeenCalledTimes(1);
      expect(forceClose).not.toHaveBeenCalled();
      expect(cancel).not.toHaveBeenCalled();
    });

    it('invokes onCancel when Cancel button clicked', async () => {
      const user = userEvent.setup();
      const { cancel, forceClose, goToOther } = renderDialog();

      await user.click(
        screen.getByRole('button', { name: /cancel.*close this tab/i }),
      );

      expect(cancel).toHaveBeenCalledTimes(1);
      expect(forceClose).not.toHaveBeenCalled();
      expect(goToOther).not.toHaveBeenCalled();
    });
  });

  describe('Timestamp rendering', () => {
    it('renders the conflicting-session timestamp when provided as valid ISO', () => {
      renderDialog({ conflictingSessionOpenedAt: FIXED_OPENED_AT });

      const timestampEl = screen.getByTestId('compose-conflict-opened-at');
      // Locale formatting varies by jsdom locale; assert the label prefix is
      // present and the element contains some non-empty content after the
      // prefix.
      expect(timestampEl.textContent).toMatch(/other session opened:/i);
      expect(timestampEl.textContent).not.toBe('Other session opened: ');
    });

    it('omits the timestamp line when timestamp is null', () => {
      renderDialog({ conflictingSessionOpenedAt: null });

      expect(
        screen.queryByTestId('compose-conflict-opened-at'),
      ).not.toBeInTheDocument();
    });

    it('omits the timestamp line when timestamp is an invalid string', () => {
      renderDialog({ conflictingSessionOpenedAt: 'not-a-date' });

      expect(
        screen.queryByTestId('compose-conflict-opened-at'),
      ).not.toBeInTheDocument();
    });
  });

  describe('Optional document display name', () => {
    it('renders generic "this document" fallback when displayName undefined', () => {
      renderDialog({ documentDisplayName: undefined });

      // The dialog is open; the fixed display name should NOT appear at all.
      expect(screen.queryByText(FIXED_DISPLAY_NAME)).not.toBeInTheDocument();
      // The body paragraph (NOT the title) should mention "this document".
      // The title contains "This document is open in another Compose session"
      // and the body contains "You already have this document open in another
      // tab or window..." — we look specifically for the body phrase.
      expect(
        screen.getByText(
          /you already have\s+this document\s+open in another tab/i,
        ),
      ).toBeInTheDocument();
    });
  });
});
