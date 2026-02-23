namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Determines how an outbound email is sent.
/// SharedMailbox uses app-only auth (GraphClientFactory.ForApp()).
/// User uses delegated auth via OBO (GraphClientFactory.ForUserAsync()).
/// </summary>
public enum SendMode
{
    SharedMailbox = 0,
    User = 1
}
