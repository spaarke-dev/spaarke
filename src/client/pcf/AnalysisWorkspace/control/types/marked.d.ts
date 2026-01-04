/**
 * Type declaration for marked library
 * Minimal declaration to satisfy TypeScript compiler
 */
declare module "marked" {
    interface MarkedOptions {
        gfm?: boolean;
        breaks?: boolean;
    }

    export function parse(markdown: string, options?: MarkedOptions): string;
}
