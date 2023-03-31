using System;
using System.Collections.Generic;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Provides data for the ConversationManagementEventArgs event.
    /// </summary>
    public class ConversationManagementEventArgs : EventArgs
    {
        /// <summary>
        /// The conversation  id 
        /// </summary>
        public String ConversationId
        {
            get;
            private set;
        }

        /// <summary>
        /// The conversation management's action create / update / delete
        /// </summary>
        public String Action
        {
            get;
            private set;
        }

        /// <summary>
        /// Data related to this conversation
        /// </summary>
        public Dictionary<string, string>Data
        {
            get;
            private set;
        }

        /// <summary>
        /// Initializes a new instance of the ConversationManagementEventArgs class.
        /// </summary>
        /// <param name="invitationId">the invitation id</param>
        /// <param name="action">the action about this invitation</param>
        /// <param name="type">the invitation's type</param>
        /// <param name="status">the invitation's status</param>
        /// <summary>
        public ConversationManagementEventArgs(String conversationId, String action, Dictionary<string, string> data)
        {
            ConversationId = conversationId;
            Action = action;
            Data = data;
        }
    }
}
