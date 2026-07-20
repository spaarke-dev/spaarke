/**
 * ComposeCommentThread.tsx — FR-23 richer comment-thread UI (spaarkeai-compose-r3 task 044).
 *
 * A docked Fluent v9 panel (mirrors {@link ./ComposeFindReplace.tsx}'s conventions: `editor` /
 * `open` / `onClose` props, `makeStyles` + semantic `tokens.*` only) listing every comment thread —
 * author, timestamp, replies, resolved state — with create/reply/resolve affordances. All state
 * lives in {@link ../hooks/useComposeCommentThreads}; this component is a thin, presentational
 * binding over it (same division of labor as `ComposeFindReplace` / `useComposeFindReplace`).
 *
 * SCOPE GUARD (binding, FR-23 Assumptions): view/create/reply/resolve ONLY — NOT full Word
 * comment-feature parity. Replies ALWAYS render as a flat list (`thread.replies.map(...)`, one
 * `<div>` per reply, no recursion) — never a nested tree, regardless of any `parentReplyId` a
 * reply carries (see `ComposeCommentThread.types.ts`).
 *
 * "Create on a selection" gesture: the HOST (`ComposeEditor.tsx`) captures the editor's selection
 * range + preview text at the moment the panel is opened (BEFORE focus moves into this panel's
 * composer textbox) and passes it as `pendingRange`. When absent, the composer is replaced by a
 * hint ("select text to add a comment") rather than silently guessing an anchor.
 *
 * Constraints honored (BINDING):
 *   - ADR-021: Fluent v9 semantic tokens only — no hex literals; correct in light AND dark theme.
 *   - ADR-022: React 19 pure functional component.
 *   - ADR-028: no network calls here — `author` is supplied by the host, never fetched.
 *   - NFR-03: no TipTap product feature — this is a plain React panel over the MIT `CommentAnchorMark`.
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: `CommentAnchorMark.ts` is a ProseMirror mark (not a React UI); `ComposeReanchorBanner`
 *     is an unrelated re-anchor summary banner. No overlap.
 *   - Extension: cannot fold thread/reply/resolve UI into either existing surface — a thread panel
 *     with composer + reply state is a cohesive new surface that REUSES the anchor mark + `w:comment`
 *     wire vocabulary rather than adding either.
 *   - Cost-of-doing-nothing: without it, comments cannot be viewed as threads with author/timestamp
 *     or replied to/resolved, and FR-25's imported threads (task 051) have no render target.
 *
 * @see ../hooks/useComposeCommentThreads.ts — thread/reply state + create/reply/resolve
 * @see ./ComposeCommentThread.types.ts — `ComposeCommentThreadModel` / `ComposeCommentReply`
 * @see ./marks/CommentAnchorMark.ts — the anchoring mark (reused, not reimplemented)
 * @see ./ComposeFindReplace.tsx — the sibling panel this mirrors conventions from
 */
import * as React from 'react';
import { type Editor } from '@tiptap/react';
import {
  Avatar,
  Badge,
  Button,
  Input,
  Text,
  Textarea,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { CommentCheckmark16Regular, Dismiss16Regular } from '@fluentui/react-icons';
import { useComposeCommentThreads } from './hooks/useComposeCommentThreads';
import type { ComposeCommentThreadModel } from './ComposeCommentThread.types';

const useStyles = makeStyles({
  panel: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalS,
    maxHeight: '40vh',
    overflowY: 'auto',
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  composer: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  anchorPreview: {
    color: tokens.colorNeutralForeground2,
    fontStyle: 'italic',
  },
  composerActions: {
    display: 'flex',
    justifyContent: 'flex-end',
  },
  hint: {
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingHorizontalS,
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingHorizontalS,
  },
  list: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
  },
  thread: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  threadResolved: {
    backgroundColor: tokens.colorNeutralBackground3,
  },
  threadHeader: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
  },
  threadHeaderText: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minWidth: 0,
  },
  authorName: {
    color: tokens.colorNeutralForeground1,
  },
  timestamp: {
    color: tokens.colorNeutralForeground3,
  },
  threadBody: {
    color: tokens.colorNeutralForeground1,
  },
  // SCOPE GUARD — every reply is a sibling in this ONE flat list, never nested (see file header).
  replyList: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalL,
    borderLeftWidth: '2px',
    borderLeftStyle: 'solid',
    borderLeftColor: tokens.colorNeutralStroke2,
  },
  reply: {
    display: 'flex',
    flexDirection: 'column',
  },
  replyComposer: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
  },
  replyInput: {
    flex: 1,
  },
});

/** Human-readable timestamp — no external formatter dependency (Prefer zero new dependency). */
function formatTimestamp(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

/** Truncated, log-safe-length label for the anchored-selection preview. */
function truncate(text: string, max: number): string {
  const trimmed = text.trim();
  return trimmed.length > max ? `${trimmed.slice(0, max)}…` : trimmed;
}

/** The captured selection a new thread will anchor to (host supplies; see file header). */
export interface ComposeCommentPendingRange {
  from: number;
  to: number;
  /** The selected text, for the "On: '…'" composer preview. */
  preview: string;
}

export interface ComposeCommentThreadProps {
  editor: Editor | null;
  /** Whether the panel is mounted/visible. When false, this component renders nothing (state persists). */
  open: boolean;
  /** Called when the user dismisses the panel (close button). */
  onClose: () => void;
  /** Display name attributed to threads/replies created via this panel. Defaults to `'You'`. */
  author?: string;
  /**
   * The selection to anchor a NEW thread to, captured by the host BEFORE this panel could steal
   * focus. `null`/`undefined` when no selection was active when the panel opened — the composer is
   * replaced by a hint instead of guessing an anchor.
   */
  pendingRange?: ComposeCommentPendingRange | null;
  /** Called after a new thread is successfully created — the host clears `pendingRange`. */
  onThreadCreated?: (threadId: string) => void;
  /** Seeds the panel's thread list on first mount (e.g. a future FR-25 hydration). */
  initialThreads?: readonly ComposeCommentThreadModel[];
}

export function ComposeCommentThread(props: ComposeCommentThreadProps): React.JSX.Element | null {
  const { editor, open, onClose, author = 'You', pendingRange, onThreadCreated, initialThreads } = props;
  const styles = useStyles();
  const threadsState = useComposeCommentThreads(editor, author, initialThreads);

  const [draftText, setDraftText] = React.useState<string>('');
  const [replyDrafts, setReplyDrafts] = React.useState<Record<string, string>>({});

  if (!open) return null;

  const handleCreate = (): void => {
    if (!pendingRange || draftText.trim().length === 0) return;
    const id = threadsState.createThread(draftText, { from: pendingRange.from, to: pendingRange.to });
    if (id) {
      setDraftText('');
      onThreadCreated?.(id);
    }
  };

  const handleReply = (threadId: string): void => {
    const text = replyDrafts[threadId] ?? '';
    if (text.trim().length === 0) return;
    threadsState.reply(threadId, text);
    setReplyDrafts(prev => ({ ...prev, [threadId]: '' }));
  };

  return (
    <div className={styles.panel} role="complementary" aria-label="Comments" data-testid="compose-comment-thread-panel">
      <div className={styles.header}>
        <Text weight="semibold">Comments</Text>
        <Tooltip content="Close comments" relationship="description" withArrow>
          <Button
            appearance="subtle"
            size="small"
            icon={<Dismiss16Regular />}
            aria-label="Close comments"
            onClick={onClose}
            data-testid="compose-comment-thread-close"
          />
        </Tooltip>
      </div>

      {pendingRange ? (
        <div className={styles.composer} data-testid="compose-comment-new-composer">
          <Text size={200} className={styles.anchorPreview} data-testid="compose-comment-new-composer-preview">
            On: &ldquo;{truncate(pendingRange.preview, 80)}&rdquo;
          </Text>
          <Textarea
            value={draftText}
            onChange={(_, data) => setDraftText(data.value)}
            placeholder="Add a comment…"
            aria-label="New comment"
            resize="vertical"
            data-testid="compose-comment-new-input"
          />
          <div className={styles.composerActions}>
            <Button
              appearance="primary"
              size="small"
              disabled={draftText.trim().length === 0}
              onClick={handleCreate}
              data-testid="compose-comment-new-post"
            >
              Comment
            </Button>
          </div>
        </div>
      ) : (
        <Text size={200} className={styles.hint} data-testid="compose-comment-new-hint">
          Select text in the document to add a comment.
        </Text>
      )}

      <div className={styles.list}>
        {threadsState.threads.length === 0 ? (
          <Text size={200} className={styles.empty} data-testid="compose-comment-thread-empty">
            No comments yet.
          </Text>
        ) : (
          threadsState.threads.map(thread => (
            <div
              key={thread.id}
              className={mergeClasses(styles.thread, thread.resolved ? styles.threadResolved : undefined)}
              data-testid={`compose-comment-thread-${thread.id}`}
              data-resolved={thread.resolved ? 'true' : 'false'}
            >
              <div className={styles.threadHeader}>
                <Avatar name={thread.author} size={24} />
                <div className={styles.threadHeaderText}>
                  <Text
                    weight="semibold"
                    size={200}
                    className={styles.authorName}
                    data-testid={`compose-comment-author-${thread.id}`}
                  >
                    {thread.author}
                  </Text>
                  <Text size={100} className={styles.timestamp} data-testid={`compose-comment-timestamp-${thread.id}`}>
                    {formatTimestamp(thread.timestamp)}
                  </Text>
                </div>
                {thread.resolved ? (
                  <Badge
                    appearance="tint"
                    color="success"
                    icon={<CommentCheckmark16Regular />}
                    data-testid={`compose-comment-resolved-${thread.id}`}
                  >
                    Resolved
                  </Badge>
                ) : null}
              </div>
              <Text size={300} className={styles.threadBody} data-testid={`compose-comment-body-${thread.id}`}>
                {thread.text}
              </Text>

              {/* SCOPE GUARD — flat list, never a nested tree (see file header). */}
              {thread.replies.length > 0 ? (
                <div className={styles.replyList} data-testid={`compose-comment-replies-${thread.id}`}>
                  {thread.replies.map(reply => (
                    <div key={reply.id} className={styles.reply} data-testid={`compose-comment-reply-${reply.id}`}>
                      <Text weight="semibold" size={100} className={styles.authorName}>
                        {reply.author}
                      </Text>
                      <Text size={100} className={styles.timestamp}>
                        {formatTimestamp(reply.timestamp)}
                      </Text>
                      <Text size={200}>{reply.text}</Text>
                    </div>
                  ))}
                </div>
              ) : null}

              {!thread.resolved ? (
                <div className={styles.replyComposer}>
                  <Input
                    className={styles.replyInput}
                    value={replyDrafts[thread.id] ?? ''}
                    onChange={(_, data) => setReplyDrafts(prev => ({ ...prev, [thread.id]: data.value }))}
                    placeholder="Reply…"
                    aria-label={`Reply to ${thread.author}'s comment`}
                    data-testid={`compose-comment-reply-input-${thread.id}`}
                  />
                  <Button
                    appearance="subtle"
                    size="small"
                    disabled={(replyDrafts[thread.id] ?? '').trim().length === 0}
                    onClick={() => handleReply(thread.id)}
                    data-testid={`compose-comment-reply-submit-${thread.id}`}
                  >
                    Reply
                  </Button>
                  <Tooltip content="Resolve this thread" relationship="description" withArrow>
                    <Button
                      appearance="subtle"
                      size="small"
                      icon={<CommentCheckmark16Regular />}
                      onClick={() => threadsState.resolve(thread.id)}
                      data-testid={`compose-comment-resolve-${thread.id}`}
                    >
                      Resolve
                    </Button>
                  </Tooltip>
                </div>
              ) : null}
            </div>
          ))
        )}
      </div>
    </div>
  );
}

ComposeCommentThread.displayName = 'ComposeCommentThread';

export default ComposeCommentThread;
