using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HonzaBotner.Scheduler.Contract;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HonzaBotner.Scheduler;

public class SchedulerHostedService : BackgroundService
{
    private readonly int _delay;
    private readonly ILogger<SchedulerHostedService> _logger;
    private readonly IList<CronJobWrapper> _cronJobs;

    public SchedulerHostedService(int delay, IEnumerable<ICronJob> cronJobs, ILogger<SchedulerHostedService> logger)
    {
        DateTime now = DateTime.UtcNow;

        _delay = delay;
        _logger = logger;
        _cronJobs = cronJobs
            .Select(job =>
            {
                var cronJobWrapper = new CronJobWrapper(job, job.CronExpression, now);
                cronJobWrapper.Next(); // Run Crons based on their expressions.
                return cronJobWrapper;
            })
            .ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            DateTime currentTime = DateTime.UtcNow;

            _logger.LogInformation("Scheduler running at: {Time}", currentTime.ToLocalTime());
            await RunOnceAsync(currentTime, cancellationToken);

            await Task.Delay(_delay, cancellationToken);
        }
    }

    private async Task RunOnceAsync(DateTime currentTime, CancellationToken cancellationToken)
    {
        IList<CronJobWrapper> jobsToRun = _cronJobs.Where(job => job.ShouldRun(currentTime)).ToList();

        foreach (CronJobWrapper cronJob in jobsToRun)
        {
            cronJob.Next();

            try
            {
                _logger.LogInformation("Starting job {JobType}", cronJob.Job.Name);
                await cronJob.Job.ExecuteAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Job {JobType} failed", cronJob.Job.Name);
            }
        }
    }
}
