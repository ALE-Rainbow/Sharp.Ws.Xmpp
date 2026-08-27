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
        public event EventHandler<Sharp.Xmpp.Extensions.XmlElementEventArgs> HubTelephonyClirUpdated;   // Use RVCP_NS

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
            Boolean eventRaised = false;

            // No XSD for this one ...
            var routingElement = message.Data["routing", RVCP_NS];
            if (routingElement != null)
            {
                if (routingElement["routingUpdated"] != null)
                {
                    HubTelephonyRoutingUpdated.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(routingElement["routingUpdated"]));
                    eventRaised = true;
                }
            }

            // Cf. https://git.openrainbow.org/rainbow-backends/servers/core/components/rvcp-pcg/-/blob/master/xsd/telephony.xsd?ref_type=heads
            var telephonyElement = message.Data["telephony", HUBTELEPHONY_NS];
            if (telephonyElement != null)
            {

                if (telephonyElement["event"] != null)
                {
                    HubTelephonyEvent.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(telephonyElement["event"]));
                    eventRaised = true;
                }

                if (telephonyElement["callLog"] != null)
                {
                    HubTelephonyCallLog.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(telephonyElement["callLog"]));
                    eventRaised = true;
                }

                if (telephonyElement["mwi"] != null)
                {
                    HubTelephonyMwi.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(telephonyElement["mwi"]));
                    eventRaised = true;
                }

                if (telephonyElement["gmwi"] != null)
                {
                    HubTelephonyGmwi.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(telephonyElement["gmwi"]));
                    eventRaised = true;
                }

                if (telephonyElement["forwardUpdated"] != null)
                {
                    HubTelephonyForwardUpdated.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(telephonyElement["forwardUpdated"]));
                    eventRaised = true;
                }
            }

            // Cf. https://git.openrainbow.org/rainbow-backends/servers/core/components/rvcp-pcg/-/blob/master/xsd/supervision.xsd?ref_type=heads
            XmlElement supervisionElement;
            String from;
            if (message.Data["forwarded"] != null)
            {
                supervisionElement = message.Data["forwarded"]["supervision"];
                from = message.Data["forwarded"]["delay"]?.GetAttribute("from");
                if(String.IsNullOrEmpty(from))
                    from = message.Data.GetAttribute("from");
            }
            else
            {
                supervisionElement = message.Data["supervision"];
                from = message.Data.GetAttribute("from");
            }

            if ((supervisionElement != null)
                    && (supervisionElement.NamespaceURI == HUBSUPERVISION_NS))
            {
                // set "from" attribute
                supervisionElement.SetAttribute("from", from ?? "");

                HubTelephonySupervision.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(supervisionElement));
                eventRaised = true;
            }

            // Cf. https://git.openrainbow.org/rainbow-backends/servers/core/components/rvcp-pcg/-/blob/master/xsd/group.xsd?ref_type=heads
            var groupElement = message.Data["group", HUBGROUP_NS];
            if (groupElement != null)
            {
                if (groupElement["realTime"] != null)
                {
                    HubTelephonyGroupRealTime.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(groupElement["realTime"]));
                    eventRaised = true;
                }

                if (groupElement["callLog"] != null)
                {
                    HubTelephonyGroupCallLog.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(groupElement["callLog"]));
                    eventRaised = true;
                }
            }

            var clirElement = message.Data["clir", RVCP_NS];
            if (clirElement != null)
            {
                HubTelephonyClirUpdated.Raise(this, new Sharp.Xmpp.Extensions.XmlElementEventArgs(clirElement));
                eventRaised = true;
            }

            return eventRaised;
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
