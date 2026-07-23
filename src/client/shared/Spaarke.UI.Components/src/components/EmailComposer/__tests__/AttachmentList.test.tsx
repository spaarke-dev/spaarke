/**
 * AttachmentList.test.tsx (task 023, W2)
 *
 * Behavior contract for the attachment cap + source-section UI (design §5.6.4):
 *   - 150-count cap → hard "too many" alert
 *   - 35 MB total  → hard "too large" alert
 *   - 25 MB total  → soft warning only (NO hard block / NO error alert)
 *   - one source badge per attachment kind
 *   - remove button → onRemove(id)
 *   - forward-mode inclusion checkbox → onToggleSelected(id)
 *
 * NOTE: the canonical `ATTACHMENTS_TOO_MANY` / `ATTACHMENT_TOO_LARGE`
 * validation codes are raised by `validateState` (see validation.test.ts).
 * `AttachmentList` renders the corresponding *visual* caps; it does not itself
 * disable the "Add files" button at the count boundary (there is no such
 * behavior in the component), so this suite asserts the alert/warning surface
 * that actually exists rather than a disabled-Add affordance.
 */
import * as React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { AttachmentList } from '../subcomponents/AttachmentList';
import {
  ATTACHMENT_MAX_COUNT,
  ATTACHMENT_MAX_TOTAL_BYTES,
  ATTACHMENT_WARN_TOTAL_BYTES,
} from '../EmailComposer.reducer';
import type {
  EmailComposerMode,
  EmailAttachmentSourceKind,
  IAttachmentItem,
  IComposerAttachmentSource,
} from '../EmailComposer.types';

const MB = 1024 * 1024;

function item(
  id: string,
  kind: EmailAttachmentSourceKind,
  sizeBytes: number,
  extra: Partial<IAttachmentItem> = {}
): IAttachmentItem {
  return { id, source: kind, fileName: `${id}.pdf`, sizeBytes, documentId: `doc-${id}`, ...extra };
}

function renderList(opts: {
  mode?: EmailComposerMode;
  items: IAttachmentItem[];
  sources?: IComposerAttachmentSource[];
  onRemove?: (id: string) => void;
  onToggleSelected?: (id: string) => void;
  onToggleLink?: (id: string) => void;
}) {
  const onAdd = jest.fn();
  const onRemove = opts.onRemove ?? jest.fn();
  const onToggleSelected = opts.onToggleSelected ?? jest.fn();
  const onToggleLink = opts.onToggleLink ?? jest.fn();
  const sources = opts.sources ?? [{ kind: 'related' }];
  const result = renderWithProviders(
    <AttachmentList
      mode={opts.mode ?? 'compose'}
      sources={sources}
      items={opts.items}
      onAdd={onAdd}
      onRemove={onRemove}
      onToggleSelected={onToggleSelected}
      onToggleLink={onToggleLink}
    />
  );
  return { ...result, onAdd, onRemove, onToggleSelected, onToggleLink };
}

describe('AttachmentList — hard caps', () => {
  it('shows the "too many" alert past the 150-attachment cap', () => {
    const many = Array.from({ length: ATTACHMENT_MAX_COUNT + 1 }, (_, i) => item(`a${i}`, 'related', 16));
    renderList({ items: many });
    expect(screen.getByText(new RegExp(`Too many attachments \\(max ${ATTACHMENT_MAX_COUNT}\\)`))).toBeInTheDocument();
  });

  it('shows the "too large" alert past the 35 MB total cap', () => {
    const half = Math.ceil(ATTACHMENT_MAX_TOTAL_BYTES / 2) + 1;
    renderList({ items: [item('big-1', 'related', half), item('big-2', 'related', half)] });
    expect(screen.getByText(/Total attachment size exceeds/)).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/exceeds/);
  });
});

describe('AttachmentList — soft 25 MB warning', () => {
  it('renders the size summary without any hard-block alert between 25 and 35 MB', () => {
    // 30 MB total → over the 25 MB warn threshold, under the 35 MB hard cap.
    const total = ATTACHMENT_WARN_TOTAL_BYTES + 5 * MB;
    expect(total).toBeLessThan(ATTACHMENT_MAX_TOTAL_BYTES);
    renderList({ items: [item('warn', 'related', total)] });

    // No hard-block alert is present (no over-count / over-size / errorMessage).
    expect(screen.queryByRole('alert')).toBeNull();
    // The size summary is still rendered so the user sees they are near the cap.
    expect(screen.getByText(new RegExp(`/ ${(ATTACHMENT_MAX_TOTAL_BYTES / MB).toFixed(1)} MB`))).toBeInTheDocument();
  });
});

describe('AttachmentList — source sections + badges', () => {
  it('renders all items in one section with no per-source pills/labels (owner UAT round 3 #2)', () => {
    renderList({
      sources: [{ kind: 'local' }, { kind: 'spe' }, { kind: 'related' }, { kind: 'wizard' }],
      items: [item('l', 'local', 16), item('s', 'spe', 16), item('r', 'related', 16), item('w', 'wizard', 16)],
    });
    // Every item renders by filename, regardless of source.
    for (const name of ['l.pdf', 's.pdf', 'r.pdf', 'w.pdf']) {
      expect(screen.getByTitle(name)).toBeInTheDocument();
    }
    // The source pills/labels are gone — one "Attachments" section header instead.
    for (const label of ['From this device', 'From SharePoint', 'Related documents', 'Uploaded in this wizard']) {
      expect(screen.queryByText(label)).toBeNull();
    }
    expect(screen.getByText(/^Attachments/)).toBeInTheDocument();
  });
});

describe('AttachmentList — remove', () => {
  it('invokes onRemove with the attachment id', () => {
    const onRemove = jest.fn();
    renderList({ items: [item('r1', 'related', 16, { fileName: 'contract.pdf' })], onRemove });
    fireEvent.click(screen.getByRole('button', { name: 'Remove contract.pdf' }));
    expect(onRemove).toHaveBeenCalledWith('r1');
  });
});

describe('AttachmentList — reply/forward include toggles (task 104)', () => {
  it('renders the Attach toggle for a source attachment and fires onToggleSelected (forward)', () => {
    const onToggleSelected = jest.fn();
    renderList({
      mode: 'forward',
      items: [item('f1', 'related', 16, { fileName: 'brief.pdf', selected: true })],
      onToggleSelected,
    });
    const checkbox = screen.getByRole('checkbox', { name: 'Attach brief.pdf as a file' });
    fireEvent.click(checkbox);
    expect(onToggleSelected).toHaveBeenCalledWith('f1');
  });

  it('renders the Attach toggle in reply mode too (opt-in)', () => {
    renderList({
      mode: 'reply',
      items: [item('r1', 'related', 16, { fileName: 'brief.pdf', selected: false })],
    });
    expect(screen.getByRole('checkbox', { name: 'Attach brief.pdf as a file' })).toBeInTheDocument();
  });

  it('renders the Link toggle only when linkUrl is present and fires onToggleLink', () => {
    const onToggleLink = jest.fn();
    renderList({
      mode: 'reply',
      items: [item('r1', 'related', 16, { fileName: 'brief.pdf', selected: false, linkUrl: 'https://x/doc' })],
      onToggleLink,
    });
    const linkBox = screen.getByRole('checkbox', { name: 'Insert a link to brief.pdf in the message body' });
    fireEvent.click(linkBox);
    expect(onToggleLink).toHaveBeenCalledWith('r1');
  });

  it('does not render the Link toggle when linkUrl is absent', () => {
    renderList({
      mode: 'reply',
      items: [item('r1', 'related', 16, { fileName: 'brief.pdf', selected: false })],
    });
    expect(screen.queryByRole('checkbox', { name: /Insert a link/ })).toBeNull();
  });

  it('renders the Attach toggle for related documents in compose mode (owner UAT 2026-07-22)', () => {
    // Related documents expose Attach/Link in ANY mode incl. compose (mockup: "New Email"
    // shows Related documents with Attach/Link). Non-Document-backed local picks still do not.
    renderList({ mode: 'compose', items: [item('c1', 'related', 16)] });
    expect(screen.getByRole('checkbox', { name: /attach/i })).toBeInTheDocument();
  });

  it('does not render include toggles for items lacking a documentId (local picks)', () => {
    renderList({
      mode: 'forward',
      items: [item('l1', 'local', 16, { fileName: 'x.pdf', documentId: undefined })],
    });
    expect(screen.queryByRole('checkbox')).toBeNull();
  });
});
