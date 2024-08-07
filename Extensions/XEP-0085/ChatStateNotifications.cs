﻿using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Implements the 'Chat States Notifications' extension as defined in XEP-0085.
    /// </summary>
    internal class ChatStateNotifications : XmppExtension, IInputFilter<Message>
    {
        /// <summary>
        /// An enumerable collection of XMPP namespaces the extension implements.
        /// </summary>
        /// <remarks>This is used for compiling the list of supported extensions
        /// advertised by the 'Service Discovery' extension.</remarks>
        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { "http://jabber.org/protocol/chatstates" };
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
                return Extension.ChatStateNotifications;
            }
        }

        /// <summary>
        /// The event that is raised when the chat-state of an XMPP entity has
        /// changed.
        /// </summary>
        public event EventHandler<ChatStateChangedEventArgs> ChatStateChanged;

        /// <summary>
        /// Invoked when a message stanza has been received.
        /// </summary>
        /// <param name="stanza">The stanza which has been received.</param>
        /// <returns>true to intercept the stanza or false to pass the stanza
        /// on to the next handler.</returns>
        public bool Input(Message stanza)
        {
            // Look for chat-state elements.
            foreach (ChatState state in Enum.GetValues(typeof(ChatState)))
            {
                string name = state.ToString().ToLowerInvariant();
                if (stanza.Data[name] != null && stanza.Data[name].NamespaceURI ==
                    "http://jabber.org/protocol/chatstates")
                {
                    ChatStateChanged.Raise(this,
                        new ChatStateChangedEventArgs(stanza.From, state));
                    
                    // Even if we have manage the ChatStateChnage event we MUST pass this message to next handler !
                }
            }
            // Pass the message on to the next handler.
            return false;
        }

        /// <summary>
        /// Sets the chat-state for the conversation with the XMPP user with the
        /// specified JID.
        /// </summary>
        /// <param name="jid">The JID of the XMPP user to set the chat-state
        /// for.</param>
        /// <param name="state">The new chat-state.</param>
        /// <exception cref="ArgumentNullException">The jid parameter is
        /// null.</exception>
        public void SetChatState(Jid jid, MessageType type, ChatState state)
        {
            AsyncHelper.RunSync(async () => await SetChatStateAsync(jid, type, state).ConfigureAwait(false));
        }

        public async Task<Boolean> SetChatStateAsync(Jid jid, MessageType type, ChatState state)
        {
            jid.ThrowIfNull("jid");
            Message m = new(jid)
            {
                Type = type
            };
            m.Data.Child(Xml.Element(state.ToString().ToLowerInvariant(),
                "http://jabber.org/protocol/chatstates"));
            return await im.SendMessageAsync(m);
        }

        /// <summary>
        /// Initializes a new instance of the ChatStateNotifications class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public ChatStateNotifications(XmppIm im, String loggerPrefix)
            : base(im, loggerPrefix)
        {
        }
    }
}