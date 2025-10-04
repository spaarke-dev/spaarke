# Dataset Component Performance Optimization

## Virtual Scrolling Implementation
### React Virtual Hook
```typescript
// hooks/useVirtualScrolling.ts
import { useVirtualizer } from "@tanstack/react-virtual";

export function useVirtualScrolling(props: IVirtualProps) {
  const parentRef = React.useRef<HTMLDivElement>(null);

  const virtualizer = useVirtualizer({
    count: props.items.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => {
      switch (props.density) {
        case "Compact": return 32;
        case "Comfortable": return 52;
        default: return 44;
      }
    },
    overscan: 5,
    measureElement: (element: Element) => (element as HTMLElement).getBoundingClientRect().height
  });

  const virtualItems = virtualizer.getVirtualItems();

  return {
    parentRef,
    virtualItems,
    totalSize: virtualizer.getTotalSize(),
    measureElement: virtualizer.measureElement
  };
}
```

### Virtual Grid Component
```typescript
// components/VirtualGrid.tsx
export const VirtualGrid: React.FC<IVirtualGridProps> = ({ items, columns, density }) => {
  const { parentRef, virtualItems, totalSize } = useVirtualScrolling({ items, density });

  return (
    <div ref={parentRef} style={{ height: "600px", overflow: "auto" }} data-testid="scroll-container">
      <div style={{ height: totalSize, position: "relative" }}>
        {virtualItems.map(vr => {
          const item = items[vr.index];
          return (
            <DataGridRow
              key={item.id}
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                width: "100%",
                transform: `translateY(${vr.start}px)`
              }}
              data-index={vr.index}
            >
              {columns.map(column => (
                <DataGridCell key={column.key}>{renderCell(item, column)}</DataGridCell>
              ))}
            </DataGridRow>
          );
        })}
      </div>
    </div>
  );
};
```

## Caching Strategy
### Multi-Level Cache
```typescript
// services/CacheService.ts
export class CacheService {
  // L1: Component state (immediate)
  private stateCache = new Map<string, any>();
  // L2: Session storage (5 min TTL)
  private sessionCache = new SessionCache(5 * 60 * 1000);
  // L3: Prefetch buffer
  private prefetchBuffer = new Map<string, Promise<any>>();

  async get(key: string, fetcher: () => Promise<any>): Promise<any> {
    if (this.stateCache.has(key)) return this.stateCache.get(key);

    const cached = this.sessionCache.get(key);
    if (cached) {
      this.stateCache.set(key, cached);
      return cached;
    }

    if (this.prefetchBuffer.has(key)) {
      const data = await this.prefetchBuffer.get(key)!;
      this.stateCache.set(key, data);
      return data;
    }

    const data = await fetcher();
    this.stateCache.set(key, data);
    this.sessionCache.set(key, data);
    return data;
  }

  prefetch(key: string, fetcher: () => Promise<any>): void {
    if (!this.prefetchBuffer.has(key)) {
      this.prefetchBuffer.set(key, fetcher());
    }
  }

  clear() {
    this.stateCache.clear();
    this.prefetchBuffer.clear();
  }
}
```

## Lazy Loading Patterns
### Progressive Data Loading
```typescript
// hooks/useProgressiveLoad.ts
export function useProgressiveLoad(props: IProgressiveLoadProps) {
  const [loadedColumns, setLoadedColumns] = React.useState<string[]>([]);
  const [loadPhase, setLoadPhase] = React.useState<"essential" | "important" | "complete">("essential");

  React.useEffect(() => {
    const essentialColumns = ["name", "status", "modifiedon"];
    setLoadedColumns(essentialColumns);

    requestIdleCallback(() => {
      const importantColumns = [...essentialColumns, "owner", "description"];
      setLoadedColumns(importantColumns);
      setLoadPhase("important");
    });

    const loadAllColumns = () => {
      setLoadedColumns(props.allColumns);
      setLoadPhase("complete");
    };

    window.addEventListener("mousemove", loadAllColumns, { once: true });
    window.addEventListener("scroll", loadAllColumns, { once: true });

    return () => {
      window.removeEventListener("mousemove", loadAllColumns);
      window.removeEventListener("scroll", loadAllColumns);
    };
  }, []);

  return { loadedColumns, loadPhase };
}
```

## Memory Management
### Cleanup and Optimization
```typescript
// utils/MemoryManager.ts
export class MemoryManager {
  private observers: IntersectionObserver[] = [];
  private timers: number[] = [];
  private abortControllers: AbortController[] = [];

  registerObserver(observer: IntersectionObserver): void { this.observers.push(observer); }
  registerTimer(timer: number): void { this.timers.push(timer); }
  registerAbortController(controller: AbortController): void { this.abortControllers.push(controller); }

  cleanup(): void {
    this.observers.forEach(o => o.disconnect());
    this.timers.forEach(t => clearTimeout(t));
    this.abortControllers.forEach(c => c.abort());
    this.observers = [];
    this.timers = [];
    this.abortControllers = [];
  }
}

// Usage in component
useEffect(() => {
  const mm = new MemoryManager();
  const observer = new IntersectionObserver(handleIntersection);
  mm.registerObserver(observer);
  return () => mm.cleanup();
}, []);
```

## Rendering Optimization
### React Optimization Patterns
```typescript
// Memoized row component
const DataRow = React.memo<IDataRowProps>(({ item, columns, selected, onSelect }) => {
  return (
    <DataGridRow selected={selected}>
      {columns.map(column => (
        <DataGridCell key={column.key}>{renderCell(item, column)}</DataGridCell>
      ))}
    </DataGridRow>
  );
}, (prev, next) => (
  prev.item.id === next.item.id &&
  prev.selected === next.selected &&
  prev.item.modifiedon === next.item.modifiedon
));

// Memoized callbacks
const handleSort = useCallback((column: string) => {/* Sort logic */}, []);
const handleFilter = useCallback((filters: IFilter[]) => {/* Filter logic */}, []);

// Memoized computations
const sortedItems = useMemo(() => items.sort(sortFunction), [items, sortColumn, sortDirection]);
```

## Performance Metrics
### Monitoring Implementation
```typescript
// utils/PerformanceMonitor.ts
export class PerformanceMonitor {
  private marks = new Map<string, number>();

  mark(name: string): void { this.marks.set(name, performance.now()); }

  measure(name: string, startMark: string, endMark?: string): number {
    const start = this.marks.get(startMark) || 0;
    const end = endMark ? this.marks.get(endMark) : performance.now();
    const duration = end - start;
    if ((window as any).appInsights) {
      (window as any).appInsights.trackMetric({ name: `Dataset.${name}`, average: duration });
    }
    return duration;
  }

  trackRender(): void {
    this.mark("renderStart");
    requestAnimationFrame(() => {
      const duration = this.measure("renderTime", "renderStart");
      if (duration > 500) { console.warn(`Slow render: ${duration}ms`); }
    });
  }
}
```

## Performance Targets
| Metric | Target | Maximum |
|--------|--------|---------|
| Initial Render | < 250ms | 500ms |
| Subsequent Updates | < 100ms | 200ms |
| Scroll FPS | 60 fps | 30 fps |
| Memory Usage | < 50MB | 100MB |
| Time to Interactive | < 1s | 2s |

## AI Coding Prompt
Add first-class performance features:
- Integrate `@tanstack/react-virtual` for row virtualization; fixed row height per density; overscan 5; measure non-Firefox only.
- Introduce `CacheService` with L1 state + session storage TTL + prefetch buffer.
- Progressive loading hook for columns (essential → important → all) using `requestIdleCallback` and first-interaction triggers.
- Memory manager to clean observers, timers, and pending fetches on unmount.
- Performance monitor that marks render times and sends metrics to Application Insights; warn if >500ms.
Deliverables: virtualization hook and grid, cache service, memory manager, perf monitor; wire into grid.
