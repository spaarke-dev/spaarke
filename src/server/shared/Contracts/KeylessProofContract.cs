// -----------------------------------------------------------------------------
// KeylessProofContract.cs — a SOURCE-LINKED contract (not a project), the same mechanism as
// SpeContainerCustomerMarker.cs next to it.
//
// customer-provisioning-orchestration-r1 task 230b (owner D13): the customer's BFF proves, with its own managed
// identity, one real call to each Azure service of its stamp; L2's acceptance gate (H13) calls that proof and refuses
// Ready unless every service is proved. Three parties must agree on these strings — the BFF endpoint and its filter,
// H3 (which defines the app role on the customer's BFF app registration and assigns it to the L2 Worker identity) and
// H13 (which calls the route and parses the outcomes). Two deployables, one definition: this file is compiled into
// both with <Compile Include="..." Link="..." /> and is `internal` in each.
// -----------------------------------------------------------------------------

namespace Spaarke.Contracts.Provisioning;

/// <summary>The keyless-proof contract shared by the BFF and the L2 control plane.</summary>
internal static class KeylessProofContract
{
    /// <summary>The BFF route H13 calls (POST).</summary>
    public const string Route = "/api/platform/keyless-proof";

    /// <summary>
    /// The application role on the customer's BFF app registration that admits a caller to <see cref="Route"/>.
    /// Application-only (<c>allowedMemberTypes: ["Application"]</c>); H3 assigns it to the L2 Worker identity only.
    /// </summary>
    public const string AppRoleValue = "Provisioning.KeylessProof";

    /// <summary>The role's id. Fixed so H3's create and reconcile are idempotent across runs and customers.</summary>
    public const string AppRoleId = "528b7c40-41f6-4dbe-aaf0-9e6d1a622f4b";

    /// <summary>The closed set of probe outcomes.</summary>
    public static class Outcomes
    {
        /// <summary>The service accepted the managed-identity call.</summary>
        public const string Proved = "proved";

        /// <summary>The service refused the identity (HTTP 401/403), or no token could be acquired.</summary>
        public const string Refused = "refused";

        /// <summary>A key or connection string is configured for the service — not keyless. Not called.</summary>
        public const string KeyCredential = "key-credential";

        /// <summary>A setting the service needs is missing. Not called.</summary>
        public const string NotConfigured = "not-configured";

        /// <summary>Transport failure, timeout, throttling or a server error — no verdict on the identity.</summary>
        public const string Unreachable = "unreachable";

        /// <summary>The service answered with another error (for example a missing deployment or index).</summary>
        public const string Failed = "failed";
    }

    /// <summary>The service ids the proof reports — every one must be present in a response.</summary>
    public static class Services
    {
        public const string ServiceBus = "service-bus";
        public const string Redis = "redis";
        public const string AiSearch = "ai-search";
        public const string OpenAiChat = "openai-chat";
        public const string OpenAiEmbeddings = "openai-embeddings";
        public const string DocumentIntelligence = "document-intelligence";
        public const string Cosmos = "cosmos";
        public const string BlobStorage = "blob-storage";
        public const string ContentSafetyPromptShield = "content-safety-prompt-shield";
        public const string ContentSafetyGroundedness = "content-safety-groundedness";

        /// <summary>Every service, in the order the BFF reports them.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            ServiceBus, Redis, OpenAiChat, OpenAiEmbeddings, DocumentIntelligence, AiSearch, Cosmos, BlobStorage,
            ContentSafetyPromptShield, ContentSafetyGroundedness,
        };
    }
}
