/**
 * Task 084 (spaarkeai-word-add-in-r1, #1037 — "pickable equals savable"). The picker lists records the
 * caller can READ, but `POST /api/office/save` demands AppendTo on the chosen record. A record the server
 * marks `canFile === false` must therefore render DISABLED, with the owner's verbatim reason, and must
 * never be selected: not by click, not by Enter/Space, not by any pre-selection. A `canFile` true / null /
 * absent record behaves exactly as before (the pre-existing RelatedToPicker suites pin that unchanged).
 *
 * Covers BOTH places the picker renders a record: search rows ("Look up another record") and the
 * engine's suggestion cards.
 *
 * NEW FILE — ADR-038 (new tests in new files); harness mirrors `RelatedToPicker.errorSurfacing.test.tsx`.
 */
import { render, screen, configure, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RelatedToPicker, type RelatedToPickerProps } from '../RelatedToPicker';
import type { EntitySearchResult, EntityType } from '../../hooks/useEntitySearch';
import type { RelatedCandidate } from '../../services/communicationSuggestionsService';

// Same budget rationale as the sibling RelatedToPicker suites: real-timer userEvent typing under
// parallel-worker load can approach the package's default 10s testTimeout. Everything is mocked.
jest.setTimeout(60000);
configure({ asyncUtilTimeout: 10000 });

/** The owner's copy, VERBATIM (task 084 constraint "exact copy"). Deliberately a literal, not an import. */
const REASON = "You can view this record but can't file to it. Filing needs Append To permission on the record.";

const BLOCKED: EntitySearchResult = {
  id: 'aaaaaaaa-0000-0000-0000-000000000001',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Secure Matter',
  displayInfo: 'SEC-001',
  canFile: false,
};
const FILEABLE: EntitySearchResult = {
  id: 'aaaaaaaa-0000-0000-0000-000000000002',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Open Matter',
  canFile: true,
};
const UNKNOWN_NULL: EntitySearchResult = {
  id: 'aaaaaaaa-0000-0000-0000-000000000003',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Past The Cap Matter',
  canFile: null,
};
const ABSENT: EntitySearchResult = {
  id: 'aaaaaaaa-0000-0000-0000-000000000004',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Legacy Matter',
};

type Overrides = Partial<Pick<RelatedToPickerProps, 'value' | 'candidates'>> & {
  onSearch?: jest.Mock<Promise<EntitySearchResult[]>, [string, EntityType]>;
};

function renderPicker(overrides: Overrides = {}) {
  const onChange = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <RelatedToPicker
        value={overrides.value ?? null}
        onChange={onChange}
        candidates={overrides.candidates ?? []}
        onSearch={overrides.onSearch ?? jest.fn().mockResolvedValue([])}
        allowedTypes={['Matter', 'Project', 'Invoice']}
        defaultType="Matter"
      />
    </FluentProvider>
  );
  return { onChange };
}

async function search(rows: EntitySearchResult[]) {
  const onSearch = jest.fn().mockResolvedValue(rows);
  const result = renderPicker({ onSearch });
  await userEvent.type(screen.getByLabelText('Search Matter records'), 'Matter');
  await userEvent.click(screen.getByRole('button', { name: 'Search' }));
  return { ...result, onSearch };
}

/** The disabled card carrying the reason. Throws (fails the test) if no reason renders. */
function blockedCardFor(reasonEl: HTMLElement): HTMLElement {
  const card = reasonEl.closest('[aria-disabled="true"]');
  if (!(card instanceof HTMLElement)) throw new Error('reason text is not inside an aria-disabled card');
  return card;
}

describe('RelatedToPicker — a search row the caller cannot file to is DISABLED (task 084)', () => {
  it('renders aria-disabled="true", out of the tab order, with the verbatim reason and no select control', async () => {
    await search([BLOCKED]);

    const reason = await screen.findByText(REASON);
    const card = blockedCardFor(reason);
    expect(card).toHaveAttribute('aria-disabled', 'true');
    expect(card).toHaveAttribute('tabindex', '-1');
    expect(within(card).getByText('SEC-001 : Secure Matter')).toBeInTheDocument();
    // No select control of any kind — nothing in the card can select it.
    expect(within(card).queryAllByRole('button')).toHaveLength(0);
    expect(screen.queryByRole('button', { name: 'Select this record' })).toBeNull();
  });

  it('a click does not select it', async () => {
    const { onChange } = await search([BLOCKED]);
    const card = blockedCardFor(await screen.findByText(REASON));

    await userEvent.click(card);
    await userEvent.click(within(card).getByText('SEC-001 : Secure Matter'));
    await userEvent.click(within(card).getByText(REASON));

    expect(onChange).not.toHaveBeenCalled();
  });

  it('Enter and Space do not select it', async () => {
    const { onChange } = await search([BLOCKED]);
    const card = blockedCardFor(await screen.findByText(REASON));

    card.focus();
    await userEvent.keyboard('{Enter}');
    await userEvent.keyboard(' ');

    expect(onChange).not.toHaveBeenCalled();
  });

  it('rows with canFile true, null and absent still select exactly as before, beside a disabled one', async () => {
    const { onChange } = await search([BLOCKED, FILEABLE, UNKNOWN_NULL, ABSENT]);
    await screen.findByText(REASON);

    // Exactly one reason (only the blocked row), and one select control per selectable row.
    expect(screen.getAllByText(REASON)).toHaveLength(1);
    const selectButtons = screen.getAllByRole('button', { name: 'Select this record' });
    expect(selectButtons).toHaveLength(3);

    await userEvent.click(selectButtons[0]!);
    await userEvent.click(selectButtons[1]!);
    await userEvent.click(selectButtons[2]!);

    expect(onChange).toHaveBeenNthCalledWith(1, FILEABLE);
    expect(onChange).toHaveBeenNthCalledWith(2, UNKNOWN_NULL);
    expect(onChange).toHaveBeenNthCalledWith(3, ABSENT);
    expect(onChange).not.toHaveBeenCalledWith(BLOCKED);
  });

  it('a selectable row still selects from the keyboard (Enter on its control)', async () => {
    const { onChange } = await search([BLOCKED, FILEABLE]);
    await screen.findByText(REASON);

    screen.getByRole('button', { name: 'Select this record' }).focus();
    await userEvent.keyboard('{Enter}');

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith(FILEABLE);
  });
});

describe('RelatedToPicker — a suggestion card the caller cannot file to is DISABLED and never pre-selected (task 084)', () => {
  const blockedCandidate: RelatedCandidate = { ...BLOCKED, confidence: 0.98 };
  const fileableCandidate: RelatedCandidate = { ...FILEABLE, confidence: 0.9 };

  it('renders disabled with the verbatim reason, and is not selected on render (even as the top match)', async () => {
    const { onChange } = renderPicker({ candidates: [blockedCandidate, fileableCandidate] });

    const card = blockedCardFor(screen.getByText(REASON));
    expect(card).toHaveAttribute('aria-disabled', 'true');
    expect(card).toHaveAttribute('tabindex', '-1');
    expect(within(card).getByText('Matter · 98% match')).toBeInTheDocument();
    expect(within(card).queryAllByRole('button')).toHaveLength(0);
    // Never pre-selected: nothing was chosen on render, and no card shows the "selected" control.
    expect(onChange).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Selected — click to clear' })).toBeNull();
  });

  it('click, Enter and Space on the disabled card do not select it; the fileable card still does', async () => {
    const { onChange } = renderPicker({ candidates: [blockedCandidate, fileableCandidate] });
    const card = blockedCardFor(screen.getByText(REASON));

    await userEvent.click(card);
    card.focus();
    await userEvent.keyboard('{Enter}');
    await userEvent.keyboard(' ');
    expect(onChange).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'Select this record' }));
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith(fileableCandidate);
  });

  it('a selection made elsewhere (e.g. the restored last association) is CLEARED once the record is reported not fileable', async () => {
    // The pane restores the last association (no canFile on it); the suggestion route then reports the
    // same record as not fileable. The selection must not stand — the save would refuse it.
    const restored: EntitySearchResult = {
      id: BLOCKED.id,
      entityType: BLOCKED.entityType,
      logicalName: BLOCKED.logicalName,
      name: BLOCKED.name,
    };
    const { onChange } = renderPicker({ value: restored, candidates: [blockedCandidate] });

    await waitFor(() => expect(onChange).toHaveBeenCalledWith(null));
    expect(onChange).not.toHaveBeenCalledWith(expect.objectContaining({ id: BLOCKED.id }));
  });
});
