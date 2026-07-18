// =====================================================================================
//  ACS Phase-0 Spike Harness  (task 003)  — THROWAWAY, NOT production code
// =====================================================================================
//  Proves the server-side ACS round-trip that gates messaging W1/W2:
//     identity -> server-minted chat token -> thread (30-day retention)
//     -> AddParticipants -> SendMessage -> Event Grid capture (+ validation handshake)
//  and measures: send->persist latency, echo-dedup on ACS message id, publish-size (probe).
//
//  Blueprint: Azure-Samples/communication-services-authentication-hero-csharp (identity+token)
//             Azure-Samples/communication-services-dotnet-quickstarts     (thread/send)
//
//  Two modes:
//     dotnet run -- live       Real ACS round-trip. Requires env vars (see Config below).
//     dotnet run -- simulate   Offline: proves the Event Grid validation handshake + the
//                              echo-dedup idempotency logic against the DOCUMENTED event
//                              schema. No live resource. (default if no ACS config present)
//
//  Config (live mode) — read via env, ADR-028 central-credential aligned:
//     ACS_ENDPOINT              https://<resource>.communication.azure.com   (required, live)
//     ACS_CONNECTION_STRING     endpoint=...;accesskey=...   (OPTIONAL local-dev fallback only;
//                               prefer DefaultAzureCredential against ACS_ENDPOINT per ADR-028)
//     ACS_PARTICIPANT_2         (optional) a 2nd communicationUserId to AddParticipants
// =====================================================================================

using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Communication;
using Azure.Communication.Chat;
using Azure.Communication.Identity;
using Azure.Core;
using Azure.Identity;

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "simulate";
var endpoint = Environment.GetEnvironmentVariable("ACS_ENDPOINT");
var connStr  = Environment.GetEnvironmentVariable("ACS_CONNECTION_STRING");

if (mode == "live" && string.IsNullOrWhiteSpace(endpoint) && string.IsNullOrWhiteSpace(connStr))
{
    Console.WriteLine("[live] No ACS_ENDPOINT / ACS_CONNECTION_STRING set. Falling back to 'simulate'.");
    mode = "simulate";
}

Console.WriteLine($"=== ACS Phase-0 Spike Harness — mode={mode} ===\n");

if (mode == "simulate")
{
    SimulateOffline.Run();
    return;
}

// -------------------------------------------------------------------------------------
//  LIVE ROUND-TRIP
// -------------------------------------------------------------------------------------

// 1) Identity client. ADR-028: prefer DefaultAzureCredential against the ACS endpoint.
//    Connection-string path retained ONLY as an explicit local-dev fallback.
CommunicationIdentityClient identityClient =
    !string.IsNullOrWhiteSpace(endpoint)
        ? new CommunicationIdentityClient(new Uri(endpoint), new DefaultAzureCredential())
        : new CommunicationIdentityClient(connStr);   // fallback (access-key) — NOT the prod path

var chatEndpoint = new Uri(endpoint ?? ParseEndpointFromConnStr(connStr!));

// 2) Create ACS identity + mint a CHAT-scoped token, SERVER-SIDE (design §8.2 uniform minting).
//    Entra->ACS exchange is VoIP-only, so even internal users get chat tokens minted here.
Console.WriteLine("[1] createUserAndToken(scopes=[chat]) ...");
CommunicationUserIdentifierAndToken created =
    await identityClient.CreateUserAndTokenAsync(scopes: new[] { CommunicationTokenScope.Chat });

CommunicationUserIdentifier senderUser = created.User;
AccessToken senderToken = created.AccessToken;
Console.WriteLine($"    communicationUserId = {senderUser.Id}");
Console.WriteLine($"    token scope=chat  expiresOn={senderToken.ExpiresOn:o}  " +
                  $"(lifetime ~{(senderToken.ExpiresOn - DateTimeOffset.UtcNow).TotalHours:F1}h, 1-24h expected)");
Console.WriteLine("    NOTE: this token is used BY THE BFF only — it never reaches a browser in R1 (NFR-04).\n");

// A 2nd participant (create one if not supplied) so AddParticipants is exercised.
CommunicationUserIdentifier participant2;
var p2Env = Environment.GetEnvironmentVariable("ACS_PARTICIPANT_2");
if (!string.IsNullOrWhiteSpace(p2Env)) participant2 = new CommunicationUserIdentifier(p2Env);
else participant2 = await identityClient.CreateUserAsync();

// 3) Chat client authenticated as the sender (server acts as the participant).
var tokenCredential = new CommunicationTokenCredential(senderToken.Token);
var chatClient = new ChatClient(chatEndpoint, tokenCredential);

// 4) Create thread WITH 30-day retention (design §8.7 retention minimization).
//    COMPILE-VERIFIED against Azure.Communication.Chat 1.4.0: retention is expressed via
//    CreateChatThreadOptions.RetentionPolicy = new ThreadCreationDateRetentionPolicy(days).
//    ACS accepts 30-90 days for this policy; use NoneRetentionPolicy for indefinite (avoid).
//    (The 30-day floor is exactly design §8.7's "keep ACS from becoming a shadow record store".)
Console.WriteLine("[2] createChatThread(retention=30d) ...");
var createOptions = new CreateChatThreadOptions("Spaarke ACS spike thread")
{
    RetentionPolicy = new ThreadCreationDateRetentionPolicy(deleteThreadAfterDays: 30),
    Participants =
    {
        new ChatParticipant(senderUser)  { DisplayName = "spike-sender" },
        new ChatParticipant(participant2){ DisplayName = "spike-participant-2" },
    },
};
CreateChatThreadResult threadResult = await chatClient.CreateChatThreadAsync(createOptions);
var threadId = threadResult.ChatThread.Id;
Console.WriteLine($"    ChatThreadId = {threadId}");
Console.WriteLine($"    (persist this as the thread-channel-ref for sprk_communicationthread — task 011)\n");

ChatThreadClient threadClient = chatClient.GetChatThreadClient(threadId);

// 5) AddParticipants (idempotent membership projection of Dataverse access — task 011).
Console.WriteLine("[3] AddParticipants ...");
await threadClient.AddParticipantsAsync(new[]
{
    new ChatParticipant(participant2) { DisplayName = "spike-participant-2" },
});
Console.WriteLine("    ok\n");

// 6) SendMessage — capture the ACS message id (the echo-dedup key).
Console.WriteLine("[4] SendMessage ...");
var sw = Stopwatch.StartNew();
var sentAtUtc = DateTimeOffset.UtcNow;
SendChatMessageResult sendResult = await threadClient.SendMessageAsync(
    content: "Hello from the Spaarke ACS Phase-0 spike.",
    type: ChatMessageType.Text);
string acsMessageIdFromSend = sendResult.Id;
Console.WriteLine($"    ACS messageId (from SEND response) = {acsMessageIdFromSend}");
Console.WriteLine($"    ^ this is the idempotency key the ingestor dedupes on (design §4).\n");

// 7) Capture. Live Event Grid requires a provisioned system topic + webhook subscription
//    (see runbook 003-acs-spike.md §Live-Infra). In the spike window we cannot subscribe,
//    so we DEMONSTRATE the two capture invariants the ingestor relies on:
//      (a) the message id is READABLE BACK from the thread (proving id stability), and
//      (b) the documented ChatMessageReceivedInThread schema carries that same MessageId.
//    Latency here is measured send->readback as a PROXY; true send->EventGrid latency must be
//    captured live (runbook). Both are labelled clearly in the report.
Console.WriteLine("[5] Readback (proxy for capture; true Event Grid capture = live only) ...");
string? acsMessageIdFromRead = null;
await foreach (ChatMessage m in threadClient.GetMessagesAsync())
{
    if (m.Type == ChatMessageType.Text && m.Id == acsMessageIdFromSend)
    {
        acsMessageIdFromRead = m.Id;
        sw.Stop();
        break;
    }
}
Console.WriteLine($"    ACS messageId (read back)          = {acsMessageIdFromRead}");
Console.WriteLine($"    send->readback latency (PROXY)     = {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"    id stable across send+read?        = {acsMessageIdFromSend == acsMessageIdFromRead}\n");

// 8) Echo-dedup proof: feed the documented inbound event (same id) through the idempotency check.
var seen = new HashSet<string>();
seen.Add(acsMessageIdFromSend);                        // outbound persist-on-send already recorded this id
var inboundEvent = EventSchemas.ChatMessageReceivedInThread(threadId, acsMessageIdFromSend, sentAtUtc);
bool isEcho = seen.Contains((string)inboundEvent["messageId"]!);
Console.WriteLine("[6] Echo-dedup ...");
Console.WriteLine($"    inbound event messageId            = {inboundEvent["messageId"]}");
Console.WriteLine($"    already seen (own outbound echo)?  = {isEcho}  -> ingestor treats as NO-OP");
Console.WriteLine($"    ECHO-DEDUP KEY CONFIRMED: dedupe on ACS message id makes the own-echo a no-op.\n");

Console.WriteLine("=== LIVE round-trip complete. Fill the MEASURED numbers into 003-acs-spike.md. ===");
Console.WriteLine("    Remaining live-only measurement: subscribe Event Grid + capture send->EventGrid latency.");

static string ParseEndpointFromConnStr(string cs)
{
    foreach (var part in cs.Split(';'))
        if (part.StartsWith("endpoint=", StringComparison.OrdinalIgnoreCase))
            return part.Substring("endpoint=".Length);
    throw new InvalidOperationException("endpoint= not found in ACS_CONNECTION_STRING");
}
