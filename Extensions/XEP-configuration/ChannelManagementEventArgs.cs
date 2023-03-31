using System;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Provides data for the ChannelManagementEventArgs event.
    /// </summary>
    public class ChannelManagementEventArgs : EventArgs
    {
        /// <summary>
        /// The user Jid
        /// </summary>
        public String Jid
        {
            get;
            private set;
        }

        /// <summary>
        /// The channel Id
        /// </summary>
        public String ChannelId
        {
            get;
            private set;
        }

        /// <summary>
        /// The action: subscribe / unsubscribe / add / remove / update
        /// </summary>
        public String Action
        {
            get;
            private set;
        }

        /// <summary>
        /// The user role/type: none / member / publisher / owner (can also be emtpy string if action = subscribe / unsubscribe)
        /// </summary>
        public String Type
        {
            get;
            private set;
        }

        /// <summary>
        /// Initializes a new instance of the ChannelManagementEventArgs class.
        /// </summary>
        /// <param name="invitationId">the invitation id</param>
        /// <param name="action">the action about this invitation</param>
        /// <param name="type">the invitation's type</param>
        /// <param name="status">the invitation's status</param>
        /// <summary>
        public ChannelManagementEventArgs(String jid, String channelId, String action, String type)
        {
            Jid = jid;
            ChannelId = channelId;
            Action = action;
            Type = type;
        }
    }
}
