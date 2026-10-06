using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Functions.Scanning;

public record RunScanInput(ScanRequest Request, string InstanceId);

public class ScanOrchestrator
{
    private readonly IScanExecutionService _exec;

    public ScanOrchestrator(IScanExecutionService exec) => _exec = exec;

    [Function(nameof(RunScanAsync))]
    public async Task RunScanAsync([OrchestrationTrigger] TaskOrchestrationContext ctx)
    {
        var request = ctx.GetInput<ScanRequest>();
        if (request is null) return;
        // The whole scan is one activity: an orchestrator is replayed after every step, so it must not
        // report progress itself, and pruning needs to know every file both folders held.
        await ctx.CallActivityAsync<ScanSummary>(nameof(RunScanActivity), new RunScanInput(request, ctx.InstanceId));
    }

    [Function(nameof(RunScanActivity))]
    public Task<ScanSummary> RunScanActivity([ActivityTrigger] RunScanInput input)
        => _exec.RunAsync(input.Request, input.InstanceId);
}
