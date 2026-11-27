/**
 * Task 141 (owner point 7): the attachment list of a saved email marks each attachment saved or not.
 *
 * - `saved` → a check mark labelled "Saved" in place of the checkbox; it cannot be ticked.
 * - `unsaved` → a checkbox, as before.
 * - `unknown` (the pane was not told) → neither mark nor checkbox; no claim either way; cannot be ticked.
 * - `nonSelectableIds` → no checkbox (nothing could save it).
 * - Without `savedStates` the list is exactly as before: every attachment a checkbox.
 */
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AttachmentSelector } from '../AttachmentSelector';
import type { AttachmentInfo } from '@shared/adapters/types';
import type { AttachmentSavedState } from '../../services/savedEmailAttachments';

const ATTACHMENTS: AttachmentInfo[] = [
  { id: 'a', name: 'Exhibit A.pdf', contentType: 'application/pdf', size: 1024, isInline: false },
  { id: 'b', name: 'Exhibit B.pdf', contentType: 'application/pdf', size: 2048, isInline: false },
  { id: 'c', name: 'Exhibit C.pdf', contentType: 'application/pdf', size: 4096, isInline: false },
];

function renderSelector(props: {
  savedStates?: ReadonlyMap<string, AttachmentSavedState>;
  nonSelectableIds?: ReadonlySet<string>;
  selectedIds?: Set<string>;
}) {
  const onSelectionChange = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <AttachmentSelector
        attachments={ATTACHMENTS}
        selectedIds={props.selectedIds ?? new Set()}
        onSelectionChange={onSelectionChange}
        {...(props.savedStates ? { savedStates: props.savedStates } : {})}
        {...(props.nonSelectableIds ? { nonSelectableIds: props.nonSelectableIds } : {})}
      />
    </FluentProvider>
  );
  return onSelectionChange;
}

describe('AttachmentSelector — saved marks (task 141)', () => {
  it('a saved attachment shows "Saved" in place of a checkbox and cannot be ticked; unsaved ones are checkboxes', () => {
    const onSelectionChange = renderSelector({
      savedStates: new Map<string, AttachmentSavedState>([
        ['a', 'saved'],
        ['b', 'unsaved'],
        ['c', 'unsaved'],
      ]),
    });

    expect(screen.getAllByRole('img', { name: 'Saved' })).toHaveLength(1);
    expect(screen.queryByRole('checkbox', { name: 'Select Exhibit A.pdf' })).toBeNull();
    expect(screen.getByRole('checkbox', { name: 'Select Exhibit B.pdf' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Select Exhibit C.pdf' })).toBeInTheDocument();

    fireEvent.click(screen.getByText('Exhibit A.pdf'));
    expect(onSelectionChange).not.toHaveBeenCalled();

    fireEvent.click(screen.getByText('Exhibit B.pdf'));
    expect(onSelectionChange).toHaveBeenCalledWith(new Set(['b']));
  });

  it('unknown: no mark and no checkbox — nothing is claimed and nothing can be ticked', () => {
    const onSelectionChange = renderSelector({
      savedStates: new Map<string, AttachmentSavedState>(ATTACHMENTS.map(a => [a.id, 'unknown'])),
    });

    expect(screen.getByText('Exhibit A.pdf')).toBeInTheDocument();
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
    expect(screen.queryAllByRole('img', { name: 'Saved' })).toHaveLength(0);
    fireEvent.click(screen.getByText('Exhibit B.pdf'));
    expect(onSelectionChange).not.toHaveBeenCalled();
  });

  it('nonSelectableIds: an unsaved attachment shown without a checkbox cannot be ticked', () => {
    const onSelectionChange = renderSelector({
      savedStates: new Map<string, AttachmentSavedState>([
        ['a', 'saved'],
        ['b', 'unsaved'],
        ['c', 'unsaved'],
      ]),
      nonSelectableIds: new Set(['b']),
    });

    expect(screen.queryByRole('checkbox', { name: 'Select Exhibit B.pdf' })).toBeNull();
    expect(screen.getByRole('checkbox', { name: 'Select Exhibit C.pdf' })).toBeInTheDocument();
    fireEvent.click(screen.getByText('Exhibit B.pdf'));
    expect(onSelectionChange).not.toHaveBeenCalled();
  });

  it('without saved states every attachment is a checkbox, as before', () => {
    renderSelector({});

    expect(screen.getAllByRole('checkbox')).toHaveLength(3);
    expect(screen.queryAllByRole('img', { name: 'Saved' })).toHaveLength(0);
    expect(screen.getByText('0/3')).toBeInTheDocument();
  });
});
