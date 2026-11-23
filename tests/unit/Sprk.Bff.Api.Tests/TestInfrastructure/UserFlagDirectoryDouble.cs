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

    /// <summary>
    /// Task 198 R2: each user's <c>isdisabled</c>. A user not listed here is returned enabled (<c>false</c>), as Dataverse always
    /// returns the selected column; a user mapped to <c>null</c> is returned WITHOUT the column (an unreadable value).
    /// </summary>
    public Dictionary<Guid, bool?> Disabled { get; } = new();

    public bool Faults { get; set; }

    /// <summary>
    /// When set, every <c>systemusers</c> read throws what this returns instead of <see cref="Faults"/>'s
    /// <see cref="HttpRequestException"/> — e.g. a <see cref="TaskCanceledException"/> (an HttpClient timeout, no caller
    /// cancellation) or an <see cref="OperationCanceledException"/> paired with a cancelled caller token (#1821).
    /// </summary>
    public Func<Exception>? Fault { get; set; }

    public List<string> Queries { get; } = new();

    /// <summary>The <c>$select</c> of every <c>systemusers</c> read, in order.</summary>
    public List<string> Selects { get; } = new();

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
        Selects.Add(select ?? string.Empty);
        if (Fault is not null)
            throw Fault();
        if (Faults)
            throw new HttpRequestException("Test: the users could not be read.");

        var rows = UserId.Matches(filter ?? string.Empty)
            .Select(m => Guid.Parse(m.Groups[1].Value))
            .Where(Flags.ContainsKey)
            .Select(id =>
            {
                var row = new Dictionary<string, object?> { ["systemuserid"] = id, ["sprk_isexternal"] = Flags[id] };
                var disabled = Disabled.TryGetValue(id, out var value) ? value : false;
                if (disabled is not null)
                    row["isdisabled"] = disabled;
                return row;
            })
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
