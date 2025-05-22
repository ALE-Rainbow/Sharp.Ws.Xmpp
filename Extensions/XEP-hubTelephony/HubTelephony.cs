using Microsoft.Extensions.Logging;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Xml;

namespace Sharp.Xmpp.Extensions.XEP_hubTelephony
{
    internal class HubTelephony : XmppExtension, IInputFilter<Sharp.Xmpp.Im.Message>
    {
        private readonly ILogger log;


        private static readonly String HUBTELEPHONY_NS = "urn:xmpp:pbxagent:telephony:1"; // cf. https://git.openrainbow.org/rainbow-backends/servers/core/components/rvcp-pcg/-/blob/master/xsd/telephony.xsd?ref_type=heads
        private static readonly String HUBGROUP_NS = "urn:xmpp:pbxagent:group:1"; // cf. https://git.openrainbow.org/rainbow-backends/servers/core/components/rvcp-pcg/-/blob/master/xsd/group.xsd?ref_type=heads
        private static readonly String HUBSUPERVISION_NS = "urn:xmpp:pbxagent:supervision:2"; // https://git.openrainbow.org/rainbow-backends/servers/core/components/rvcp-pcg/-/blob/master/xsd/supervision.xsd?ref_type=heads

        private static readonly String RVCP_NS = "urn:xmpp:rvcp:userConfiguration:1";

        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonyRoutingUpdated;// Use RVCP_NS

        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonyEvent;         // Use HUBTELEPHONY_NS
        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonyCallLog;       // Use HUBTELEPHONY_NS
        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonyMwi;           // Use HUBTELEPHONY_NS
        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonyGmwi;          // Use HUBTELEPHONY_NS
        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonyForwardUpdated;// Use HUBTELEPHONY_NS

        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonySupervision;   // Use HUBSUPERVISION_NS

        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonyGroupRealTime; // Use HUBGROUP_NS
        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonyGroupCallLog;  // Use HUBGROUP_NS

        /// <summary>
        /// An enumerable collection of XMPP namespaces the extension implements.
        /// </summary>
        /// <remarks>This is used for compiling the list of supported extensions
        /// advertised by the 'Service Discovery' extension.</remarks>
        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { HUBTELEPHONY_NS, HUBGROUP_NS, HUBSUPERVISION_NS,
                                        RVCP_NS};
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
            // No XSD for this one ...
            var routingElement = message.Data["routing"];
            if ((routingElement != null)
                && (routingElement.NamespaceURI == RVCP_NS))
            {
                if (routingElement["routingUpdated"] != null)
                {
                    HubTelephonyRoutingUpdated.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(routingElement["routingUpdated"]));
                    return true;
                }
            }

            // Cf. https://git.openrainbow.org/rainbow-backends/servers/core/components/rvcp-pcg/-/blob/master/xsd/telephony.xsd?ref_type=heads
            var telephonyElement = message.Data["telephony"];
            if ((telephonyElement != null)
                && (telephonyElement.NamespaceURI == HUBTELEPHONY_NS))
            {

                if (telephonyElement["event"] != null)
                {
                    HubTelephonyEvent.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(telephonyElement["event"]));
                    return true;
                }

                if (telephonyElement["callLog"] != null)
                {
                    HubTelephonyCallLog.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(telephonyElement["callLog"]));
                    return true;
                }

                if (telephonyElement["mwi"] != null)
                {
                    HubTelephonyMwi.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(telephonyElement["mwi"]));
                    return true;
                }

                if (telephonyElement["gmwi"] != null)
                {
                    HubTelephonyGmwi.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(telephonyElement["gmwi"]));
                    return true;
                }

                if (telephonyElement["forwardUpdated"] != null)
                {
                    HubTelephonyForwardUpdated.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(telephonyElement["forwardUpdated"]));
                    return true;
                }
            }

            // Cf. https://git.openrainbow.org/rainbow-backends/servers/core/components/rvcp-pcg/-/blob/master/xsd/supervision.xsd?ref_type=heads
            XmlElement supervisionElement;
            String from;
            if (message.Data["forwarded"] != null)
            {
                supervisionElement = message.Data["forwarded"]["supervision"];
                from = message.Data["forwarded"]["delay"]?.GetAttribute("from");
            }
            else
            {
                supervisionElement = message.Data["supervision"];
                from = message.Data.GetAttribute("from");
            }

            if ((supervisionElement != null)
                    && (supervisionElement.NamespaceURI == HUBSUPERVISION_NS))
            {
                var delay = message.Data["forwarded"]["delay"];
                if (delay != null)
                {
                    // set "from" attribute
                    supervisionElement.SetAttribute("from", from);

                    HubTelephonySupervision.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(supervisionElement));
                    return true;
                }
            }

            // Cf. https://git.openrainbow.org/rainbow-backends/servers/core/components/rvcp-pcg/-/blob/master/xsd/group.xsd?ref_type=heads
            var groupElement = message.Data["group"];
            if ((groupElement != null)
                && (groupElement.NamespaceURI == HUBGROUP_NS))
            {
                if (groupElement["realTime"] != null)
                {
                    HubTelephonyGroupRealTime.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(groupElement["realTime"]));
                    return true;
                }

                if (groupElement["callLog"] != null)
                {
                    HubTelephonyGroupCallLog.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(groupElement["callLog"]));
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
