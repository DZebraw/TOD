using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace DawnTODEditor.AI
{
    internal interface IDawnTodAiMainThreadDispatcher
    {
        Task<T> RunAsync<T>(Func<T> action);
    }

    internal sealed class DawnTodAiMainThreadDispatcher : IDawnTodAiMainThreadDispatcher
    {
        private readonly int _mainThreadId;
        private readonly SynchronizationContext _context;

        public DawnTodAiMainThreadDispatcher()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _context = SynchronizationContext.Current;
        }

        public Task<T> RunAsync<T>(Func<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                return RunImmediately(action);
            }

            var completion = new TaskCompletionSource<T>();
            SendOrDelay(() => Complete(action, completion));
            return completion.Task;
        }

        private void SendOrDelay(Action action)
        {
            if (_context != null)
            {
                _context.Post(_ => action(), null);
            }
            else
            {
                EditorApplication.delayCall += () => action();
            }
        }

        private static Task<T> RunImmediately<T>(Func<T> action)
        {
            try
            {
                return Task.FromResult(action());
            }
            catch (Exception exception)
            {
                var completion = new TaskCompletionSource<T>();
                completion.SetException(exception);
                return completion.Task;
            }
        }

        private static void Complete<T>(Func<T> action, TaskCompletionSource<T> completion)
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }
    }
}
