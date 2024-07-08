using Microsoft.Extensions.Logging;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Xml;

namespace Sharp.Xmpp.Extensions
{
    internal class RainbowMessage : XmppExtension, IInputFilter<Sharp.Xmpp.Im.Message>
    {
        private readonly ILogger log;

        private readonly static string namespaceUsed;

        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { RainbowMessage.namespaceUsed };
            }
        }

        public override Extension Xep
        {
            get
            {
                return Extension.Rainbow;
            }
        }

        public event EventHandler<XmlElementEventArgs> ApplicationMessage;

        static RainbowMessage()
        {
            RainbowMessage.namespaceUsed = "jabber:iq:rainbow:cpaas:message";
        }

        public RainbowMessage(XmppIm im, string loggerPrefix) : base(im, loggerPrefix)
        {
            this.log = LogFactory.CreateLogger<RainbowMessage>(loggerPrefix);
        }

        public bool Input(Sharp.Xmpp.Im.Message message)
        {
            XmlElement xmlElement;
            bool flag = false;

            if ((message.Data["sent"] != null) && (message.Data["sent"]["forwarded"] != null) && (message.Data["sent"]["forwarded"]["message"] != null))
            {
                xmlElement = message.Data["sent"]["forwarded"]["message"]["rainbow-cpaas", RainbowMessage.namespaceUsed];
                if (xmlElement != null)
                {
                    flag = true;
                    ApplicationMessage.Raise(this, new XmlElementEventArgs(xmlElement));
                }
            }

            return flag;
        }


    }
}