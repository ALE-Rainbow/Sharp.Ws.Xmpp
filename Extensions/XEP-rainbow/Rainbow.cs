using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Implements the 'Rainbow' extension used in Rainbow Hub
    /// </summary>
    internal class Rainbow : XmppExtension, IInputFilter<Sharp.Xmpp.Core.Iq>
    {
        private readonly ILogger log;

        private static readonly String namespaceUsed = "jabber:iq:rainbow:cpaas:message";

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
                return Extension.Rainbow;
            }
        }
       
        /// <summary>
        /// The event raised when an AckMessage has been received
        /// </summary>
        public event EventHandler<XmlElementEventArgs> AckMessage;


        /// <summary>
        /// Invoked when an IQ stanza is being received.
        /// </summary>
        /// <param name="stanza">The stanza which is being received.</param>
        /// <returns>true to intercept the stanza or false to pass the stanza
        /// on to the next handler.</returns>
        public bool Input(Sharp.Xmpp.Core.Iq stanza)
        {
            if( ( stanza.Type == Core.IqType.Result) || (stanza.Type == Core.IqType.Error) )
                return false;

            var conversation = stanza.Data["message-with-ack"];
            if (conversation == null || conversation.NamespaceURI != namespaceUsed)
                return false;

            AckMessage.Raise(this, new XmlElementEventArgs(stanza.Data));

            // We took care of this IQ request, so intercept it and don't pass it
            // on to other handlers.
            return true;
        }

        /// <summary>
        /// Initializes a new instance of the Rainbow class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public Rainbow(XmppIm im, String loggerPrefix)
            : base(im, loggerPrefix)
        {
            log = LogFactory.CreateLogger<Rainbow>(loggerPrefix);
        }
    }
}