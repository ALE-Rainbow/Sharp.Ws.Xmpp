using Microsoft.Extensions.Logging;
using Sharp.Xmpp.Core;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Implements OMEMO Encryption (XEP-0384): OMEMO 0.9 (urn:xmpp:omemo:2) and, for clients such
    /// as Conversations, legacy OMEMO 0.3 (eu.siacs.conversations.axolotl). Both share one
    /// identity, device id, signed PreKey and PreKey pool.
    /// </summary>
    internal sealed class OmemoEncryption : XmppExtension, IInputFilter<Im.Message>, IOutputFilter<Im.Message>, IInputFilter<Im.Presence>
    {
        internal const string FallbackBody = "I sent you an OMEMO encrypted message but your client doesn't seem to support that.";
        internal const string WarningNotForThisDevice = "[OMEMO] This message was not encrypted for this device.";
        internal const string WarningDecryptionFailed = "[OMEMO] A message could not be decrypted.";
        internal const string WarningNoSession = "[OMEMO] A message could not be decrypted: no session with the sending device. A new session is being set up.";
        internal const string WarningInvalidEnvelope = "[OMEMO] A message was rejected because its encrypted envelope did not match the message.";

        private const int IqTimeout = 30000;
        private const string CarbonsNamespace = "urn:xmpp:carbons:2";
        private const string ForwardNamespace = "urn:xmpp:forward:0";
        private const string MucUserNamespace = "http://jabber.org/protocol/muc#user";
        private const string MucAdminNamespace = "http://jabber.org/protocol/muc#admin";
        private const string PubSubOwnerNamespace = "http://jabber.org/protocol/pubsub#owner";
        private const string PubSubErrorsNamespace = "http://jabber.org/protocol/pubsub#errors";
        private const string EmeNamespace = "urn:xmpp:eme:0";

        private static readonly string[] _namespaces =
        {
            OmemoConstants.OmemoNamespace,
            OmemoConstants.DevicesNode + "+notify",
            OmemoConstants.LegacyDevicesNode + "+notify"
        };

        private readonly ILogger logger = LogFactory.CreateLogger<OmemoEncryption>();
        private readonly object syncRoot = new object();

        // Per-connection caches.
        private readonly HashSet<string> fetchedDeviceLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> occupantRealJids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> roomAffiliations = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> fetchedRoomAffiliations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Pep pep;
        private IOmemoStore store;
        private OmemoSessionManager sessions;
        private OmemoTrustStore trust;
        private IOmemoTransport transport;

        public OmemoEncryption(XmppIm im) : base(im)
        {
            transport = new XmppImOmemoTransport(im);
        }

        /// <summary>Replaces the XMPP transport and store (tests only).</summary>
        internal void UseForTesting(IOmemoTransport testTransport, IOmemoStore testStore)
        {
            transport = testTransport;
            store = testStore;
        }

        /// <summary>
        /// Enables encryption of outgoing messages. Incoming OMEMO messages are always decrypted.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Also announces the device and encrypts with legacy OMEMO (eu.siacs.conversations.axolotl)
        /// for devices that do not support OMEMO 0.9.
        /// </summary>
        public bool LegacyEnabled { get; set; } = true;

        /// <summary>Optional label for the local device, published signed (§5.3.1).</summary>
        public string DeviceLabel { get; set; }

        public event EventHandler<OmemoDecryptionFailedEventArgs> DecryptionFailed;
        public event EventHandler<OmemoOptOutEventArgs> OptOutReceived;
        public event EventHandler<JidEventArgs> DevicesChanged;

        public override IEnumerable<string> Namespaces => _namespaces;

        public override Extension Xep => Extension.OmemoEncryption;

        public override void Initialize()
        {
            pep = im.GetExtension<Pep>();
            pep?.Subscribe(OmemoConstants.DevicesNode, (from, item) => OnDevicesUpdated(OmemoVersion.Omemo2, from, item));
            pep?.Subscribe(OmemoConstants.LegacyDevicesNode, (from, item) => OnDevicesUpdated(OmemoVersion.Legacy, from, item));
        }

        /// <summary>The local device id, or 0 before login.</summary>
        public int LocalDeviceId => TryEnsureReady() ? sessions.DeviceId : 0;

        /// <summary>The local device's fingerprint, or null before login.</summary>
        public string LocalFingerprint => TryEnsureReady() ? OmemoSessionManager.Fingerprint(sessions.IdentityKey) : null;

        #region Announcing support (§5.3)

        /// <summary>
        /// Rotates keys if due, publishes the bundles and makes sure the local device is on the
        /// account's device lists (§5.3). Call after every login.
        /// </summary>
        public void PublishOwnDeviceAndBundle()
        {
            EnsureReadyOrThrow();
            string own = OwnBareJid;

            sessions.MaintainKeys(DateTime.UtcNow);
            List<OmemoDevice> devices = FetchDeviceList(OmemoVersion.Omemo2, own).ToList();
            List<OmemoDevice> legacyDevices = LegacyEnabled ? TryFetchLegacyDeviceList(own) : new List<OmemoDevice>();

            // A fresh device id MUST NOT collide with an existing one (§6).
            var taken = devices.Concat(legacyDevices).Select(d => d.Id).ToList();
            if (!sessions.DeviceIdAnnounced && taken.Contains(sessions.DeviceId))
                sessions.RegenerateDeviceId(taken);

            PublishBundle();
            AnnounceDevice(devices);

            if (LegacyEnabled)
            {
                try
                {
                    PublishLegacyBundle();
                    AnnounceLegacyDevice(legacyDevices);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not announce legacy OMEMO support");
                }
            }

            sessions.MarkDeviceIdAnnounced();
        }

        /// <summary>
        /// Removes the local device from the device lists and deletes its bundles (§6).
        /// </summary>
        public void UnpublishOwnDevice()
        {
            EnsureReadyOrThrow();
            string id = sessions.DeviceId.ToString();

            var devices = FetchDeviceList(OmemoVersion.Omemo2, OwnBareJid).Where(d => d.Id != sessions.DeviceId).ToList();
            PublishItem(OmemoConstants.DevicesNode, "current", OmemoXml.BuildDevicesElement(devices), OpenAccess());
            store.SetDevices(DeviceStoreKey(OmemoVersion.Omemo2, OwnBareJid), devices);
            Retract(OmemoConstants.BundlesNode, id);

            if (LegacyEnabled)
            {
                try
                {
                    var legacy = TryFetchLegacyDeviceList(OwnBareJid).Where(d => d.Id != sessions.DeviceId).ToList();
                    PublishItem(OmemoConstants.LegacyDevicesNode, "current", OmemoLegacyXml.BuildDevicesElement(legacy.Select(d => d.Id)), OpenAccess());
                    store.SetDevices(DeviceStoreKey(OmemoVersion.Legacy, OwnBareJid), legacy);
                    Retract(OmemoConstants.LegacyBundlesNodePrefix + id, "current");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not remove the legacy OMEMO device");
                }
            }
            Enabled = false;
        }

        private void AnnounceDevice(List<OmemoDevice> devices)
        {
            var own = devices.FirstOrDefault(d => d.Id == sessions.DeviceId);
            string label = String.IsNullOrWhiteSpace(DeviceLabel) ? null : DeviceLabel.Trim();
            bool labelMatches = own != null && own.Label == label && (label == null || VerifyLabel(own, OmemoPrimitives.Ed25519PublicToX25519(sessions.IdentityKey)));
            if (own != null && labelMatches)
            {
                CacheDevices(OmemoVersion.Omemo2, OwnBareJid, devices);
                return;
            }

            devices.RemoveAll(d => d.Id == sessions.DeviceId);
            devices.Add(new OmemoDevice
            {
                Id = sessions.DeviceId,
                Label = label,
                LabelSignature = label == null ? null : Convert.ToBase64String(sessions.Sign(Encoding.UTF8.GetBytes(label)))
            });

            PublishItem(OmemoConstants.DevicesNode, "current", OmemoXml.BuildDevicesElement(devices), OpenAccess());
            CacheDevices(OmemoVersion.Omemo2, OwnBareJid, devices);
        }

        private void AnnounceLegacyDevice(List<OmemoDevice> devices)
        {
            if (devices.All(d => d.Id != sessions.DeviceId))
            {
                devices.Add(new OmemoDevice { Id = sessions.DeviceId });
                PublishItem(OmemoConstants.LegacyDevicesNode, "current", OmemoLegacyXml.BuildDevicesElement(devices.Select(d => d.Id)), OpenAccess());
            }
            CacheDevices(OmemoVersion.Legacy, OwnBareJid, devices);
        }

        private void PublishBundle()
        {
            var options = OpenAccess();
            options["pubsub#max_items"] = "max";
            PublishItem(OmemoConstants.BundlesNode, sessions.DeviceId.ToString(), OmemoXml.BuildBundleElement(sessions.GetBundle()), options);
        }

        private void PublishLegacyBundle() =>
            PublishItem(OmemoConstants.LegacyBundlesNodePrefix + sessions.DeviceId, "current",
                OmemoLegacyXml.BuildBundleElement(sessions.GetLegacyBundle()), OpenAccess());

        private void RepublishBundles()
        {
            PublishBundle();
            if (LegacyEnabled)
                PublishLegacyBundle();
        }

        private static Dictionary<string, string> OpenAccess() =>
            new Dictionary<string, string> { { "pubsub#access_model", "open" } };

        /// <summary>
        /// Publishes an item with publish-options; if the node exists with a different
        /// configuration it is reconfigured (XEP-0060 §8.2) and the publish retried.
        /// </summary>
        private void PublishItem(string node, string itemId, XmlElement payload, IDictionary<string, string> options)
        {
            Iq iq = transport.IqRequest(IqType.Set, null, BuildPublish(node, itemId, payload, options), IqTimeout);
            if (iq.Type == IqType.Error && IsPreconditionNotMet(iq))
            {
                ConfigureNode(node, options);
                iq = transport.IqRequest(IqType.Set, null, BuildPublish(node, itemId, payload, options), IqTimeout);
            }
            if (iq.Type == IqType.Error)
                throw Util.ExceptionFromError(iq, "Could not publish to " + node + ".");
        }

        private void Retract(string node, string itemId)
        {
            var retract = Xml.Element("pubsub", OmemoConstants.PubSubNamespace)
                .Child(Xml.Element("retract").Attr("node", node).Attr("notify", "true")
                    .Child(Xml.Element("item").Attr("id", itemId)));
            Iq iq = transport.IqRequest(IqType.Set, null, retract, IqTimeout);
            if (iq.Type == IqType.Error)
                throw Util.ExceptionFromError(iq, "Could not retract from " + node + ".");
        }

        private static XmlElement BuildPublish(string node, string itemId, XmlElement payload, IDictionary<string, string> options)
        {
            return Xml.Element("pubsub", OmemoConstants.PubSubNamespace)
                .Child(Xml.Element("publish").Attr("node", node)
                    .Child(Xml.Element("item").Attr("id", itemId).Child(payload)))
                .Child(Xml.Element("publish-options")
                    .Child(OmemoXml.BuildForm("http://jabber.org/protocol/pubsub#publish-options", options)));
        }

        private void ConfigureNode(string node, IDictionary<string, string> options)
        {
            var configure = Xml.Element("pubsub", PubSubOwnerNamespace)
                .Child(Xml.Element("configure").Attr("node", node)
                    .Child(OmemoXml.BuildForm("http://jabber.org/protocol/pubsub#node_config", options)));
            Iq iq = transport.IqRequest(IqType.Set, null, configure, IqTimeout);
            if (iq.Type == IqType.Error)
                throw Util.ExceptionFromError(iq, "Could not configure " + node + ".");
        }

        private static bool IsPreconditionNotMet(Iq iq)
        {
            var error = iq.Data["error"];
            return error != null && error.ChildNodes.OfType<XmlElement>()
                .Any(e => e.LocalName == "precondition-not-met" && e.NamespaceURI == PubSubErrorsNamespace);
        }

        #endregion

        #region Devices, bundles and trust (§5.2, §5.4, §8)

        /// <summary>Returns the devices of a bare JID in both versions, with fingerprints and trust.</summary>
        public IList<OmemoDeviceInfo> GetDevices(Jid jid, bool refresh)
        {
            jid.ThrowIfNull("jid");
            EnsureReadyOrThrow();
            string bare = jid.GetBareJid().ToString();

            var result = new List<OmemoDeviceInfo>();
            foreach (var version in ActiveVersions())
            {
                IEnumerable<OmemoDevice> listed = refresh ? FetchAndCacheDeviceList(version, bare) : GetDeviceList(version, bare);
                var ids = new HashSet<int>();
                foreach (var d in listed)
                {
                    ids.Add(d.Id);
                    result.Add(DescribeDevice(version, bare, d.Id, d, true, true));
                }
                // Devices with a session that are no longer on the list.
                foreach (int id in SessionDeviceIds(version, bare).Where(id => !ids.Contains(id)))
                    result.Add(DescribeDevice(version, bare, id, null, false, false));
            }
            return result;
        }

        /// <summary>Sets the trust decision for the IdentityKey(s) a device id announced.</summary>
        public void SetTrust(Jid jid, int deviceId, OmemoTrustLevel level)
        {
            jid.ThrowIfNull("jid");
            EnsureReadyOrThrow();
            string bare = jid.GetBareJid().ToString();
            bool any = false;
            foreach (var version in ActiveVersions())
            {
                byte[] key = KnownCurveKey(version, bare, deviceId) ?? CurveKey(version, TryFetchBundle(version, bare, deviceId)?.IdentityKeyPublic);
                if (key == null)
                    continue;
                trust.RecordIdentityKey(version, bare, deviceId, key);
                trust.SetTrust(bare, key, level);
                any = true;
            }
            if (!any)
                throw new InvalidOperationException("The IdentityKey of device " + deviceId + " is not known.");
        }

        /// <summary>Sets the trust decision for an IdentityKey given as its hex fingerprint.</summary>
        public void SetTrust(Jid jid, string fingerprint, OmemoTrustLevel level)
        {
            jid.ThrowIfNull("jid");
            fingerprint.ThrowIfNullOrEmpty("fingerprint");
            EnsureReadyOrThrow();
            byte[] key = ParseFingerprint(fingerprint) ?? throw new ArgumentException("Invalid fingerprint.", nameof(fingerprint));
            trust.SetTrust(jid.GetBareJid().ToString(), key, level);
        }

        /// <summary>
        /// Replaces the sessions with all devices of a bare JID (§6 broken sessions).
        /// </summary>
        public void ResetSessions(Jid jid)
        {
            jid.ThrowIfNull("jid");
            EnsureReadyOrThrow();
            string bare = jid.GetBareJid().ToString();

            foreach (var version in ActiveVersions())
            {
                foreach (int id in SessionDeviceIds(version, bare).ToList())
                    DeleteSession(version, bare, id);

                foreach (var d in FetchAndCacheDeviceList(version, bare))
                {
                    if (IsLocalDevice(bare, d.Id))
                        continue;
                    try
                    {
                        var bundle = TryFetchBundle(version, bare, d.Id);
                        if (bundle == null || !IsTrusted(bare, CurveKey(version, bundle.IdentityKeyPublic)))
                            continue;
                        BuildSession(version, bare, d.Id, bundle);
                        SendEmptyMessage(version, bare, d.Id);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not rebuild OMEMO session with {0}:{1}", bare, d.Id);
                    }
                }
            }
        }

        private OmemoDeviceInfo DescribeDevice(OmemoVersion version, string bare, int id, OmemoDevice listed, bool active, bool fetchBundle)
        {
            bool local = IsLocalDevice(bare, id);
            byte[] key = local ? OmemoPrimitives.Ed25519PublicToX25519(sessions.IdentityKey) : KnownCurveKey(version, bare, id);
            if (key == null && fetchBundle)
            {
                key = CurveKey(version, TryFetchBundle(version, bare, id)?.IdentityKeyPublic);
                trust.RecordIdentityKey(version, bare, id, key);
            }

            return new OmemoDeviceInfo
            {
                Jid = new Jid(bare),
                DeviceId = id,
                Version = version,
                Label = listed != null && key != null && VerifyLabel(listed, key) ? listed.Label : null,
                Fingerprint = OmemoSessionManager.CurveFingerprint(key),
                Trust = local ? OmemoTrustLevel.Verified : trust.GetTrust(bare, key),
                IsLocalDevice = local,
                Active = active
            };
        }

        /// <summary>Verifies a device label signature; the Edwards sign bit is unknown from the Curve25519 key.</summary>
        private static bool VerifyLabel(OmemoDevice device, byte[] curveKey)
        {
            if (String.IsNullOrEmpty(device.Label) || String.IsNullOrEmpty(device.LabelSignature))
                return false;
            byte[] sig = OmemoXml.FromBase64(device.LabelSignature);
            byte[] message = Encoding.UTF8.GetBytes(device.Label);
            byte[] ed;
            try
            {
                ed = OmemoPrimitives.X25519PublicToEd25519(curveKey);
            }
            catch (CryptographicException)
            {
                return false;
            }
            if (OmemoPrimitives.Ed25519Verify(ed, message, sig))
                return true;
            ed[31] |= 0x80;
            return OmemoPrimitives.Ed25519Verify(ed, message, sig);
        }

        private byte[] KnownCurveKey(OmemoVersion version, string bare, int deviceId) =>
            CurveKey(version, SessionIdentityKey(version, bare, deviceId)) ?? trust.GetIdentityKey(version, bare, deviceId);

        private bool IsTrusted(string bare, byte[] curveKey)
        {
            var level = trust.GetTrust(bare, curveKey);
            return level == OmemoTrustLevel.Trusted || level == OmemoTrustLevel.Verified;
        }

        private IEnumerable<OmemoVersion> ActiveVersions() =>
            LegacyEnabled ? new[] { OmemoVersion.Omemo2, OmemoVersion.Legacy } : new[] { OmemoVersion.Omemo2 };

        private static string DeviceStoreKey(OmemoVersion version, string bare) =>
            version == OmemoVersion.Legacy ? "legacy|" + bare : bare;

        private void CacheDevices(OmemoVersion version, string bare, IEnumerable<OmemoDevice> devices)
        {
            store.SetDevices(DeviceStoreKey(version, bare), devices);
            lock (syncRoot)
                fetchedDeviceLists.Add(DeviceStoreKey(version, bare));
        }

        private IEnumerable<OmemoDevice> GetDeviceList(OmemoVersion version, string bare)
        {
            bool fetched;
            lock (syncRoot)
                fetched = fetchedDeviceLists.Contains(DeviceStoreKey(version, bare));
            return fetched ? store.GetDevices(DeviceStoreKey(version, bare)) : FetchAndCacheDeviceList(version, bare);
        }

        private IEnumerable<OmemoDevice> FetchAndCacheDeviceList(OmemoVersion version, string bare)
        {
            var devices = version == OmemoVersion.Legacy ? TryFetchLegacyDeviceList(bare) : FetchDeviceList(version, bare).ToList();
            CacheDevices(version, bare, devices);
            return devices;
        }

        private IEnumerable<OmemoDevice> FetchDeviceList(OmemoVersion version, string bare)
        {
            string node = version == OmemoVersion.Legacy ? OmemoConstants.LegacyDevicesNode : OmemoConstants.DevicesNode;
            var items = RetrieveItems(new Jid(bare), node, null);
            if (items == null)
                return new OmemoDevice[0];
            var item = items.FirstOrDefault(i => i.GetAttribute("id") == "current") ?? items.LastOrDefault();
            return version == OmemoVersion.Legacy ? OmemoLegacyXml.ParseDevices(item) : OmemoXml.ParseDevices(item);
        }

        /// <summary>Legacy support is optional: failures yield an empty list.</summary>
        private List<OmemoDevice> TryFetchLegacyDeviceList(string bare)
        {
            try
            {
                return FetchDeviceList(OmemoVersion.Legacy, bare).ToList();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not fetch the legacy OMEMO devices of {0}", bare);
                return new List<OmemoDevice>();
            }
        }

        private OmemoBundle TryFetchBundle(OmemoVersion version, string bare, int deviceId)
        {
            try
            {
                if (version == OmemoVersion.Legacy)
                {
                    var legacyItems = RetrieveItems(new Jid(bare), OmemoConstants.LegacyBundlesNodePrefix + deviceId, null);
                    var legacyItem = legacyItems?.FirstOrDefault(i => i.GetAttribute("id") == "current") ?? legacyItems?.FirstOrDefault();
                    return OmemoLegacyXml.ParseBundle(legacyItem);
                }

                var items = RetrieveItems(new Jid(bare), OmemoConstants.BundlesNode, deviceId.ToString());
                var item = items?.FirstOrDefault(i => i.GetAttribute("id") == deviceId.ToString());
                return OmemoXml.ParseBundle(item);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not fetch the OMEMO bundle of {0}:{1}", bare, deviceId);
                return null;
            }
        }

        /// <summary>
        /// Retrieves pubsub items; returns null if the node or item does not exist.
        /// </summary>
        private List<XmlElement> RetrieveItems(Jid jid, string node, string itemId)
        {
            var itemsRequest = Xml.Element("items").Attr("node", node);
            if (itemId != null)
                itemsRequest.Child(Xml.Element("item").Attr("id", itemId));

            Iq iq = transport.IqRequest(IqType.Get, jid, Xml.Element("pubsub", OmemoConstants.PubSubNamespace).Child(itemsRequest), IqTimeout);
            if (iq.Type == IqType.Error)
            {
                var ex = Util.ExceptionFromError(iq, "Could not retrieve " + node + " from " + jid + ".");
                if (ex is XmppErrorException xe && xe.Error.Condition == ErrorCondition.ItemNotFound)
                    return null;
                throw ex;
            }

            var items = iq.Data["pubsub", OmemoConstants.PubSubNamespace]?["items", OmemoConstants.PubSubNamespace];
            if (items == null)
                return null;
            return items.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "item").ToList();
        }

        private void OnDevicesUpdated(OmemoVersion version, Jid from, XmlElement item)
        {
            try
            {
                if (!TryEnsureReady() || (version == OmemoVersion.Legacy && !LegacyEnabled))
                    return;
                string bare = from?.GetBareJid().ToString() ?? OwnBareJid;
                var devices = (version == OmemoVersion.Legacy ? OmemoLegacyXml.ParseDevices(item) : OmemoXml.ParseDevices(item)).ToList();
                CacheDevices(version, bare, devices);

                // Another device may have overwritten our entry (§5.3.1): re-announce.
                if (String.Equals(bare, OwnBareJid, StringComparison.OrdinalIgnoreCase) &&
                    sessions.DeviceIdAnnounced && devices.All(d => d.Id != sessions.DeviceId))
                {
                    if (version == OmemoVersion.Legacy)
                        RunInBackground("re-announce legacy device", () => AnnounceLegacyDevice(TryFetchLegacyDeviceList(OwnBareJid)));
                    else
                        RunInBackground("re-announce device", () => AnnounceDevice(FetchDeviceList(OmemoVersion.Omemo2, OwnBareJid).ToList()));
                }

                DevicesChanged.Raise(this, new JidEventArgs(bare));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not process an OMEMO device list update");
            }
        }

        #endregion

        #region Sessions per version

        private bool HasSession(OmemoVersion v, string bare, int id) =>
            v == OmemoVersion.Legacy ? sessions.HasLegacySession(bare, id) : sessions.HasSession(bare, id);

        /// <summary>The session's remote IdentityKey in its native form (Ed25519 for v2, Curve25519 for legacy).</summary>
        private byte[] SessionIdentityKey(OmemoVersion v, string bare, int id) =>
            v == OmemoVersion.Legacy ? sessions.GetLegacySessionIdentityKey(bare, id) : sessions.GetSessionIdentityKey(bare, id);

        private void BuildSession(OmemoVersion v, string bare, int id, OmemoBundle bundle)
        {
            if (v == OmemoVersion.Legacy)
                sessions.BuildLegacySession(bare, id, bundle);
            else
                sessions.BuildSession(bare, id, bundle);
        }

        private byte[] EncryptKey(OmemoVersion v, string bare, int id, byte[] material, out bool kex) =>
            v == OmemoVersion.Legacy ? sessions.EncryptLegacy(bare, id, material, out kex) : sessions.Encrypt(bare, id, material, out kex);

        private void DeleteSession(OmemoVersion v, string bare, int id)
        {
            if (v == OmemoVersion.Legacy)
                sessions.DeleteLegacySession(bare, id);
            else
                sessions.DeleteSession(bare, id);
        }

        private IEnumerable<int> SessionDeviceIds(OmemoVersion v, string bare) =>
            v == OmemoVersion.Legacy ? sessions.GetLegacySessionDeviceIds(bare) : sessions.GetSessionDeviceIds(bare);

        /// <summary>Converts a native IdentityKey to the Curve25519 form used for trust and fingerprints.</summary>
        private static byte[] CurveKey(OmemoVersion v, byte[] nativeKey)
        {
            if (nativeKey == null)
                return null;
            if (v == OmemoVersion.Legacy)
                return nativeKey;
            try
            {
                return OmemoPrimitives.Ed25519PublicToX25519(nativeKey);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        #endregion

        #region Sending (§5.5, §5.7, §5.8)

        public void Output(Im.Message stanza)
        {
            if (stanza == null || !Enabled)
                return;
            if (stanza.Data["encrypted", OmemoConstants.OmemoNamespace] != null || stanza.Data["encrypted", OmemoConstants.LegacyNamespace] != null)
                return;
            if (stanza.Type == MessageType.Error || stanza.Type == MessageType.Headline)
                return;
            if (stanza.Body == null || stanza.To == null)
                return;

            EnsureReadyOrThrow();

            if (stanza.Type == MessageType.Groupchat)
            {
                EncryptGroupChat(stanza);
                return;
            }

            string peer = stanza.To.GetBareJid().ToString();
            switch (GetOptOutState(peer))
            {
                case OptOutState.Pending:
                    throw new OmemoSendException(OmemoSendFailure.OptedOut, new Jid(peer),
                        peer + " asked to stop using OMEMO. Confirm plaintext before sending.");
                case OptOutState.PlaintextConfirmed:
                    return;
            }

            EncryptAndAttach(stanza, new[] { OmemoXml.CreateBody(stanza.Body) }, stanza.Body, new[] { peer }, new Jid(peer));
        }

        /// <summary>
        /// Sends an encrypted &lt;opt-out/&gt; to a contact (§5.7) and switches the chat to plaintext.
        /// Opt-out only exists in OMEMO 0.9.
        /// </summary>
        public void SendOptOut(Jid jid, string reason)
        {
            jid.ThrowIfNull("jid");
            EnsureReadyOrThrow();
            string peer = jid.GetBareJid().ToString();
            var message = new Im.Message(new Jid(peer), (string)null, null, null, MessageType.Chat);
            EncryptAndAttach(message, new[] { OmemoXml.CreateOptOut(reason) }, null, new[] { peer }, new Jid(peer), false);
            transport.SendMessage(message);
            SetOptOutState(peer, OptOutState.PlaintextConfirmed);
        }

        /// <summary>Confirms sending plaintext to a contact that opted out (§5.7).</summary>
        public void ConfirmPlaintext(Jid jid)
        {
            jid.ThrowIfNull("jid");
            EnsureReadyOrThrow();
            SetOptOutState(jid.GetBareJid().ToString(), OptOutState.PlaintextConfirmed);
        }

        /// <summary>Goes back to OMEMO for a contact after an opt-out (§5.7).</summary>
        public void ResumeEncryption(Jid jid)
        {
            jid.ThrowIfNull("jid");
            EnsureReadyOrThrow();
            SetOptOutState(jid.GetBareJid().ToString(), OptOutState.None);
        }

        public bool IsOptedOut(Jid jid) =>
            jid != null && TryEnsureReady() && GetOptOutState(jid.GetBareJid().ToString()) != OptOutState.None;

        private void EncryptGroupChat(Im.Message stanza)
        {
            string room = stanza.To.GetBareJid().ToString();
            var members = GetRoomMembers(room);
            if (members.Count == 0)
            {
                throw new OmemoSendException(OmemoSendFailure.GroupChatNotSupported, new Jid(room),
                    "The members of " + room + " are unknown. OMEMO requires a non-anonymous room.");
            }

            var recipients = members.Where(m => !String.Equals(m, OwnBareJid, StringComparison.OrdinalIgnoreCase)).ToList();
            // The <to/> affix is mandatory in group chats (§5.5.1).
            EncryptAndAttach(stanza, new[] { OmemoXml.CreateBody(stanza.Body) }, stanza.Body, recipients, new Jid(room));
        }

        /// <param name="legacyBody">The body for legacy OMEMO devices, or null if the content cannot be
        /// expressed in legacy OMEMO (which only transports a body).</param>
        private void EncryptAndAttach(Im.Message stanza, IEnumerable<XmlElement> content, string legacyBody, IEnumerable<string> recipients, Jid sceTo, bool addFallback = true)
        {
            // OuterXml escapes text content, which ToXmlString does not.
            byte[] envelope = Encoding.UTF8.GetBytes(OmemoXml.CreateSceEnvelope(content, new Jid(OwnBareJid), sceTo).OuterXml);
            OmemoPayloadMaterial payload = OmemoPayloadCipher.Encrypt(envelope);
            var material = new KeyMaterial { V2 = OmemoPayloadCipher.BuildTransportPlaintext(payload) };

            byte[] legacyPayload = null, legacyIv = null;
            if (legacyBody != null && LegacyEnabled)
            {
                legacyPayload = OmemoLegacyPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(legacyBody), out byte[] keyAndTag, out legacyIv);
                material.Legacy = keyAndTag;
            }

            var v2Keys = new List<OmemoHeaderKey>();
            var legacyKeys = new List<OmemoHeaderKey>();
            foreach (string recipient in recipients)
                EncryptForJid(recipient, material, true, v2Keys, legacyKeys);
            // The sender's other devices (§5.5.2).
            EncryptForJid(OwnBareJid, material, false, v2Keys, legacyKeys);

            foreach (var body in stanza.Data.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "body").ToList())
                stanza.Data.RemoveChild(body);

            if (v2Keys.Count > 0 || legacyKeys.Count == 0)
            {
                stanza.Data.Child(OmemoXml.BuildEncryptedElement(sessions.DeviceId, v2Keys, payload.Ciphertext));
                stanza.Data.Child(Eme(OmemoConstants.OmemoNamespace));
            }
            if (legacyKeys.Count > 0)
            {
                stanza.Data.Child(OmemoLegacyXml.BuildEncryptedElement(sessions.DeviceId, legacyKeys, legacyIv, legacyPayload));
                stanza.Data.Child(Eme(OmemoConstants.LegacyNamespace));
            }
            if (addFallback)
                stanza.Data.Child(Xml.Element("body").Text(FallbackBody));
            if (stanza.Data["store", OmemoConstants.HintsNamespace] == null)
                stanza.Data.Child(Xml.Element("store", OmemoConstants.HintsNamespace));
        }

        private static XmlElement Eme(string ns) =>
            Xml.Element("encryption", EmeNamespace).Attr("namespace", ns).Attr("name", "OMEMO");

        private sealed class KeyMaterial
        {
            public byte[] V2 { get; set; }
            public byte[] Legacy { get; set; }
        }

        /// <summary>
        /// Encrypts the key material for every trusted device of a bare JID: OMEMO 0.9 devices get
        /// OMEMO 0.9 keys, devices only on the legacy list get legacy keys. For a required recipient,
        /// throws unless at least one device is used.
        /// </summary>
        private void EncryptForJid(string bare, KeyMaterial material, bool required, List<OmemoHeaderKey> v2Keys, List<OmemoHeaderKey> legacyKeys)
        {
            var undecided = new List<OmemoDeviceInfo>();
            var v2Ids = GetDeviceList(OmemoVersion.Omemo2, bare).Select(d => d.Id).Where(id => !IsLocalDevice(bare, id)).ToList();
            var legacyIds = material.Legacy == null || !LegacyEnabled
                ? new List<int>()
                : GetDeviceList(OmemoVersion.Legacy, bare).Select(d => d.Id).Where(id => !IsLocalDevice(bare, id) && !v2Ids.Contains(id)).ToList();

            if (required && v2Ids.Count == 0 && legacyIds.Count == 0)
            {
                throw new OmemoSendException(OmemoSendFailure.NoDevices, new Jid(bare),
                    bare + " has no OMEMO devices. The message was not sent.");
            }

            int used = 0;
            foreach (var (version, id) in v2Ids.Select(id => (OmemoVersion.Omemo2, id)).Concat(legacyIds.Select(id => (OmemoVersion.Legacy, id))))
            {
                var key = TryEncryptForDevice(version, bare, id, version == OmemoVersion.Legacy ? material.Legacy : material.V2, undecided);
                if (key == null)
                    continue;
                (version == OmemoVersion.Legacy ? legacyKeys : v2Keys).Add(key);
                used++;
            }

            if (required && used == 0)
            {
                if (undecided.Count > 0)
                {
                    throw new OmemoSendException(OmemoSendFailure.UntrustedDevices, new Jid(bare),
                        "None of the OMEMO devices of " + bare + " is trusted yet. The message was not sent.", undecided);
                }
                throw new OmemoSendException(OmemoSendFailure.SessionFailure, new Jid(bare),
                    "No OMEMO session could be established with " + bare + ". The message was not sent.");
            }
        }

        private OmemoHeaderKey TryEncryptForDevice(OmemoVersion version, string bare, int id, byte[] material, List<OmemoDeviceInfo> undecided)
        {
            try
            {
                OmemoBundle bundle = null;
                byte[] nativeKey = SessionIdentityKey(version, bare, id);
                if (nativeKey == null)
                {
                    bundle = TryFetchBundle(version, bare, id);
                    nativeKey = bundle?.IdentityKeyPublic;
                }
                byte[] curve = CurveKey(version, nativeKey);
                if (curve == null)
                    return null;

                trust.RecordIdentityKey(version, bare, id, curve);
                var level = trust.GetTrust(bare, curve);
                if (level == OmemoTrustLevel.Distrusted)
                    return null;
                if (level == OmemoTrustLevel.Undecided)
                {
                    // Never send to a device without a trust decision (§8).
                    undecided.Add(DescribeDevice(version, bare, id, null, true, false));
                    return null;
                }

                if (!HasSession(version, bare, id))
                    BuildSession(version, bare, id, bundle ?? TryFetchBundle(version, bare, id) ??
                        throw new InvalidOperationException("Bundle unavailable."));

                byte[] key = EncryptKey(version, bare, id, material, out bool kex);
                return new OmemoHeaderKey { BareJid = bare, RecipientDeviceId = id, IsKeyExchange = kex, Ciphertext = key };
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not encrypt for OMEMO device {0}:{1} ({2})", bare, id, version);
                return null;
            }
        }

        /// <summary>
        /// Sends an empty OMEMO message (§5.5.3; a key transport message in legacy OMEMO) to one
        /// device, e.g. to complete a key exchange or as a heartbeat (§6). Trust does not apply to
        /// empty messages (§8).
        /// </summary>
        private void SendEmptyMessage(OmemoVersion version, string bare, int deviceId)
        {
            if (!HasSession(version, bare, deviceId))
            {
                var bundle = TryFetchBundle(version, bare, deviceId) ??
                    throw new InvalidOperationException("Bundle of " + bare + ":" + deviceId + " unavailable.");
                BuildSession(version, bare, deviceId, bundle);
            }

            XmlElement encrypted;
            if (version == OmemoVersion.Legacy)
            {
                byte[] key = sessions.EncryptLegacy(bare, deviceId, OmemoPrimitives.RandomBytes(32), out bool preKey);
                encrypted = OmemoLegacyXml.BuildEncryptedElement(sessions.DeviceId,
                    new[] { new OmemoHeaderKey { BareJid = bare, RecipientDeviceId = deviceId, IsKeyExchange = preKey, Ciphertext = key } },
                    OmemoPrimitives.RandomBytes(12), null);
            }
            else
            {
                byte[] key = sessions.Encrypt(bare, deviceId, new byte[32], out bool kex);
                encrypted = OmemoXml.BuildEncryptedElement(sessions.DeviceId,
                    new[] { new OmemoHeaderKey { BareJid = bare, RecipientDeviceId = deviceId, IsKeyExchange = kex, Ciphertext = key } },
                    null);
            }

            var message = new Im.Message(new Jid(bare), (string)null, null, null, MessageType.Chat);
            message.Data.Child(encrypted);
            message.Data.Child(Xml.Element("store", OmemoConstants.HintsNamespace));
            // Session management only: no carbon copies to our other devices (XEP-0280 §7).
            message.Data.Child(Xml.Element("private", CarbonsNamespace));
            message.Data.Child(Xml.Element("no-copy", OmemoConstants.HintsNamespace));
            transport.SendMessage(message);
        }

        #endregion

        #region Receiving (§5.6)

        public bool Input(Im.Presence stanza)
        {
            if (stanza?.From != null)
                TrackMucUser(stanza.Data, stanza.From, stanza.Type == PresenceType.Unavailable);
            return false;
        }

        public bool Input(Im.Message stanza)
        {
            if (stanza == null)
                return false;

            if (stanza.From != null)
                TrackMucUser(stanza.Data, stanza.From, false);

            if (!TryEnsureReady())
                return false;

            try
            {
                // Message Carbons (XEP-0280): only accept carbons from our own account.
                var carbon = stanza.Data["received", CarbonsNamespace] ?? stanza.Data["sent", CarbonsNamespace];
                if (carbon != null)
                {
                    if (stanza.From != null && !String.Equals(stanza.From.ToString(), OwnBareJid, StringComparison.OrdinalIgnoreCase))
                        return true;
                    var inner = carbon["forwarded", ForwardNamespace]?["message"];
                    return inner != null && ProcessElement(inner) == ProcessResult.Swallow;
                }

                // Message Archive Management (XEP-0313): decrypt the archived message in place.
                var result = stanza.Data["result"];
                if (result != null && result.NamespaceURI.StartsWith("urn:xmpp:mam:", StringComparison.Ordinal))
                {
                    var inner = result["forwarded", ForwardNamespace]?["message"];
                    if (inner != null)
                        ProcessElement(inner);
                    return false;
                }

                return ProcessElement(stanza.Data) == ProcessResult.Swallow;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error while processing an OMEMO message");
                return false;
            }
        }

        private ProcessResult ProcessElement(XmlElement message)
        {
            var v2 = message["encrypted", OmemoConstants.OmemoNamespace];
            var legacy = LegacyEnabled ? message["encrypted", OmemoConstants.LegacyNamespace] : null;
            if (v2 == null && legacy == null)
                return ProcessResult.NotOmemo;

            Jid from = ParseJid(message.GetAttribute("from"));
            bool group = message.GetAttribute("type") == "groupchat";

            var header = (v2 ?? legacy)["header", (v2 ?? legacy).NamespaceURI];
            int sid = 0;
            if (header == null || !Int32.TryParse(header.GetAttribute("sid"), out sid) || sid <= 0)
                return Fail(message, OmemoVersion.Omemo2, from, sid, OmemoReceiveFailure.InvalidEnvelope, null);

            string sender = group ? ResolveRealJid(from) : (from?.GetBareJid().ToString() ?? OwnBareJid);
            if (sender == null)
                return Fail(message, OmemoVersion.Omemo2, from, sid, OmemoReceiveFailure.NoSession, null);

            // Our own message (group chat reflection or archive copy): it was never encrypted for us.
            if (IsLocalDevice(sender, sid))
            {
                StripOmemo(message);
                return ProcessResult.Swallow;
            }

            var v2Key = v2 == null ? null : FindOwnKey(v2["header", OmemoConstants.OmemoNamespace]);
            if (v2Key != null)
                return ProcessV2(message, v2, v2Key, from, group, sender, sid);

            var legacyKey = legacy == null ? null : FindOwnLegacyKey(legacy["header", OmemoConstants.LegacyNamespace]);
            if (legacyKey != null)
                return ProcessLegacy(message, legacy, legacyKey, from, sender, sid);

            return Fail(message, v2 != null ? OmemoVersion.Omemo2 : OmemoVersion.Legacy, from, sid, OmemoReceiveFailure.NotEncryptedForThisDevice, null);
        }

        private ProcessResult ProcessV2(XmlElement message, XmlElement encrypted, XmlElement key, Jid from, bool group, string sender, int sid)
        {
            const OmemoVersion v = OmemoVersion.Omemo2;
            Jid to = ParseJid(message.GetAttribute("to"));
            bool kex = String.Equals(key.GetAttribute("kex"), "true", StringComparison.OrdinalIgnoreCase) || key.GetAttribute("kex") == "1";
            byte[] data = OmemoXml.FromBase64(key.InnerText);
            if (data == null)
                return Fail(message, v, from, sid, OmemoReceiveFailure.InvalidEnvelope, null);

            OmemoDecryptResult r;
            try
            {
                r = sessions.Decrypt(sender, sid, kex, data);
            }
            catch (Exception ex)
            {
                return Fail(message, v, from, sid, OmemoReceiveFailure.DecryptionFailed, ex);
            }

            if (r.Duplicate)
            {
                // Already decrypted once: ignore silently (§6).
                StripOmemo(message);
                return ProcessResult.Swallow;
            }

            if (r.NoSession)
            {
                // Build a session and tell the device about it (§6).
                RunInBackground("session repair", () => SendEmptyMessage(v, sender, sid));
                return Fail(message, v, from, sid, OmemoReceiveFailure.NoSession, null);
            }

            byte[] curve = CurveKey(v, r.RemoteIdentityKey);
            trust.RecordIdentityKey(v, sender, sid, curve);
            ScheduleFollowUps(v, sender, sid, r);

            var payload = encrypted["payload", OmemoConstants.OmemoNamespace];
            if (payload == null)
            {
                // Empty OMEMO message: only moves the session forward.
                StripOmemo(message);
                return ProcessResult.Swallow;
            }

            OmemoEnvelope envelope;
            try
            {
                OmemoPayloadCipher.ParseTransportPlaintext(r.Plaintext, out byte[] payloadKey, out byte[] payloadMac);
                byte[] cipher = OmemoXml.FromBase64(payload.InnerText) ?? throw new FormatException("Invalid payload.");
                envelope = OmemoXml.ParseEnvelope(OmemoPayloadCipher.Decrypt(cipher, payloadKey, payloadMac));
            }
            catch (Exception ex)
            {
                return Fail(message, v, from, sid, OmemoReceiveFailure.DecryptionFailed, ex);
            }

            // The affixes must match the stanza, so the server cannot re-route messages (§5.5.1).
            string expectedTo = group ? from?.GetBareJid().ToString() : to?.GetBareJid().ToString();
            if ((group && envelope.To == null) ||
                (envelope.To != null && expectedTo != null && !SameBareJid(envelope.To, expectedTo)) ||
                (envelope.From != null && !SameBareJid(envelope.From, sender)))
            {
                return Fail(message, v, from, sid, OmemoReceiveFailure.InvalidEnvelope, null);
            }

            HandleOptOut(sender, envelope);

            StripOmemo(message);
            foreach (var e in envelope.Content)
            {
                if (e.LocalName == "opt-out" && e.NamespaceURI == OmemoConstants.OmemoNamespace)
                    continue;
                message.AppendChild(message.OwnerDocument.ImportNode(e, true));
            }

            message.Child(OmemoMessageInfo.CreateElement(v, sid, trust.GetTrust(sender, curve), OmemoSessionManager.CurveFingerprint(curve), null));
            return ProcessResult.Continue;
        }

        private ProcessResult ProcessLegacy(XmlElement message, XmlElement encrypted, XmlElement key, Jid from, string sender, int sid)
        {
            const OmemoVersion v = OmemoVersion.Legacy;
            var header = encrypted["header", OmemoConstants.LegacyNamespace];
            bool preKey = String.Equals(key.GetAttribute("prekey"), "true", StringComparison.OrdinalIgnoreCase) || key.GetAttribute("prekey") == "1";
            byte[] data = OmemoXml.FromBase64(key.InnerText);
            byte[] iv = OmemoXml.FromBase64(header.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "iv")?.InnerText);
            if (data == null || iv == null)
                return Fail(message, v, from, sid, OmemoReceiveFailure.InvalidEnvelope, null);

            OmemoDecryptResult r;
            try
            {
                r = sessions.DecryptLegacy(sender, sid, preKey, data);
            }
            catch (Exception ex)
            {
                return Fail(message, v, from, sid, OmemoReceiveFailure.DecryptionFailed, ex);
            }

            if (r.Duplicate)
            {
                StripOmemo(message);
                return ProcessResult.Swallow;
            }

            if (r.NoSession)
            {
                RunInBackground("legacy session repair", () => SendEmptyMessage(v, sender, sid));
                return Fail(message, v, from, sid, OmemoReceiveFailure.NoSession, null);
            }

            trust.RecordIdentityKey(v, sender, sid, r.RemoteIdentityKey);
            ScheduleFollowUps(v, sender, sid, r);

            var payload = encrypted.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "payload");
            if (payload == null)
            {
                // Key transport message: only moves the session forward.
                StripOmemo(message);
                return ProcessResult.Swallow;
            }

            string body;
            try
            {
                byte[] cipher = OmemoXml.FromBase64(payload.InnerText) ?? throw new FormatException("Invalid payload.");
                byte[] keyMaterial = r.Plaintext;
                if (keyMaterial.Length == 16 && cipher.Length >= 16)
                {
                    // Old senders append the auth tag to the payload instead of the key.
                    keyMaterial = OmemoPrimitives.Concat(keyMaterial, OmemoPrimitives.Slice(cipher, cipher.Length - 16, 16));
                    cipher = OmemoPrimitives.Slice(cipher, 0, cipher.Length - 16);
                }
                // Conversations may pad the body with whitespace.
                body = Encoding.UTF8.GetString(OmemoLegacyPayloadCipher.Decrypt(keyMaterial, iv, cipher)).Trim();
            }
            catch (Exception ex)
            {
                return Fail(message, v, from, sid, OmemoReceiveFailure.DecryptionFailed, ex);
            }

            if (!String.Equals(sender, OwnBareJid, StringComparison.OrdinalIgnoreCase) && GetOptOutState(sender) != OptOutState.None)
                SetOptOutState(sender, OptOutState.None);

            StripOmemo(message);
            message.Child(Xml.Element("body").Text(body));
            message.Child(OmemoMessageInfo.CreateElement(v, sid, trust.GetTrust(sender, r.RemoteIdentityKey),
                OmemoSessionManager.CurveFingerprint(r.RemoteIdentityKey), null));
            return ProcessResult.Continue;
        }

        private void ScheduleFollowUps(OmemoVersion version, string sender, int sid, OmemoDecryptResult r)
        {
            if (r.BundleChanged)
                RunInBackground("bundle republish", RepublishBundles);

            // Complete the key exchange (§6) or forward the ratchet (heartbeat, §6).
            if (r.ViaKeyExchange || r.HeartbeatRequired)
            {
                byte[] ratchetKey = r.RatchetKey;
                RunInBackground("empty message", () =>
                {
                    SendEmptyMessage(version, sender, sid);
                    if (ratchetKey != null)
                        sessions.MarkHeartbeatSent(sender, sid, ratchetKey);
                });
            }

            // Unknown device: refresh the cached device list (§6).
            if (!store.GetDevices(DeviceStoreKey(version, sender)).Any(d => d.Id == sid))
                RunInBackground("device list refresh", () => FetchAndCacheDeviceList(version, sender));
        }

        private void HandleOptOut(string sender, OmemoEnvelope envelope)
        {
            if (String.Equals(sender, OwnBareJid, StringComparison.OrdinalIgnoreCase))
                return;

            var optOut = envelope.Content.FirstOrDefault(e => e.LocalName == "opt-out" && e.NamespaceURI == OmemoConstants.OmemoNamespace);
            if (optOut != null)
            {
                SetOptOutState(sender, OptOutState.Pending);
                OptOutReceived.Raise(this, new OmemoOptOutEventArgs(new Jid(sender), optOut["reason", OmemoConstants.OmemoNamespace]?.InnerText?.Trim()));
            }
            else if (GetOptOutState(sender) != OptOutState.None)
            {
                // The peer went back to OMEMO (§5.7).
                SetOptOutState(sender, OptOutState.None);
            }
        }

        private XmlElement FindOwnKey(XmlElement header)
        {
            if (header == null)
                return null;
            string own = OwnBareJid;
            int ownId = sessions.DeviceId;
            foreach (var keys in header.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "keys"))
            {
                if (!SameBareJid(keys.GetAttribute("jid"), own))
                    continue;
                foreach (var key in keys.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "key"))
                {
                    if (Int32.TryParse(key.GetAttribute("rid"), out int rid) && rid == ownId)
                        return key;
                }
            }
            return null;
        }

        /// <summary>Legacy keys are addressed by device id only.</summary>
        private XmlElement FindOwnLegacyKey(XmlElement header)
        {
            if (header == null)
                return null;
            int ownId = sessions.DeviceId;
            return header.ChildNodes.OfType<XmlElement>()
                .FirstOrDefault(k => k.LocalName == "key" && Int32.TryParse(k.GetAttribute("rid"), out int rid) && rid == ownId);
        }

        private ProcessResult Fail(XmlElement message, OmemoVersion version, Jid from, int sid, OmemoReceiveFailure failure, Exception ex)
        {
            logger.LogWarning(ex, "OMEMO message from {0}:{1} not decrypted: {2}", from, sid, failure);

            // An empty OMEMO message carries no content the user could miss.
            bool hasPayload = message.ChildNodes.OfType<XmlElement>()
                .Where(e => e.LocalName == "encrypted" && (e.NamespaceURI == OmemoConstants.OmemoNamespace || e.NamespaceURI == OmemoConstants.LegacyNamespace))
                .Any(e => e.ChildNodes.OfType<XmlElement>().Any(c => c.LocalName == "payload"));
            if (!hasPayload)
            {
                StripOmemo(message);
                return ProcessResult.Swallow;
            }

            DecryptionFailed.Raise(this, new OmemoDecryptionFailedEventArgs(from, sid, failure, ex));

            // Show a warning instead of the message (§5.6).
            StripOmemo(message);
            string text;
            switch (failure)
            {
                case OmemoReceiveFailure.NotEncryptedForThisDevice: text = WarningNotForThisDevice; break;
                case OmemoReceiveFailure.NoSession: text = WarningNoSession; break;
                case OmemoReceiveFailure.InvalidEnvelope: text = WarningInvalidEnvelope; break;
                default: text = WarningDecryptionFailed; break;
            }
            message.Child(Xml.Element("body").Text(text));
            message.Child(OmemoMessageInfo.CreateElement(version, sid, OmemoTrustLevel.Undecided, null, failure));
            return ProcessResult.Continue;
        }

        private static void StripOmemo(XmlElement message)
        {
            foreach (var e in message.ChildNodes.OfType<XmlElement>().ToList())
            {
                if ((e.LocalName == "encrypted" && (e.NamespaceURI == OmemoConstants.OmemoNamespace || e.NamespaceURI == OmemoConstants.LegacyNamespace)) ||
                    (e.LocalName == "encryption" && e.NamespaceURI == EmeNamespace) ||
                    e.LocalName == "body")
                {
                    message.RemoveChild(e);
                }
            }
        }

        #endregion

        #region Group chat membership (§5.8)

        private void TrackMucUser(XmlElement stanza, Jid from, bool unavailable)
        {
            var x = stanza["x", MucUserNamespace];
            if (x == null)
                return;
            string room = from.GetBareJid().ToString();
            foreach (var item in x.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "item"))
            {
                string realJid = item.GetAttribute("jid");
                if (String.IsNullOrEmpty(realJid))
                    continue;
                string realBare = ParseJid(realJid)?.GetBareJid().ToString();
                if (realBare == null)
                    continue;

                lock (syncRoot)
                {
                    if (from.Resource != null && !unavailable)
                        occupantRealJids[from.ToString()] = realBare;

                    string affiliation = item.GetAttribute("affiliation");
                    if (String.IsNullOrEmpty(affiliation))
                        continue;
                    if (!roomAffiliations.TryGetValue(room, out var members))
                        roomAffiliations[room] = members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (affiliation == "member" || affiliation == "admin" || affiliation == "owner")
                        members[realBare] = affiliation;
                    else
                        members.Remove(realBare);
                }
            }
        }

        private List<string> GetRoomMembers(string room)
        {
            bool fetched;
            lock (syncRoot)
                fetched = fetchedRoomAffiliations.Contains(room);

            if (!fetched)
            {
                // On join, the member, admin and owner lists are combined (§5.8.1).
                foreach (var affiliation in new[] { "member", "admin", "owner" })
                {
                    try
                    {
                        var query = Xml.Element("query", MucAdminNamespace).Child(Xml.Element("item").Attr("affiliation", affiliation));
                        Iq iq = transport.IqRequest(IqType.Get, new Jid(room), query, IqTimeout);
                        if (iq.Type == IqType.Error)
                            continue;
                        var items = iq.Data["query", MucAdminNamespace]?.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "item");
                        lock (syncRoot)
                        {
                            if (!roomAffiliations.TryGetValue(room, out var members))
                                roomAffiliations[room] = members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var item in items ?? Enumerable.Empty<XmlElement>())
                            {
                                string bare = ParseJid(item.GetAttribute("jid"))?.GetBareJid().ToString();
                                if (bare != null)
                                    members[bare] = affiliation;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not retrieve the {0} list of {1}", affiliation, room);
                    }
                }
                lock (syncRoot)
                    fetchedRoomAffiliations.Add(room);
            }

            lock (syncRoot)
                return roomAffiliations.TryGetValue(room, out var m) ? m.Keys.ToList() : new List<string>();
        }

        private string ResolveRealJid(Jid occupant)
        {
            if (occupant == null)
                return null;
            lock (syncRoot)
                return occupantRealJids.TryGetValue(occupant.ToString(), out string real) ? real : null;
        }

        #endregion

        #region Opt-out state (§5.7)

        private enum OptOutState : byte
        {
            None = 0,
            Pending = 1,
            PlaintextConfirmed = 2
        }

        private string OptOutKey(string bare) => "omemo2|" + OwnBareJid + "|optout|" + OmemoSessionManager.Normalize(bare);

        private OptOutState GetOptOutState(string bare)
        {
            byte[] v = store.GetValue(OptOutKey(bare));
            return v == null || v.Length == 0 ? OptOutState.None : (OptOutState)v[0];
        }

        private void SetOptOutState(string bare, OptOutState state) =>
            store.SetValue(OptOutKey(bare), state == OptOutState.None ? null : new[] { (byte)state });

        #endregion

        #region Helpers

        private enum ProcessResult
        {
            NotOmemo,
            Continue,
            Swallow
        }

        private string OwnBareJid => sessions.OwnBareJid;

        private bool IsLocalDevice(string bare, int deviceId) =>
            deviceId == sessions.DeviceId && String.Equals(bare, OwnBareJid, StringComparison.OrdinalIgnoreCase);

        private bool TryEnsureReady()
        {
            if (transport.Jid == null)
                return false;
            try
            {
                EnsureReadyOrThrow();
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OMEMO is not available");
                return false;
            }
        }

        private void EnsureReadyOrThrow()
        {
            if (transport.Jid == null)
                throw new InvalidOperationException("OMEMO requires an authenticated connection.");

            string own = OmemoSessionManager.Normalize(transport.Jid.GetBareJid().ToString());
            lock (syncRoot)
            {
                if (sessions != null && sessions.OwnBareJid == own)
                    return;
                store = store ?? OmemoRuntime.CreateStore();
                sessions = new OmemoSessionManager(store, own);
                trust = new OmemoTrustStore(store, own);
                fetchedDeviceLists.Clear();
            }
        }

        private void RunInBackground(string operation, Action action)
        {
            Task.Run(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "OMEMO {0} failed", operation);
                }
            });
        }

        private static byte[] ParseFingerprint(string fingerprint)
        {
            string hex = new string(fingerprint.Where(Uri.IsHexDigit).ToArray());
            if (hex.Length == 66 && hex.StartsWith("05", StringComparison.Ordinal))
                hex = hex.Substring(2);
            if (hex.Length != 64)
                return null;
            var result = new byte[32];
            for (int i = 0; i < 32; i++)
                result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return result;
        }

        private static bool SameBareJid(string a, string b)
        {
            var ja = ParseJid(a);
            var jb = ParseJid(b);
            return ja != null && jb != null &&
                String.Equals(ja.GetBareJid().ToString(), jb.GetBareJid().ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static Jid ParseJid(string value)
        {
            if (String.IsNullOrEmpty(value))
                return null;
            try
            {
                return new Jid(value);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        #endregion
    }
}
