using System;

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
        /// <see cref="String"/> - Xriticality of the status (if any)
        /// 'fatal': we are disconnected and the AutoReconnection service (if used) will stop immediatly.
        /// 'error': we are disconnected and the AutoReconnection service (if used) will continue its job (so it will try to reconnect to the server).
        /// 'info': we are not disconnected but the server returns an error
        /// </summary>
        public String Criticality { get; private set; }

        /// <summary>
        /// Initializes a new instance of the ConnectionStatusEventArgs class.
        /// </summary>
        public ConnectionStatusEventArgs(bool connected, String criticality, String reason, String details = "")
        { 
            Connected = connected;
            Criticality = criticality;
            Reason = reason;
            Details = details;
            
        }

        /// <summary>
        /// Initializes a new instance of the ConnectionStatusEventArgs class.
        /// </summary>
        public ConnectionStatusEventArgs(bool connected)
        {
            Connected = connected;
            if(connected)
                Criticality = "info";
            else
                Criticality = "error";
            Reason = "";
            Details = "";
        }
    }
}