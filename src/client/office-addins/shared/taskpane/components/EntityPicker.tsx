import React, {
  useState,
  useCallback,
  useRef,
  useEffect,
  forwardRef,
  type KeyboardEvent,
} from 'react';
import {
  makeStyles,
  tokens,
  Combobox,
  Option,
  OptionGroup,
  Button,
  Badge,
  Spinner,
  Text,
  Body1,
  mergeClasses,
  Divider,
} from '@fluentui/react-components';
import {
  SearchRegular,
  AddRegular,
  BuildingRegular,
  PersonRegular,
  DocumentRegular,
  GavelRegular,
  ReceiptRegular,
  DismissCircleRegular,
  HistoryRegular,
  ChevronDownRegular,
} from '@fluentui/react-icons';
import {
  useEntitySearch,
  type EntityType,
  type EntitySearchResult,
  type UseEntitySearchOptions,
  ALL_ENTITY_TYPES,
} from '../hooks/useEntitySearch';

/**
 * Styles using Fluent UI v9 design tokens (ADR-021).
 */
const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    width: '100%',
  },
  filterChips: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    marginBottom: tokens.spacingVerticalXS,
  },
  filterChip: {
    cursor: 'pointer',
    transition: 'all 0.15s ease-in-out',
    '&:hover': {
      transform: 'scale(1.02)',
    },
  },
  filterChipActive: {
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  comboboxWrapper: {
    position: 'relative',
    width: '100%',
  },
  combobox: {
    width: '100%',
  },
  loadingSpinner: {
    position: 'absolute',
    right: tokens.spacingHorizontalM,
    top: '50%',
    transform: 'translateY(-50%)',
  },
  optionContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    padding: tokens.spacingVerticalXS,
  },
  optionName: {
    fontWeight: tokens.fontWeightSemibold,
  },
  optionInfo: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  optionIcon: {
    marginRight: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground2,
  },
  optionRow: {
    display: 'flex',
    alignItems: 'center',
  },
  entityBadge: {
    marginLeft: tokens.spacingHorizontalS,
    fontSize: tokens.fontSizeBase100,
  },
  emptyState: {
    padding: tokens.spacingVerticalL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
  createNewOption: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalS,
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
    cursor: 'pointer',
    '&:hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  error: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
    marginTop: tokens.spacingVerticalXS,
  },
  selectedEntity: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
  },
  selectedInfo: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  clearButton: {
    minWidth: 'auto',
    padding: tokens.spacingHorizontalXS,
  },
  recentHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

/**
 * Entity type to icon mapping.
 */
const ENTITY_ICONS: Record<EntityType, React.ReactElement> = {
  Matter: <GavelRegular />,
  Project: <DocumentRegular />,
  Invoice: <ReceiptRegular />,
  Account: <BuildingRegular />,
  Contact: <PersonRegular />,
};

/**
 * Entity type badge colors.
 */
const ENTITY_BADGE_COLORS: Record<
  EntityType,
  'brand' | 'danger' | 'important' | 'informative' | 'severe' | 'subtle' | 'success' | 'warning'
> = {
  Matter: 'brand',
  Project: 'informative',
  Invoice: 'warning',
  Account: 'success',
  Contact: 'subtle',
};

/**
 * Props for the EntityPicker component.
 */
export interface EntityPickerProps {
  /** Currently selected entity */
  value?: EntitySearchResult | null;
  /** Callback when an entity is selected */
  onChange?: (entity: EntitySearchResult | null) => void;
  /** Callback when Quick Create is triggered */
  onQuickCreate?: (entityType: EntityType, searchQuery: string) => void;
  /** Placeholder text */
  placeholder?: string;
  /** Entity types to show in filter (default: all) */
  allowedTypes?: EntityType[];
  /** Whether the picker is disabled */
  disabled?: boolean;
  /** Whether the picker is required */
  required?: boolean;
  /** Error message to display */
  errorMessage?: string;
  /** Label for the picker */
  label?: string;
  /** Whether to show the type filter chips */
  showTypeFilter?: boolean;
  /** Whether to show recent entities */
  showRecent?: boolean;
  /** Whether to show Quick Create option */
  showQuickCreate?: boolean;
  /** Search options */
  searchOptions?: UseEntitySearchOptions;
  /** Accessible label for screen readers */
  'aria-label'?: string;
  /** ID for form association */
  id?: string;
  /** Class name for custom styling */
  className?: string;
}

/**
 * EntityPicker component for selecting association targets.
 *
 * Supports:
 * - Typeahead search with debouncing (500ms response per spec)
 * - Entity type filtering via chips
 * - Recent entities display
 * - Quick Create integration
 * - Full keyboard navigation (arrow keys, enter, escape)
 * - Dark mode and high-contrast support (ADR-021)
 * - WCAG 2.1 AA accessibility
 *
 * @example
 * ```tsx
 * const [entity, setEntity] = useState<EntitySearchResult | null>(null);
 *
 * <EntityPicker
 *   value={entity}
 *   onChange={setEntity}
 *   onQuickCreate={(type, query) => openQuickCreateDialog(type, query)}
 *   placeholder="Search for Matter, Project, Account..."
 *   showTypeFilter
 *   showRecent
 *   showQuickCreate
 * />
 * ```
 */
export const EntityPicker = forwardRef<HTMLInputElement, EntityPickerProps>(
  function EntityPicker(props, ref) {
    const {
      value,
      onChange,
      onQuickCreate,
      placeholder = 'Search for an association target...',
      allowedTypes = ALL_ENTITY_TYPES,
      disabled = false,
      required = false,
      errorMessage,
      label,
      showTypeFilter = true,
      showRecent = true,
      showQuickCreate = true,
      searchOptions,
      'aria-label': ariaLabel,
      id,
      className,
    } = props;

    const styles = useStyles();
    const comboboxRef = useRef<HTMLInputElement>(null);
    const [isOpen, setIsOpen] = useState(false);
    const [highlightedIndex, setHighlightedIndex] = useState(-1);

    // Use the entity search hook
    const {
      query,
      setQuery,
      results,
      isLoading,
      error: searchError,
      clearError,
      recentEntities,
      typeFilter,
      setTypeFilter,
      toggleTypeFilter,
      addToRecent,
      clear,
    } = useEntitySearch({
      ...searchOptions,
      initialTypeFilter: allowedTypes.length < ALL_ENTITY_TYPES.length ? allowedTypes : [],
    });

    // Filter allowed types based on props
    const effectiveAllowedTypes = allowedTypes.filter((t) => ALL_ENTITY_TYPES.includes(t));

    // Combine recent and search results for rendering
    const showRecentSection = showRecent && recentEntities.length > 0 && query.length < 2;
    const showResultsSection = results.length > 0 && query.length >= 2;
    const showEmptyState = query.length >= 2 && !isLoading && results.length === 0;
    const showCreateOption = showQuickCreate && query.length >= 2 && !isLoading;

    // All options for keyboard navigation
    const allOptions: (EntitySearchResult | { type: 'create'; entityType: EntityType })[] = [];
    if (showRecentSection) {
      allOptions.push(...recentEntities);
    }
    if (showResultsSection) {
      allOptions.push(...results);
    }
    if (showCreateOption) {
      // Add Quick Create options for active type filters
      const createTypes = typeFilter.length > 0 ? typeFilter : effectiveAllowedTypes;
      createTypes.forEach((type) => {
        allOptions.push({ type: 'create', entityType: type });
      });
    }

    // Handle entity selection
    const handleSelect = useCallback(
      (entity: EntitySearchResult) => {
        addToRecent(entity);
        onChange?.(entity);
        clear();
        setIsOpen(false);
      },
      [addToRecent, onChange, clear]
    );

    // Handle Quick Create
    const handleQuickCreate = useCallback(
      (entityType: EntityType) => {
        onQuickCreate?.(entityType, query);
        setIsOpen(false);
      },
      [onQuickCreate, query]
    );

    // Handle clearing selection
    const handleClear = useCallback(() => {
      onChange?.(null);
      clear();
      comboboxRef.current?.focus();
    }, [onChange, clear]);

    // Handle input change
    const handleInputChange = useCallback(
      (event: React.ChangeEvent<HTMLInputElement>) => {
        const newValue = event.target.value;
        setQuery(newValue);
        clearError();
        setHighlightedIndex(-1);
        if (!isOpen) {
          setIsOpen(true);
        }
      },
      [setQuery, clearError, isOpen]
    );

    // Keyboard navigation
    const handleKeyDown = useCallback(
      (event: KeyboardEvent<HTMLInputElement>) => {
        const optionsCount = allOptions.length;

        switch (event.key) {
          case 'ArrowDown':
            event.preventDefault();
            if (!isOpen) {
              setIsOpen(true);
            }
            setHighlightedIndex((prev) =>
              prev < optionsCount - 1 ? prev + 1 : 0
            );
            break;

          case 'ArrowUp':
            event.preventDefault();
            if (!isOpen) {
              setIsOpen(true);
            }
            setHighlightedIndex((prev) =>
              prev > 0 ? prev - 1 : optionsCount - 1
            );
            break;

          case 'Enter':
            event.preventDefault();
            if (highlightedIndex >= 0 && highlightedIndex < optionsCount) {
              const selected = allOptions[highlightedIndex];
              if ('type' in selected && selected.type === 'create') {
                handleQuickCreate(selected.entityType);
              } else {
                handleSelect(selected as EntitySearchResult);
              }
            }
            break;

          case 'Escape':
            event.preventDefault();
            setIsOpen(false);
            setHighlightedIndex(-1);
            clear();
            break;

          case 'Tab':
            setIsOpen(false);
            setHighlightedIndex(-1);
            break;
        }
      },
      [allOptions, highlightedIndex, isOpen, handleSelect, handleQuickCreate, clear]
    );

    // Handle focus
    const handleFocus = useCallback(() => {
      setIsOpen(true);
    }, []);

    // Handle blur (with delay to allow click on options)
    const handleBlur = useCallback(() => {
      // Delay to allow click events on options
      setTimeout(() => {
        setIsOpen(false);
        setHighlightedIndex(-1);
      }, 200);
    }, []);

    // Sync highlighted index when results change
    useEffect(() => {
      if (results.length > 0 && highlightedIndex === -1) {
        setHighlightedIndex(0);
      }
    }, [results, highlightedIndex]);

    // Render entity type icon
    const renderEntityIcon = (entityType: EntityType) => (
      <span className={styles.optionIcon}>{ENTITY_ICONS[entityType]}</span>
    );

    // Render option
    const renderOption = (entity: EntitySearchResult, index: number) => {
      const isHighlighted = index === highlightedIndex;
      return (
        <Option
          key={entity.id}
          value={entity.id}
          text={entity.name}
          onClick={() => handleSelect(entity)}
          aria-selected={isHighlighted}
        >
          <div className={styles.optionContent}>
            <div className={styles.optionRow}>
              {renderEntityIcon(entity.entityType)}
              <span className={styles.optionName}>{entity.name}</span>
              <Badge
                className={styles.entityBadge}
                appearance="filled"
                color={ENTITY_BADGE_COLORS[entity.entityType]}
                size="small"
              >
                {entity.entityType}
              </Badge>
            </div>
            {entity.displayInfo && (
              <span className={styles.optionInfo}>{entity.displayInfo}</span>
            )}
          </div>
        </Option>
      );
    };

    // Render create option
    const renderCreateOption = (entityType: EntityType, index: number) => {
      const isHighlighted = index === highlightedIndex;
      return (
        <Option
          key={`create-${entityType}`}
          value={`create-${entityType}`}
          text={`Create new ${entityType}`}
          onClick={() => handleQuickCreate(entityType)}
          aria-selected={isHighlighted}
        >
          <div className={styles.createNewOption}>
            <AddRegular />
            <span>Create new {entityType}: &quot;{query}&quot;</span>
          </div>
        </Option>
      );
    };

    // If entity is selected, show selected state
    if (value) {
      return (
        <div className={mergeClasses(styles.container, className)}>
          {label && <Text weight="semibold">{label}</Text>}
          <div className={styles.selectedEntity}>
            <div className={styles.selectedInfo}>
              {renderEntityIcon(value.entityType)}
              <div>
                <Text weight="semibold">{value.name}</Text>
                <Badge
                  className={styles.entityBadge}
                  appearance="filled"
                  color={ENTITY_BADGE_COLORS[value.entityType]}
                  size="small"
                >
                  {value.entityType}
                </Badge>
              </div>
            </div>
            <Button
              className={styles.clearButton}
              appearance="subtle"
              icon={<DismissCircleRegular />}
              onClick={handleClear}
              disabled={disabled}
              aria-label="Clear selection"
            />
          </div>
          {errorMessage && <span className={styles.error}>{errorMessage}</span>}
        </div>
      );
    }

    // Calculate option indices for highlighting
    let optionIndex = 0;

    return (
      <div className={mergeClasses(styles.container, className)}>
        {label && <Text weight="semibold">{label}</Text>}

        {/* Type Filter Chips */}
        {showTypeFilter && (
          <div
            className={styles.filterChips}
            role="group"
            aria-label="Filter by entity type"
          >
            {effectiveAllowedTypes.map((type) => {
              const isActive = typeFilter.length === 0 || typeFilter.includes(type);
              return (
                <Badge
                  key={type}
                  className={mergeClasses(
                    styles.filterChip,
                    isActive && styles.filterChipActive
                  )}
                  appearance={isActive ? 'filled' : 'outline'}
                  color={isActive ? ENTITY_BADGE_COLORS[type] : 'subtle'}
                  onClick={() => toggleTypeFilter(type)}
                  role="checkbox"
                  aria-checked={isActive}
                  tabIndex={0}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      toggleTypeFilter(type);
                    }
                  }}
                >
                  {ENTITY_ICONS[type]}
                  <span style={{ marginLeft: tokens.spacingHorizontalXXS }}>
                    {type}
                  </span>
                </Badge>
              );
            })}
          </div>
        )}

        {/* Search Combobox */}
        <div className={styles.comboboxWrapper}>
          <Combobox
            ref={ref}
            id={id}
            className={styles.combobox}
            placeholder={placeholder}
            value={query}
            onInput={handleInputChange}
            onKeyDown={handleKeyDown}
            onFocus={handleFocus}
            onBlur={handleBlur}
            open={isOpen}
            disabled={disabled}
            required={required}
            aria-label={ariaLabel || label || 'Select association target'}
            aria-expanded={isOpen}
            aria-haspopup="listbox"
            aria-invalid={!!errorMessage || !!searchError}
            aria-describedby={errorMessage || searchError ? `${id}-error` : undefined}
            expandIcon={isLoading ? null : <ChevronDownRegular />}
            freeform
          >
            {/* Loading State */}
            {isLoading && (
              <Option value="loading" disabled>
                <div style={{ display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS }}>
                  <Spinner size="tiny" />
                  <span>Searching...</span>
                </div>
              </Option>
            )}

            {/* Recent Entities */}
            {showRecentSection && !isLoading && (
              <OptionGroup label={
                <div className={styles.recentHeader}>
                  <HistoryRegular />
                  <span>Recent</span>
                </div>
              }>
                {recentEntities.map((entity) => {
                  const idx = optionIndex++;
                  return renderOption(entity, idx);
                })}
              </OptionGroup>
            )}

            {/* Search Results */}
            {showResultsSection && !isLoading && (
              <OptionGroup label="Search Results">
                {results.map((entity) => {
                  const idx = optionIndex++;
                  return renderOption(entity, idx);
                })}
              </OptionGroup>
            )}

            {/* Empty State */}
            {showEmptyState && (
              <Option value="empty" disabled>
                <div className={styles.emptyState}>
                  <SearchRegular />
                  <Body1>No results found for &quot;{query}&quot;</Body1>
                </div>
              </Option>
            )}

            {/* Divider before Quick Create */}
            {showCreateOption && (showResultsSection || showEmptyState) && (
              <Divider />
            )}

            {/* Quick Create Options */}
            {showCreateOption && (
              <OptionGroup label="Quick Create">
                {(typeFilter.length > 0 ? typeFilter : effectiveAllowedTypes).map((type) => {
                  const idx = optionIndex++;
                  return renderCreateOption(type, idx);
                })}
              </OptionGroup>
            )}
          </Combobox>

          {/* Loading indicator */}
          {isLoading && (
            <div className={styles.loadingSpinner}>
              <Spinner size="tiny" aria-hidden="true" />
            </div>
          )}
        </div>

        {/* Error Messages */}
        {(errorMessage || searchError) && (
          <span
            id={`${id}-error`}
            className={styles.error}
            role="alert"
          >
            {errorMessage || searchError}
          </span>
        )}
      </div>
    );
  }
);

export default EntityPicker;
