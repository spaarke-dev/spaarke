/**
 * Task 141 review (finding 3): an attachment no email save can create is shown as such — "Can't be saved", no check mark
 * and no checkbox — and cannot be ticked.
 */
import { render, screen, fireEvent, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AttachmentSelector } from '../AttachmentSelector';
import type { AttachmentInfo } from '@shared/adapters/types';
import type { AttachmentSavedState } from '../../services/savedEmailAttachments';

const ATTACHMENTS: AttachmentInfo[] = [
  { id: 'a', name: 'Exhibit A.pdf', contentType: 'application/pdf', size: 1024, isInline: false },
  { id: 'ics', name: 'Meeting.ics', contentType: 'text/calendar', size: 2048, isInline: false },
];

describe('AttachmentSelector — not saveable (task 141 review)', () => {
  it('a notSaveable attachment says "Can\'t be saved", shows no Saved mark or checkbox, and cannot be ticked', () => {
    const onSelectionChange = jest.fn();
    render(
      <FluentProvider theme={webLightTheme}>
        <AttachmentSelector
          attachments={ATTACHMENTS}
          selectedIds={new Set()}
          onSelectionChange={onSelectionChange}
          savedStates={
            new Map<string, AttachmentSavedState>([
              ['a', 'unsaved'],
              ['ics', 'notSaveable'],
            ])
          }
          showHeader
        />
      </FluentProvider>
    );

    const row = screen.getByText('Meeting.ics').closest('[data-saved-state]') as HTMLElement;
    expect(row).toHaveAttribute('data-saved-state', 'notSaveable');
    expect(within(row).getByText("Can't be saved")).toBeInTheDocument();
    expect(within(row).queryByRole('img', { name: 'Saved' })).toBeNull();
    expect(screen.queryByRole('checkbox', { name: 'Select Meeting.ics' })).toBeNull();
    expect(screen.getByText('0/1')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Meeting.ics'));
    expect(onSelectionChange).not.toHaveBeenCalled();

    fireEvent.click(screen.getByText('Exhibit A.pdf'));
    expect(onSelectionChange).toHaveBeenCalledWith(new Set(['a']));
  });
});
