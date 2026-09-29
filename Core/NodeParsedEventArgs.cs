using System;

namespace Sharp.Ws.Xmpp.Core
{
    public class NodeParsedEventArgs : EventArgs
    {
        public NodeParsedEventArgs(string xml)
        {
            Xml = xml;
        }

        public string Xml { get; set; }

    }
}
