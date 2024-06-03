using System;
using System.Collections.Generic;


namespace Sharp.Xmpp.Extensions
{
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
        /// Additionnal metadata infomations to enable the service
        /// </summary>
        public Dictionary<string, string> Metadata
        {
            get;
            private set;
        }

        public ServiceRequestEventArgs(string name, string roomId, string requester, Dictionary<string, string> metadata)
        {
            Name = name;
            RoomId = roomId;
            Requester = requester;
            Metadata = metadata;
        }
    }
}