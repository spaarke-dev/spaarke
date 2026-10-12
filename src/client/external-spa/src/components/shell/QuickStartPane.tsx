/**
 * QuickStartPane — the body of the pinned "Quick Start" home tab (task 012 §4).
 *
 * Role-relevant "do something" action cards (wizards/actions), gated per `/me` — an action the
 * caller may not start never renders as a card. Every card here starts a Legal Front Door intake
 * (a `sprk_requesttype`), so its visibility is `isIntakeTypeAvailable` (`intake/intakeRegistry.ts`):
 * the `legal-front-door` entitlement plus the server's `intakeRequestTypes`, NOT the plane (review
 * 011 K3 - a CIAM partner sees the cards when the partner switch gives it `legal-front-door`).
 *
 * Launch (r3 task 052): a card whose request type has a registered intake wizard
 * (`INTAKE_WIZARDS`: NDA Assessment, Policy Question) opens that wizard in `WizardShell`; the
 * More Services entry for it does the same. A card with no wizard yet but a registered widget
 * (Invention Submission → `inventions`) opens that widget's tab via `onOpenWidget`. A card with
 * neither (Trademark Search Request - task 053) falls through to the "More Services" modal, which
 * lists every available action with a "Coming soon" badge for the ones not yet wired.
 *
 * Rendered with the SHARED library primitives — `SectionPanel` + `ActionCardRow`/`ActionCard`
 * from `@spaarke/ui-components` (CLAUDE.md §11) — and the SHARED `FormModal` (SprkModal preset,
 * ADR-050) for "More Services". No hand-rolled dialog.
 *
 * Fluent v9 semantic tokens only — zero hardcoded hex (ADR-021).
 */
import * as React from 'react';
import { makeStyles, tokens, Text, Badge } from '@fluentui/react-components';
import {
  ShieldTaskRegular,
  QuestionCircleRegular,
  LightbulbRegular,
  TagRegular,
  AppsListRegular,
  type FluentIcon,
} from '@fluentui/react-icons';
// SHARED library — the canonical Quick Start / action-card primitives (§11).
import { SectionPanel } from '@spaarke/ui-components/components/WorkspaceShell/SectionPanel';
import { ActionCardRow } from '@spaarke/ui-components/components/WorkspaceShell/ActionCardRow';
import { ActionCard } from '@spaarke/ui-components/components/WorkspaceShell/ActionCard';
import type { ActionCardConfig } from '@spaarke/ui-components/components/WorkspaceShell/types';
// SHARED library — the canonical modal system (ADR-050); FormModal preset for "More Services"
// (a potentially >4-item catalog — outside ChoiceModal's 2-4-choice scope per ADR-050).
import { FormModal } from '@spaarke/ui-components/components/SprkModal';
import type { MeEntitlementsResponse } from '../../api/me-client';
import { INTAKE_REQUEST_TYPE } from '../../api/intake-client';
import { getIntakeWizard, isIntakeTypeAvailable } from '../../intake/intakeRegistry';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  lead: { color: tokens.colorNeutralForeground2 },
  intro: { color: tokens.colorNeutralForeground3, marginBottom: tokens.spacingVerticalS },
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(150px, 1fr))',
    gap: tokens.spacingHorizontalM,
  },
  cell: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  caption: { color: tokens.colorNeutralForeground3, textAlign: 'center' },
  badgeRow: { display: 'flex', justifyContent: 'center' },
});

interface QuickStartActionDef {
  id: string;
  label: string;
  icon: FluentIcon;
  ariaLabel: string;
  description: string;
  /**
   * The `sprk_requesttype` this action starts. Visibility is `isIntakeTypeAvailable(me, requestType)`; the wizard (if
   * any) is `getIntakeWizard(requestType)`.
   */
  requestType: number;
  /** A registered widget this action opens when it has no wizard. Omit = not yet wired ("coming soon"). */
  opensWidgetId?: string;
}

const MORE_SERVICES_ID = 'more-services';

/**
 * Quick Start action catalog. NDA Assessment and Policy Question have intake wizards (`INTAKE_WIZARDS`); Invention
 * Submission still opens its placeholder widget and Trademark Search is a "coming soon" entry in the More Services modal
 * only - both become wizards in task 053 (append them to the intake registry; no change needed here).
 */
const QUICK_START_ACTIONS: QuickStartActionDef[] = [
  {
    id: 'nda-assessment',
    label: 'NDA Assessment',
    icon: ShieldTaskRegular,
    ariaLabel: 'Start an NDA assessment',
    description: 'Ask Legal to review an NDA or issue our standard one.',
    requestType: INTAKE_REQUEST_TYPE.ndaAssessment,
  },
  {
    id: 'policy-question',
    label: 'Submit Policy Question',
    icon: QuestionCircleRegular,
    ariaLabel: 'Submit a policy question to the law department',
    description: 'Get an official human answer from the legal team.',
    requestType: INTAKE_REQUEST_TYPE.policyQuestion,
  },
  {
    id: 'invention-submission',
    label: 'Invention Submission',
    icon: LightbulbRegular,
    ariaLabel: 'Submit an invention disclosure',
    description: 'File an invention disclosure for patent review.',
    requestType: INTAKE_REQUEST_TYPE.inventionSubmission,
    opensWidgetId: 'inventions',
  },
  {
    id: 'trademark-search',
    label: 'Trademark Search Request',
    icon: TagRegular,
    ariaLabel: 'Request a trademark clearance search',
    description: 'Ask the legal team to clear a proposed mark.',
    requestType: INTAKE_REQUEST_TYPE.trademarkSearch,
    // No wizard and no widget yet (task 053); "coming soon" in More Services only.
  },
];

/** The widget the wizards' success screens link to. */
const SERVICE_REQUESTS_WIDGET_ID = 'service-requests';

export interface QuickStartPaneProps {
  me: MeEntitlementsResponse;
  /** Open (or focus) a widget tab by id — the caller (WorkspaceHomePage) re-checks entitlement. */
  onOpenWidget: (widgetId: string) => void;
}

export const QuickStartPane: React.FC<QuickStartPaneProps> = ({ me, onOpenWidget }) => {
  const s = useStyles();
  const [moreOpen, setMoreOpen] = React.useState(false);
  // The request type of the intake wizard that is open (mounted only while open, so each launch starts fresh).
  const [wizardType, setWizardType] = React.useState<number | null>(null);

  // Entitled = the caller may start this intake (legal-front-door + the server's intakeRequestTypes; not the plane).
  const entitledActions = React.useMemo(
    () => QUICK_START_ACTIONS.filter(a => isIntakeTypeAvailable(me, a.requestType)),
    [me]
  );

  /** An action is launchable when it has a wizard or a widget; otherwise it is "coming soon". */
  const isLaunchable = (action: QuickStartActionDef): boolean =>
    getIntakeWizard(action.requestType) !== undefined || action.opensWidgetId !== undefined;

  const launch = React.useCallback(
    (action: QuickStartActionDef) => {
      if (getIntakeWizard(action.requestType)) {
        setWizardType(action.requestType);
      } else if (action.opensWidgetId) {
        onOpenWidget(action.opensWidgetId);
      } else {
        setMoreOpen(true);
      }
    },
    [onOpenWidget]
  );

  const ActiveWizard = wizardType === null ? undefined : getIntakeWizard(wizardType)?.Wizard;
  const openServiceRequests = React.useCallback(() => onOpenWidget(SERVICE_REQUESTS_WIDGET_ID), [onOpenWidget]);

  const rowCards: ActionCardConfig[] = React.useMemo(() => {
    const live: ActionCardConfig[] = entitledActions.map(a => ({
      id: a.id,
      label: a.label,
      icon: a.icon,
      ariaLabel: a.ariaLabel,
    }));
    live.push({
      id: MORE_SERVICES_ID,
      label: 'More Services',
      icon: AppsListRegular,
      ariaLabel: 'Browse all legal services',
    });
    return live;
  }, [entitledActions]);

  const onCardClick = React.useMemo<Record<string, () => void>>(() => {
    const map: Record<string, () => void> = { [MORE_SERVICES_ID]: () => setMoreOpen(true) };
    for (const a of entitledActions) map[a.id] = () => launch(a);
    return map;
  }, [entitledActions, launch]);

  return (
    <div className={s.root}>
      <SectionPanel title="Quick Start">
        <Text className={s.lead} block>
          Start a request or find an answer. Pick an action to begin.
        </Text>
        <ActionCardRow cards={rowCards} onCardClick={onCardClick} />
      </SectionPanel>

      <FormModal
        open={moreOpen}
        onClose={() => setMoreOpen(false)}
        onSubmit={() => setMoreOpen(false)}
        title="Legal services"
        submitLabel="Done"
        cancelLabel="Close"
      >
        <Text className={s.intro}>
          Everything you can start from here. You only see services you&apos;re entitled to.
        </Text>
        <div className={s.grid}>
          {entitledActions.map(a => (
            <div key={a.id} className={s.cell}>
              <ActionCard
                icon={a.icon}
                label={a.label}
                ariaLabel={a.ariaLabel}
                disabled={!isLaunchable(a)}
                onClick={
                  isLaunchable(a)
                    ? () => {
                        setMoreOpen(false);
                        launch(a);
                      }
                    : undefined
                }
              />
              {isLaunchable(a) ? (
                <Text size={100} className={s.caption}>
                  {a.description}
                </Text>
              ) : (
                <div className={s.badgeRow}>
                  <Badge appearance="tint" color="informative">
                    Coming soon
                  </Badge>
                </div>
              )}
            </div>
          ))}
          {entitledActions.length === 0 && (
            <Text className={s.caption}>More services arrive in an upcoming release.</Text>
          )}
        </div>
      </FormModal>

      {ActiveWizard && (
        <ActiveWizard open onClose={() => setWizardType(null)} onOpenServiceRequests={openServiceRequests} />
      )}
    </div>
  );
};

export default QuickStartPane;
