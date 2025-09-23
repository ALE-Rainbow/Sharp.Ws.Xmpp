using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Implements the 'RPC' extension 
    /// </summary>
    internal class RPC : XmppExtension, IInputFilter<Sharp.Xmpp.Core.Iq>
    {
        private readonly ILogger log;

        private static readonly String namespaceUsed = "jabber:iq:rpc";

        /// <summary>
        /// An enumerable collection of XMPP namespaces the extension implements.
        /// </summary>
        /// <remarks>This is used for compiling the list of supported extensions
        /// advertised by the 'Service Discovery' extension.</remarks>
        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { namespaceUsed };
            }
        }

        /// <summary>
        /// The named constant of the Extension enumeration that corresponds to this
        /// extension.
        /// </summary>
        public override Extension Xep
        {
            get
            {
                return Extension.RPC;
            }
        }
       
        /// <summary>
        /// The event raised when an AckMessage has been received
        /// </summary>
        public event EventHandler<XmlElementEventArgs> RPCMessage;


        /// <summary>
        /// Invoked when an IQ stanza is being received.
        /// </summary>
        /// <param name="stanza">The stanza which is being received.</param>
        /// <returns>true to intercept the stanza or false to pass the stanza
        /// on to the next handler.</returns>
        public bool Input(Sharp.Xmpp.Core.Iq stanza)
        {
            // If it's a result or an error, it's directly managed by the sender of the iq. 
            if( (stanza.Type == Core.IqType.Result) || (stanza.Type == Core.IqType.Error) )
                return false;

            var query = stanza.Data["query"];
            if (query == null || query.NamespaceURI != namespaceUsed)
                return false;

            RPCMessage.Raise(this, new XmlElementEventArgs(stanza.Data));

            // We took care of this IQ request, so intercept it and don't pass it
            // on to other handlers.
            return true;
        }

        /// <summary>
        /// Initializes a new instance of the Rainbow class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public RPC(XmppIm im, String loggerPrefix)
            : base(im, loggerPrefix)
        {
            log = LogFactory.CreateLogger<Rainbow>(loggerPrefix);
        }
    }
}