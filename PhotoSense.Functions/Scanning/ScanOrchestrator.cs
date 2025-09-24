using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using PhotoSense.Core.Domain.Entities;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;

namespace PhotoSense.Functions.Scanning;

public record CountLocationRequest(string Path, bool Recursive);
public record ProcessLocationRequest(string Path, PhotoSet Set, bool Recursive, string InstanceId);

public class ScanOrchestrator
{
    private readonly IScanProgressStore _progress;
    private readonly IScanExecutionService _exec;

    public ScanOrchestrator(IScanProgressStore progress, IScanExecutionService exec)
    {
        _progress = progress;
        _exec = exec;
    }

    [Function(nameof(RunScanAsync))]
    public async Task RunScanAsync([OrchestrationTrigger] TaskOrchestrationContext ctx)
    {
        var input = ctx.GetInput<(string primary, string secondary, bool recursive)>();
        var instanceId = ctx.InstanceId;
        _progress.ScanStarted(instanceId);
        if (input == default) { _progress.ScanCompleted(instanceId); return; }

        var (primary, secondary, recursive) = input;

        var primaryTotal = await ctx.CallActivityAsync<int>(nameof(CountLocationActivity), new CountLocationRequest(primary, recursive));
        var secondaryTotal = await ctx.CallActivityAsync<int>(nameof(CountLocationActivity), new CountLocationRequest(secondary, recursive));
        _progress.SetTotals(instanceId, primaryTotal, secondaryTotal);

        await ctx.CallActivityAsync(nameof(ProcessLocationActivity), new ProcessLocationRequest(primary, PhotoSet.Primary, recursive, instanceId));
        await ctx.CallActivityAsync(nameof(ProcessLocationActivity), new ProcessLocationRequest(secondary, PhotoSet.Secondary, recursive, instanceId));

        _progress.ScanCompleted(instanceId);
    }

    [Function(nameof(CountLocationActivity))]
    public int CountLocationActivity([ActivityTrigger] CountLocationRequest req)
        => ScanExecutionService.EnumerateFiles(req.Path, req.Recursive).Count();

    [Function(nameof(ProcessLocationActivity))]
    public async Task ProcessLocationActivity([ActivityTrigger] ProcessLocationRequest req)
    {
        var files = ScanExecutionService.EnumerateFiles(req.Path, req.Recursive).ToList();
        await _exec.ProcessAsync(files, req.Set, req.InstanceId);
    }
}
