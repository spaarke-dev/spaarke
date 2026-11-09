using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// The <c>systemusers</c> flag read (<c>sprk_isexternal</c>) the Restricted-filing bar makes; every other Web API read answers
/// nothing. Shared by the task 198 Part C and Part D tests.
/// </summary>
internal sealed class UserFlagDirectoryDouble : DataverseWebApiClient
{
    private static readonly Regex UserId = new(@"systemuserid eq ([0-9a-fA-F-]{36})");

    public UserFlagDirectoryDouble()
        : base(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
        }).Build(), NullLogger<DataverseWebApiClient>.Instance, new NoCredential())
    {
    }

    /// <summary>Each user's stored flag (<c>null</c> = blank); a user absent here is not returned.</summary>
    public Dictionary<Guid, bool?> Flags { get; } = new();

    public bool Faults { get; set; }

    public List<string> Queries { get; } = new();

    /// <summary>Marks every user in <paramref name="users"/> internal.</summary>
    public UserFlagDirectoryDouble Internal(params Guid[] users)
    {
        foreach (var user in users)
            Flags[user] = false;
        return this;
    }

    public override Task<List<T>> QueryAsync<T>(
        string entitySetName, string? filter = null, string? select = null, int? top = null, int? skip = null,
        CancellationToken cancellationToken = default)
    {
        if (entitySetName != "systemusers")
            return Task.FromResult(new List<T>());

        Queries.Add(filter ?? string.Empty);
        if (Faults)
            throw new HttpRequestException("Test: the users could not be read.");

        var rows = UserId.Matches(filter ?? string.Empty)
            .Select(m => Guid.Parse(m.Groups[1].Value))
            .Where(Flags.ContainsKey)
            .Select(id => new Dictionary<string, object?> { ["systemuserid"] = id, ["sprk_isexternal"] = Flags[id] })
            .ToList();
        return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
    }

    private sealed class NoCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Test: this double never authenticates.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Test: this double never authenticates.");
    }
}
