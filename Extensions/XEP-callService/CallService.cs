using Sharp.Xmpp.Core;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Xml;

using log4net;


namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Implements the 'CallService' extension used in Rainbow Hub
    /// </summary>
    internal class CallService : XmppExtension, IInputFilter<Sharp.Xmpp.Im.Message>
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(CallService));

        /// <summary>
        /// An enumerable collection of XMPP namespaces the extension implements.
        /// </summary>
        /// <remarks>This is used for compiling the list of supported extensions
        /// advertised by the 'Service Discovery' extension.</remarks>
        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { "urn:xmpp:pbxagent:callservice:1" };
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
        /// The event that is raised when the call forward has been updated
        /// </summary>
        public event EventHandler<CallForwardEventArgs> CallForwardUpdated;

        /// <summary>
        /// The event that is raised when the nomadic status has been updated
        /// </summary>
        public event EventHandler<NomadicEventArgs> NomadicUpdated;

        /// <summary>
        /// The event that is raised when the PBX Agent info is updated/received
        /// </summary>
        public event EventHandler<PbxAgentInfoEventArgs> PbxAgentInfoUpdated;

        /// <summary>
        /// The event that is raised when voice messages are updated
        /// </summary>
        public event EventHandler<VoiceMessagesEventArgs> VoiceMessagesUpdated;

        /// <summary>
        /// The event that is raised when a call service message not specifically managed is received
        /// </summary>
        public event EventHandler<MessageEventArgs> MessageReceived;

        /// <summary>
        /// Invoked when a message stanza has been received.
        /// </summary>
        /// <param name="stanza">The stanza which has been received.</param>
        /// <returns>true to intercept the stanza or false to pass the stanza
        /// on to the next handler.</returns>
        public bool Input(Sharp.Xmpp.Im.Message message)
        {
            if ( (message.Data["callservice"] != null)
                && (message.Data["callservice"].NamespaceURI == "urn:xmpp:pbxagent:callservice:1") )
            {
                if (message.Data["callservice"]["forwarded"] != null)
                {
                    String forwardType = message.Data["callservice"]["forwarded"].GetAttribute("forwardType");
                    String forwardTo = message.Data["callservice"]["forwarded"].GetAttribute("forwardTo");

                    CallForwardUpdated.Raise(this, new CallForwardEventArgs(forwardType, forwardTo));

                    return true;
                }
                else if (message.Data["callservice"]["nomadicStatus"] != null)
                {
                    Boolean featureActivated = message.Data["callservice"]["nomadicStatus"].GetAttribute("featureActivated").ToLower() == "true";
                    Boolean modeActivated = message.Data["callservice"]["nomadicStatus"].GetAttribute("modeActivated").ToLower() == "true";
                    Boolean makeCallInitiatorIsMain = message.Data["callservice"]["nomadicStatus"].GetAttribute("makeCallInitiatorIsMain").ToLower() == "true";
                    String destination = message.Data["callservice"]["nomadicStatus"].GetAttribute("destination");

                    NomadicUpdated.Raise(this, new NomadicEventArgs(featureActivated, modeActivated, makeCallInitiatorIsMain, destination));

                    return true;
                }
                else if ( (message.Data["callservice"]["messaging"] != null)
                    && (message.Data["callservice"]["messaging"]["voiceMessageCounter"] != null) )
                {
                    String msg = message.Data["callservice"]["messaging"]["voiceMessageCounter"].InnerText;
                    VoiceMessagesUpdated.Raise(this, new VoiceMessagesEventArgs(msg));

                    return true;
                }
                else if ((message.Data["callservice"]["voiceMessages"] != null)
                    && (message.Data["callservice"]["voiceMessages"]["voiceMessagesCounters"] != null))
                {
                    String msg = message.Data["callservice"]["voiceMessages"]["voiceMessagesCounters"].GetAttribute("unreadVoiceMessages");
                    VoiceMessagesUpdated.Raise(this, new VoiceMessagesEventArgs(msg));

                    return true;
                }
                else if ((message.Data["callservice"]["messaging"] != null)
                    && (message.Data["callservice"]["messaging"]["voiceMessageWaiting"] != null))
                {
                    String msg = message.Data["callservice"]["messaging"]["voiceMessageWaiting"].InnerText;
                    VoiceMessagesUpdated.Raise(this, new VoiceMessagesEventArgs(msg));

                    return true;
                }
                else
                {
                    MessageReceived.Raise(this, new MessageEventArgs(message.ToString()));
                    return true;
                }
            }

            // Pass the message to the next handler.
            return false;
        }

        /// <summary>
        /// Ask then number of voice messages
        /// </summary>
        /// <param name="to">The JID to send the request</param>
        public void AskVoiceMessagesNumber(String to)
        {
            var xml = Xml.Element("callservice", "urn:xmpp:pbxagent:callservice:1");
            xml.Child(Xml.Element("messaging"));
            //The Request is Async
            im.IqRequestAsync(IqType.Get, to, im.Jid, xml, null, (id, iq) =>
            {
                //For any reply we execute the callback
                if (iq.Type == IqType.Error)
                {
                    log.ErrorFormat("AskVoiceMessagesNumber - Iq sent not valid - server sent an error as response");
                    return;
                }

                // Nothing to more - we will receive a specific message with voice message counter
                //if (iq.Type == IqType.Result)
                //{
                //}
            });
        }

        public void AskPbxAgentInfo(String to)
        {
            var xml = Xml.Element("pbxagentstatus", "urn:xmpp:pbxagent:monitoring:1");
            //The Request is Async
            im.IqRequestAsync(IqType.Get, to, im.Jid, xml, null, (id, iq) =>
            {
                //For any reply we execute the callback
                if (iq.Type == IqType.Error)
                {
                    log.ErrorFormat("AskPbxAgentInfo - Iq sent not valid - server sent an error as response");
                    return;
                }

                if (iq.Type == IqType.Result)
                {
                    try
                    {
                        if (iq.Data["pbxagentstatus"] != null)
                        {
                            XmlElement e = iq.Data["pbxagentstatus"];

                            String phoneapi = (iq.Data["pbxagentstatus"]["phoneapi"] != null) ? iq.Data["pbxagentstatus"]["phoneapi"].InnerText : "";
                            String xmppagent = (iq.Data["pbxagentstatus"]["xmppagent"] != null) ? iq.Data["pbxagentstatus"]["xmppagent"].InnerText : "";
                            String version = (iq.Data["pbxagentstatus"]["version"] != null) ? iq.Data["pbxagentstatus"]["version"].InnerText : "";
                            String features = (iq.Data["pbxagentstatus"]["features"] != null) ? iq.Data["pbxagentstatus"]["features"].InnerText : "";

                            PbxAgentInfoUpdated.Raise(this, new PbxAgentInfoEventArgs(phoneapi, xmppagent, version, features));

                            return;
                        }
                    }
                    catch (Exception)
                    {
                        log.ErrorFormat("AskPbxAgentInfo - an error occurred ...");
                    }

                }
            });
        }

        /// <summary>
        /// Initializes a new instance of the CaCallServicellLog class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public CallService(XmppIm im)
            : base(im)
        {
        }
    }
}