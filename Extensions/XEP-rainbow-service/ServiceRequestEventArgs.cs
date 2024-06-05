using System;
using System.Collections.Generic;


namespace Sharp.Xmpp.Extensions
{
    public enum ActionType
    {
        /// <summary>
        /// Service activation request.
        /// </summary>
        Activate,
        /// <summary>
        /// Service pause request. Do not remove existing processing setup, but pause the associated processing
        /// </summary>
        Pause,
        /// <summary>
        /// Service resume request. Resume a previous paused processing, no effect on non paused state.
        /// </summary>
        Resume,
        /// <summary>
        /// Service disable request.
        /// </summary>
        Disable
    }

    /// <summary>
    /// Provides data for the ServiceRequestEventArgs event
    /// </summary>
    public class ServiceRequestEventArgs : EventArgs
    {
        /// <summary>
        /// Service name
        /// </summary>
        public String Name
        {
            get;
            private set;
        }
        /// <summary>
        /// Room Id associated to the service request
        /// </summary>
        public String RoomId
        {
            get;
            private set;
        }
        /// <summary>
        /// Initiator of the service request
        /// </summary>
        public String Requester
        {
            get;
            private set;
        }
        /// <summary>
         /// Room Id associated to the service request
         /// </summary>
        public ActionType Action
        {
            get;
            private set;
        }
        /// <summary>
        /// Additionnal metadata infomations to enable the service
        /// </summary>
        public Dictionary<String, String> Metadata
        {
            get;
            private set;
        }

        public ServiceRequestEventArgs(string name, string roomId, string requester, ActionType action, Dictionary<String, String> metadata)
        {
            Name = name;
            RoomId = roomId;
            Requester = requester;
            Action = action;
            Metadata = metadata;
        }
    }
}