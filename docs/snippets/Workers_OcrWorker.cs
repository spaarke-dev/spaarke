public sealed class OcrWorker : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly ILogger<OcrWorker> _log;
    private readonly IOcrJobHandler _handler;

    public OcrWorker(ServiceBusClient client, IOptions<ServiceBusOptions> opts, IOcrJobHandler handler, ILogger<OcrWorker> log)
    {
        _log = log; _handler = handler;
        _processor = client.CreateProcessor(opts.Value.JobsTopic, "ocr");
        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;
    }

    protected override Task ExecuteAsync(CancellationToken ct) => _processor.StartProcessingAsync(ct);

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            await _handler.ProcessAsync(args.Message.Body.ToString(), args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "OCR job failed");
            await args.DeadLetterMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    { _log.LogError(args.Exception, "Service Bus error"); return Task.CompletedTask; }
}