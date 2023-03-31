using System;

namespace Sharp.Xmpp.Extensions
{
    public enum CallLogResult
    {
        /// <summary>
        /// An error occured when asking messages archive
        /// </summary>
        Error,

        /// <summary>
        /// We have not asked all messages archive
        /// </summary>
        InProgress,

        /// <summary>
        /// All messages archives has been asked
        /// </summary>
        Complete
    }

    /// <summary>
    /// Provides data for the CallLogResultEventArgs event
    /// </summary>
    public class CallLogResultEventArgs : EventArgs
    {
        /// <summary>
        /// Id of the query which asked for messages stored in archive
        /// </summary>
        public String QueryId
        {
            get;
            private set;
        }

        /// <summary>
        /// Call log result
        /// </summary>
        public CallLogResult Result
        {
            get;
            private set;
        }

        /// <summary>
        /// The count of call logs entries
        /// </summary>
        public int Count
        {
            get;
            private set;
        }

        /// <summary>
        /// Message ID of the first call log received
        /// </summary>
        public string First
        {
            get;
            private set;
        }

        /// <summary>
        /// Message ID of the last call log received
        /// </summary>
        public string Last
        {
            get;
            private set;
        }

        public CallLogResultEventArgs(String queryId, CallLogResult result, int count, string first, string last)
        {
            QueryId = queryId;
            Result = result;
            Count = count;
            First = first;
            Last = last;
        }

        public CallLogResultEventArgs()
        {
            Result = CallLogResult.Error;
        }
    }
}
