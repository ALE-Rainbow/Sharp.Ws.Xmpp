using System;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Provides data for the CallLogItemEventArgs event
    /// </summary>
    public class VoiceMessagesEventArgs : EventArgs
    {
        /// <summary>
        /// Info - equals to "changed" or contains the number of voice messages
        /// </summary>
        public String Info
        {
            get;
            private set;
        }

        public VoiceMessagesEventArgs(String info)
        {
            Info = info;
        }

    }
}
