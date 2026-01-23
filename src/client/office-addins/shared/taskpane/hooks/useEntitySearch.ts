import { useState, useCallback, useRef, useEffect, useMemo } from 'react';

/**
 * Entity types that can be selected as association targets.
 */
export type EntityType = 'Matter' | 'Project' | 'Invoice' | 'Account' | 'Contact';

/**
 * All valid entity types for filtering.
 */
export const ALL_ENTITY_TYPES: EntityType[] = [
  'Matter',
  'Project',
  'Invoice',
  'Account',
  'Contact',
];

/**
 * Entity logical names mapping for Dataverse.
 */
export const ENTITY_LOGICAL_NAMES: Record<EntityType, string> = {
  Matter: 'sprk_matter',
  Project: 'sprk_project',
  Invoice: 'sprk_invoice',
  Account: 'account',
  Contact: 'contact',
};

/**
 * Search result entity item.
 */
export interface EntitySearchResult {
  /** Unique identifier */
  id: string;
  /** Entity type */
  entityType: EntityType;
  /** Dataverse logical name */
  logicalName: string;
  /** Display name */
  name: string;
  /** Secondary display information */
  displayInfo?: string;
  /** Icon URL or identifier */
  iconUrl?: string;
}

/**
 * Recent entity item with usage timestamp.
 */
export interface RecentEntity extends EntitySearchResult {
  /** Last time this entity was used */
  lastUsed: string;
}

/**
 * Hook options.
 */
export interface UseEntitySearchOptions {
  /** Debounce delay in milliseconds (default: 300ms) */
  debounceMs?: number;
  /** Minimum characters to trigger search (default: 2) */
  minChars?: number;
  /** Maximum results to return (default: 20) */
  maxResults?: number;
  /** Initial entity type filter */
  initialTypeFilter?: EntityType[];
  /** API base URL for search endpoint */
  apiBaseUrl?: string;
  /** Access token getter for authenticated requests */
  getAccessToken?: () => Promise<string>;
}

/**
 * Hook return value.
 */
export interface UseEntitySearchResult {
  /** Current search query */
  query: string;
  /** Set search query (triggers debounced search) */
  setQuery: (query: string) => void;
  /** Search results */
  results: EntitySearchResult[];
  /** Whether a search is in progress */
  isLoading: boolean;
  /** Error message if search failed */
  error: string | null;
  /** Clear error state */
  clearError: () => void;
  /** Recent entities */
  recentEntities: RecentEntity[];
  /** Entity type filter */
  typeFilter: EntityType[];
  /** Set entity type filter */
  setTypeFilter: (types: EntityType[]) => void;
  /** Toggle a single entity type in filter */
  toggleTypeFilter: (type: EntityType) => void;
  /** Whether there are more results available */
  hasMore: boolean;
  /** Total count of matching results */
  totalCount: number;
  /** Add entity to recent list */
  addToRecent: (entity: EntitySearchResult) => void;
  /** Perform search immediately (bypasses debounce) */
  searchNow: () => Promise<void>;
  /** Clear search query and results */
  clear: () => void;
}

/**
 * Storage key for recent entities.
 */
const RECENT_ENTITIES_KEY = 'spaarke-recent-entities';
const MAX_RECENT_ENTITIES = 10;

/**
 * Get recent entities from sessionStorage.
 */
function getStoredRecentEntities(): RecentEntity[] {
  if (typeof sessionStorage === 'undefined') {
    return [];
  }

  try {
    const stored = sessionStorage.getItem(RECENT_ENTITIES_KEY);
    if (!stored) {
      return [];
    }
    return JSON.parse(stored) as RecentEntity[];
  } catch {
    return [];
  }
}

/**
 * Save recent entities to sessionStorage.
 */
function saveRecentEntities(entities: RecentEntity[]): void {
  if (typeof sessionStorage === 'undefined') {
    return;
  }

  try {
    sessionStorage.setItem(
      RECENT_ENTITIES_KEY,
      JSON.stringify(entities.slice(0, MAX_RECENT_ENTITIES))
    );
  } catch {
    // Ignore storage errors
  }
}

/**
 * Mock search function for development/testing.
 * In production, this should call the actual /office/search/entities endpoint.
 */
async function mockSearchEntities(
  query: string,
  typeFilter: EntityType[],
  maxResults: number
): Promise<{ results: EntitySearchResult[]; totalCount: number; hasMore: boolean }> {
  // Simulate network delay
  await new Promise((resolve) => setTimeout(resolve, 200));

  // Mock data - in production, call API
  const mockEntities: EntitySearchResult[] = [
    { id: '1', entityType: 'Matter', logicalName: 'sprk_matter', name: 'Smith vs Jones', displayInfo: 'Client: Acme Corp | Status: Active' },
    { id: '2', entityType: 'Matter', logicalName: 'sprk_matter', name: 'Johnson Estate Planning', displayInfo: 'Client: Johnson Family | Status: Active' },
    { id: '3', entityType: 'Project', logicalName: 'sprk_project', name: 'Website Redesign', displayInfo: 'Account: TechCorp | Due: Mar 2026' },
    { id: '4', entityType: 'Project', logicalName: 'sprk_project', name: 'Mobile App Development', displayInfo: 'Account: StartupXYZ | Status: In Progress' },
    { id: '5', entityType: 'Invoice', logicalName: 'sprk_invoice', name: 'INV-2026-001', displayInfo: 'Amount: $5,000 | Status: Pending' },
    { id: '6', entityType: 'Invoice', logicalName: 'sprk_invoice', name: 'INV-2026-002', displayInfo: 'Amount: $12,500 | Status: Paid' },
    { id: '7', entityType: 'Account', logicalName: 'account', name: 'Acme Corporation', displayInfo: 'Industry: Manufacturing | City: Chicago' },
    { id: '8', entityType: 'Account', logicalName: 'account', name: 'TechCorp Industries', displayInfo: 'Industry: Technology | City: San Francisco' },
    { id: '9', entityType: 'Contact', logicalName: 'contact', name: 'John Smith', displayInfo: 'Email: john@acme.com | Account: Acme Corp' },
    { id: '10', entityType: 'Contact', logicalName: 'contact', name: 'Jane Doe', displayInfo: 'Email: jane@techcorp.com | Account: TechCorp' },
  ];

  // Filter by type
  let filtered = mockEntities;
  if (typeFilter.length > 0 && typeFilter.length < ALL_ENTITY_TYPES.length) {
    filtered = filtered.filter((e) => typeFilter.includes(e.entityType));
  }

  // Filter by query
  if (query.length >= 2) {
    const lowerQuery = query.toLowerCase();
    filtered = filtered.filter(
      (e) =>
        e.name.toLowerCase().includes(lowerQuery) ||
        (e.displayInfo && e.displayInfo.toLowerCase().includes(lowerQuery))
    );
  }

  const totalCount = filtered.length;
  const results = filtered.slice(0, maxResults);
  const hasMore = totalCount > maxResults;

  return { results, totalCount, hasMore };
}

/**
 * React hook for entity search with debouncing, type filtering, and recent items.
 *
 * Supports typeahead search for Matter, Project, Invoice, Account, and Contact entities.
 * Results are shown within 500ms per spec requirement (debounce + API call).
 *
 * @example
 * ```tsx
 * const { query, setQuery, results, isLoading, typeFilter, setTypeFilter } = useEntitySearch();
 *
 * return (
 *   <Combobox
 *     value={query}
 *     onInput={(e) => setQuery(e.target.value)}
 *   >
 *     {results.map((entity) => (
 *       <Option key={entity.id}>{entity.name}</Option>
 *     ))}
 *   </Combobox>
 * );
 * ```
 */
export function useEntitySearch(options: UseEntitySearchOptions = {}): UseEntitySearchResult {
  const {
    debounceMs = 300,
    minChars = 2,
    maxResults = 20,
    initialTypeFilter = [],
    apiBaseUrl,
    getAccessToken,
  } = options;

  // State
  const [query, setQueryState] = useState('');
  const [results, setResults] = useState<EntitySearchResult[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [recentEntities, setRecentEntities] = useState<RecentEntity[]>(() =>
    getStoredRecentEntities()
  );
  const [typeFilter, setTypeFilterState] = useState<EntityType[]>(initialTypeFilter);
  const [hasMore, setHasMore] = useState(false);
  const [totalCount, setTotalCount] = useState(0);

  // Refs for debouncing
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);

  // Clear error
  const clearError = useCallback(() => {
    setError(null);
  }, []);

  // Perform search
  const performSearch = useCallback(
    async (searchQuery: string, filter: EntityType[]) => {
      // Cancel any pending request
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }

      // Check minimum characters
      if (searchQuery.length < minChars) {
        setResults([]);
        setTotalCount(0);
        setHasMore(false);
        return;
      }

      abortControllerRef.current = new AbortController();
      setIsLoading(true);
      setError(null);

      try {
        // Use real API if apiBaseUrl and getAccessToken are provided
        if (apiBaseUrl && getAccessToken) {
          const token = await getAccessToken();
          const typeParam = filter.length > 0 ? `&type=${filter.join(',')}` : '';

          const response = await fetch(
            `${apiBaseUrl}/office/search/entities?q=${encodeURIComponent(searchQuery)}${typeParam}&top=${maxResults}`,
            {
              headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
              },
              signal: abortControllerRef.current.signal,
            }
          );

          if (!response.ok) {
            throw new Error(`Search failed: ${response.status} ${response.statusText}`);
          }

          const data = await response.json();

          // Map API response to EntitySearchResult format
          const mappedResults: EntitySearchResult[] = (data.results || []).map((item: {
            id: string;
            entityType: string;
            logicalName: string;
            name: string;
            displayInfo?: string;
          }) => ({
            id: item.id,
            entityType: item.entityType as EntityType,
            logicalName: item.logicalName,
            name: item.name,
            displayInfo: item.displayInfo,
          }));

          setResults(mappedResults);
          setTotalCount(data.totalCount || mappedResults.length);
          setHasMore(data.hasMore || false);
        } else {
          // Fall back to mock data if API is not configured
          console.warn('[useEntitySearch] API not configured, using mock data');
          const data = await mockSearchEntities(searchQuery, filter, maxResults);
          setResults(data.results);
          setTotalCount(data.totalCount);
          setHasMore(data.hasMore);
        }
      } catch (err) {
        if (err instanceof Error && err.name === 'AbortError') {
          // Request was cancelled, ignore
          return;
        }
        setError('Failed to search entities. Please try again.');
        setResults([]);
        setTotalCount(0);
        setHasMore(false);
      } finally {
        setIsLoading(false);
      }
    },
    [minChars, maxResults, apiBaseUrl, getAccessToken]
  );

  // Debounced search trigger
  useEffect(() => {
    // Clear previous timer
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }

    // Set new timer
    debounceTimerRef.current = setTimeout(() => {
      performSearch(query, typeFilter);
    }, debounceMs);

    // Cleanup
    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }
    };
  }, [query, typeFilter, debounceMs, performSearch]);

  // Set query
  const setQuery = useCallback((newQuery: string) => {
    setQueryState(newQuery);
  }, []);

  // Set type filter
  const setTypeFilter = useCallback((types: EntityType[]) => {
    setTypeFilterState(types);
  }, []);

  // Toggle type filter
  const toggleTypeFilter = useCallback((type: EntityType) => {
    setTypeFilterState((prev) => {
      if (prev.includes(type)) {
        return prev.filter((t) => t !== type);
      }
      return [...prev, type];
    });
  }, []);

  // Add to recent
  const addToRecent = useCallback((entity: EntitySearchResult) => {
    setRecentEntities((prev) => {
      const filtered = prev.filter((e) => e.id !== entity.id);
      const newRecent: RecentEntity = {
        ...entity,
        lastUsed: new Date().toISOString(),
      };
      const updated = [newRecent, ...filtered].slice(0, MAX_RECENT_ENTITIES);
      saveRecentEntities(updated);
      return updated;
    });
  }, []);

  // Search immediately (bypass debounce)
  const searchNow = useCallback(async () => {
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }
    await performSearch(query, typeFilter);
  }, [query, typeFilter, performSearch]);

  // Clear search
  const clear = useCallback(() => {
    setQueryState('');
    setResults([]);
    setTotalCount(0);
    setHasMore(false);
    setError(null);
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
    }
  }, []);

  // Filter recent entities by type filter
  const filteredRecentEntities = useMemo(() => {
    if (typeFilter.length === 0 || typeFilter.length === ALL_ENTITY_TYPES.length) {
      return recentEntities;
    }
    return recentEntities.filter((e) => typeFilter.includes(e.entityType));
  }, [recentEntities, typeFilter]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, []);

  return {
    query,
    setQuery,
    results,
    isLoading,
    error,
    clearError,
    recentEntities: filteredRecentEntities,
    typeFilter,
    setTypeFilter,
    toggleTypeFilter,
    hasMore,
    totalCount,
    addToRecent,
    searchNow,
    clear,
  };
}
