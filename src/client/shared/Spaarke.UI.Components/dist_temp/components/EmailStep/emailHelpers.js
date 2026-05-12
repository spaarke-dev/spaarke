/**
 * emailHelpers.ts
 * Utility functions for the generic SendEmailStep component.
 */
/**
 * Extract an email address from a lookup item name formatted as
 * "Display Name (user@example.com)".
 *
 * Returns the email portion if the pattern matches, otherwise an empty string.
 */
export function extractEmailFromUserName(name) {
    const match = name.match(/\(([^)]+@[^)]+)\)/);
    return match ? match[1] : '';
}
//# sourceMappingURL=emailHelpers.js.map