using System;
using System.Threading.Tasks;
using System.Threading;

namespace Sharp.Xmpp
{
    /// <summary>
    /// Utility class to use synchronously asynchronous method
    /// </summary>
    public static class AsyncHelper
    {
        private static readonly TaskFactory _myTaskFactory = new
          TaskFactory(CancellationToken.None,
                      TaskCreationOptions.None,
                      TaskContinuationOptions.None,
                      TaskScheduler.Default);

        /// <summary>
        /// To use synchronously asynchronous method which returns a <see cref="T:Task{TResult}"/>
        /// </summary>
        /// <typeparam name="TResult">Object expected in the <see cref="Task"/></typeparam>
        /// <param name="func"><see cref="T:Func{Task{TResult}}"/> Async function which returns a <see cref="Task"/> with an Object</param>
        /// <returns><see cref="Object"/> - Object expected with **TResult** type</returns>
        public static TResult RunSync<TResult>(Func<Task<TResult>> func)
        {
            return AsyncHelper._myTaskFactory
              .StartNew<Task<TResult>>(func)
              .Unwrap<TResult>()
              .GetAwaiter()
              .GetResult();
        }

        /// <summary>
        /// To use synchronously asynchronous method which returns a <see cref="Task"/>
        /// </summary>
        /// <param name="func"><see cref="T:Func{Task}"/> Async function which returns a <see cref="Task"/></param>
        /// <returns><see cref="Object"/> - Object expected with **TResult** type</returns>
        public static void RunSync(Func<Task> func)
        {
            AsyncHelper._myTaskFactory
              .StartNew<Task>(func)
              .Unwrap()
              .GetAwaiter()
              .GetResult();
        }
    }
}
