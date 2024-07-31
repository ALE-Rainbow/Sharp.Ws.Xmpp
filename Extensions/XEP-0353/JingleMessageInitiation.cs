using Microsoft.Extensions.Logging;
using Sharp.Xmpp.Core;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;

namespace Sharp.Xmpp.Extensions
{
    internal class JingleMessageInitiation : XmppExtension, IInputFilter<Sharp.Xmpp.Im.Message>, IInputFilter<Iq>
    {
        private readonly ILogger log;

        private static readonly String NamespaceJingleMessage = "urn:xmpp:jingle-message:0";
        private static readonly String NamespaceJingleIq = "urn:xmpp:jingle:1";

        /// <summary>
        /// Raised when an Jingle Message Initiation has beend received
        /// </summary>
        public event EventHandler<JingleMessageEventArgs> JingleMessageInitiationReceived;

        /// <summary>
        /// Raised when an Jingle Iq has beend received
        /// </summary>
        public event EventHandler<XmlElementEventArgs> JingleIqReceived;

        /// <summary>
        /// An enumerable collection of XMPP namespaces the extension implements.
        /// </summary>
        /// <remarks>This is used for compiling the list of supported extensions
        /// advertised by the 'Service Discovery' extension.</remarks>
        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { "urn:xmpp:jingle:1"                           // XEP-0166: Jingle
                                        , "urn:xmpp:jingle:apps:rtp:1"              // XEP-0167: Jingle RTP Sessions
                                        , "urn:xmpp:jingle:apps:rtp:audio"          // XEP-0167: Jingle RTP Sessions
                                        , "urn:xmpp:jingle:apps:rtp:video"          // XEP-0167: Jingle RTP Sessions
                                        , "urn:xmpp:jingle:transports:ice-udp:1"    // XEP-0176: Jingle ICE-UDP Transport Method
                                        , "urn:xmpp:jingle:apps:rtp:rtcp-fb:0"      // XEP-0293: Jingle RTP Feedback Negotiation
                                        , "urn:xmpp:jingle:apps:rtp:rtp-hdrext:0"   // XEP-0294: Jingle RTP Header Extensions Negotiation
                                        , "urn:xmpp:jingle:apps:dtls:0"             // XEP-0320: Use of DTLS-SRTP in Jingle Sessions
                                        , "urn:ietf:rfc:5888"                       // XEP-0338: Jingle Grouping Framework
                                        , "urn:ietf:rfc:5576"                       // XEP-0339: Source-Specific Media Attributes in Jingle
                                        , "urn:ietf:rfc:5761"                       // RTCP MUX (RFC 5761)
                                    };
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
                return Extension.JingleMessageInitiation;
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
            String action = null;
            if (message.Data["propose"] != null)
                action = "propose";
            else if (message.Data["retract"] != null)
                action = "retract";
            else if (message.Data["reject"] != null)
                action = "reject";
            else if (message.Data["accept"] != null)
                action = "accept";
            else if (message.Data["proceed"] != null)
                action = "proceed";

            if (!String.IsNullOrEmpty(action))
            {
                if (message.Data[action].NamespaceURI == NamespaceJingleMessage)
                {
                    XmlElement element = message.Data[action];

                    JingleMessageEventArgs jingleMessageEventArgs = new()
                    {
                        JingleMessage = new JingleMessage
                        {
                            Id = element.GetAttribute("id"),
                            FromJid = message.From?.GetBareJid()?.ToString(),
                            FromResource = message.From?.Resource,
                            ToJid = message.To?.GetBareJid()?.ToString(),
                            ToResource = message.To?.Resource,
                            Action = action,
                            UnifiedPlan = (element["unifiedplan"] != null),
                            DisplayName = element.GetAttribute("displayname")
                        }
                    };

                    // Subject
                    if (element["subject"] != null)
                        jingleMessageEventArgs.JingleMessage.Subject = element["subject"].InnerText;

                    // Media
                    XmlNodeList nodeList = element.GetElementsByTagName("description");
                    if(nodeList != null)
                    {
                        jingleMessageEventArgs.JingleMessage.Media = new List<string>();

                        XmlElement description;
                        foreach (XmlNode node in nodeList)
                        {
                            description = node as XmlElement;
                            jingleMessageEventArgs.JingleMessage.Media.Add(description.GetAttribute("media"));
                        }
                    }

                    // We need also to check if there is an error
                    if (message.Data["error"] != null)
                    {
                        jingleMessageEventArgs.JingleMessage.ErrorCode = message.Data["error"]?.GetAttribute("code");
                        jingleMessageEventArgs.JingleMessage.ErrorType = message.Data["error"]?.GetAttribute("type");
                    }

                    JingleMessageInitiationReceived.Raise(this, jingleMessageEventArgs);

                    // We have well managed this message
                    return true;
                }
            }

            // Pass the message to the next handler.
            return false;
        }

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
            var jingle = stanza.Data["jingle"];
            if (jingle == null || jingle.NamespaceURI != NamespaceJingleIq)
                return false;

            // Directly send "result" for this Iq
            im.IqResult(stanza);

            // TODO - potentially send "error" (bad SDP for example)

            JingleIqReceived.Raise(this, new XmlElementEventArgs(stanza.Data));

            // We took care of this IQ request, so intercept it and don't pass it
            // on to other handlers.
            return true;
        }

        /// <summary>
        /// To send (answer) a Jingle Message Initiation
        /// </summary>
        /// <param name="jingleMessage">Jingle Message to send</param>
        public void Send(JingleMessage jingleMessage)
        {
            AsyncHelper.RunSync(async () => await SendAsync(jingleMessage).ConfigureAwait(false));
        }

        public async Task<Boolean> SendAsync(JingleMessage jingleMessage)
        {
            Sharp.Xmpp.Im.Message message = new(jingleMessage.ToJid);
            XmlElement e = message.Data;

            XmlElement actionElement = e.OwnerDocument.CreateElement(jingleMessage.Action, NamespaceJingleMessage);
            e.AppendChild(actionElement);

            // Id
            actionElement.SetAttribute("id", jingleMessage.Id);

            // Unified Plan
            if ((jingleMessage.UnifiedPlan)
                && ((jingleMessage.Action == "propose") || (jingleMessage.Action == "proceed")))
            {
                XmlElement unifiedplan = e.OwnerDocument.CreateElement("unifiedplan", "urn:xmpp:jingle:apps:jsep:1");
                actionElement.AppendChild(unifiedplan);
            }

            if (jingleMessage.Action == "propose")
            {
                // Display Name
                if (!String.IsNullOrEmpty(jingleMessage.DisplayName))
                    actionElement.SetAttribute("displayname", jingleMessage.DisplayName);

                // Subject
                if (!String.IsNullOrEmpty(jingleMessage.Subject))
                {
                    XmlElement subject = e.OwnerDocument.CreateElement("subject", "urn:xmpp:jingle-subject:0");
                    subject.InnerText = jingleMessage.Subject;
                    actionElement.AppendChild(subject);
                }

                // Media
                if (jingleMessage.Media != null)
                {
                    foreach (String media in jingleMessage.Media)
                    {
                        XmlElement description = e.OwnerDocument.CreateElement("description", "urn:xmpp:jingle:apps:rtp:1");
                        description.SetAttribute("media", media);
                        actionElement.AppendChild(description);
                    }
                }
            }

            return await im.SendMessageAsync(message);
        }

        /// <summary>
        /// Initializes a new instance of the CallLog class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public JingleMessageInitiation(XmppIm im, String loggerPrefix)
            : base(im, loggerPrefix)
        {
            log = LogFactory.CreateLogger<JingleMessageInitiation>(loggerPrefix);
        }
    }
}
