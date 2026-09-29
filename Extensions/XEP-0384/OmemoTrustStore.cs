using System;
using System.Linq;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Persists trust decisions per (bare JID, IdentityKey), with the IdentityKey in its Curve25519
    /// form, so one decision covers a device in both OMEMO versions. Also remembers which
    /// IdentityKey each device id announced, per version.
    /// </summary>
    internal sealed class OmemoTrustStore
    {
        private readonly IOmemoStore store;
        private readonly string keyPrefix;

        public OmemoTrustStore(IOmemoStore store, string ownBareJid)
        {
            this.store = store;
            keyPrefix = "omemo2|" + OmemoSessionManager.Normalize(ownBareJid) + "|";
        }

        /// <summary>Returns the Curve25519 IdentityKey last seen for the device, or null.</summary>
        public byte[] GetIdentityKey(OmemoVersion version, string bareJid, int deviceId) =>
            store.GetValue(IdentityKeyKey(version, bareJid, deviceId));

        public void RecordIdentityKey(OmemoVersion version, string bareJid, int deviceId, byte[] curveKey)
        {
            if (curveKey == null)
                return;
            store.SetValue(IdentityKeyKey(version, bareJid, deviceId), curveKey);
        }

        public OmemoTrustLevel GetTrust(string bareJid, byte[] curveKey)
        {
            if (curveKey == null)
                return OmemoTrustLevel.Undecided;
            byte[] v = store.GetValue(TrustKey(bareJid, curveKey));
            return v == null || v.Length == 0 ? OmemoTrustLevel.Undecided : (OmemoTrustLevel)v[0];
        }

        public void SetTrust(string bareJid, byte[] curveKey, OmemoTrustLevel level)
        {
            curveKey.ThrowIfNull("curveKey");
            store.SetValue(TrustKey(bareJid, curveKey), level == OmemoTrustLevel.Undecided ? null : new[] { (byte)level });
        }

        private string TrustKey(string bareJid, byte[] curveKey) =>
            keyPrefix + "trust|" + OmemoSessionManager.Normalize(bareJid) + "|" + OmemoSessionManager.CurveFingerprint(curveKey);

        private string IdentityKeyKey(OmemoVersion version, string bareJid, int deviceId) =>
            keyPrefix + "ik|" + (version == OmemoVersion.Legacy ? "legacy" : "v2") + "|" + OmemoSessionManager.Normalize(bareJid) + "|" + deviceId;
    }
}
