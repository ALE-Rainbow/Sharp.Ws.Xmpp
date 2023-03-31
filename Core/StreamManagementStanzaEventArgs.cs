using System;

namespace Sharp.Xmpp.Core
{
    /// <summary>
    /// Provides data for the Presence event.
    /// </summary>
    public class StreamManagementStanzaEventArgs : EventArgs
    {
        /// <summary>
        /// The StreamManagementStanza stanza.
        /// </summary>
        public StreamManagementStanza Stanza
        {
            get;
            private set;
        }

        /// <summary>
        /// Initializes a new instance of the PresenceEventArgs class.
        /// </summary>
        /// <param name="stanza">The Presence stanza on whose behalf the event is
        /// raised.</param>
        /// <exception cref="ArgumentNullException">The stanza parameter is
        /// null.</exception>
        public StreamManagementStanzaEventArgs(StreamManagementStanza stanza)
        {
            stanza.ThrowIfNull("stanza");
            Stanza = stanza;
        }
    }
}