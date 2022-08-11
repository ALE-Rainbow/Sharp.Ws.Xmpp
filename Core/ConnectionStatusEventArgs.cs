using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Sharp.Xmpp.Core
{
    /// <summary>
    /// Provides event about for the connection status.
    /// </summary>
    public class ConnectionStatusEventArgs : EventArgs
    {
        /// <summary>
        /// The status of the connection
        /// </summary>
        public bool Connected { get; private set; }

        /// <summary>
        /// <see cref="String"/> - Reason of the disconnection if done by the server (can be null)
        /// </summary>
        public String Reason { get; private set; }

        /// <summary>
        /// <see cref="String"/> - Details of the disconnection if done by the server (can be null)
        /// </summary>
        public String Details { get; private set; }

        /// <summary>
        /// Initializes a new instance of the ConnectionStatusEventArgs class.
        /// </summary>
        public ConnectionStatusEventArgs(bool connected, String reason = null, String details = null)
        { 
            Connected = connected;
            Reason = reason;
            Details = details;
        }
    }
}