/**
 * ControlPanel component tests
 *
 * Tests:
 * - Initial rendering with default settings
 * - Similarity threshold slider interaction
 * - Depth limit slider interaction
 * - Max nodes slider interaction
 * - Document type checkbox filtering
 * - Active filters badge display
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
    ControlPanel,
    DEFAULT_FILTER_SETTINGS,
    DOCUMENT_TYPES,
    type FilterSettings,
} from '../components/ControlPanel';

// Wrapper to provide Fluent UI context
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
    <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

// Helper to render ControlPanel with default props
const renderControlPanel = (
    settings: FilterSettings = DEFAULT_FILTER_SETTINGS,
    onSettingsChange = jest.fn()
) => {
    return render(
        <TestWrapper>
            <ControlPanel settings={settings} onSettingsChange={onSettingsChange} />
        </TestWrapper>
    );
};

describe('ControlPanel', () => {
    describe('Initial Rendering', () => {
        it('renders with title "Visualization Settings"', () => {
            renderControlPanel();

            expect(screen.getByText('Visualization Settings')).toBeInTheDocument();
        });

        it('renders similarity section', () => {
            renderControlPanel();

            expect(screen.getByText('Similarity Settings')).toBeInTheDocument();
            expect(screen.getByText('Minimum Similarity')).toBeInTheDocument();
        });

        it('renders graph settings section', () => {
            renderControlPanel();

            expect(screen.getByText('Graph Settings')).toBeInTheDocument();
            expect(screen.getByText('Depth Limit')).toBeInTheDocument();
            expect(screen.getByText('Max Nodes per Level')).toBeInTheDocument();
        });

        it('renders document type filters section', () => {
            renderControlPanel();

            expect(screen.getByText('Document Types')).toBeInTheDocument();
            DOCUMENT_TYPES.forEach((type) => {
                expect(screen.getByText(type.label)).toBeInTheDocument();
            });
        });

        it('displays default similarity threshold value', () => {
            renderControlPanel();

            expect(screen.getByText('65%')).toBeInTheDocument();
        });

        it('displays default depth limit value', () => {
            renderControlPanel();

            expect(screen.getByText('1 level')).toBeInTheDocument();
        });

        it('displays default max nodes value', () => {
            renderControlPanel();

            expect(screen.getByText('25')).toBeInTheDocument();
        });
    });

    describe('Similarity Threshold Slider', () => {
        it('displays current similarity value', () => {
            const settings = { ...DEFAULT_FILTER_SETTINGS, similarityThreshold: 0.80 };
            renderControlPanel(settings);

            expect(screen.getByText('80%')).toBeInTheDocument();
        });

        it('calls onSettingsChange when slider value changes', () => {
            const onSettingsChange = jest.fn();
            renderControlPanel(DEFAULT_FILTER_SETTINGS, onSettingsChange);

            const slider = screen.getByRole('slider', { name: /minimum similarity/i });
            fireEvent.change(slider, { target: { value: '75' } });

            expect(onSettingsChange).toHaveBeenCalledWith(
                expect.objectContaining({
                    similarityThreshold: 0.75,
                })
            );
        });
    });

    describe('Depth Limit Slider', () => {
        it('displays depth with correct singular/plural', () => {
            const settings1 = { ...DEFAULT_FILTER_SETTINGS, depthLimit: 1 };
            const { rerender } = renderControlPanel(settings1);
            expect(screen.getByText('1 level')).toBeInTheDocument();

            const settings2 = { ...DEFAULT_FILTER_SETTINGS, depthLimit: 2 };
            rerender(
                <TestWrapper>
                    <ControlPanel settings={settings2} onSettingsChange={jest.fn()} />
                </TestWrapper>
            );
            expect(screen.getByText('2 levels')).toBeInTheDocument();
        });

        it('calls onSettingsChange when depth changes', () => {
            const onSettingsChange = jest.fn();
            renderControlPanel(DEFAULT_FILTER_SETTINGS, onSettingsChange);

            const slider = screen.getByRole('slider', { name: /depth limit/i });
            fireEvent.change(slider, { target: { value: '2' } });

            expect(onSettingsChange).toHaveBeenCalledWith(
                expect.objectContaining({
                    depthLimit: 2,
                })
            );
        });
    });

    describe('Max Nodes Per Level Slider', () => {
        it('displays current max nodes value', () => {
            const settings = { ...DEFAULT_FILTER_SETTINGS, maxNodesPerLevel: 30 };
            renderControlPanel(settings);

            expect(screen.getByText('30')).toBeInTheDocument();
        });

        it('calls onSettingsChange when max nodes changes', () => {
            const onSettingsChange = jest.fn();
            renderControlPanel(DEFAULT_FILTER_SETTINGS, onSettingsChange);

            const slider = screen.getByRole('slider', { name: /max nodes per level/i });
            fireEvent.change(slider, { target: { value: '35' } });

            expect(onSettingsChange).toHaveBeenCalledWith(
                expect.objectContaining({
                    maxNodesPerLevel: 35,
                })
            );
        });
    });

    describe('Document Type Checkboxes', () => {
        it('renders all document type checkboxes as checked by default', () => {
            renderControlPanel();

            DOCUMENT_TYPES.forEach((type) => {
                const checkbox = screen.getByRole('checkbox', { name: type.label });
                expect(checkbox).toBeChecked();
            });
        });

        it('calls onSettingsChange when checkbox is unchecked', () => {
            const onSettingsChange = jest.fn();
            renderControlPanel(DEFAULT_FILTER_SETTINGS, onSettingsChange);

            const pdfCheckbox = screen.getByRole('checkbox', { name: 'PDF Documents' });
            fireEvent.click(pdfCheckbox);

            expect(onSettingsChange).toHaveBeenCalledWith(
                expect.objectContaining({
                    documentTypes: expect.not.arrayContaining(['pdf']),
                })
            );
        });

        it('calls onSettingsChange when checkbox is checked', () => {
            const settings = {
                ...DEFAULT_FILTER_SETTINGS,
                documentTypes: ['docx', 'xlsx'] as typeof DEFAULT_FILTER_SETTINGS.documentTypes,
            };
            const onSettingsChange = jest.fn();
            renderControlPanel(settings, onSettingsChange);

            const pdfCheckbox = screen.getByRole('checkbox', { name: 'PDF Documents' });
            fireEvent.click(pdfCheckbox);

            expect(onSettingsChange).toHaveBeenCalledWith(
                expect.objectContaining({
                    documentTypes: expect.arrayContaining(['pdf']),
                })
            );
        });
    });

    describe('Active Filters Badge', () => {
        it('does not show badge when all settings are default', () => {
            renderControlPanel();

            expect(screen.queryByText(/active$/)).not.toBeInTheDocument();
        });

        it('shows "1 active" badge when one setting differs from default', () => {
            const settings = { ...DEFAULT_FILTER_SETTINGS, similarityThreshold: 0.80 };
            renderControlPanel(settings);

            expect(screen.getByText('1 active')).toBeInTheDocument();
        });

        it('shows "2 active" badge when two settings differ', () => {
            const settings = {
                ...DEFAULT_FILTER_SETTINGS,
                similarityThreshold: 0.80,
                depthLimit: 2,
            };
            renderControlPanel(settings);

            expect(screen.getByText('2 active')).toBeInTheDocument();
        });

        it('counts document type filter as active when not all types selected', () => {
            const settings = {
                ...DEFAULT_FILTER_SETTINGS,
                documentTypes: ['pdf', 'docx'] as typeof DEFAULT_FILTER_SETTINGS.documentTypes,
            };
            renderControlPanel(settings);

            expect(screen.getByText('1 active')).toBeInTheDocument();
        });
    });

    describe('Slider Hints', () => {
        it('shows similarity slider hint text', () => {
            renderControlPanel();

            expect(
                screen.getByText('Higher values show more similar documents only')
            ).toBeInTheDocument();
        });

        it('shows depth slider hint text', () => {
            renderControlPanel();

            expect(
                screen.getByText('How many levels of related documents to show')
            ).toBeInTheDocument();
        });

        it('shows max nodes slider hint text', () => {
            renderControlPanel();

            expect(
                screen.getByText('Maximum documents shown at each depth level')
            ).toBeInTheDocument();
        });
    });
});
