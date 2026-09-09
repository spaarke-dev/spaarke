/**
 * SecureProjectSection.test.tsx — the Secure Project step's copy (task 068, spec FR-31).
 *
 * This suite exists because the step SHIPPED two false statements for months: it promised an
 * external portal that is retired and does not get activated, and it warned that the secure
 * designation could never be removed when a shipped endpoint removes it. Neither was caught by a
 * type, a build, or a review — copy is the one part of a component nothing mechanical checks.
 *
 * So the assertions here are deliberately about RENDERED TEXT, not props: the failure mode is a
 * sentence that renders and is untrue, and only the rendered output can catch that.
 *
 * Every positive assertion is traceable to `ProvisionProjectEndpoint.cs` /
 * `UnsecureProjectEndpoint.cs` as merged by task 061 — see the per-test notes.
 *
 * NOTE FOR THE FR-31 GREP GATE: the retired product name appears in THIS FILE, and only inside the
 * `.not.toContain(…)` assertions that exist to keep it out of the component. That is the gate, not a
 * violation of it. Every other file under `CreateProjectWizard/**` is clean; the retired copy is
 * preserved in `projects/unified-access-control-r2/notes/task-068-secure-step-copy.md`.
 */
import * as React from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { SecureProjectSection } from '../SecureProjectSection';

const noop = () => {};

/**
 * Everything the expanded panel renders, as one lower-cased blob for phrase checks.
 *
 * Asserts the DOM is actually populated first. Every check below this line is a NEGATIVE one
 * ("does not contain"), and a negative assertion over an empty string passes for the wrong
 * reason — a suite that renders nothing would report itself green while testing nothing at all.
 */
const panelText = () => {
  const text = document.body.textContent ?? '';
  expect(text).toContain('Secure Project');
  return text.toLowerCase();
};

describe('SecureProjectSection — copy matches shipped behaviour (FR-31)', () => {
  describe('the retired claims are gone', () => {
    it('never mentions the retired external portal product, toggled on or off', () => {
      const { rerender } = renderWithProviders(<SecureProjectSection isSecure={false} onSecureChange={noop} />);
      expect(panelText()).not.toContain('power pages');

      rerender(<SecureProjectSection isSecure onSecureChange={noop} />);
      expect(panelText()).not.toContain('power pages');
      // The claim was that a portal is "activated" for external users. Provisioning activates no
      // portal, so the concept must not reappear under a different name either. Only "portal" is
      // asserted, not "workspace" — "workspace" is a word Spaarke uses legitimately elsewhere, and
      // banning it would fail this suite on a rewrite that is perfectly truthful.
      expect(panelText()).not.toContain('portal');
    });

    it('carries no permanence warning about the designation', () => {
      renderWithProviders(<SecureProjectSection isSecure onSecureChange={noop} />);

      const text = panelText();
      expect(text).not.toContain('permanent');
      expect(text).not.toContain('cannot be removed');
      expect(text).not.toContain('irreversible');
    });

    it('does not claim a business unit or an account is created for the project', () => {
      // BFF task 021 stopped creating both. The BU is RESOLVED by name; no account exists at all.
      renderWithProviders(<SecureProjectSection isSecure onSecureChange={noop} />);

      const text = panelText();
      expect(text).not.toContain('business unit is created');
      expect(text).not.toContain('dedicated business unit');
      expect(text).not.toContain('external access account');
    });
  });

  describe('what it says instead', () => {
    it('states the share-only mechanism', () => {
      // Traceable to ProvisionProjectEndpoint.ShareToCreatorAndPrincipalsAsync: the owner team is
      // memberless, so the creator's explicit share is the only way in, and everyone else needs one.
      renderWithProviders(<SecureProjectSection isSecure onSecureChange={noop} />);

      expect(screen.getByText(/shared with you, and only with people you add/i)).toBeInTheDocument();
      expect(screen.getByText(/needs an explicit grant before they can see it/i)).toBeInTheDocument();
    });

    it('states that ownership grants nobody access', () => {
      // Traceable to design.md §5.1 + the endpoint's owner-team assignment (steps 2, 3, 5).
      // "by design" is asserted deliberately: emptiness of the owner team is an environment
      // invariant, not something provisioning checks, so the copy must not claim it as a fact
      // the code enforces.
      renderWithProviders(<SecureProjectSection isSecure onSecureChange={noop} />);

      expect(screen.getByText(/by design has no people in it/i)).toBeInTheDocument();
      expect(screen.getByText(/ownership therefore grants nobody access/i)).toBeInTheDocument();
    });

    it('states the project gets its own document container', () => {
      // Traceable to endpoint steps 6 and 7 (create container, record it on sprk_containerid).
      renderWithProviders(<SecureProjectSection isSecure onSecureChange={noop} />);

      expect(screen.getByText(/given its own document container/i)).toBeInTheDocument();
    });

    it('replaces the permanence warning with a reversibility note', () => {
      // Traceable to UnsecureProjectEndpoint: reassign owner → revoke shares → clear sprk_issecure.
      renderWithProviders(<SecureProjectSection isSecure onSecureChange={noop} />);

      expect(screen.getByText(/this can be reversed/i)).toBeInTheDocument();
      expect(screen.getByText(/returns the project to a named owner/i)).toBeInTheDocument();
    });

    it('attributes the reversal to an administrator, not to the person at the wizard', () => {
      // No client surface calls /unsecure-project yet. Copy that reads as self-service would be a
      // fresh instance of the exact defect FR-31 removed: a promise the code does not keep.
      renderWithProviders(<SecureProjectSection isSecure onSecureChange={noop} />);

      expect(screen.getByText(/an administrator can remove the secure designation later/i)).toBeInTheDocument();
    });
  });

  describe('the panel is gated on the toggle', () => {
    it('shows none of the provisioning copy until the toggle is on', () => {
      renderWithProviders(<SecureProjectSection isSecure={false} onSecureChange={noop} />);

      expect(screen.queryByText(/what happens when this project is created/i)).not.toBeInTheDocument();
      expect(screen.queryByText(/this can be undone later/i)).not.toBeInTheDocument();
    });

    it('reports the flip to the parent — the section owns no state of its own', () => {
      const onSecureChange = jest.fn();
      renderWithProviders(<SecureProjectSection isSecure={false} onSecureChange={onSecureChange} />);

      return userEvent.click(screen.getByRole('switch')).then(() => {
        expect(onSecureChange).toHaveBeenCalledWith(true);
      });
    });
  });

  describe('ADR-021 / accessibility', () => {
    it("names the switch with the word the user can SEE (WCAG 2.5.3 Label in Name)", () => {
      // The accessible name must CONTAIN the visible label, or a speech-input user saying the word
      // on screen gets no match. This suite previously pinned the opposite: a visible "Enabled"
      // next to an aria-label of "Mark this project as a Secure Project", sharing no words at all.
      const { rerender } = renderWithProviders(<SecureProjectSection isSecure={false} onSecureChange={noop} />);
      expect(screen.getByRole('switch', { name: /secure project/i })).toHaveAccessibleName(
        expect.stringContaining('Disabled')
      );

      rerender(<SecureProjectSection isSecure onSecureChange={noop} />);
      expect(screen.getByRole('switch', { name: /secure project/i })).toHaveAccessibleName(
        expect.stringContaining('Enabled')
      );
    });

    it('announces the panel it discloses', () => {
      // Flipping the switch mounts the provisioning copy. Without aria-expanded/aria-controls that
      // disclosure is silent, and the copy this task exists to correct would be corrected for
      // sighted users only.
      const { rerender } = renderWithProviders(<SecureProjectSection isSecure={false} onSecureChange={noop} />);
      const collapsed = screen.getByRole('switch');
      expect(collapsed).toHaveAttribute('aria-expanded', 'false');
      const panelId = collapsed.getAttribute('aria-controls');
      expect(panelId).toBeTruthy();
      expect(document.getElementById(panelId!)).toBeNull();

      rerender(<SecureProjectSection isSecure onSecureChange={noop} />);
      expect(screen.getByRole('switch')).toHaveAttribute('aria-expanded', 'true');
      // The referenced element must actually exist once expanded — a dangling aria-controls is
      // worse than none, because it promises a target assistive tech then cannot find.
      expect(document.getElementById(panelId!)).toBeInTheDocument();
    });
  });
});
