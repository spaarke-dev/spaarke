/**
 * QuickStartPane — the body of the pinned "Quick Start" home tab (task 012 §4).
 *
 * Role-relevant "do something" action cards (wizards/actions), entitlement-gated per `/me` — an
 * action the caller is not entitled to never renders as a card, mirroring the widget registry's
 * Tier-1 gating. Cards that map to a registered widget (NDA Assessment → `nda`, Submit Policy
 * Question → `policy-library` per the "Submit Policy Question available inside the Policy
 * Library" UX refinement, Invention Submission → the existing `inventions` default widget) open
 * that widget's tab via `onOpenWidget`. Actions with no built wizard yet (Trademark Search
 * Request — P3 scope) fall through to the "More Services" modal, listing every entitled action
 * with a "Coming soon" badge for the ones not yet wired — real wizard launches are P3 (FR-23–27).
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
import type { MeEntitlementsResponse, Plane } from '../../api/me-client';
import { isEntitled } from '../../registry/widgetRegistry';

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
  /** Planes entitled to this action (hard gate, mirrors widgetRegistry's `planes`). */
  planes: Plane[];
  /** Module-level entitlement id required from `/me.entitlements`. Omit = entitled to all `planes`. */
  requiredEntitlement?: string;
  /** The registered widget this action opens. Omit = not yet wired (More-Services-only, "coming soon"). */
  opensWidgetId?: string;
}

const MORE_SERVICES_ID = 'more-services';

/**
 * Quick Start action catalog. NDA/Policy-Question/Invention map to registered widgets (see
 * widgetRegistry.ts); Trademark Search has no widget yet (P3) and renders as a "coming soon"
 * entry in the More Services modal only.
 */
const QUICK_START_ACTIONS: QuickStartActionDef[] = [
  {
    id: 'nda-assessment',
    label: 'NDA Assessment',
    icon: ShieldTaskRegular,
    ariaLabel: 'Start an NDA assessment',
    description: 'Upload an NDA and get an AI compliance assessment.',
    planes: ['workforce'],
    requiredEntitlement: 'legal-front-door',
    opensWidgetId: 'nda',
  },
  {
    id: 'policy-question',
    label: 'Submit Policy Question',
    icon: QuestionCircleRegular,
    ariaLabel: 'Submit a policy question to the law department',
    description: 'Get an official human answer from the legal team.',
    planes: ['workforce'],
    requiredEntitlement: 'policy-library',
    opensWidgetId: 'policy-library',
  },
  {
    id: 'invention-submission',
    label: 'Invention Submission',
    icon: LightbulbRegular,
    ariaLabel: 'Submit an invention disclosure',
    description: 'File an invention disclosure for patent review.',
    planes: ['workforce'],
    requiredEntitlement: 'legal-front-door',
    opensWidgetId: 'inventions',
  },
  {
    id: 'trademark-search',
    label: 'Trademark Search Request',
    icon: TagRegular,
    ariaLabel: 'Request a trademark clearance search',
    description: 'Ask the legal team to clear a proposed mark.',
    planes: ['workforce'],
    requiredEntitlement: 'legal-front-door',
    // No opensWidgetId — not yet built (P3); "coming soon" in More Services only.
  },
];

export interface QuickStartPaneProps {
  me: MeEntitlementsResponse;
  /** Open (or focus) a widget tab by id — the caller (WorkspaceHomePage) re-checks entitlement. */
  onOpenWidget: (widgetId: string) => void;
}

export const QuickStartPane: React.FC<QuickStartPaneProps> = ({ me, onOpenWidget }) => {
  const s = useStyles();
  const [moreOpen, setMoreOpen] = React.useState(false);

  const entitledActions = React.useMemo(() => QUICK_START_ACTIONS.filter((a) => isEntitled(me, a)), [me]);

  const launch = React.useCallback(
    (action: QuickStartActionDef) => {
      if (action.opensWidgetId) {
        onOpenWidget(action.opensWidgetId);
      } else {
        setMoreOpen(true);
      }
    },
    [onOpenWidget]
  );

  const rowCards: ActionCardConfig[] = React.useMemo(() => {
    const live: ActionCardConfig[] = entitledActions.map((a) => ({
      id: a.id,
      label: a.label,
      icon: a.icon,
      ariaLabel: a.ariaLabel,
    }));
    live.push({ id: MORE_SERVICES_ID, label: 'More Services', icon: AppsListRegular, ariaLabel: 'Browse all legal services' });
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

      <FormModal open={moreOpen} onClose={() => setMoreOpen(false)} onSubmit={() => setMoreOpen(false)} title="Legal services" submitLabel="Done" cancelLabel="Close">
        <Text className={s.intro}>
          Everything you can start from here. You only see services you&apos;re entitled to.
        </Text>
        <div className={s.grid}>
          {entitledActions.map((a) => (
            <div key={a.id} className={s.cell}>
              <ActionCard
                icon={a.icon}
                label={a.label}
                ariaLabel={a.ariaLabel}
                disabled={!a.opensWidgetId}
                onClick={
                  a.opensWidgetId
                    ? () => {
                        setMoreOpen(false);
                        launch(a);
                      }
                    : undefined
                }
              />
              {a.opensWidgetId ? (
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
    </div>
  );
};

export default QuickStartPane;
