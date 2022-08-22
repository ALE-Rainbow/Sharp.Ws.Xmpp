using Microsoft.Extensions.Logging;

using Sharp.Xmpp.Core;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Implements the 'Stream Management' extension as defined in XEP-0198.
    /// </summary>
    internal class StreamManagement : XmppExtension, IInputFilter<StreamManagementStanza>
    {
        private readonly ILogger log;

        public event EventHandler<EventArgs> Failed;
        public event EventHandler<EventArgs> Resumed;

        uint stanzasReceivedHandled;

        /// <summary>
        /// An enumerable collection of XMPP namespaces the extension implements.
        /// </summary>
        /// <remarks>This is used for compiling the list of supported extensions
        /// advertised by the 'Service Discovery' extension.</remarks>
        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { "urn:xmpp:sm:3" };
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
                return Extension.StreamManagement;
            }
        }

        /// <summary>
        /// Invoked after all extensions have been loaded.
        /// </summary>
        public override void Initialize()
        {
        }

        internal void RequestAcknowledgement()
        {
            var xml = Xml.Element("r", "urn:xmpp:sm:3");
            im.Send(xml, false);
        }

        internal void SendkAcknowledgement(uint h)
        {
            var xml = Xml.Element("a", "urn:xmpp:sm:3");
            xml.SetAttribute("h", h.ToString());
            im.Send(xml, false);
        }

        /// <summary>
        /// Invoked when an StreamManagementStanza stanza is being received.
        /// </summary>
        /// <param name="stanza">The stanza which is being received.</param>
        /// <returns>true to intercept the stanza or false to pass the stanza
        /// on to the next handler.</returns>
        public bool Input(StreamManagementStanza stanza)
        {
            if (stanza.Data.NamespaceURI == "urn:xmpp:sm:3")
            {
                // We need to manage: 
                //  "enabled":  in response to "enable"
                //  "a":        read answer from a previous request (at each ping)
                //  "r":        request asked by the sver
                //  "resumed":  in response to "resume"
                //  "failed":   in case of pb

                switch (stanza.Data.LocalName)
                {
                    case "failed":
                        im.StreamManagementResumeId = "";

                        Failed.Raise(this, null); //  /!\ This event must be ALWAYS raised before RaiseConnectionStatus !!!
                        im.RaiseConnectionStatus(false);
                        break;

                    case "resumed": // in response to "resume"
                        Resumed.Raise(this, null); //  /!\ This event must be ALWAYS raised before RaiseConnectionStatus !!!
                        im.RaiseConnectionStatus(true);
                        break;

                    case "enabled": // in response to "enable"
                        // Check if the server accepts resume
                        string resume = stanza.Data.GetAttribute("resume");
                        Boolean resumeHandled = (resume == "true") || (resume == "1");

                        // Get Id used for resume purpose
                        String resumeId = stanza.Data.GetAttribute("id");

                        // Has the server accepted Stream management ?
                        im.StreamManagementEnabled = resumeHandled && !String.IsNullOrEmpty(resumeId);

                        // Get max delay for the resume
                        String maxDelay = stanza.Data.GetAttribute("max");
                        int delay;
                        if (!int.TryParse(maxDelay, out delay))
                            delay = 86400;

                        // Store id and delay
                        im.StreamManagementResumeId = resumeId;
                        im.StreamManagementResumeDelay = delay;

                        log.LogDebug("[Input] - StreamManagementEnabled:[{0}] - ResumeStreamManagementId:[{1}] - ResumeStreamManagementDelay:[{2}]", im.StreamManagementEnabled, im.StreamManagementResumeId, im.StreamManagementResumeDelay);

                        break;

                    case "a": // read answer for a request
                        // Store last handled stanzas
                        String h = stanza.Data.GetAttribute("h");
                        if (!uint.TryParse(h, out stanzasReceivedHandled))
                            stanzasReceivedHandled = 0;

                        im.StreamManagementLastStanzaReceivedAndHandledByServer = stanzasReceivedHandled;
                        im.StreamManagementLastStanzaDateReceivedAndHandledByServer = DateTime.UtcNow;

                        //log.LogDebug("[Input] - StreamManagementLastStanzaReceivedAndHandledByClient:[{0}]", im.StreamManagementLastStanzaReceivedAndHandledByClient);

                        break;

                    case "r": // answer to the request
                        im.StreamManagementLastStanzaReceivedAndHandledByClient = im.StreamManagementLastStanzaReceivedByClient;
                        SendkAcknowledgement(im.StreamManagementLastStanzaReceivedAndHandledByClient);
                        break;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Initializes a new instance of the StreamManagement class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public StreamManagement(XmppIm im, String loggerPrefix)
            : base(im, loggerPrefix)
        {
            log = LogFactory.CreateLogger<StreamManagement>(loggerPrefix);
        }
    }
}