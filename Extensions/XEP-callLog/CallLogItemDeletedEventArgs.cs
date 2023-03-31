using System;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Provides data for the CallLogItemDeletedEventArgs event
    /// </summary>
    public class CallLogItemDeletedEventArgs : EventArgs
    {
        /// <summary>
        /// Call logs item deleted usin peerId
        /// </summary>
        public String PeerId
        {
            get;
            private set;
        }

        /// <summary>
        /// Call logs item deleted usin callId
        /// </summary>
        public String CallId
        {
            get;
            private set;
        }

        public CallLogItemDeletedEventArgs(String peerId, String callId)
        {
            PeerId = peerId;
            CallId = callId;
        }
    }
}