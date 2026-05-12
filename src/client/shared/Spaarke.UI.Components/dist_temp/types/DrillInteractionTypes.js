/**
 * Drill Interaction Types - Spaarke Visuals Framework
 * Defines the contract for chart click interactions that trigger drill-through filtering
 * Project: visualization-module
 */
/**
 * Convert a DrillInteraction to FetchXML filter condition
 *
 * @param interaction - The drill interaction to convert
 * @returns FetchXML condition element as string
 */
export function drillInteractionToFetchXml(interaction) {
    const { field, operator, value } = interaction;
    switch (operator) {
        case 'eq':
            return `<condition attribute="${field}" operator="eq" value="${value}" />`;
        case 'in':
            if (Array.isArray(value)) {
                const values = value.map(v => `<value>${v}</value>`).join('');
                return `<condition attribute="${field}" operator="in">${values}</condition>`;
            }
            return `<condition attribute="${field}" operator="eq" value="${value}" />`;
        case 'between':
            if (Array.isArray(value) && value.length === 2) {
                return `<filter type="and">
          <condition attribute="${field}" operator="ge" value="${value[0]}" />
          <condition attribute="${field}" operator="le" value="${value[1]}" />
        </filter>`;
            }
            return `<condition attribute="${field}" operator="eq" value="${value}" />`;
        default:
            return `<condition attribute="${field}" operator="eq" value="${value}" />`;
    }
}
/**
 * Convert a DrillInteraction to OData filter string
 *
 * @param interaction - The drill interaction to convert
 * @returns OData $filter string
 */
export function drillInteractionToOData(interaction) {
    const { field, operator, value } = interaction;
    switch (operator) {
        case 'eq':
            if (typeof value === 'string') {
                return `${field} eq '${value}'`;
            }
            return `${field} eq ${value}`;
        case 'in':
            if (Array.isArray(value)) {
                const conditions = value.map(v => {
                    if (typeof v === 'string') {
                        return `${field} eq '${v}'`;
                    }
                    return `${field} eq ${v}`;
                });
                return `(${conditions.join(' or ')})`;
            }
            return `${field} eq ${value}`;
        case 'between':
            if (Array.isArray(value) && value.length === 2) {
                return `(${field} ge ${value[0]} and ${field} le ${value[1]})`;
            }
            return `${field} eq ${value}`;
        default:
            return `${field} eq ${value}`;
    }
}
/**
 * Type guard to check if a value is a DrillInteraction
 */
export function isDrillInteraction(value) {
    if (typeof value !== 'object' || value === null) {
        return false;
    }
    const obj = value;
    const validOperators = ['eq', 'in', 'between'];
    return (typeof obj.field === 'string' &&
        typeof obj.operator === 'string' &&
        validOperators.indexOf(obj.operator) !== -1 &&
        obj.value !== undefined);
}
//# sourceMappingURL=DrillInteractionTypes.js.map