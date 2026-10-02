// -----------------------------------------------------------------------------
// UserProvisioningIntake.cs
//
// Task 245c (G25 — run context, part 3). H11's input rules, in ONE place:
// POST /api/runs applies them at intake (400 before any registry lookup, Cosmos
// write or enqueue) and H11 applies them again before its first Graph call
// (defence in depth). The same code in both places is what makes "intake
// accepts exactly what H11 accepts" true by construction rather than by review.
//
// Before T245c the rules lived inline in H11, nothing checked them at intake,
// and two were only checked mid-loop, after Graph calls had already happened:
//   - a B2BGuest entry without an email was found while inviting, so a list
//     whose third entry had none failed after two invitations had gone out;
//   - a NativeAccount entry without a first or last name threw inside UPN
//     building (GraphRestUserProvisioner.SanitizeName), after the earlier users
//     had been created, and surfaced as an "infrastructure error".
// The rules are what each preset actually needs — no stricter: NativeAccount
// builds the UPN and display name from the names (both required); a B2BGuest
// invitation is sent to the email (required) and the names only set the
// optional display name. At most MaxUsers entries (the list is stored in the
// Cosmos run document, owner decision D15; an unbounded list could exceed the
// 2 MB item limit).
//
// Diagnostics name an entry by its 1-based position, never by its email or name,
// and never echo parser internals.
//
// §11 justification — existing: H11UserProvisioningHandler's inline guards
// (identityPreset / usersJson). Extension: those guards moved here, H11 calls
// this; a second copy in RunsEndpoints would drift. Cost of doing nothing: a run
// whose user list H11 would refuse is accepted, creates the whole stamp (H0–H10),
// and stops at H11 with no way to correct its intake (intake is fixed at
// POST /api/runs).
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>H11's identity-preset + user-list rules, shared by <c>POST /api/runs</c> and H11 (task 245c).</summary>
public static class UserProvisioningIntake
{
    /// <summary>D6 identity preset — cross-tenant B2B guest access.</summary>
    public const string B2BGuest = "B2BGuest";

    /// <summary>D6 identity preset — native account in the stamp's tenant.</summary>
    public const string NativeAccount = "NativeAccount";

    /// <summary>The accepted identity presets (ordinal — exact case).</summary>
    public static IReadOnlyList<string> IdentityPresets { get; } = [B2BGuest, NativeAccount];

    /// <summary>Most users one run provisions (intake.schema.json <c>users.maxItems</c> — IntakeSchemaProfileParityTests).</summary>
    public const int MaxUsers = 500;

    private const int MaxEchoedLength = 64;

    private static readonly JsonSerializerOptions UsersJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Validates the run's <c>identityPreset</c> and <c>usersJson</c> values. Every entry is checked before the
    /// result is returned, so a caller acting on <see cref="UserProvisioningIntakeOutcome.Valid"/> never meets an
    /// unusable entry part-way through the list.
    /// </summary>
    public static UserProvisioningIntakeOutcome Validate(string? identityPreset, string? usersJson)
    {
        if (string.IsNullOrWhiteSpace(identityPreset))
        {
            return new UserProvisioningIntakeOutcome.Invalid(H11Rejections.MissingIdentityPreset,
                $"'identityPreset' is required (design.md D6) — '{B2BGuest}' or '{NativeAccount}'.");
        }
        var isNativeAccount = string.Equals(identityPreset, NativeAccount, StringComparison.Ordinal);
        if (!isNativeAccount && !string.Equals(identityPreset, B2BGuest, StringComparison.Ordinal))
        {
            var echoed = identityPreset.Length > MaxEchoedLength ? identityPreset[..MaxEchoedLength] + "…" : identityPreset;
            return new UserProvisioningIntakeOutcome.Invalid(H11Rejections.InvalidIdentityPreset,
                $"'identityPreset' value '{echoed}' is not one of the two D6 presets " +
                $"('{B2BGuest}', '{NativeAccount}'; exact case).");
        }

        if (string.IsNullOrWhiteSpace(usersJson))
        {
            return new UserProvisioningIntakeOutcome.Invalid(H11Rejections.MissingUsers,
                "'usersJson' is required — a JSON array of {firstName, lastName, email, companyName} entries " +
                "with at least one user.");
        }

        IReadOnlyList<UserProvisioningEntry?>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<IReadOnlyList<UserProvisioningEntry?>>(usersJson, UsersJsonOptions);
        }
        catch (JsonException ex)
        {
            // Position only — ex.Message names internal .NET types and can echo the offending text.
            return new UserProvisioningIntakeOutcome.Invalid(H11Rejections.MalformedUsersPayload,
                "'usersJson' is not a JSON array of {firstName, lastName, email, companyName} objects " +
                $"(invalid at path '{ex.Path ?? "$"}', line {ex.LineNumber ?? 0}, byte {ex.BytePositionInLine ?? 0}).");
        }

        if (parsed is null)
        {
            return new UserProvisioningIntakeOutcome.Invalid(H11Rejections.MissingUsers,
                "'usersJson' is JSON null — a JSON array with at least one user is required.");
        }
        var users = parsed;
        if (users.Count == 0)
        {
            return new UserProvisioningIntakeOutcome.Invalid(H11Rejections.MissingUsers,
                "'usersJson' is an empty array — at least one user is required.");
        }
        if (users.Count > MaxUsers)
        {
            return new UserProvisioningIntakeOutcome.Invalid(H11Rejections.TooManyUsers,
                $"'usersJson' has {users.Count} entries — at most {MaxUsers} users per run.");
        }

        for (var i = 0; i < users.Count; i++)
        {
            var entry = users[i];
            var position = i + 1;
            if (entry is null)
            {
                return new UserProvisioningIntakeOutcome.Invalid(H11Rejections.InvalidUserEntry,
                    $"'usersJson' entry {position} is null.");
            }
            if (isNativeAccount && (string.IsNullOrWhiteSpace(entry.FirstName) || string.IsNullOrWhiteSpace(entry.LastName)))
            {
                return new UserProvisioningIntakeOutcome.Invalid(H11Rejections.InvalidUserEntry,
                    $"'usersJson' entry {position} needs a non-blank 'firstName' and 'lastName' — a {NativeAccount} " +
                    "user's UPN and display name are built from them.");
            }
            if (!isNativeAccount && string.IsNullOrWhiteSpace(entry.Email))
            {
                return new UserProvisioningIntakeOutcome.Invalid(H11Rejections.InvalidUserEntry,
                    $"'usersJson' entry {position} has no 'email' — a {B2BGuest} invitation is sent to it " +
                    "(invitedUserEmailAddress).");
            }
        }

        return new UserProvisioningIntakeOutcome.Valid(isNativeAccount, users.Select(u => u!).ToList());
    }
}

/// <summary>Result of <see cref="UserProvisioningIntake.Validate"/>.</summary>
public abstract record UserProvisioningIntakeOutcome
{
    private UserProvisioningIntakeOutcome() { }

    /// <summary>The values are usable: the preset, and every entry checked.</summary>
    /// <param name="IsNativeAccount"><c>true</c> for <see cref="UserProvisioningIntake.NativeAccount"/>, <c>false</c> for <see cref="UserProvisioningIntake.B2BGuest"/>.</param>
    /// <param name="Users">1 to <see cref="UserProvisioningIntake.MaxUsers"/> entries; NativeAccount entries have a first and last name, B2BGuest entries an email.</param>
    public sealed record Valid(bool IsNativeAccount, IReadOnlyList<UserProvisioningEntry> Users) : UserProvisioningIntakeOutcome;

    /// <summary>The values break a rule.</summary>
    /// <param name="RejectionCode">An <see cref="H11Rejections"/> code — the same code at intake and in H11.</param>
    /// <param name="Diagnostic">What is wrong; names entries by position, never by personal data.</param>
    public sealed record Invalid(string RejectionCode, string Diagnostic) : UserProvisioningIntakeOutcome;
}
