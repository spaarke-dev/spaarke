/**
 * useFilterState Hook - DrillThroughWorkspace
 *
 * Re-exports the useFilterState hook from FilterStateContext for cleaner imports.
 * Use this hook to access filter state in chart and grid components.
 *
 * @example
 * ```tsx
 * import { useFilterState } from '../hooks/useFilterState';
 *
 * const MyComponent = () => {
 *   const { activeFilter, setFilter, clearFilter, isFiltered, dataset } = useFilterState();
 *
 *   // Apply filter from chart click
 *   const handleClick = (field: string, value: string) => {
 *     setFilter({ field, operator: 'eq', value, label: `${field}: ${value}` });
 *   };
 *
 *   return (
 *     <div>
 *       {isFiltered && <Button onClick={clearFilter}>Clear Filter</Button>}
 *     </div>
 *   );
 * };
 * ```
 */

export {
  useFilterState,
  FilterStateContext,
  FilterStateProvider,
  drillInteractionToFilterExpression,
  type IFilterStateContextValue,
  type IFilterStateProviderProps,
} from "../context/FilterStateContext";
