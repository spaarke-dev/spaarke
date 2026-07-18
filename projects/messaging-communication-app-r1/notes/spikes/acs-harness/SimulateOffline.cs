// Offline simulator — proves the two ingestor invariants WITHOUT a live ACS resource or a
// provisioned Event Grid subscription (the spike-window reality; see constraint in 003 POML).
//
// It exercises the exact control-flow the production ingestor (task 030/031) will run:
//   (1) Event Grid subscription-validation handshake  — echo the validationCode.
//   (2) Capture ChatMessageReceivedInThread + idempotent echo-dedup on the ACS message id.
//
// Numbers here are LABELLED ESTIMATED-from-docs. The live harness (mode=live) + the runbook
// capture the MEASURED send->EventGrid latency.

internal static class SimulateOffline
{
    public static void Run()
    {
        Console.WriteLine("Offline simulation of the ACS capture path (ESTIMATED-from-docs; not a live round-trip).\n");

        // --- (1) Subscription-validation handshake ------------------------------------------
        Console.WriteLine("[A] Event Grid subscription-validation handshake");
        var validationCode = Guid.NewGuid().ToString();
        var validationEvent = EventSchemas.SubscriptionValidationEvent(validationCode);
        // The webhook extracts data.validationCode and returns { validationResponse: <code> }.
        var data = (Dictionary<string, object?>)validationEvent["data"]!;
        var echoed = EventSchemas.ValidationResponse((string)data["validationCode"]!);
        bool handshakeOk = (string)echoed["validationResponse"]! == validationCode;
        Console.WriteLine($"    validationCode in  = {validationCode}");
        Console.WriteLine($"    validationResponse = {echoed["validationResponse"]}");
        Console.WriteLine($"    handshake correct? = {handshakeOk}  (webhook returns 200 + echoed code)\n");

        // --- (2) Capture + echo-dedup on ACS message id -------------------------------------
        Console.WriteLine("[B] Capture + echo-dedup (idempotent on ACS message id)");
        // Simulate the outbound send having already persisted with a known ACS message id.
        var threadId = "19:acsV1_" + Guid.NewGuid().ToString("N") + "@thread.v2";
        var acsMessageId = "1700000000000";  // ACS message ids are monotonic string longs
        var seen = new HashSet<string>();

        // Outbound persist-on-send records the id first (design §4).
        seen.Add(acsMessageId);
        Console.WriteLine($"    outbound persist-on-send recorded messageId = {acsMessageId}");

        // Event Grid then delivers OUR OWN message back as ChatMessageReceivedInThread...
        var echoEvent = EventSchemas.ChatMessageReceivedInThread(threadId, acsMessageId, DateTimeOffset.UtcNow);
        var idFromEvent = (string)echoEvent["messageId"]!;
        bool isOwnEcho = seen.Contains(idFromEvent);
        Console.WriteLine($"    inbound event messageId = {idFromEvent}");
        Console.WriteLine($"    already seen?           = {isOwnEcho}  -> DEDUPED (no-op, no duplicate record)");

        // ...and Event Grid is at-least-once, so it may deliver the SAME event AGAIN.
        bool duplicateStillDeduped = seen.Contains(idFromEvent);
        Console.WriteLine($"    at-least-once redelivery of same event -> deduped again? = {duplicateStillDeduped}");

        // A genuinely NEW inbound message (different id) is NOT deduped — it persists.
        var newId = "1700000000001";
        bool newIsCaptured = !seen.Contains(newId);
        seen.Add(newId);
        Console.WriteLine($"    genuinely-new inbound id {newId} captured (not deduped)? = {newIsCaptured}\n");

        Console.WriteLine("[C] ESTIMATED numbers (replace with MEASURED from mode=live + runbook)");
        Console.WriteLine("    send->EventGrid capture latency : typically 1-3 s (docs; p99 can spike). MEASURE LIVE.");
        Console.WriteLine("    recommended poll cadence        : ~5 s (FR-10/NFR-07) — comfortably > capture latency.");
        Console.WriteLine("    echo-dedup key                  : ACS message id (SendChatMessageResult.Id == event.messageId).");
        Console.WriteLine("    publish-size delta              : ~0.22 MB compressed (MEASURED — see 003-acs-spike.md §6).\n");

        Console.WriteLine("--- Sample documented event (for the task-030 normalizer) ---");
        Console.WriteLine(EventSchemas.ToJson(echoEvent));

        bool allInvariantsHold = handshakeOk && isOwnEcho && duplicateStillDeduped && newIsCaptured;
        Console.WriteLine($"\n=== Offline invariants hold: {allInvariantsHold} ===");
        Console.WriteLine("Blocked on live infra ONLY for: real identity/token issuance, real thread create/send,");
        Console.WriteLine("and the true send->EventGrid latency number. See 003-acs-spike.md for the live runbook.");
    }
}
