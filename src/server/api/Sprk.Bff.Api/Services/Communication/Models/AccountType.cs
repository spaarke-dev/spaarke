namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Communication account type matching Dataverse sprk_accounttype choice values.
/// </summary>
public enum AccountType
{
    SharedAccount = 100000000,
    ServiceAccount = 100000001,
    UserAccount = 100000002
}
