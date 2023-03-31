using System;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Provides data for the CallLogItemEventArgs event
    /// </summary>
    public class CallLogItemEventArgs : EventArgs
    {
        /// <summary>
        /// Id of the call log item
        /// </summary>
        public String Id
        {
            get;
            private set;
        }

        /// <summary>
        /// Id of the call
        /// </summary>
        public String CallId
        {
            get;
            private set;
        }

        /// <summary>
        /// Type of the call: webrtc / phone / conference
        /// </summary>
        public String Type
        {
            get;
            private set;
        }

        /// <summary>
        /// State of the call: missed / answered / failed
        /// </summary>
        public String State
        {
            get;
            private set;
        }

        /// <summary>
        /// Callee
        /// </summary>
        public String Callee
        {
            get;
            private set;
        }

        /// <summary>
        /// Caller
        /// </summary>
        public String Caller
        {
            get;
            private set;
        }

        /// <summary>
        /// Media of the call
        /// </summary>
        public String Media
        {
            get;
            private set;
        }

        /// <summary>
        /// Duration of the call
        /// </summary>
        public Int32 Duration
        {
            get;
            private set;
        }

        /// <summary>
        /// True is call og is marked as Read
        /// </summary>
        public Boolean Read
        {
            get;
            private set;
        }

        /// <summary>
        /// The time at which the call was originally started.
        /// </summary>
        public DateTime TimeStamp
        {
            get;
            private set;
        }

        public CallLogItemEventArgs(String id, String callId, String state, String callee, String caller, String media, String timeStamp, String duration, bool read, String type)
        {
            Id = id;
            CallId = callId;
            State = state;
            Callee = callee;
            Caller = caller;
            Media = media;
            Read = read;
            Type = type;

            DateTime result;
            if (!DateTime.TryParse(timeStamp, out result))
                result = DateTime.Now;
            TimeStamp = result;

            Int32 _duration;
            if(!Int32.TryParse(duration, out _duration))
                _duration = 0;
            Duration = _duration;
        }

    }
}
