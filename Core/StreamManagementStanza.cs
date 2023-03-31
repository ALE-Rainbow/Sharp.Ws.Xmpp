using System;
using System.Xml;

namespace Sharp.Xmpp.Core
{

    public class StreamManagementStanza : Stanza
    {

        /// <summary>
        /// Initializes a new instance of the StreamManagementStanza class from the specified
        /// Xml element.
        /// </summary>
        /// <param name="element">An Xml element representing an IQ stanza.</param>
        /// <exception cref="ArgumentNullException">The element parameter is
        /// null.</exception>
        public StreamManagementStanza(XmlElement element)
            : base(element)
        {
        }

        /// <summary>
        /// Initializes a new instance of the StreamManagementStanza class from the specified         /// instance.
        /// </summary>
        /// <param name="sms">An instance of the StreamManagementStanza class to
        /// initialize this instance with.</param>
        /// <exception cref="ArgumentNullException">The message parameter is null.</exception>
        /// <exception cref="ArgumentException">The 'type' attribute of
        /// the specified message stanza is invalid.</exception>
        public StreamManagementStanza(StreamManagementStanza sms)
        {
            sms.ThrowIfNull("message");
            element = sms.element;
        }

    }
}
