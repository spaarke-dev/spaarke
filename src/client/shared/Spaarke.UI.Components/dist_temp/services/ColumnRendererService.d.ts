/**
 * Column renderer service - Type-based cell rendering
 */
import { IDatasetColumn } from '../types/DatasetTypes';
import { ColumnRenderer } from '../types/ColumnRendererTypes';
/**
 * Column renderer registry
 */
export declare class ColumnRendererService {
    /**
     * Get appropriate renderer for a column based on its dataType
     */
    static getRenderer(column: IDatasetColumn): ColumnRenderer;
    /**
     * Render secured/masked field
     */
    private static renderSecuredField;
    /**
     * Render plain text
     */
    private static renderText;
    /**
     * Render email with link
     */
    private static renderEmail;
    /**
     * Render phone
     */
    private static renderPhone;
    /**
     * Render URL with link
     */
    private static renderUrl;
    /**
     * Render number with locale formatting
     */
    private static renderNumber;
    /**
     * Render money with currency symbol
     */
    private static renderMoney;
    /**
     * Render date and time
     */
    private static renderDateTime;
    /**
     * Render date only
     */
    private static renderDateOnly;
    /**
     * Render two options (boolean) with icons
     */
    private static renderTwoOptions;
    /**
     * Render option set (choice) with badge
     */
    private static renderOptionSet;
    /**
     * Render multi-select option set with multiple badges
     */
    private static renderMultiSelectOptionSet;
    /**
     * Render lookup with entity reference
     */
    private static renderLookup;
    /**
     * Render boolean
     */
    private static renderBoolean;
}
//# sourceMappingURL=ColumnRendererService.d.ts.map