/**
 * Task 116a (spaarkeai-word-add-in-r1): the attachment picker refuses exactly what one save can carry.
 *
 * Since the add-in sends the selected attachments in the save request (base64, inside the BFF's 30,000,000-byte
 * request limit), one attachment can be at most 20 MB. The picker used to allow 25 MB per file ("per spec", true only
 * while the server fetched attachments through Graph) — a 21-25 MB file would have been accepted here and then left
 * out at save time. It must say so when the box is ticked instead.
 */
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AttachmentSelector } from '../AttachmentSelector';
import type { AttachmentInfo } from '@shared/adapters/types';

const MB = 1024 * 1024;

function att(id: string, size: number): AttachmentInfo {
  return { id, name: `${id}.pdf`, contentType: 'application/pdf', size, isInline: false };
}

it('a file over 20 MB is refused with the per-file reason; a 19 MB file is not', () => {
  render(
    <FluentProvider theme={webLightTheme}>
      <AttachmentSelector
        attachments={[att('big', 21 * MB), att('ok', 19 * MB)]}
        selectedIds={new Set()}
        onSelectionChange={jest.fn()}
      />
    </FluentProvider>
  );

  expect(screen.getAllByText('File exceeds 20 MB limit')).toHaveLength(1);
});

it('selected files over 20 MB in total show the total-size warning', () => {
  render(
    <FluentProvider theme={webLightTheme}>
      <AttachmentSelector
        attachments={[att('a', 12 * MB), att('b', 12 * MB)]}
        selectedIds={new Set(['a', 'b'])}
        onSelectionChange={jest.fn()}
      />
    </FluentProvider>
  );

  expect(screen.getByText(/Total size exceeds 20 MB limit/)).toBeInTheDocument();
});
