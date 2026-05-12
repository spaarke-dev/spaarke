export interface VirtualizationConfig {
    enabled: boolean;
    threshold: number;
    itemHeight: number;
    overscanCount?: number;
}
export interface VirtualizationResult {
    shouldVirtualize: boolean;
    itemHeight: number;
    overscanCount: number;
}
/**
 * Hook to determine if virtualization should be enabled based on record count
 */
export declare function useVirtualization(recordCount: number, config?: Partial<VirtualizationConfig>): VirtualizationResult;
//# sourceMappingURL=useVirtualization.d.ts.map