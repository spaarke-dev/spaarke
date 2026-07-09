/**
 * Deterministic channel renderer tests — R5 task 016 (FR-A7), the client half of the
 * briefing-accuracy family (the server half is tests/integration/contract/Eval/
 * BriefingAccuracyEvalSuiteTests.cs).
 *
 * Task 011 made each Activity-Notes row render deterministically from ONE source item.
 * These tests pin the render-layer accuracy contract that the old, since-removed
 * LLM-narrative-matching approach could violate:
 *
 *   1. A row's click-through link target is built EXCLUSIVELY from the item's structured
 *      props (primaryEntityType/Id), NOT parsed from the narrative text — so an entity name
 *      that merely appears in the prose can never hijack the link (the render-layer form of
 *      "zero cross-item pairing").
 *   2. The rendered narrative is the item's own text verbatim.
 *   3. Two rows each link to their OWN item — the renderer is a pure function of its props.
 *
 * (Placed under test/ to match this package's jest testMatch; the task POML suggested
 * src/components/__tests__/ — adapted per directional step mode.)
 */

import * as React from 'react';
import { render, screen, fireEvent, act, cleanup } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { NarrativeBullet } from '../src/components/NarrativeBullet';
import type { NarrativeBulletProps } from '../src/components/NarrativeBullet';

function renderWithProvider(ui: React.ReactElement) {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
}

function baseProps(overrides: Partial<NarrativeBulletProps> = {}): NarrativeBulletProps {
  return {
    narrative: 'Review motion to dismiss.',
    primaryEntityName: 'Acme Matter',
    primaryEntityType: 'sprk_matter',
    primaryEntityId: '11111111-1111-1111-1111-111111111111',
    itemIds: ['n-1'],
    onAddToTodo: jest.fn(),
    onDismiss: jest.fn(),
    isTodoCreated: false,
    isTodoPending: false,
    ...overrides,
  };
}

function openRecordViaMenu(): void {
  act(() => {
    fireEvent.click(screen.getByRole('button', { name: /More actions/i }));
  });
  act(() => {
    fireEvent.click(screen.getByRole('menuitem', { name: /^Open record$/i }));
  });
}

describe('deterministic channel renderer (R5 task 016 / FR-A7)', () => {
  afterEach(() => cleanup());

  it('links to the item structured props, NOT any entity named in the narrative text', () => {
    // The narrative prose mentions a DIFFERENT matter ("Beta Holdings"); the structured item
    // is Acme Matter. A text-parsing renderer (the old cross-pairing bug) would link to Beta.
    // The deterministic renderer must link to Acme — the item's own primaryEntity props.
    const onOpenRecord = jest.fn();
    renderWithProvider(
      <NarrativeBullet
        {...baseProps({
          narrative: 'Discuss the Beta Holdings merger before Friday.',
          primaryEntityName: 'Acme Matter',
          primaryEntityType: 'sprk_matter',
          primaryEntityId: 'aaaa1111-0000-0000-0000-000000000001',
          onOpenRecord,
        })}
      />
    );

    // Row renders the item's own narrative verbatim.
    expect(screen.getByText(/Discuss the Beta Holdings merger before Friday\./)).toBeInTheDocument();

    // Link target is the structured item, not the "Beta Holdings" named in the prose.
    openRecordViaMenu();
    expect(onOpenRecord).toHaveBeenCalledTimes(1);
    expect(onOpenRecord).toHaveBeenCalledWith('sprk_matter', 'aaaa1111-0000-0000-0000-000000000001');
  });

  it('a to-do row with no regarding falls back to its OWN source record for the link', () => {
    // Orphan item (no regarding matter): the deterministic 3-tier resolution links to the
    // source row itself — still the item's own entity, never another item's.
    const onOpenRecord = jest.fn();
    renderWithProvider(
      <NarrativeBullet
        {...baseProps({
          narrative: 'Return the client call.',
          primaryEntityName: 'Return the client call',
          primaryEntityType: 'sprk_todo',
          primaryEntityId: 'todo-9999',
          itemIds: ['todo-9999'],
          onOpenRecord,
        })}
      />
    );

    openRecordViaMenu();
    expect(onOpenRecord).toHaveBeenCalledWith('sprk_todo', 'todo-9999');
  });

  it('two rows each link to their OWN item — the renderer is a pure function of its props', () => {
    // Row A.
    const onOpenA = jest.fn();
    const { unmount } = renderWithProvider(
      <NarrativeBullet
        {...baseProps({
          narrative: 'Northwind acquisition update.',
          primaryEntityName: 'Northwind acquisition',
          primaryEntityType: 'sprk_matter',
          primaryEntityId: 'aaaa0000-0000-0000-0000-00000000000a',
          onOpenRecord: onOpenA,
        })}
      />
    );
    openRecordViaMenu();
    expect(onOpenA).toHaveBeenCalledWith('sprk_matter', 'aaaa0000-0000-0000-0000-00000000000a');
    unmount();

    // Row B — different item; its link must target B, never A.
    const onOpenB = jest.fn();
    renderWithProvider(
      <NarrativeBullet
        {...baseProps({
          narrative: 'Contoso divestiture update.',
          primaryEntityName: 'Contoso divestiture',
          primaryEntityType: 'sprk_project',
          primaryEntityId: 'bbbb0000-0000-0000-0000-00000000000b',
          onOpenRecord: onOpenB,
        })}
      />
    );
    openRecordViaMenu();
    expect(onOpenB).toHaveBeenCalledWith('sprk_project', 'bbbb0000-0000-0000-0000-00000000000b');
    expect(onOpenB).not.toHaveBeenCalledWith('sprk_matter', 'aaaa0000-0000-0000-0000-00000000000a');
  });
});
