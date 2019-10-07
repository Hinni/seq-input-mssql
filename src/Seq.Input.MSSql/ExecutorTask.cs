using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Seq.Input.MSSql
{
    public class ExecutorTask : IDisposable
    {
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly Task _executorTask;

        public ExecutorTask(ILogger logger, TimeSpan interval, Executor executor)
        {
            _executorTask = Task.Run(() => Run(logger, interval, executor, _cancel.Token), _cancel.Token);
        }

        private static async Task Run(ILogger logger, TimeSpan interval, Executor executor, CancellationToken cancel)
        {
            try
            {
                while (!cancel.IsCancellationRequested)
                {
                    executor.Start();
                    await Task.Delay(interval, cancel);
                }
            }
            catch (OperationCanceledException)
            {
                // Unloading
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "The executor task threw an unhandled exception.");
            }
        }

        public void Stop()
        {
            _cancel.Cancel();
            _executorTask.Wait();
        }

        public void Dispose()
        {
            _cancel.Dispose();
            _executorTask.Dispose();
        }
    }
}