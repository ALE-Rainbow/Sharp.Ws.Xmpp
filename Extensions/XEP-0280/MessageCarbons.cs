using Sharp.Xmpp.Core;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sharp.Xmpp.Extensions
{
    internal class MessageCarbons : XmppExtension
    {
        private static readonly string[] _namespaces = ["urn:xmpp:carbons:2"];
        private EntityCapabilities ecapa;

        public override IEnumerable<string> Namespaces
        {
            get { return _namespaces; }
        }

        public override Extension Xep
        {
            get { return Extension.MessageCarbons; }
        }

        public override void Initialize()
        {
            ecapa = im.GetExtension(typeof(EntityCapabilities)) as EntityCapabilities;
        }

        public async Task<Boolean> EnableCarbonsAsync(bool enable = true)
        {
            if (!ecapa.Supports(im.Jid.Domain, Extension.MessageCarbons))
            {
                throw new NotSupportedException("The XMPP server does not support " +
                    "the 'Message Carbons' extension.");
            }
            var result = await im.IqRequestAsync(IqType.Set, to:null, from: im.Jid, language:null, msDelay: 60000, data: Xml.Element(enable ? "enable" : "disable", _namespaces[0]));
            return (result.Iq.Type != IqType.Error);
        }

        public MessageCarbons(XmppIm im, String loggerPrefix) :
            base(im, loggerPrefix)
        {
            
        }
    }
}