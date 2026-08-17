/**
 * agendaFollowOnCards — the OPEN-TAB-GATED follow-on launcher cards offered after
 * the FR-01 task-agenda answer (spaarkeai-assistant-enhancements-r4 task 023, FR-06).
 *
 * After the Assistant answers "what do I need to do today" (the FR-01 advisory
 * task-agenda capability — task 012 — whose answer opens the My Tasks grid via a
 * `list-tasks` surface_launch), it offers to ALSO open the two dashboard surfaces
 * that complete the daily-agenda picture: Daily Briefing and Smart To Do. Per
 * ASSISTANT-UI-ELEMENT-CRITERIA these persistent act-on launchers are CARDS (not
 * chips); when both show they collapse behind ONE disclosure header — which the host
 * gets for free by feeding these as independent slots into `ProactiveCardStack`
 * (0/1 present renders unwrapped, 2+ behind one header).
 *
 * Gating (owner decision 2026-08-17):
 *  - TRIGGER: `armed` — set true once the FR-01 answer's `list-tasks` surface_launch
 *    fires (a STRUCTURAL consumerType signal in `ConversationPane.handleSurfaceLaunch`,
 *    never a keyword heuristic — ADR-039). Session-scoped.
 *  - SUPPRESS-WHEN-OPEN: each card is dropped when its target layout tab is already
 *    open, read from the live `openLayoutIds` set (the `workspace_tabs_snapshot` bus
 *    event WorkspacePane broadcasts). WorkspacePane ALSO de-dupes `widget_load` by
 *    layoutId, so "no duplicate tab" holds even if a stale card were clicked.
 *
 * §11 reuse-first: the layoutId AND the display title come from the task-022
 * `surfaceLaunchRegistry` entry (`resolveSurfaceLaunch`) — the SINGLE source of truth
 * for each surface's identity — so there is NO re-hardcoded GUID or duplicated name
 * here. The click routes through the host's `onOpenSurface(consumerType)` →
 * `handleSurfaceLaunch` → the SAME registry entry (no per-card dispatch branch).
 */
import * as React from 'react';
import { NewsRegular, TaskListSquareLtrRegular } from '@fluentui/react-icons';
import { resolveSurfaceLaunch } from '@spaarke/ui-components';
import type { ProactiveCardSlot } from './ProactiveCardStack';
import { WorkspaceLauncherCard } from './WorkspaceLauncherCard';

/** One agenda follow-on surface: the consumerType (registry key) + card copy/icon. */
interface AgendaFollowOn {
  /** `surfaceLaunchRegistry` consumerType key (task 022). Source of truth for layoutId + title. */
  readonly consumerType: string;
  /** One-line supporting description (card copy — not carried in the registry). */
  readonly description: string;
  readonly icon: React.ReactElement;
}

/**
 * The two agenda follow-on surfaces, in a stable display order (Briefing first,
 * then Smart To Do). Extending FR-06 to another dashboard surface = one entry here
 * plus its `surfaceLaunchRegistry` entry — no other change.
 */
const AGENDA_FOLLOW_ONS: ReadonlyArray<AgendaFollowOn> = [
  {
    consumerType: 'daily-briefing',
    description: "Review today's priorities and deadlines at a glance.",
    icon: <NewsRegular />,
  },
  {
    consumerType: 'smart-todo',
    description: 'See your prioritized task list in one place.',
    icon: <TaskListSquareLtrRegular />,
  },
];

export interface AgendaFollowOnCardsParams {
  /** True once the FR-01 task-agenda answer has fired (the `list-tasks` surface launch). */
  readonly armed: boolean;
  /** The live set of open embedded-layout ids (from the `workspace_tabs_snapshot` bus event). */
  readonly openLayoutIds: ReadonlySet<string>;
  /** Host launch seam: routed to `handleSurfaceLaunch({ consumerType })` (registry-driven). */
  readonly onOpenSurface: (consumerType: string) => void;
}

/**
 * Build the agenda follow-on card slots for `ProactiveCardStack`.
 *
 * Returns one slot per follow-on surface, in `AGENDA_FOLLOW_ONS` order. A slot's
 * `node` is `null` (rendered nothing — `ProactiveCardStack` filters nulls before
 * counting) when: the cards aren't armed, the surface has no registry entry, or its
 * target layout tab is already open. Pure — no hooks, no side effects — so the gating
 * is unit-testable in isolation.
 */
export function buildAgendaFollowOnSlots(params: AgendaFollowOnCardsParams): ProactiveCardSlot[] {
  const { armed, openLayoutIds, onOpenSurface } = params;

  return AGENDA_FOLLOW_ONS.map((followOn): ProactiveCardSlot => {
    const key = `agenda-${followOn.consumerType}`;
    if (!armed) return { key, node: null };

    const entry = resolveSurfaceLaunch(followOn.consumerType);
    // Belt-and-suspenders: no registry entry (mis-seeded env) ⇒ nothing to launch, no card.
    if (!entry) return { key, node: null };

    const layoutId = (entry.widgetData as { layoutId?: string } | undefined)?.layoutId;
    // Suppress when the target layout tab is already open (the FR-06 no-duplicate gate).
    if (layoutId && openLayoutIds.has(layoutId)) return { key, node: null };

    return {
      key,
      node: (
        <WorkspaceLauncherCard
          title={`Open ${entry.title}`}
          description={followOn.description}
          icon={followOn.icon}
          onOpen={() => onOpenSurface(followOn.consumerType)}
          testId={followOn.consumerType}
        />
      ),
    };
  });
}
