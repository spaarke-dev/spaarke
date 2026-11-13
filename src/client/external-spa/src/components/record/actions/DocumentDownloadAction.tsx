/**
 * DocumentDownloadAction — the Document record's toolbar "Download" (R3 task 066 `DocumentDownload`, moved from
 * R3's record-detail modal and started from the toolbar; R4 task 005, FR-05 / FR-07).
 *
 * The same download code the record's Documents section uses (`downloadDocumentFromParents` over the existing content
 * route; no new route, no signed link). It asks the parents the row is filed to in the BFF's order and the server decides
 * which one the caller may read. The action is offered only when `documentFileOf` finds a usable parent link and a file
 * (`hasFile !== false`); otherwise it is hidden, not disabled. A failure is shown here, with R3's text, never only
 * logged; the next attempt clears it. The accessible name keeps R3's "Download {name}" (it contains the label).
 */
import * as React from 'react';
import { MessageBar, MessageBarBody, Spinner } from '@fluentui/react-components';
import { downloadDocumentFromParents, downloadFailureLogText, downloadFailureMessage } from '../../DocumentLibrary';
import { documentFileOf } from '../sections';
import type { RecordActionHostContext } from '../recordTemplate.types';

export const DocumentDownloadAction: React.FC<RecordActionHostContext> = ({ record, recordId, onActionChange }) => {
  const file = React.useMemo(() => documentFileOf(record), [record]);
  const [error, setError] = React.useState<string | null>(null);
  // R3 showed a spinner on its button while the bytes were on their way; the toolbar button has none, so it shows here.
  const [downloading, setDownloading] = React.useState(false);
  // A second click while the bytes are on their way starts nothing (R3 disabled its button for this).
  const busy = React.useRef(false);

  const downloadRef = React.useRef<() => Promise<void>>(async () => undefined);
  downloadRef.current = async () => {
    if (!file || busy.current) return;
    busy.current = true;
    setDownloading(true);
    setError(null);
    try {
      await downloadDocumentFromParents(file.parents, recordId, file.name);
    } catch (err) {
      console.error(`[RecordTemplateModal] Download failed for document ${recordId}: ${downloadFailureLogText(err)}`);
      setError(downloadFailureMessage(err));
    } finally {
      busy.current = false;
      setDownloading(false);
    }
  };

  const visible = file !== null;
  const tooltip = file ? `Download ${file.name}` : undefined;
  React.useEffect(() => {
    onActionChange({ visible, tooltip, onClick: () => void downloadRef.current() });
  }, [visible, tooltip, onActionChange]);
  React.useEffect(() => () => onActionChange(null), [onActionChange]);

  if (downloading) return <Spinner size="tiny" label="Downloading..." />;
  if (!error) return null;
  return (
    <MessageBar intent="error" data-testid="record-detail-download-error">
      <MessageBarBody>{error}</MessageBarBody>
    </MessageBar>
  );
};
