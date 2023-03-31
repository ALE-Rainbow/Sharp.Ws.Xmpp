using System;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Provides data for the UserInvitationReceivedEventArgs event.
    /// </summary>
    [Serializable]
    public class UserInvitationEventArgs : EventArgs
    {
        /// <summary>
        /// The invitation id 
        /// </summary>
        public String InvitationId
        {
            get;
            private set;
        }

        /// <summary>
        /// The invitation's action 
        /// </summary>
        public String Action
        {
            get;
            private set;
        }

        /// <summary>
        /// The invitation's type 
        /// </summary>
        public String Type
        {
            get;
            private set;
        }

        /// <summary>
        /// The invitation's status 
        /// </summary>
        public String Status
        {
            get;
            private set;
        }

        /// <summary>
        /// Initializes a new instance of the UserInvitationReceivedEventArgs class.
        /// </summary>
        /// <param name="invitationId">the invitation id</param>
        /// <param name="action">the action about this invitation</param>
        /// <param name="type">the invitation's type</param>
        /// <param name="status">the invitation's status</param>
        /// <summary>
        public UserInvitationEventArgs(string invitationId, String action, String type, String status)
        {
            InvitationId = invitationId;
            Action = action;
            Type = type;
            Status = status;
        }
    }
}
