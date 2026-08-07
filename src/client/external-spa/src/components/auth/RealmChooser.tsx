/**
 * RealmChooser — the browser home-realm discovery gate (spec FR-03, ux-brief §3).
 *
 * Presents the explicit "My organization / Partner" choice that selects the sign-in PLANE before any
 * authority is contacted. Shown ONLY in a browser and ONLY when the tab has no stored realm yet; it
 * NEVER appears inside Teams (the Teams host does silent workforce SSO — criterion 2 / ADR-021
 * constraint). Once chosen, the plane is invisible everywhere else (ux-brief §4).
 *
 * Shared-library mandate (CLAUDE.md §11 + ADR-050): built on the canonical `ChoiceModal` preset —
 * the exact "force a conscious choice between 2-4 rich, described options" surface — rather than a
 * hand-rolled dialog. Fluent v9 semantic tokens only, so it renders correctly in light, dark, and
 * (defensively) Teams themes (ADR-021) with zero hardcoded hex.
 */
import * as React from 'react';
import { Building24Regular, PeopleTeam24Regular } from '@fluentui/react-icons';
import { ChoiceModal } from '@spaarke/ui-components/components/SprkModal';
import type { Realm } from '../../auth/realm';

export interface RealmChooserProps {
  /** Invoked with the plane the user picked; the bootstrap then signs in against that authority. */
  onChoose: (realm: Realm) => void;
}

export const RealmChooser: React.FC<RealmChooserProps> = ({ onChoose }) => {
  return (
    <ChoiceModal
      open
      // Mandatory sign-in gate: there is no surface behind it to dismiss TO, so Cancel/× is a
      // deliberate no-op — the user must pick a plane to proceed. `dismiss="explicit"` (ChoiceModal
      // default) already blocks ESC/backdrop dismissal, reinforcing the "conscious choice required"
      // model; this no-op simply neutralizes the always-present Cancel affordance for this one gate.
      onClose={() => {
        /* no dismiss target — see comment above */
      }}
      title="Sign in to Spaarke"
      message="Choose how you'd like to sign in. We'll take you to the right sign-in for your account."
      choices={[
        {
          id: 'workforce',
          label: 'My organization',
          description: 'Sign in with your work or school account (Microsoft Entra).',
          icon: <Building24Regular />,
        },
        {
          id: 'ciam',
          label: 'Partner',
          description: 'Sign in as an external collaborator with your Spaarke partner account.',
          icon: <PeopleTeam24Regular />,
        },
      ]}
      onSelect={(choiceId) => onChoose(choiceId as Realm)}
      cancelLabel="Cancel"
    />
  );
};

export default RealmChooser;
