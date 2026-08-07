/**
 * QuickStartPane — the body of the pinned "Quick Start" home tab (task 011 §5).
 *
 * "Do something" action cards (wizards/actions) rendered with the SHARED library
 * primitives — `SectionPanel` + `ActionCardRow`/`ActionCard` from
 * `@spaarke/ui-components` (CLAUDE.md §11 default-to-reuse; the same "Get Started"
 * action-card pattern as corporate-legal-home). A "More Services" card opens the
 * SHARED `ChoiceModal` (the canonical SprkModal preset, ADR-050) — no hand-rolled
 * dialog.
 *
 * PLACEHOLDER SCOPE (task 011): the card set here is a representative, non-functional
 * preview so the pinned Quick Start chassis is demonstrable. The role-defaulted card
 * set + real wizard launches (NDA Assessment, Submit Policy Question, …) are wired by
 * task 012 (widget registry + role defaults) and the P3 Front Door tasks. All cards
 * currently open the "More Services" ChoiceModal, which lists services as forthcoming.
 *
 * Fluent v9 semantic tokens only — zero hardcoded hex (ADR-021).
 */
import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import {
  ShieldTaskRegular,
  QuestionCircleRegular,
  LightbulbRegular,
  TagRegular,
  AppsListRegular,
} from '@fluentui/react-icons';
// SHARED library — the canonical Quick Start / action-card primitives (§11).
import { SectionPanel } from '@spaarke/ui-components/components/WorkspaceShell/SectionPanel';
import { ActionCardRow } from '@spaarke/ui-components/components/WorkspaceShell/ActionCardRow';
import type { ActionCardConfig } from '@spaarke/ui-components/components/WorkspaceShell/types';
// SHARED library — the canonical modal system (ADR-050); ChoiceModal preset for "More Services".
import { ChoiceModal } from '@spaarke/ui-components/components/SprkModal';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  lead: { color: tokens.colorNeutralForeground2 },
});

/** Representative Quick Start actions (placeholder set — real role-defaults are task 012 / P3). */
const QUICK_START_CARDS: ActionCardConfig[] = [
  {
    id: 'nda-assessment',
    label: 'NDA Assessment',
    icon: ShieldTaskRegular,
    ariaLabel: 'Start an NDA assessment',
  },
  {
    id: 'policy-question',
    label: 'Submit Policy Question',
    icon: QuestionCircleRegular,
    ariaLabel: 'Submit a policy question to the law department',
  },
  {
    id: 'invention-submission',
    label: 'Invention Submission',
    icon: LightbulbRegular,
    ariaLabel: 'Submit an invention disclosure',
  },
  {
    id: 'trademark-search',
    label: 'Trademark Search Request',
    icon: TagRegular,
    ariaLabel: 'Request a trademark search',
  },
  {
    id: 'more-services',
    label: 'More Services',
    icon: AppsListRegular,
    ariaLabel: 'Browse all legal services',
  },
];

export const QuickStartPane: React.FC = () => {
  const s = useStyles();
  const [servicesOpen, setServicesOpen] = React.useState(false);
  const openServices = React.useCallback(() => setServicesOpen(true), []);

  // Task-011 placeholder: every card routes to the "More Services" ChoiceModal, which
  // presents the services as forthcoming. Task 012 / P3 replace these with real launches.
  const onCardClick = React.useMemo<Record<string, () => void>>(
    () => Object.fromEntries(QUICK_START_CARDS.map((c) => [c.id, openServices])),
    [openServices],
  );

  return (
    <div className={s.root}>
      <SectionPanel title="Quick Start">
        <Text className={s.lead} block>
          Start a request or find an answer. Pick an action to begin.
        </Text>
        <ActionCardRow cards={QUICK_START_CARDS} onCardClick={onCardClick} />
      </SectionPanel>

      <ChoiceModal
        open={servicesOpen}
        onClose={() => setServicesOpen(false)}
        title="Legal services"
        message="These services arrive in an upcoming release. This is a preview of the Legal Front Door."
        choices={[
          {
            id: 'nda',
            label: 'NDA Assessment',
            description: 'Upload an NDA for automated triage and next-step routing.',
            icon: <ShieldTaskRegular />,
          },
          {
            id: 'policy',
            label: 'Submit a Policy Question',
            description: 'Ask the law department a Policy & Procedures question for an official answer.',
            icon: <QuestionCircleRegular />,
          },
          {
            id: 'more',
            label: 'More coming soon',
            description: 'Invention disclosures, trademark search, and other intake types.',
            icon: <AppsListRegular />,
          },
        ]}
        onSelect={() => setServicesOpen(false)}
      />
    </div>
  );
};

export default QuickStartPane;
