// -----------------------------------------------------------------------------
// UserProvisioningEntry.cs
//
// One operator-supplied user entry deserialized from
// ProvisioningRun.Parameters.NonSecret["usersJson"] (task 054, wave C4 Batch
// 3F). RunParameters.NonSecret is a flat IDictionary<string,string> by design
// (RunParameters.cs header: "keeps the shape open without opening a
// JSON-fragment hole that could accidentally carry a secret payload") — a
// user LIST does not fit a flat string dict, so H11 stores it as a single
// JSON-array-encoded string value under one non-secret key, matching
// RunParameters.cs's own documented allowance ("richer scalar types can be
// encoded as strings"). No secret fields appear here (email/name are not
// secrets) so this does not open the JSON-fragment hole RunParameters.cs
// guards against.
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>
/// One user to provision. <see cref="Email"/> is required for the B2BGuest
/// branch (Graph <c>/invitations</c> requires <c>invitedUserEmailAddress</c>)
/// and optional for the NativeAccount branch (UPN is generated from
/// <see cref="FirstName"/>/<see cref="LastName"/>, not from email).
/// </summary>
public sealed record UserProvisioningEntry(
    [property: JsonPropertyName("firstName")] string FirstName,
    [property: JsonPropertyName("lastName")] string LastName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("companyName")] string? CompanyName);
