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
        /// <see cref="String"/> - Criticity of the status (if any)
        /// 'fatal': we are disconnected and the AutoReconnection service (if used) will stop immediatly.
        /// 'error': we are disconnected and the AutoReconnection service (if used) will continue its job (so it will try to reconnect to the server).
        /// 'info': we are not disconnected but the server returns an error
        /// </summary>
        public String Criticity { get; private set; }

        /// <summary>
        /// Initializes a new instance of the ConnectionStatusEventArgs class.
        /// </summary>
        public ConnectionStatusEventArgs(bool connected, String reason = null, String details = null, String criticity = null)
        { 
            Connected = connected;
            Reason = reason;
            Details = details;
            Criticity = criticity;
        }
    }
}