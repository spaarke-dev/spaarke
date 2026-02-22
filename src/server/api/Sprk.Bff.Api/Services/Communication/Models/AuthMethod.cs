namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Derived authentication method for Graph API calls.
/// Not stored in Dataverse — derived from AccountType.
/// </summary>
public enum AuthMethod
{
    AppOnly,
    OnBehalfOf
}
