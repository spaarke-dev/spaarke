// -----------------------------------------------------------------------------
// ProductionCosmosSerializer.cs
//
// unified-access-control-r2 task 165, owner round 49 item 2 — "the fake repository
// round-trips through the production serializer, so the tests see what production
// sees".
//
// The serializer is NOT a hand-picked equivalent: it is the one the production
// CosmosClient carries — CosmosModule.BuildCosmosClient(...).ClientOptions.Serializer
// (the SDK's default Newtonsoft-based serializer, camelCase, exactly as L2 builds
// it). Building the client opens no connection, so no Cosmos account is needed.
// A fake repository that stores the serialized text and hands back a fresh
// deserialized run cannot hide a field that does not survive Cosmos — the H8
// resume record lost that way (JsonElement evidence → {"valueKind":1}) is what the
// round-41 tests could not see.
// -----------------------------------------------------------------------------

using System.Text;
using Azure.Core;
using Microsoft.Azure.Cosmos;
using Sprk.Provisioning.ControlPlane.Modules;

namespace Sprk.Provisioning.ControlPlane.Tests.Models;

/// <summary>The serializer the production <see cref="CosmosClient"/> persists runs with.</summary>
internal static class ProductionCosmosSerializer
{
    private static readonly Lazy<CosmosSerializer> Serializer = new(() =>
        CosmosModule.BuildCosmosClient("https://localhost:8081/", new UnusedCredential()).ClientOptions.Serializer
        ?? throw new InvalidOperationException("The production CosmosClient carries no serializer — update this helper."));

    /// <summary>The JSON text Cosmos would store for <paramref name="value"/>.</summary>
    public static string Serialize<T>(T value)
    {
        using var stream = Serializer.Value.ToStream(value);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>The object a Cosmos read of <paramref name="json"/> would return.</summary>
    public static T Deserialize<T>(string json) =>
        Serializer.Value.FromStream<T>(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    /// <summary>What a write followed by a read would hand back.</summary>
    public static T RoundTrip<T>(T value) => Deserialize<T>(Serialize(value));

    private sealed class UnusedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No Cosmos request is made — only the client's serializer is used.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No Cosmos request is made — only the client's serializer is used.");
    }
}
