/**
 * agendaFollowOnCards.test.tsx — task 023 (FR-06).
 *
 * Proves the OPEN-TAB-GATED follow-on launcher cards (Daily Briefing / Smart To Do):
 *  - not armed → no cards (both slots null);
 *  - armed + target layout tab CLOSED → the card renders (a CARD, not a chip);
 *  - armed + target layout tab OPEN → the card is SUPPRESSED (negative case, AC-1/AC-3);
 *  - clicking a card dispatches the task-022 single-surface launch by consumerType (AC-2);
 *  - both cards present → collapsed behind ONE disclosure header via ProactiveCardStack (AC-5).
 *
 * The layoutIds are read from the REAL task-022 `surfaceLaunchRegistry` (`resolveSurfaceLaunch`)
 * — the single source of truth — so the test never hardcodes a GUID and tracks the registry.
 */
import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { resolveSurfaceLaunch } from '@spaarke/ui-components';

import { buildAgendaFollowOnSlots } from '../agendaFollowOnCards';
import { ProactiveCardStack } from '../ProactiveCardStack';

function layoutIdOf(consumerType: string): string {
  const entry = resolveSurfaceLaunch(consumerType);
  const id = (entry?.widgetData as { layoutId?: string } | undefined)?.layoutId;
  if (!id) throw new Error(`test setup: no layoutId for ${consumerType}`);
  return id;
}

const BRIEFING = 'daily-briefing';
const SMART_TODO = 'smart-todo';

function renderStack(node: React.ReactNode) {
  return render(<FluentProvider theme={webLightTheme}>{node}</FluentProvider>);
}

describe('task 023 (FR-06): agenda follow-on launcher cards', () => {
  it('renders NOTHING when not armed (no FR-01 answer yet)', () => {
    const slots = buildAgendaFollowOnSlots({
      armed: false,
      openLayoutIds: new Set(),
      onOpenSurface: jest.fn(),
    });
    expect(slots.every((s) => s.node === null)).toBe(true);
  });

  it('renders BOTH cards when armed and both target tabs are closed (AC-2/AC-4 — cards, not chips)', () => {
    const slots = buildAgendaFollowOnSlots({
      armed: true,
      openLayoutIds: new Set(),
      onOpenSurface: jest.fn(),
    });
    const present = slots.filter((s) => s.node !== null);
    expect(present).toHaveLength(2);

    renderStack(<ProactiveCardStack slots={slots} />);
    // Both render as CARDS (workspace-launcher-card-*), never chips.
    expect(screen.getByTestId(`workspace-launcher-card-${BRIEFING}`)).toBeInTheDocument();
    expect(screen.getByTestId(`workspace-launcher-card-${SMART_TODO}`)).toBeInTheDocument();
  });

  it('SUPPRESSES the Daily Briefing card when its tab is already open (AC-1 negative)', () => {
    const slots = buildAgendaFollowOnSlots({
      armed: true,
      openLayoutIds: new Set([layoutIdOf(BRIEFING)]),
      onOpenSurface: jest.fn(),
    });
    renderStack(<ProactiveCardStack slots={slots} />);
    expect(screen.queryByTestId(`workspace-launcher-card-${BRIEFING}`)).not.toBeInTheDocument();
    // Smart To Do is still closed → its card still shows.
    expect(screen.getByTestId(`workspace-launcher-card-${SMART_TODO}`)).toBeInTheDocument();
  });

  it('SUPPRESSES the Smart To Do card when its tab is already open (AC-3 negative)', () => {
    const slots = buildAgendaFollowOnSlots({
      armed: true,
      openLayoutIds: new Set([layoutIdOf(SMART_TODO)]),
      onOpenSurface: jest.fn(),
    });
    renderStack(<ProactiveCardStack slots={slots} />);
    expect(screen.queryByTestId(`workspace-launcher-card-${SMART_TODO}`)).not.toBeInTheDocument();
    expect(screen.getByTestId(`workspace-launcher-card-${BRIEFING}`)).toBeInTheDocument();
  });

  it('renders NOTHING when armed but BOTH tabs are already open', () => {
    const slots = buildAgendaFollowOnSlots({
      armed: true,
      openLayoutIds: new Set([layoutIdOf(BRIEFING), layoutIdOf(SMART_TODO)]),
      onOpenSurface: jest.fn(),
    });
    expect(slots.every((s) => s.node === null)).toBe(true);
  });

  it('dispatches the single-surface launch by consumerType on click (AC-2)', () => {
    const onOpenSurface = jest.fn();
    const slots = buildAgendaFollowOnSlots({
      armed: true,
      openLayoutIds: new Set(),
      onOpenSurface,
    });
    renderStack(<ProactiveCardStack slots={slots} />);

    fireEvent.click(screen.getByTestId(`workspace-launcher-card-${BRIEFING}`));
    expect(onOpenSurface).toHaveBeenCalledWith(BRIEFING);

    fireEvent.click(screen.getByTestId(`workspace-launcher-card-${SMART_TODO}`));
    expect(onOpenSurface).toHaveBeenCalledWith(SMART_TODO);
    expect(onOpenSurface).toHaveBeenCalledTimes(2);
  });

  it('collapses BOTH cards behind ONE disclosure header when both are present (AC-5)', () => {
    const slots = buildAgendaFollowOnSlots({
      armed: true,
      openLayoutIds: new Set(),
      onOpenSurface: jest.fn(),
    });
    renderStack(<ProactiveCardStack slots={slots} />);
    // 2 present → ProactiveCardStack wraps them under one outer disclosure header.
    expect(screen.getByTestId('proactive-card-stack-toggle')).toHaveTextContent('You have 2 pending actions');
  });

  it('renders a lone card UNWRAPPED (no disclosure header) when only one is present', () => {
    const slots = buildAgendaFollowOnSlots({
      armed: true,
      openLayoutIds: new Set([layoutIdOf(BRIEFING)]),
      onOpenSurface: jest.fn(),
    });
    renderStack(<ProactiveCardStack slots={slots} />);
    expect(screen.queryByTestId('proactive-card-stack-toggle')).not.toBeInTheDocument();
    expect(screen.getByTestId(`workspace-launcher-card-${SMART_TODO}`)).toBeInTheDocument();
  });
});
