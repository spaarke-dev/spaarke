/**
 * Jest mock for the shared `RichFilePreviewDialog`. Renders a minimal surface
 * that exposes the document name + the wired callbacks so the App's
 * row-click -> open-preview wiring can be asserted without the real modal
 * (which pulls in iframe/Graph machinery unsuitable for jsdom).
 */
import * as React from 'react';

export interface IFilePreviewDialogProps {
  open: boolean;
  documentName: string;
  documentId: string;
  documentType?: string;
  onClose: () => void;
  fetchPreviewUrl: () => Promise<string | null>;
  onOpenFile: (mode: 'desktop' | 'web') => void;
  onOpenRecord: () => void;
  onEmailDocument: () => void;
  onCopyLink: () => void;
}

export const RichFilePreviewDialog: React.FC<IFilePreviewDialogProps> = props => {
  if (!props.open) return null;
  return (
    <div data-testid="rich-file-preview-dialog" data-document-id={props.documentId}>
      <span data-testid="preview-doc-name">{props.documentName}</span>
      <button data-testid="preview-fetch-url" onClick={() => void props.fetchPreviewUrl()}>
        fetch
      </button>
      <button data-testid="preview-open-file" onClick={() => props.onOpenFile('desktop')}>
        open-file
      </button>
      <button data-testid="preview-open-record" onClick={() => props.onOpenRecord()}>
        open-record
      </button>
      <button data-testid="preview-close" onClick={() => props.onClose()}>
        close
      </button>
    </div>
  );
};

export default RichFilePreviewDialog;
