/**
 * Unit tests for ErrorState component
 *
 * @see ErrorState.tsx for implementation
 */
import * as React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { ErrorState } from "../../components/ErrorState";

// Wrapper for Fluent Provider
const renderWithProvider = (ui: React.ReactElement) => {
    return render(
        <FluentProvider theme={webLightTheme}>
            {ui}
        </FluentProvider>
    );
};

describe("ErrorState", () => {
    it("should render error heading", () => {
        renderWithProvider(
            <ErrorState
                message="Network error"
                retryable={false}
                onRetry={jest.fn()}
            />
        );

        // The component always shows "Something went wrong" as the heading
        expect(screen.getByText("Something went wrong")).toBeInTheDocument();
    });

    it("should show Try Again button when retryable", () => {
        renderWithProvider(
            <ErrorState
                message="Network error"
                retryable={true}
                onRetry={jest.fn()}
            />
        );

        expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
    });

    it("should not show Try Again button when not retryable", () => {
        renderWithProvider(
            <ErrorState
                message="Invalid request"
                retryable={false}
                onRetry={jest.fn()}
            />
        );

        expect(screen.queryByRole("button")).not.toBeInTheDocument();
    });

    it("should call onRetry when Try Again button clicked", () => {
        const onRetry = jest.fn();

        renderWithProvider(
            <ErrorState
                message="Network error"
                retryable={true}
                onRetry={onRetry}
            />
        );

        fireEvent.click(screen.getByRole("button", { name: /try again/i }));

        expect(onRetry).toHaveBeenCalledTimes(1);
    });

    it("should render error icon", () => {
        const { container } = renderWithProvider(
            <ErrorState
                message="Error"
                retryable={false}
                onRetry={jest.fn()}
            />
        );

        // Check for SVG icon (Error icon from Fluent)
        const svg = container.querySelector("svg");
        expect(svg).toBeInTheDocument();
    });

    it("should display user-friendly network error message", () => {
        renderWithProvider(
            <ErrorState
                message="Network error occurred"
                retryable={true}
                onRetry={jest.fn()}
            />
        );

        // The component maps error messages to user-friendly versions
        expect(screen.getByText(/unable to connect/i)).toBeInTheDocument();
    });

    it("should display user-friendly auth error message", () => {
        renderWithProvider(
            <ErrorState
                message="401 Unauthorized"
                retryable={true}
                onRetry={jest.fn()}
            />
        );

        expect(screen.getByText(/session has expired/i)).toBeInTheDocument();
    });

    it("should display generic retry message for unknown errors", () => {
        renderWithProvider(
            <ErrorState
                message="Unknown error XYZ"
                retryable={true}
                onRetry={jest.fn()}
            />
        );

        expect(screen.getByText(/please try again/i)).toBeInTheDocument();
    });
});
