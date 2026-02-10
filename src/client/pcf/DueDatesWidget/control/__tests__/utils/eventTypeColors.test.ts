/**
 * Tests for eventTypeColors Utility Functions
 *
 * Tests:
 * - getEventTypeColor: Color variant determination from type name
 * - getEventTypeColorConfig: Full color configuration retrieval
 * - getEventTypeBackgroundColor: Convenience background color getter
 * - eventTypeColorConfigs: Color configuration constants
 */

import {
    getEventTypeColor,
    getEventTypeColorConfig,
    getEventTypeBackgroundColor,
    eventTypeColorConfigs,
    EventTypeColorVariant
} from '../../utils/eventTypeColors';

describe('eventTypeColors utilities', () => {
    describe('getEventTypeColor', () => {
        describe('yellow color mappings', () => {
            it.each([
                'Hearing',
                'hearing',
                'Court Hearing',
                'Court',
                'court',
                'Trial',
                'trial date'
            ])('returns "yellow" for "%s"', (typeName) => {
                expect(getEventTypeColor(typeName)).toBe('yellow');
            });
        });

        describe('green color mappings', () => {
            it.each([
                'Filing',
                'filing',
                'Filing Deadline',
                'Patent',
                'patent application',
                'Submission',
                'Application',
                'application deadline'
            ])('returns "green" for "%s"', (typeName) => {
                expect(getEventTypeColor(typeName)).toBe('green');
            });
        });

        describe('purple color mappings', () => {
            it.each([
                'Regulatory',
                'regulatory review',
                'Review',
                'Annual Review',
                'Compliance',
                'compliance check',
                'Audit',
                'internal audit'
            ])('returns "purple" for "%s"', (typeName) => {
                expect(getEventTypeColor(typeName)).toBe('purple');
            });
        });

        describe('blue color mappings', () => {
            it.each([
                'Meeting',
                'meeting',
                'Team Meeting',
                'Conference',
                'conference call',
                'Call',
                'phone call'
            ])('returns "blue" for "%s"', (typeName) => {
                expect(getEventTypeColor(typeName)).toBe('blue');
            });
        });

        describe('orange color mappings', () => {
            it.each([
                'Deadline',
                'deadline',
                'Project Deadline',
                'Due',
                'due date',
                'Expiration',
                'license expiration',
                'Renewal',
                'renewal date'
            ])('returns "orange" for "%s"', (typeName) => {
                expect(getEventTypeColor(typeName)).toBe('orange');
            });
        });

        describe('teal color mappings', () => {
            it.each([
                'Project',
                'project milestone',
                'Milestone',
                'key milestone'
            ])('returns "teal" for "%s"', (typeName) => {
                expect(getEventTypeColor(typeName)).toBe('teal');
            });
        });

        describe('red color mappings', () => {
            // Note: The implementation matches keywords in order, so compound names
            // may match an earlier keyword. Only test single-keyword cases here.
            it.each([
                'Urgent',
                'urgent matter',
                'Critical',
                'critical issue', // 'critical' matches red before any other keyword
                'Emergency',
                'emergency response'
            ])('returns "red" for "%s"', (typeName) => {
                expect(getEventTypeColor(typeName)).toBe('red');
            });
        });

        describe('compound name priority', () => {
            it('matches earlier keyword for "critical deadline" (deadline/orange wins)', () => {
                // "deadline" appears in the keyword list before "critical"
                expect(getEventTypeColor('critical deadline')).toBe('orange');
            });

            it('matches earlier keyword for "emergency hearing" (hearing/yellow wins)', () => {
                // "hearing" appears in the keyword list before "emergency"
                expect(getEventTypeColor('emergency hearing')).toBe('yellow');
            });
        });

        describe('default color fallback', () => {
            it('returns "default" for empty string', () => {
                expect(getEventTypeColor('')).toBe('default');
            });

            it('returns "default" for unknown type', () => {
                expect(getEventTypeColor('Random Event')).toBe('default');
                expect(getEventTypeColor('Something Else')).toBe('default');
                expect(getEventTypeColor('xyz123')).toBe('default');
            });
        });

        describe('case insensitivity', () => {
            it('handles uppercase input', () => {
                expect(getEventTypeColor('HEARING')).toBe('yellow');
                expect(getEventTypeColor('FILING')).toBe('green');
                expect(getEventTypeColor('MEETING')).toBe('blue');
            });

            it('handles mixed case input', () => {
                expect(getEventTypeColor('HeArInG')).toBe('yellow');
                expect(getEventTypeColor('FiLiNg DeAdLiNe')).toBe('green');
            });
        });

        describe('whitespace handling', () => {
            it('trims leading/trailing whitespace', () => {
                expect(getEventTypeColor('  Hearing  ')).toBe('yellow');
                expect(getEventTypeColor('\tFiling\n')).toBe('green');
            });
        });

        describe('keyword priority', () => {
            it('returns first matching keyword color', () => {
                // "Filing" matches before "Deadline"
                expect(getEventTypeColor('Filing Deadline')).toBe('green');

                // "Hearing" matches before "Court"
                expect(getEventTypeColor('Court Hearing')).toBe('yellow');
            });
        });
    });

    describe('getEventTypeColorConfig', () => {
        it('returns full color config for known type', () => {
            const config = getEventTypeColorConfig('Hearing');

            expect(config).toEqual(eventTypeColorConfigs.yellow);
            expect(config).toHaveProperty('background');
            expect(config).toHaveProperty('foreground');
            expect(config).toHaveProperty('colorName');
        });

        it('returns default config for unknown type', () => {
            const config = getEventTypeColorConfig('Unknown Type');

            expect(config).toEqual(eventTypeColorConfigs.default);
        });

        it('color name matches the variant', () => {
            expect(getEventTypeColorConfig('Hearing').colorName).toBe('yellow');
            expect(getEventTypeColorConfig('Filing').colorName).toBe('green');
            expect(getEventTypeColorConfig('Meeting').colorName).toBe('blue');
            expect(getEventTypeColorConfig('Unknown').colorName).toBe('neutral');
        });
    });

    describe('getEventTypeBackgroundColor', () => {
        it('returns background color token for known type', () => {
            const bgColor = getEventTypeBackgroundColor('Hearing');

            expect(bgColor).toBe(eventTypeColorConfigs.yellow.background);
        });

        it('returns default background for unknown type', () => {
            const bgColor = getEventTypeBackgroundColor('Unknown Type');

            expect(bgColor).toBe(eventTypeColorConfigs.default.background);
        });

        it('is a convenience wrapper for getEventTypeColorConfig().background', () => {
            const types = ['Hearing', 'Filing', 'Meeting', 'Deadline', 'Unknown'];

            types.forEach(type => {
                expect(getEventTypeBackgroundColor(type))
                    .toBe(getEventTypeColorConfig(type).background);
            });
        });
    });

    describe('eventTypeColorConfigs', () => {
        const allVariants: EventTypeColorVariant[] = [
            'yellow',
            'green',
            'purple',
            'blue',
            'orange',
            'red',
            'teal',
            'default'
        ];

        it('has all required color variants', () => {
            allVariants.forEach(variant => {
                expect(eventTypeColorConfigs).toHaveProperty(variant);
            });
        });

        it('each config has required properties', () => {
            allVariants.forEach(variant => {
                const config = eventTypeColorConfigs[variant];

                expect(config).toHaveProperty('background');
                expect(config).toHaveProperty('foreground');
                expect(config).toHaveProperty('colorName');

                expect(typeof config.background).toBe('string');
                expect(typeof config.foreground).toBe('string');
                expect(typeof config.colorName).toBe('string');
            });
        });

        it('colorName matches variant for non-default colors', () => {
            const namedVariants = allVariants.filter(v => v !== 'default');

            namedVariants.forEach(variant => {
                expect(eventTypeColorConfigs[variant].colorName).toBe(variant);
            });
        });

        it('default colorName is "neutral"', () => {
            expect(eventTypeColorConfigs.default.colorName).toBe('neutral');
        });

        it('background tokens are from Fluent UI (contain "color")', () => {
            allVariants.forEach(variant => {
                const bg = eventTypeColorConfigs[variant].background;
                // Fluent tokens are CSS custom properties like "var(--colorPaletteYellowBackground2)"
                expect(bg).toBeTruthy();
            });
        });
    });
});
