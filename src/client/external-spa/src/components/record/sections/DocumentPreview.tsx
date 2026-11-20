/**
 * DocumentPreview — the Document record's inline file preview (spaarke-SPA-external-access-platform-r4 task 022, FR-14 / FR-15).
 *
 * WHAT IS PREVIEWED. Exactly PDF, PNG, JPEG, GIF and WebP up to 25 MB (owner 2026-11-09), decided by `previewKindOf` from the
 * document's `sprk_filetype` and, when that is absent, the extension of its name. Everything else, a larger file and a
 * document with no file shows a short Download-only note; the Download itself is the record toolbar's action
 * (`DocumentDownloadAction`) and this component never blocks or replaces it.
 *
 * WHERE THE BYTES COME FROM. Only the existing external content route (`documentContentPath`), read with the SPA's
 * authenticated binary read `bffApiBlob` (the spec says `authenticatedFetch`; this SPA does not use `@spaarke/auth`, see
 * `auth/bff-client.ts`), then an object URL. No Graph, no signed URL, no SPE pointer, no new route (ADR-007, ADR-028 A1).
 * The route answers `application/octet-stream`, so the blob is re-typed with the MIME type decided from the file type before
 * `URL.createObjectURL`: what the browser renders is what the SPA decided, never what the bytes claim to be.
 *
 * SIZE CAP. `bffApiBlob` is asked for at most 25 MB: a `Content-Length` over it aborts the request, and with no header the
 * bytes are discarded when `blob.size` is over it (`BlobTooLargeError`). An oversize body is never rendered.
 *
 * PARENTS. A document can be filed to several records, so the content route is asked per parent in the BFF's order and moves
 * on only on a 403, the rule of `downloadDocumentFromParents` (DocumentLibrary.tsx; that function saves the file, so this
 * mirrors its loop for a read. Task 023 reworks DocumentLibrary and may hoist the shared loop).
 *
 * LIFETIME. The object URL is revoked when the section unmounts and whenever the shown document (or its parents) changes
 * (record paging); an in-flight read is aborted then too. A late answer for a document no longer shown is dropped.
 *
 * ADR-021: Fluent v9 semantic tokens via `makeStyles` only. The stage mirrors `PreviewModal`'s `previewStage`.
 */
import * as React from 'react';
import { makeStyles, MessageBar, MessageBarBody, Spinner, Text, tokens } from '@fluentui/react-components';
import { documentContentPath, type DocumentRoot } from '../../../api/web-api-client';
import { ApiError } from '../../../types';
import { BlobTooLargeError, bffApiBlob } from '../../../auth/bff-client';
import { downloadFailureLogText, downloadFailureMessage } from '../../DocumentLibrary';

/** Largest file shown inline: 25 MB (owner 2026-11-09). A file of exactly this size is previewed. */
export const PREVIEW_MAX_BYTES = 26_214_400;

export type PreviewKind = { kind: 'pdf'; mime: 'application/pdf' } | { kind: 'image'; mime: string };

const PREVIEWABLE: Readonly<Record<string, PreviewKind>> = {
  pdf: { kind: 'pdf', mime: 'application/pdf' },
  png: { kind: 'image', mime: 'image/png' },
  jpg: { kind: 'image', mime: 'image/jpeg' },
  jpeg: { kind: 'image', mime: 'image/jpeg' },
  gif: { kind: 'image', mime: 'image/gif' },
  webp: { kind: 'image', mime: 'image/webp' },
};

/**
 * How a document previews, or null (Download only). `fileType` is the row's `sprk_filetype` (the BFF stores the upper-cased
 * extension without its dot: "PDF"); when it is blank or absent the extension of `name` decides. A `fileType` that is present
 * and not previewable (DOCX) is final: the name is not consulted then.
 */
export function previewKindOf(
  fileType: string | null | undefined,
  name: string | null | undefined
): PreviewKind | null {
  const fromType = (fileType ?? '').trim().replace(/^\./, '').toLowerCase();
  let extension = fromType;
  if (!extension) {
    const dot = (name ?? '').trim().lastIndexOf('.');
    extension =
      dot >= 0
        ? (name ?? '')
            .trim()
            .slice(dot + 1)
            .toLowerCase()
        : '';
  }
  // Own-property lookup only: an extension such as "constructor" is not a type.
  return Object.prototype.hasOwnProperty.call(PREVIEWABLE, extension) ? PREVIEWABLE[extension] : null;
}

/** The document to preview: the parents to ask (BFF order) and the name for the accessible names. */
export interface PreviewFile {
  parents: ReadonlyArray<{ root: DocumentRoot; id: string }>;
  name: string;
}

export interface DocumentPreviewProps {
  documentId: string;
  /** The document's downloadable file; null = it has none (no parent link, or `hasFile === false`): nothing is read. */
  file: PreviewFile | null;
  /** The row's `sprk_filetype`, when it was read. */
  fileType?: string | null;
}

type PreviewState =
  | { status: 'loading'; key: string }
  | { status: 'ready'; key: string; url: string }
  | { status: 'too-large'; key: string }
  | { status: 'error'; key: string; message: string };

/** Reads the document's bytes through the first parent that allows it: moves on only on 403, any other failure stops. */
async function readDocumentFromParents(
  parents: PreviewFile['parents'],
  documentId: string,
  signal: AbortSignal
): Promise<Blob> {
  let lastRefusal: unknown = new Error('No parent record to read the document through.');
  for (const parent of parents) {
    try {
      return await bffApiBlob(
        documentContentPath(parent.root, parent.id, documentId),
        { signal },
        {
          maxBytes: PREVIEW_MAX_BYTES,
        }
      );
    } catch (err) {
      if (!(err instanceof ApiError) || err.statusCode !== 403) throw err;
      lastRefusal = err;
    }
  }
  throw lastRefusal;
}

const useStyles = makeStyles({
  // PreviewModal's `previewStage`: neutral background 3, centred, clamped to its cell, overflow hidden. The height is the
  // part PreviewModal gets from its grid: a fraction of the viewport, capped, so a PDF has room and the record still scrolls.
  stage: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground3,
    minWidth: 0,
    minHeight: 0,
    height: 'min(70vh, 720px)',
    overflow: 'hidden',
    borderRadius: tokens.borderRadiusMedium,
  },
  stageText: {
    paddingBlock: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground2,
  },
  frame: {
    width: '100%',
    height: '100%',
    border: 'none',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  image: {
    maxWidth: '100%',
    maxHeight: '100%',
    objectFit: 'contain',
  },
});

export const DocumentPreview: React.FC<DocumentPreviewProps> = ({ documentId, file, fileType }) => {
  const styles = useStyles();
  const name = file?.name ?? '';
  const preview = React.useMemo(() => (file ? previewKindOf(fileType, name) : null), [file, fileType, name]);
  const mime = preview?.mime ?? null;
  // A stable identity for "which file is shown": the effect re-reads when it changes, not when the record object is re-made.
  const parentsKey = file ? file.parents.map(p => `${p.root}:${p.id}`).join('|') : '';
  const key = `${documentId}#${parentsKey}#${mime ?? ''}`;
  const [state, setState] = React.useState<PreviewState | null>(null);

  // `file` is read through a ref: its identity changes with the record object, its content is `parentsKey`.
  const parentsRef = React.useRef<PreviewFile['parents']>([]);
  parentsRef.current = file?.parents ?? [];

  React.useEffect(() => {
    if (!mime || !parentsKey) return undefined;
    const controller = new AbortController();
    let cancelled = false;
    let objectUrl: string | null = null;
    setState({ status: 'loading', key });
    void (async () => {
      try {
        const bytes = await readDocumentFromParents(parentsRef.current, documentId, controller.signal);
        if (cancelled) return;
        objectUrl = window.URL.createObjectURL(new Blob([bytes], { type: mime }));
        setState({ status: 'ready', key, url: objectUrl });
      } catch (err) {
        if (cancelled) return;
        if (err instanceof BlobTooLargeError) {
          setState({ status: 'too-large', key });
          return;
        }
        console.error(`[DocumentPreview] Preview failed for document ${documentId}: ${downloadFailureLogText(err)}`);
        setState({ status: 'error', key, message: downloadFailureMessage(err) });
      }
    })();
    return () => {
      cancelled = true;
      controller.abort();
      if (objectUrl) window.URL.revokeObjectURL(objectUrl);
    };
  }, [documentId, parentsKey, mime, key]);

  // FR-15: no file, nothing is read and nothing is previewed.
  if (!file) {
    return (
      <Text className={styles.stageText} data-testid="record-preview-no-file">
        There is no file to show for this document.
      </Text>
    );
  }
  if (!preview) {
    return (
      <Text className={styles.stageText} data-testid="record-preview-download-only">
        This type of file cannot be previewed here. Use Download in the toolbar to open it.
      </Text>
    );
  }

  // A state left over from another document (record paging) is never shown: it is for a different key.
  const current = state && state.key === key ? state : null;
  if (current?.status === 'too-large') {
    return (
      <Text className={styles.stageText} data-testid="record-preview-too-large">
        This file is larger than 25 MB, so it cannot be previewed here. Use Download in the toolbar to open it.
      </Text>
    );
  }
  if (current?.status === 'error') {
    return (
      <MessageBar intent="error" data-testid="record-preview-error">
        <MessageBarBody>{current.message}</MessageBarBody>
      </MessageBar>
    );
  }
  return (
    <div className={styles.stage} data-testid="record-preview-stage">
      {current?.status === 'ready' ? (
        preview.kind === 'pdf' ? (
          <iframe
            className={styles.frame}
            src={current.url}
            title={`Preview of ${name}`}
            data-testid="record-preview-pdf"
          />
        ) : (
          <img
            className={styles.image}
            src={current.url}
            alt={`Preview of ${name}`}
            data-testid="record-preview-image"
          />
        )
      ) : (
        <Spinner size="small" label="Loading preview..." data-testid="record-preview-loading" />
      )}
    </div>
  );
};
