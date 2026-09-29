using Sharp.Xmpp.Core;
using Sharp.Xmpp.Im;
using System.Xml;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// The XMPP operations the OMEMO extension needs; replaced in tests.
    /// </summary>
    internal interface IOmemoTransport
    {
        Jid Jid { get; }
        Iq IqRequest(IqType type, Jid to, XmlElement data, int millisecondsTimeout);
        void SendMessage(Im.Message message);
    }

    internal sealed class XmppImOmemoTransport : IOmemoTransport
    {
        private readonly XmppIm im;

        public XmppImOmemoTransport(XmppIm im)
        {
            this.im = im;
        }

        public Jid Jid => im.Jid;

        public Iq IqRequest(IqType type, Jid to, XmlElement data, int millisecondsTimeout) =>
            im.IqRequest(type, to, im.Jid, data, null, millisecondsTimeout);

        public void SendMessage(Im.Message message) => im.SendMessage(message);
    }
}
