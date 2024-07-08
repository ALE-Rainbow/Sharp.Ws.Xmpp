using Microsoft.Extensions.Logging;
using Sharp.Xmpp.Core;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Implements the 'AdHocCommand' extension
    /// </summary>
    internal class AdHocCommand : XmppExtension, IInputFilter<Iq>
    {
        private readonly ILogger log;

        private static readonly String NamespaceAdHocCommandIq = "http://jabber.org/protocol/commands";

        /// <summary>
        /// An enumerable collection of XMPP namespaces the extension implements.
        /// </summary>
        /// <remarks>This is used for compiling the list of supported extensions
        /// advertised by the 'Service Discovery' extension.</remarks>
        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { NamespaceAdHocCommandIq };
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
                return Extension.AdHocCommand;
            }
        }

        /// <summary>
        /// Event raised when a Ad-Hoc Command has been updated
        /// </summary>
        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> AdHocCommandReceived;


        /// <summary>
        /// Invoked when an IQ stanza is being received.
        /// If the Iq is correctly received a Result response is included
        /// with extension specific metadata included.
        /// If the Iq is not correctly received an error is returned
        /// Semantics of error on the response refer only to the XMPP level
        /// and not the application specific level
        /// </summary>
        /// <param name="stanza">The stanza which is being received.</param>
        /// <returns>true to intercept the stanza or false to pass the stanza
        /// on to the next handler.</returns>
        public bool Input(Iq stanza)
        {
            var command = stanza.Data["command"];
            if (command == null || command.NamespaceURI != NamespaceAdHocCommandIq)
                return false;

            AdHocCommandReceived.Raise(this, new XmlElementEventArgs(stanza.Data));
            return true;
        }

        /// <summary>
        /// Initializes a new instance of the Conference class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public AdHocCommand(XmppIm im, String loggerPrefix)
            : base(im, loggerPrefix)
        {
            log = LogFactory.CreateLogger<AdHocCommand>(loggerPrefix);
        }
    }
}
