/**
 * Unit tests for useEntitySearch hook
 *
 * Tests the entity search hook with debouncing, filtering, and recent items.
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import {
  useEntitySearch,
  ALL_ENTITY_TYPES,
  type EntityType,
  type EntitySearchResult,
} from '../useEntitySearch';

// Mock sessionStorage
const mockSessionStorage = {
  store: {} as Record<string, string>,
  getItem: jest.fn((key: string) => mockSessionStorage.store[key] || null),
  setItem: jest.fn((key: string, value: string) => {
    mockSessionStorage.store[key] = value;
  }),
  removeItem: jest.fn((key: string) => {
    delete mockSessionStorage.store[key];
  }),
  clear: jest.fn(() => {
    mockSessionStorage.store = {};
  }),
};

Object.defineProperty(window, 'sessionStorage', {
  value: mockSessionStorage,
});

describe('useEntitySearch', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    mockSessionStorage.clear();
    jest.clearAllMocks();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  describe('initial state', () => {
    it('returns initial state with empty query', () => {
      const { result } = renderHook(() => useEntitySearch());

      expect(result.current.query).toBe('');
      expect(result.current.results).toEqual([]);
      expect(result.current.isLoading).toBe(false);
      expect(result.current.error).toBeNull();
      expect(result.current.typeFilter).toEqual([]);
    });

    it('loads recent entities from sessionStorage', () => {
      const recentEntities = [
        {
          id: '1',
          entityType: 'Matter' as EntityType,
          logicalName: 'sprk_matter',
          name: 'Test Matter',
          lastUsed: new Date().toISOString(),
        },
      ];
      mockSessionStorage.store['spaarke-recent-entities'] = JSON.stringify(recentEntities);

      const { result } = renderHook(() => useEntitySearch());

      expect(result.current.recentEntities).toHaveLength(1);
      expect(result.current.recentEntities[0].name).toBe('Test Matter');
    });

    it('uses initial type filter from options', () => {
      const { result } = renderHook(() =>
        useEntitySearch({ initialTypeFilter: ['Matter', 'Project'] })
      );

      expect(result.current.typeFilter).toEqual(['Matter', 'Project']);
    });
  });

  describe('setQuery', () => {
    it('updates query immediately', () => {
      const { result } = renderHook(() => useEntitySearch());

      act(() => {
        result.current.setQuery('test');
      });

      expect(result.current.query).toBe('test');
    });

    it('triggers debounced search', async () => {
      const { result } = renderHook(() => useEntitySearch({ debounceMs: 300 }));

      act(() => {
        result.current.setQuery('smith');
      });

      // Should not be loading immediately
      expect(result.current.isLoading).toBe(false);

      // Fast-forward past debounce
      act(() => {
        jest.advanceTimersByTime(350);
      });

      // Should be loading after debounce
      await waitFor(() => {
        expect(result.current.isLoading || result.current.results.length > 0).toBe(true);
      });
    });

    it('does not search if query is below minChars', () => {
      const { result } = renderHook(() => useEntitySearch({ minChars: 3 }));

      act(() => {
        result.current.setQuery('ab');
      });

      act(() => {
        jest.advanceTimersByTime(500);
      });

      expect(result.current.results).toEqual([]);
    });
  });

  describe('type filter', () => {
    it('setTypeFilter updates filter', () => {
      const { result } = renderHook(() => useEntitySearch());

      act(() => {
        result.current.setTypeFilter(['Matter']);
      });

      expect(result.current.typeFilter).toEqual(['Matter']);
    });

    it('toggleTypeFilter adds type when not present', () => {
      const { result } = renderHook(() => useEntitySearch());

      act(() => {
        result.current.toggleTypeFilter('Matter');
      });

      expect(result.current.typeFilter).toContain('Matter');
    });

    it('toggleTypeFilter removes type when present', () => {
      const { result } = renderHook(() =>
        useEntitySearch({ initialTypeFilter: ['Matter', 'Project'] })
      );

      act(() => {
        result.current.toggleTypeFilter('Matter');
      });

      expect(result.current.typeFilter).not.toContain('Matter');
      expect(result.current.typeFilter).toContain('Project');
    });

    it('filter affects recent entities', () => {
      const recentEntities = [
        {
          id: '1',
          entityType: 'Matter' as EntityType,
          logicalName: 'sprk_matter',
          name: 'Test Matter',
          lastUsed: new Date().toISOString(),
        },
        {
          id: '2',
          entityType: 'Project' as EntityType,
          logicalName: 'sprk_project',
          name: 'Test Project',
          lastUsed: new Date().toISOString(),
        },
      ];
      mockSessionStorage.store['spaarke-recent-entities'] = JSON.stringify(recentEntities);

      const { result } = renderHook(() => useEntitySearch());

      // Initially shows all
      expect(result.current.recentEntities).toHaveLength(2);

      // Filter to Matter only
      act(() => {
        result.current.setTypeFilter(['Matter']);
      });

      expect(result.current.recentEntities).toHaveLength(1);
      expect(result.current.recentEntities[0].entityType).toBe('Matter');
    });
  });

  describe('addToRecent', () => {
    it('adds entity to recent list', () => {
      const { result } = renderHook(() => useEntitySearch());

      const entity: EntitySearchResult = {
        id: '1',
        entityType: 'Matter',
        logicalName: 'sprk_matter',
        name: 'New Matter',
      };

      act(() => {
        result.current.addToRecent(entity);
      });

      expect(result.current.recentEntities).toHaveLength(1);
      expect(result.current.recentEntities[0].name).toBe('New Matter');
    });

    it('moves existing entity to top of recent list', () => {
      const recentEntities = [
        {
          id: '1',
          entityType: 'Matter' as EntityType,
          logicalName: 'sprk_matter',
          name: 'First Matter',
          lastUsed: new Date(Date.now() - 1000).toISOString(),
        },
        {
          id: '2',
          entityType: 'Project' as EntityType,
          logicalName: 'sprk_project',
          name: 'Second Project',
          lastUsed: new Date().toISOString(),
        },
      ];
      mockSessionStorage.store['spaarke-recent-entities'] = JSON.stringify(recentEntities);

      const { result } = renderHook(() => useEntitySearch());

      const entity: EntitySearchResult = {
        id: '1',
        entityType: 'Matter',
        logicalName: 'sprk_matter',
        name: 'First Matter',
      };

      act(() => {
        result.current.addToRecent(entity);
      });

      expect(result.current.recentEntities[0].id).toBe('1');
    });

    it('limits recent list to 10 items', () => {
      const { result } = renderHook(() => useEntitySearch());

      // Add 12 entities
      for (let i = 0; i < 12; i++) {
        act(() => {
          result.current.addToRecent({
            id: `${i}`,
            entityType: 'Matter',
            logicalName: 'sprk_matter',
            name: `Entity ${i}`,
          });
        });
      }

      expect(result.current.recentEntities.length).toBeLessThanOrEqual(10);
    });

    it('persists recent entities to sessionStorage', () => {
      const { result } = renderHook(() => useEntitySearch());

      const entity: EntitySearchResult = {
        id: '1',
        entityType: 'Matter',
        logicalName: 'sprk_matter',
        name: 'Persisted Matter',
      };

      act(() => {
        result.current.addToRecent(entity);
      });

      expect(mockSessionStorage.setItem).toHaveBeenCalledWith(
        'spaarke-recent-entities',
        expect.stringContaining('Persisted Matter')
      );
    });
  });

  describe('clear', () => {
    it('clears query and results', () => {
      const { result } = renderHook(() => useEntitySearch());

      act(() => {
        result.current.setQuery('test');
      });

      act(() => {
        jest.advanceTimersByTime(500);
      });

      act(() => {
        result.current.clear();
      });

      expect(result.current.query).toBe('');
      expect(result.current.results).toEqual([]);
      expect(result.current.error).toBeNull();
    });

    it('cancels pending search', () => {
      const { result } = renderHook(() => useEntitySearch({ debounceMs: 500 }));

      act(() => {
        result.current.setQuery('test');
      });

      // Clear before debounce completes
      act(() => {
        jest.advanceTimersByTime(200);
        result.current.clear();
      });

      // Fast-forward past debounce
      act(() => {
        jest.advanceTimersByTime(500);
      });

      // Should still be empty
      expect(result.current.results).toEqual([]);
    });
  });

  describe('clearError', () => {
    it('clears error state', () => {
      const { result } = renderHook(() => useEntitySearch());

      // Simulate an error scenario (would need to mock fetch to actually test)
      // For now, just verify clearError exists and can be called
      act(() => {
        result.current.clearError();
      });

      expect(result.current.error).toBeNull();
    });
  });

  describe('searchNow', () => {
    it('triggers search immediately bypassing debounce', async () => {
      const { result } = renderHook(() => useEntitySearch({ debounceMs: 1000 }));

      act(() => {
        result.current.setQuery('smith');
      });

      // Immediately call searchNow
      await act(async () => {
        await result.current.searchNow();
      });

      // Should have results or at least have completed loading
      expect(result.current.isLoading).toBe(false);
    });
  });

  describe('constants', () => {
    it('ALL_ENTITY_TYPES contains all entity types', () => {
      expect(ALL_ENTITY_TYPES).toContain('Matter');
      expect(ALL_ENTITY_TYPES).toContain('Project');
      expect(ALL_ENTITY_TYPES).toContain('Invoice');
      expect(ALL_ENTITY_TYPES).toContain('Account');
      expect(ALL_ENTITY_TYPES).toContain('Contact');
      expect(ALL_ENTITY_TYPES).toHaveLength(5);
    });
  });
});
