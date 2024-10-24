using Microsoft.Extensions.Logging;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sharp.Xmpp.Extensions.XEP_hubTelephony
{
    internal class HubTelephony : XmppExtension, IInputFilter<Sharp.Xmpp.Im.Message>
    {
        private readonly ILogger log;

        private static readonly String HUBTELEPHONY_NS = "urn:xmpp:pbxagent:telephony:1";
        private static readonly String RVCP_NS = "urn:xmpp:rvcp:userConfiguration:1";

        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonyRoutingUpdated;
        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonyForwardUpdated;

        /// <summary>
        /// An enumerable collection of XMPP namespaces the extension implements.
        /// </summary>
        /// <remarks>This is used for compiling the list of supported extensions
        /// advertised by the 'Service Discovery' extension.</remarks>
        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { HUBTELEPHONY_NS };
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
                return Extension.CallService;
            }
        }

        /// <summary>
        /// Invoked when a message stanza has been received.
        /// </summary>
        /// <param name="stanza">The stanza which has been received.</param>
        /// <returns>true to intercept the stanza or false to pass the stanza
        /// on to the next handler.</returns>
        public bool Input(Sharp.Xmpp.Im.Message message)
        {
            if ((message.Data["routing"] != null)
                && (message.Data["routing"].NamespaceURI == RVCP_NS))
            {
                if (message.Data["routing"]["routingUpdated"] != null)
                {
                    HubTelephonyRoutingUpdated.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(message.Data["routing"]["routingUpdated"]));
                    return true;
                }
            }

            if ((message.Data["telephony"] != null)
                && (message.Data["telephony"].NamespaceURI == HUBTELEPHONY_NS))
            {
                if (message.Data["telephony"]["forwardUpdated"] != null)
                {
                    HubTelephonyForwardUpdated.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(message.Data["telephony"]["forwardUpdated"]));
                    return true;
                }
            }


            // Pass the message to the next handler.
            return false;
        }

        /// <summary>
        /// Initializes a new instance of the HubTelephony class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public HubTelephony(XmppIm im, String loggerPrefix)
            : base(im, loggerPrefix)
        {
            log = LogFactory.CreateLogger<HubTelephony>(loggerPrefix);
        }
    }
}
