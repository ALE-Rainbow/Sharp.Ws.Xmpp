namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Namespaces and node names used by OMEMO (XEP-0384).
    /// </summary>
    public static class OmemoConstants
    {
        /// <summary>OMEMO 0.9 namespace.</summary>
        public const string OmemoNamespace = "urn:xmpp:omemo:2";
        /// <summary>OMEMO 0.9 device list node.</summary>
        public const string DevicesNode = "urn:xmpp:omemo:2:devices";
        /// <summary>OMEMO 0.9 bundles node (one item per device).</summary>
        public const string BundlesNode = "urn:xmpp:omemo:2:bundles";

        /// <summary>Legacy OMEMO (v0.3, Conversations) namespace.</summary>
        public const string LegacyNamespace = "eu.siacs.conversations.axolotl";
        /// <summary>Legacy OMEMO device list node.</summary>
        public const string LegacyDevicesNode = LegacyNamespace + ".devicelist";
        /// <summary>Prefix of the legacy per-device bundle nodes; the device id is appended.</summary>
        public const string LegacyBundlesNodePrefix = LegacyNamespace + ".bundles:";

        /// <summary>Publish-subscribe namespace.</summary>
        public const string PubSubNamespace = "http://jabber.org/protocol/pubsub";
        /// <summary>Data forms namespace.</summary>
        public const string DataFormsNamespace = "jabber:x:data";

        /// <summary>Stanza Content Encryption namespace.</summary>
        public const string SceNamespace = "urn:xmpp:sce:1";
        /// <summary>Message processing hints namespace.</summary>
        public const string HintsNamespace = "urn:xmpp:hints";

        /// <summary>HKDF info string for the OMEMO 0.9 payload key.</summary>
        public const string PayloadKdfInfo = "OMEMO Payload";

        /// <summary>Environment variable naming the SQLite file that stores OMEMO state.</summary>
        public const string DbPathEnvVar = "XMPP_OMEMO_DB_PATH";
    }
}
