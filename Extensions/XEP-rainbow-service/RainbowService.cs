using Sharp.Xmpp.Core;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Xml;

using Microsoft.Extensions.Logging;


namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Implements the 'RainbowService' extension used in Rainbow Hub
    /// </summary>
    internal class RainbowService : XmppExtension, IInputFilter<Iq>
    {
        private readonly ILogger log;

        /// <summary>
        /// An enumerable collection of XMPP namespaces the extension implements.
        /// </summary>
        /// <remarks>This is used for compiling the list of supported extensions
        /// advertised by the 'Service Discovery' extension.</remarks>
        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { "jabber:iq:rainbow:service" };
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
                return Extension.CallLog;
            }
        }

        /// <summary>
        /// The event that is raised when a service request is received
        /// </summary>
        public event EventHandler<ServiceRequestEventArgs> ServiceRequest;

        /// <summary>
        /// Invoked when a message stanza has been received.
        /// </summary>
        /// <param name="stanza">The stanza which has been received.</param>
        /// <returns>true to intercept the stanza or false to pass the stanza
        /// on to the next handler.</returns>
        public bool Input(Iq stanza)
        {
            if (stanza.Type != IqType.Set)
            {
                return false;
            }
            var service = stanza.Data["service"];
            if (service == null || service.NamespaceURI != "jabber:iq:rainbow:service")
            {
                return false;
            }

            var name = service["name"];
            var roomId = service["room-id"];
            var requester = service["requester"];
            var _action = service["action"];

            
            if (name == null || roomId == null || requester == null || _action == null)
            {
                return false;
            }

            if( ! Enum.TryParse(_action.InnerText, out ActionType action))
            {
                return false;
            }

            var metadata = service["metadata"];
            var metadataDictionary = new Dictionary<String,String>();
            if (metadata != null)
            {
                foreach (XmlElement item in service.GetElementsByTagName("param"))
                {
                    var _name = item.GetAttribute("name");
                    var _value = item.GetAttribute("value");
                    metadataDictionary.Add(_name, _value);
                }
            }

            ServiceRequest.Raise(this, new ServiceRequestEventArgs(name.InnerText, roomId.InnerText, requester.InnerText, action, metadataDictionary));

            // Pass the message to the next handler.
            return false;
        }

                /// <summary>
        /// Initializes a new instance of the XmppIm class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public RainbowService(XmppIm im, string loggerPrefix)
            : base(im, loggerPrefix)
        {
            log = LogFactory.CreateLogger<RainbowService>(loggerPrefix);
        }

    }
}