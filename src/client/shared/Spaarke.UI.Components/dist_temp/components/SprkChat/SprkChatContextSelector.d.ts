/**
 * SprkChatContextSelector - Document + playbook dropdown selector with multi-document support
 *
 * Allows users to switch the document and playbook context for an active chat session.
 * Supports selecting up to 5 additional documents for multi-document AI context.
 * Uses Fluent UI v9 components (Select, Combobox, Tag, TagGroup, Button, CounterBadge, Tooltip).
 *
 * The additional document picker uses a Combobox with type-ahead search/filter,
 * enabling users to quickly find documents in large lists.
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 * @see ADR-012 - Shared Component Library
 */
import * as React from 'react';
import { ISprkChatContextSelectorProps } from './types';
/**
 * SprkChatContextSelector - Compact dropdowns for document and playbook selection
 * with multi-document support for additional context documents.
 *
 * @example
 * ```tsx
 * <SprkChatContextSelector
 *   documents={documents}
 *   playbooks={playbooks}
 *   selectedDocumentId={currentDocId}
 *   selectedPlaybookId={currentPlaybookId}
 *   onDocumentChange={(id) => switchContext(id)}
 *   onPlaybookChange={(id) => switchPlaybook(id)}
 *   additionalDocumentIds={additionalDocs}
 *   onAdditionalDocumentsChange={(ids) => updateAdditionalDocs(ids)}
 *   maxAdditionalDocuments={5}
 * />
 * ```
 */
export declare const SprkChatContextSelector: React.FC<ISprkChatContextSelectorProps>;
export default SprkChatContextSelector;
//# sourceMappingURL=SprkChatContextSelector.d.ts.map