using System;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Provides data for the PbxAgentInfoEventArgs event
    /// </summary>
    public class PbxAgentInfoEventArgs : EventArgs
    {
        /// <summary>
        /// Phone Api Status
        /// </summary>
        public String PhoneApiStatus
        {
            get;
            private set;
        }

        /// <summary>
        /// Xmpp Agent Status
        /// </summary>
        public String XmppAgentStatus
        {
            get;
            private set;
        }

        /// <summary>
        /// Version
        /// </summary>
        public String Version
        {
            get;
            private set;
        }

        /// <summary>
        /// Features
        /// </summary>
        public String Features
        {
            get;
            private set;
        }

        /// <summary>
        /// Type
        /// </summary>
        public String Type
        {
            get;
            private set;
        }


        public PbxAgentInfoEventArgs(String phoneApiStatus, String xmppAgentStatus, String version, String features, string type)
        {
            PhoneApiStatus = phoneApiStatus;
            XmppAgentStatus = xmppAgentStatus;
            Version = version;
            Features = features;
            Type = type;
        }

    }
}
