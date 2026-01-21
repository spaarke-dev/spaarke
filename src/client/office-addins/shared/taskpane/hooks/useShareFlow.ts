import { useState, useCallback, useMemo } from 'react';

/**
 * Document result from search API.
 */
export interface ShareDocument {
  /** Unique document identifier */
  id: string;
  /** Document display name */
  name: string;
  /** File extension (e.g., '.docx', '.pdf') */
  extension: string;
  /** MIME content type */
  contentType: string;
  /** File size in bytes */
  size: number;
  /** Path or location in Spaarke */
  path: string;
  /** Last modified date ISO string */
  modifiedDate: string;
  /** Modified by user name */
  modifiedBy?: string;
  /** Associated entity type */
  associationType?: string;
  /** Associated entity name */
  associationName?: string;
  /** Thumbnail URL (optional) */
  thumbnailUrl?: string;
  /** Whether user can share this document */
  canShare: boolean;
  /** Available sharing roles */
  availableRoles: ShareRole[];
}

/**
 * Sharing role options.
 */
export type ShareRole = 'ViewOnly' | 'Download' | 'Edit';

/**
 * Share action type.
 */
export type ShareActionType = 'insertLink' | 'attachCopy' | 'copyLink';

/**
 * Share link generation result.
 */
export interface ShareLink {
  documentId: string;
  url: string;
  title: string;
  role: ShareRole;
  expiresAt?: string;
}

/**
 * Attachment download result.
 */
export interface ShareAttachment {
  documentId: string;
  filename: string;
  contentType: string;
  size: number;
  downloadUrl: string;
  urlExpiry: string;
}

/**
 * Share flow state.
 */
export interface ShareFlowState {
  /** Search query string */
  searchQuery: string;
  /** Whether search is in progress */
  isSearching: boolean;
  /** Search results */
  searchResults: ShareDocument[];
  /** Search error message */
  searchError: string | null;
  /** Selected document IDs */
  selectedDocumentIds: Set<string>;
  /** Selected share role */
  selectedRole: ShareRole;
  /** Generated share links */
  generatedLinks: ShareLink[];
  /** Prepared attachments */
  preparedAttachments: ShareAttachment[];
  /** Whether generating links/attachments */
  isGenerating: boolean;
  /** Whether inserting into email/document */
  isInserting: boolean;
  /** Generation/insertion error */
  actionError: string | null;
  /** Success message */
  successMessage: string | null;
  /** Whether to grant access to recipients (Outlook compose mode) */
  grantAccessToRecipients: boolean;
  /** Recent documents (quick access) */
  recentDocuments: ShareDocument[];
  /** Whether loading recent documents */
  isLoadingRecent: boolean;
}

/**
 * Share flow API callbacks.
 */
export interface ShareFlowCallbacks {
  /** Search for documents */
  onSearch?: (query: string) => Promise<ShareDocument[]>;
  /** Generate share links */
  onGenerateLinks?: (
    documentIds: string[],
    role: ShareRole,
    recipientEmails?: string[]
  ) => Promise<ShareLink[]>;
  /** Prepare document attachments */
  onPrepareAttachments?: (documentIds: string[]) => Promise<ShareAttachment[]>;
  /** Insert links into email/document via host adapter */
  onInsertLinks?: (links: ShareLink[]) => Promise<void>;
  /** Attach files to email via host adapter (Outlook only) */
  onAttachFiles?: (attachments: ShareAttachment[]) => Promise<void>;
  /** Fetch recent documents */
  onFetchRecent?: () => Promise<ShareDocument[]>;
  /** Copy text to clipboard */
  onCopyToClipboard?: (text: string) => Promise<void>;
}

/**
 * Share flow actions returned by the hook.
 */
export interface ShareFlowActions {
  /** Update search query */
  setSearchQuery: (query: string) => void;
  /** Execute search */
  executeSearch: () => Promise<void>;
  /** Clear search results */
  clearSearch: () => void;
  /** Select a document */
  selectDocument: (documentId: string) => void;
  /** Deselect a document */
  deselectDocument: (documentId: string) => void;
  /** Toggle document selection */
  toggleDocumentSelection: (documentId: string) => void;
  /** Select all documents */
  selectAll: () => void;
  /** Clear all selections */
  clearSelection: () => void;
  /** Set share role */
  setSelectedRole: (role: ShareRole) => void;
  /** Toggle grant access option */
  setGrantAccessToRecipients: (value: boolean) => void;
  /** Generate share links for selected documents */
  generateLinks: (recipientEmails?: string[]) => Promise<void>;
  /** Prepare attachments for selected documents */
  prepareAttachments: () => Promise<void>;
  /** Insert generated links into email/document */
  insertLinks: () => Promise<void>;
  /** Attach prepared files to email */
  attachFiles: () => Promise<void>;
  /** Copy all generated links to clipboard */
  copyLinksToClipboard: () => Promise<void>;
  /** Load recent documents */
  loadRecentDocuments: () => Promise<void>;
  /** Clear error messages */
  clearError: () => void;
  /** Clear success message */
  clearSuccess: () => void;
  /** Reset entire flow */
  reset: () => void;
}

/**
 * Share flow hook result.
 */
export interface UseShareFlowResult {
  /** Current state */
  state: ShareFlowState;
  /** Available actions */
  actions: ShareFlowActions;
  /** Selected documents (derived) */
  selectedDocuments: ShareDocument[];
  /** Whether any documents are selected */
  hasSelection: boolean;
  /** Whether links are ready to insert */
  hasLinksReady: boolean;
  /** Whether attachments are ready */
  hasAttachmentsReady: boolean;
  /** Total size of selected documents (bytes) */
  totalSelectedSize: number;
  /** Whether selected documents exceed attachment size limit */
  exceedsAttachmentLimit: boolean;
}

/** Default attachment size limit (25MB per file, 100MB total) */
const MAX_ATTACHMENT_SIZE_BYTES = 25 * 1024 * 1024;
const MAX_TOTAL_ATTACHMENT_SIZE_BYTES = 100 * 1024 * 1024;

/**
 * Initial state for share flow.
 */
const initialState: ShareFlowState = {
  searchQuery: '',
  isSearching: false,
  searchResults: [],
  searchError: null,
  selectedDocumentIds: new Set(),
  selectedRole: 'ViewOnly',
  generatedLinks: [],
  preparedAttachments: [],
  isGenerating: false,
  isInserting: false,
  actionError: null,
  successMessage: null,
  grantAccessToRecipients: false,
  recentDocuments: [],
  isLoadingRecent: false,
};

/**
 * Hook to manage share flow state and actions.
 *
 * Provides:
 * - Document search with results
 * - Multi-document selection
 * - Share link generation
 * - Attachment preparation
 * - Insert/attach actions
 * - Recent documents
 *
 * @param callbacks - API callbacks for share operations
 * @returns Share flow state and actions
 *
 * @example
 * ```tsx
 * const { state, actions, selectedDocuments, hasSelection } = useShareFlow({
 *   onSearch: apiClient.searchDocuments,
 *   onGenerateLinks: apiClient.generateShareLinks,
 *   onInsertLinks: hostAdapter.insertLinks,
 * });
 * ```
 */
export function useShareFlow(callbacks: ShareFlowCallbacks = {}): UseShareFlowResult {
  const [state, setState] = useState<ShareFlowState>(initialState);

  // Derived: selected documents from search results and recent
  const selectedDocuments = useMemo(() => {
    const allDocuments = [...state.searchResults, ...state.recentDocuments];
    const uniqueDocuments = allDocuments.filter(
      (doc, index, self) => self.findIndex((d) => d.id === doc.id) === index
    );
    return uniqueDocuments.filter((doc) => state.selectedDocumentIds.has(doc.id));
  }, [state.searchResults, state.recentDocuments, state.selectedDocumentIds]);

  // Derived: has selection
  const hasSelection = state.selectedDocumentIds.size > 0;

  // Derived: has links ready
  const hasLinksReady = state.generatedLinks.length > 0;

  // Derived: has attachments ready
  const hasAttachmentsReady = state.preparedAttachments.length > 0;

  // Derived: total selected size
  const totalSelectedSize = useMemo(
    () => selectedDocuments.reduce((sum, doc) => sum + doc.size, 0),
    [selectedDocuments]
  );

  // Derived: exceeds attachment limit
  const exceedsAttachmentLimit = useMemo(() => {
    if (selectedDocuments.length === 0) return false;
    const exceedsSingle = selectedDocuments.some((doc) => doc.size > MAX_ATTACHMENT_SIZE_BYTES);
    const exceedsTotal = totalSelectedSize > MAX_TOTAL_ATTACHMENT_SIZE_BYTES;
    return exceedsSingle || exceedsTotal;
  }, [selectedDocuments, totalSelectedSize]);

  // Actions
  const setSearchQuery = useCallback((query: string) => {
    setState((prev) => ({ ...prev, searchQuery: query }));
  }, []);

  const executeSearch = useCallback(async () => {
    const query = state.searchQuery.trim();
    if (!query || !callbacks.onSearch) return;

    setState((prev) => ({
      ...prev,
      isSearching: true,
      searchError: null,
      searchResults: [],
    }));

    try {
      const results = await callbacks.onSearch(query);
      setState((prev) => ({
        ...prev,
        isSearching: false,
        searchResults: results,
      }));
    } catch (error) {
      setState((prev) => ({
        ...prev,
        isSearching: false,
        searchError: error instanceof Error ? error.message : 'Search failed',
      }));
    }
  }, [state.searchQuery, callbacks]);

  const clearSearch = useCallback(() => {
    setState((prev) => ({
      ...prev,
      searchQuery: '',
      searchResults: [],
      searchError: null,
    }));
  }, []);

  const selectDocument = useCallback((documentId: string) => {
    setState((prev) => {
      const newSelected = new Set(prev.selectedDocumentIds);
      newSelected.add(documentId);
      return {
        ...prev,
        selectedDocumentIds: newSelected,
        generatedLinks: [], // Clear links when selection changes
        preparedAttachments: [],
        successMessage: null,
      };
    });
  }, []);

  const deselectDocument = useCallback((documentId: string) => {
    setState((prev) => {
      const newSelected = new Set(prev.selectedDocumentIds);
      newSelected.delete(documentId);
      return {
        ...prev,
        selectedDocumentIds: newSelected,
        generatedLinks: [],
        preparedAttachments: [],
        successMessage: null,
      };
    });
  }, []);

  const toggleDocumentSelection = useCallback((documentId: string) => {
    setState((prev) => {
      const newSelected = new Set(prev.selectedDocumentIds);
      if (newSelected.has(documentId)) {
        newSelected.delete(documentId);
      } else {
        newSelected.add(documentId);
      }
      return {
        ...prev,
        selectedDocumentIds: newSelected,
        generatedLinks: [],
        preparedAttachments: [],
        successMessage: null,
      };
    });
  }, []);

  const selectAll = useCallback(() => {
    const allDocuments = [...state.searchResults, ...state.recentDocuments];
    const shareableIds = allDocuments.filter((doc) => doc.canShare).map((doc) => doc.id);
    setState((prev) => ({
      ...prev,
      selectedDocumentIds: new Set(shareableIds),
      generatedLinks: [],
      preparedAttachments: [],
      successMessage: null,
    }));
  }, [state.searchResults, state.recentDocuments]);

  const clearSelection = useCallback(() => {
    setState((prev) => ({
      ...prev,
      selectedDocumentIds: new Set(),
      generatedLinks: [],
      preparedAttachments: [],
      successMessage: null,
    }));
  }, []);

  const setSelectedRole = useCallback((role: ShareRole) => {
    setState((prev) => ({
      ...prev,
      selectedRole: role,
      generatedLinks: [], // Clear links when role changes
    }));
  }, []);

  const setGrantAccessToRecipients = useCallback((value: boolean) => {
    setState((prev) => ({ ...prev, grantAccessToRecipients: value }));
  }, []);

  const generateLinks = useCallback(
    async (recipientEmails?: string[]) => {
      if (!callbacks.onGenerateLinks || state.selectedDocumentIds.size === 0) return;

      setState((prev) => ({
        ...prev,
        isGenerating: true,
        actionError: null,
        generatedLinks: [],
      }));

      try {
        const documentIds = Array.from(state.selectedDocumentIds);
        const links = await callbacks.onGenerateLinks(
          documentIds,
          state.selectedRole,
          state.grantAccessToRecipients ? recipientEmails : undefined
        );
        setState((prev) => ({
          ...prev,
          isGenerating: false,
          generatedLinks: links,
          successMessage: `Generated ${links.length} share link${links.length !== 1 ? 's' : ''}`,
        }));
      } catch (error) {
        setState((prev) => ({
          ...prev,
          isGenerating: false,
          actionError: error instanceof Error ? error.message : 'Failed to generate links',
        }));
      }
    },
    [callbacks, state.selectedDocumentIds, state.selectedRole, state.grantAccessToRecipients]
  );

  const prepareAttachments = useCallback(async () => {
    if (!callbacks.onPrepareAttachments || state.selectedDocumentIds.size === 0) return;

    setState((prev) => ({
      ...prev,
      isGenerating: true,
      actionError: null,
      preparedAttachments: [],
    }));

    try {
      const documentIds = Array.from(state.selectedDocumentIds);
      const attachments = await callbacks.onPrepareAttachments(documentIds);
      setState((prev) => ({
        ...prev,
        isGenerating: false,
        preparedAttachments: attachments,
        successMessage: `Prepared ${attachments.length} attachment${attachments.length !== 1 ? 's' : ''}`,
      }));
    } catch (error) {
      setState((prev) => ({
        ...prev,
        isGenerating: false,
        actionError: error instanceof Error ? error.message : 'Failed to prepare attachments',
      }));
    }
  }, [callbacks, state.selectedDocumentIds]);

  const insertLinks = useCallback(async () => {
    if (!callbacks.onInsertLinks || state.generatedLinks.length === 0) return;

    setState((prev) => ({
      ...prev,
      isInserting: true,
      actionError: null,
    }));

    try {
      await callbacks.onInsertLinks(state.generatedLinks);
      setState((prev) => ({
        ...prev,
        isInserting: false,
        successMessage: `Inserted ${state.generatedLinks.length} link${state.generatedLinks.length !== 1 ? 's' : ''} into email`,
      }));
    } catch (error) {
      setState((prev) => ({
        ...prev,
        isInserting: false,
        actionError: error instanceof Error ? error.message : 'Failed to insert links',
      }));
    }
  }, [callbacks, state.generatedLinks]);

  const attachFiles = useCallback(async () => {
    if (!callbacks.onAttachFiles || state.preparedAttachments.length === 0) return;

    setState((prev) => ({
      ...prev,
      isInserting: true,
      actionError: null,
    }));

    try {
      await callbacks.onAttachFiles(state.preparedAttachments);
      setState((prev) => ({
        ...prev,
        isInserting: false,
        successMessage: `Attached ${state.preparedAttachments.length} file${state.preparedAttachments.length !== 1 ? 's' : ''} to email`,
      }));
    } catch (error) {
      setState((prev) => ({
        ...prev,
        isInserting: false,
        actionError: error instanceof Error ? error.message : 'Failed to attach files',
      }));
    }
  }, [callbacks, state.preparedAttachments]);

  const copyLinksToClipboard = useCallback(async () => {
    if (state.generatedLinks.length === 0) return;

    const linkText = state.generatedLinks
      .map((link) => `${link.title}: ${link.url}`)
      .join('\n');

    try {
      if (callbacks.onCopyToClipboard) {
        await callbacks.onCopyToClipboard(linkText);
      } else {
        await navigator.clipboard.writeText(linkText);
      }
      setState((prev) => ({
        ...prev,
        successMessage: 'Links copied to clipboard',
      }));
    } catch (error) {
      setState((prev) => ({
        ...prev,
        actionError: error instanceof Error ? error.message : 'Failed to copy to clipboard',
      }));
    }
  }, [callbacks, state.generatedLinks]);

  const loadRecentDocuments = useCallback(async () => {
    if (!callbacks.onFetchRecent) return;

    setState((prev) => ({
      ...prev,
      isLoadingRecent: true,
    }));

    try {
      const recent = await callbacks.onFetchRecent();
      setState((prev) => ({
        ...prev,
        isLoadingRecent: false,
        recentDocuments: recent,
      }));
    } catch (error) {
      setState((prev) => ({
        ...prev,
        isLoadingRecent: false,
        // Don't set error for recent - it's not critical
      }));
    }
  }, [callbacks]);

  const clearError = useCallback(() => {
    setState((prev) => ({
      ...prev,
      actionError: null,
      searchError: null,
    }));
  }, []);

  const clearSuccess = useCallback(() => {
    setState((prev) => ({ ...prev, successMessage: null }));
  }, []);

  const reset = useCallback(() => {
    setState(initialState);
  }, []);

  const actions: ShareFlowActions = useMemo(
    () => ({
      setSearchQuery,
      executeSearch,
      clearSearch,
      selectDocument,
      deselectDocument,
      toggleDocumentSelection,
      selectAll,
      clearSelection,
      setSelectedRole,
      setGrantAccessToRecipients,
      generateLinks,
      prepareAttachments,
      insertLinks,
      attachFiles,
      copyLinksToClipboard,
      loadRecentDocuments,
      clearError,
      clearSuccess,
      reset,
    }),
    [
      setSearchQuery,
      executeSearch,
      clearSearch,
      selectDocument,
      deselectDocument,
      toggleDocumentSelection,
      selectAll,
      clearSelection,
      setSelectedRole,
      setGrantAccessToRecipients,
      generateLinks,
      prepareAttachments,
      insertLinks,
      attachFiles,
      copyLinksToClipboard,
      loadRecentDocuments,
      clearError,
      clearSuccess,
      reset,
    ]
  );

  return {
    state,
    actions,
    selectedDocuments,
    hasSelection,
    hasLinksReady,
    hasAttachmentsReady,
    totalSelectedSize,
    exceedsAttachmentLimit,
  };
}
