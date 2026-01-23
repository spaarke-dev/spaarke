/**
 * Unit tests for SearchInput component
 *
 * @see SearchInput.tsx for implementation
 */
import * as React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { SearchInput } from "../../components/SearchInput";

// Wrapper for Fluent Provider
const renderWithProvider = (ui: React.ReactElement) => {
    return render(
        <FluentProvider theme={webLightTheme}>
            {ui}
        </FluentProvider>
    );
};

describe("SearchInput", () => {
    const defaultProps = {
        value: "",
        placeholder: "Search documents...",
        disabled: false,
        onValueChange: jest.fn(),
        onSearch: jest.fn(),
    };

    beforeEach(() => {
        jest.clearAllMocks();
    });

    it("should render input with placeholder", () => {
        renderWithProvider(<SearchInput {...defaultProps} />);

        expect(screen.getByPlaceholderText("Search documents...")).toBeInTheDocument();
    });

    it("should render search button", () => {
        renderWithProvider(<SearchInput {...defaultProps} />);

        expect(screen.getByRole("button")).toBeInTheDocument();
    });

    it("should display value in input", () => {
        renderWithProvider(<SearchInput {...defaultProps} value="test query" />);

        expect(screen.getByDisplayValue("test query")).toBeInTheDocument();
    });

    it("should call onValueChange when input changes", () => {
        const onValueChange = jest.fn();
        renderWithProvider(
            <SearchInput {...defaultProps} onValueChange={onValueChange} />
        );

        fireEvent.change(screen.getByRole("textbox"), {
            target: { value: "new query" },
        });

        expect(onValueChange).toHaveBeenCalledWith("new query");
    });

    it("should call onSearch when search button clicked", () => {
        const onSearch = jest.fn();
        renderWithProvider(
            <SearchInput {...defaultProps} value="test" onSearch={onSearch} />
        );

        fireEvent.click(screen.getByRole("button"));

        expect(onSearch).toHaveBeenCalledTimes(1);
    });

    it("should call onSearch when Enter key pressed", () => {
        const onSearch = jest.fn();
        renderWithProvider(
            <SearchInput {...defaultProps} value="test" onSearch={onSearch} />
        );

        fireEvent.keyDown(screen.getByRole("textbox"), { key: "Enter" });

        expect(onSearch).toHaveBeenCalledTimes(1);
    });

    it("should not call onSearch for other keys", () => {
        const onSearch = jest.fn();
        renderWithProvider(
            <SearchInput {...defaultProps} value="test" onSearch={onSearch} />
        );

        fireEvent.keyDown(screen.getByRole("textbox"), { key: "Tab" });
        fireEvent.keyDown(screen.getByRole("textbox"), { key: "a" });

        expect(onSearch).not.toHaveBeenCalled();
    });

    it("should disable input and button when disabled", () => {
        renderWithProvider(<SearchInput {...defaultProps} disabled={true} />);

        expect(screen.getByRole("textbox")).toBeDisabled();
        expect(screen.getByRole("button")).toBeDisabled();
    });

    it("should not call onSearch when disabled and Enter pressed", () => {
        const onSearch = jest.fn();
        renderWithProvider(
            <SearchInput
                {...defaultProps}
                value="test"
                disabled={true}
                onSearch={onSearch}
            />
        );

        fireEvent.keyDown(screen.getByRole("textbox"), { key: "Enter" });

        expect(onSearch).not.toHaveBeenCalled();
    });

    it("should have search icon on button", () => {
        const { container } = renderWithProvider(<SearchInput {...defaultProps} />);

        // Check for SVG icon (Search icon from Fluent)
        const button = screen.getByRole("button");
        const svg = button.querySelector("svg");
        expect(svg).toBeInTheDocument();
    });
});
