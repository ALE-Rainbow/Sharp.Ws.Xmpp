using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Xml;

namespace Sharp.Xmpp.Extensions
{
    public enum ReceiptType
    {
        ServerReceived,
        ClientReceived,
        ClientRead
    }

    /// <summary>
    /// Implements Mechanism for providing Custom IQ Extensions
    /// </summary>
    internal class MessageDeliveryReceipts : XmppExtension, IOutputFilter<Message>, IInputFilter<Message>
    {
        /// <summary>
		/// event raised when a message delivery is received
		/// </summary>
		public event EventHandler<MessageDeliveryReceivedEventArgs> MessageDeliveryReceived;

        /// <summary>
		/// event raised when a alert message delivery is received
		/// </summary>
		public event EventHandler<MessageDeliveryReceivedEventArgs> AlertMessageDeliveryReceived;

        /// <summary>
		/// event raised when all message are set read
		/// </summary>
        public event EventHandler<JidEventArgs> MessagesAllRead;

        /// <summary>
        /// A reference to the 'Entity Capabilities' extension instance.
        /// </summary>
        private EntityCapabilities ecapa;

        /// <summary>
        /// An enumerable collection of XMPP namespaces the extension implements.
        /// </summary>
        /// <remarks>This is used for compiling the list of supported extensions
        /// advertised by the 'Service Discovery' extension.</remarks>
        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { "urn:xmpp:receipts" };
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
                return Extension.MessageDeliveryReceipts;
            }
        }

        /// <summary>
        /// Invoked after all extensions have been loaded.
        /// </summary>
        public override void Initialize()
        {
            ecapa = im.GetExtension<EntityCapabilities>();
        }

        // <summary>
        /// Invoked when a Message stanza is being received.
        /// </summary>
        /// <param name="message">The message stanza which is being received.</param>
        /// <returns>true to intercept the stanza or false to pass the stanza
        /// on to the next handler.</returns>
        public bool Input(Message message)
        {
            if ( (message.Type == MessageType.Chat)
                || (message.Type == MessageType.Headline) )
            {
                XmlElement received;
                
                received= message.Data["received", "urn:xmpp:receipts"];
                if (received != null)
                {
                    string messageId = received.GetAttribute("id");

                    // Set Receipt Type according entity and event
                    ReceiptType receiptType;
                    var entity = received.GetAttribute("entity");
                    var evt = received.GetAttribute("event");
                    if (entity == "server")
                        receiptType = ReceiptType.ServerReceived;
                    else if (evt == "received")
                        receiptType = ReceiptType.ClientReceived;
                    else
                        receiptType = ReceiptType.ClientRead;


                    DateTime dateTime = DateTime.MinValue;
                    var timestamp = message.Data["timestamp"];
                    if (timestamp != null)
                    {
                        String value = timestamp.GetAttribute("value");
                        if (!String.IsNullOrEmpty(value))
                            DateTime.TryParse(value, out dateTime);
                    }
                    
                    if (message.Type == MessageType.Chat)
                        MessageDeliveryReceived.Raise(this, new MessageDeliveryReceivedEventArgs(messageId, receiptType, dateTime));
                    else
                        AlertMessageDeliveryReceived.Raise(this, new MessageDeliveryReceivedEventArgs(messageId, receiptType, dateTime));
                    return true;
                }

                // Do we receive a "mark as read" for all messages ?
                received = message.Data["mark_as_read", "jabber:iq:notification"];
                if(received != null)
                {
                    string id = received.GetAttribute("id");
                    //if(id == "all-received")
                    {
                        String jid = received.GetAttribute("with");
                        MessagesAllRead.Raise(this, new JidEventArgs(jid));
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Invoked when a message stanza is being sent.
        /// </summary>
        /// <param name="message">The message stanza which is being sent.</param>
        public void Output(Message message)
        {
            // We ask a receipt only if ther is not already a received node
            if (message.Data["received"] == null)
            {
                // Don't ask receipts for Jingle message
                if (message.Data.FirstChild.NamespaceURI.StartsWith("urn:xmpp:jingle"))
                    return;

                XmlElement e = message.Data;
                XmlNode child = e.OwnerDocument.CreateNode(XmlNodeType.Element, "request", "urn:xmpp:receipts");
                message.Data.AppendChild(child);
            }
        }

        /// <summary>
        /// Initializes a new instance of the CustomIq class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public MessageDeliveryReceipts(XmppIm im, String loggerPrefix)
            : base(im, loggerPrefix)
        {
        }
    }
}