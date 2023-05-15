using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SingleInstanceManager
{
    internal class MutexContext : IDisposable
    {
        private readonly BlockingCollection<Action> _pendingOperations = new BlockingCollection<Action>();

        public MutexContext()
        {
            Task.Factory.StartNew(
                ProcessLoop,
                default,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
        }

        public void Dispose()
        {
            _pendingOperations.CompleteAdding();
        }

        public void Execute(Action? action)
        {
            TaskCompletionSource<object?> tcs = new TaskCompletionSource<object?>();

            void ExecuteInternal()
            {
                try
                {
                    action?.Invoke();
                    tcs.SetResult(null);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            }

            _pendingOperations.Add(ExecuteInternal);
            tcs.Task.GetAwaiter().GetResult();
        }

        public T Execute<T>(Func<T> action)
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();

            void ExecuteInternal()
            {
                try
                {
                    T result = action.Invoke();
                    tcs.SetResult(result);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            }

            _pendingOperations.Add(ExecuteInternal);
            return tcs.Task.GetAwaiter().GetResult();
        }

        private void ProcessLoop()
        {
            IEnumerable<Action> operations = _pendingOperations.GetConsumingEnumerable();
            foreach (Action operation in operations)
            {
                operation.Invoke();
            }
        }
    }
}
