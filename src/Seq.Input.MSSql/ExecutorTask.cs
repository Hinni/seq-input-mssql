using System;
using System.Threading;
using System.Threading.Tasks;
using Seq.Input.MSSql;
using Serilog;

namespace Seq.Input.MsSql
{
    public class ExecutorTask : IDisposable
    {
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly Task _executorTask;

        public ExecutorTask(ILogger logger, TimeSpan interval, string timePeriod, Executor executor)
        {
            _executorTask = Task.Run(() => Run(logger, interval, timePeriod, executor, _cancel.Token), _cancel.Token);
        }

        private static async Task Run(ILogger logger, TimeSpan interval, string timePeriod, Executor executor, CancellationToken cancel)
        {
            try
            {
                while (!cancel.IsCancellationRequested)
                {
                    // In valid time period?
                    if (TimePeriodHelper.IsValidTimePeriod(DateTime.Now, timePeriod))
                    {
                        await executor.Start();
                    }
                    await Task.Delay(interval, cancel);
                }
            }
            catch (OperationCanceledException)
            {
                logger.Information("The executor task was canceled.");
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "The executor task threw an unhandled exception.");
            }
        }

        public void Stop()
        {
            _cancel?.Cancel();
            _executorTask?.Wait();
        }

        public void Dispose()
        {
            _cancel?.Dispose();
            _executorTask?.Dispose();
        }
    }
}