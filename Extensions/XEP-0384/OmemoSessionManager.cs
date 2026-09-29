using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Owns the local OMEMO identity and all Double Ratchet sessions of one account and performs
    /// X3DH key agreement (XEP-0384 §4). All state is persisted through <see cref="IOmemoStore"/>.
    /// </summary>
    internal sealed class OmemoSessionManager
    {
        public const int PreKeyTarget = 100;
        public const int PreKeyMinimum = 25;
        public const int HeartbeatThreshold = 53;
        public static readonly TimeSpan SignedPreKeyRotation = TimeSpan.FromDays(7);

        private const int MaxRetiredPreKeys = 100;
        private static readonly byte[] x3dhInfo = Encoding.ASCII.GetBytes("OMEMO X3DH");

        private readonly object syncRoot = new object();
        private readonly IOmemoStore store;
        private readonly string keyPrefix;
        private OmemoIdentityState identity;

        public OmemoSessionManager(IOmemoStore store, string ownBareJid)
        {
            store.ThrowIfNull("store");
            ownBareJid.ThrowIfNullOrEmpty("ownBareJid");

            this.store = store;
            OwnBareJid = Normalize(ownBareJid);
            keyPrefix = "omemo2|" + OwnBareJid + "|";
            LoadOrCreateIdentity();
        }

        public string OwnBareJid { get; }

        public int DeviceId { get { lock (syncRoot) return identity.DeviceId; } }

        /// <summary>The public IdentityKey in Ed25519 form.</summary>
        public byte[] IdentityKey { get { lock (syncRoot) return identity.IdentityPublic.ToArray(); } }

        public bool DeviceIdAnnounced { get { lock (syncRoot) return identity.DeviceIdAnnounced; } }

        /// <summary>
        /// Picks a new random device id, e.g. after a collision with an existing device (§6).
        /// Only valid before the device id was announced.
        /// </summary>
        public void RegenerateDeviceId(ICollection<int> taken)
        {
            lock (syncRoot)
            {
                int id;
                do
                    id = OmemoPrimitives.RandomPositiveInt32();
                while (taken != null && taken.Contains(id));
                identity.DeviceId = id;
                SaveIdentity();
            }
        }

        public void MarkDeviceIdAnnounced()
        {
            lock (syncRoot)
            {
                if (identity.DeviceIdAnnounced)
                    return;
                identity.DeviceIdAnnounced = true;
                SaveIdentity();
            }
        }

        /// <summary>
        /// Rotates the signed PreKey when it is older than the rotation period and tops up the
        /// PreKeys. Returns true if the bundle changed and needs to be republished.
        /// </summary>
        public bool MaintainKeys(DateTime utcNow)
        {
            lock (syncRoot)
            {
                bool changed = false;
                var current = identity.CurrentSignedPreKey;
                if (current == null || utcNow - current.CreatedUtc >= SignedPreKeyRotation)
                {
                    AddSignedPreKey(utcNow);
                    // Keep the previous signed PreKey for one more period for delayed messages.
                    while (identity.SignedPreKeys.Count > 2)
                        identity.SignedPreKeys.RemoveAt(0);
                    changed = true;
                }

                if (identity.PreKeys.Count < PreKeyTarget)
                {
                    FillPreKeys();
                    changed = true;
                }

                if (changed)
                    SaveIdentity();
                return changed;
            }
        }

        public OmemoBundle GetBundle()
        {
            lock (syncRoot)
            {
                var spk = identity.CurrentSignedPreKey;
                return new OmemoBundle
                {
                    SignedPreKeyId = spk.Id,
                    SignedPreKeyPublic = spk.PublicKey.ToArray(),
                    SignedPreKeySignature = spk.Signature.ToArray(),
                    IdentityKeyPublic = identity.IdentityPublic.ToArray(),
                    PreKeys = identity.PreKeys.Values.ToDictionary(p => p.Id, p => p.PublicKey.ToArray())
                };
            }
        }

        /// <summary>Signs data (e.g. a device label) with the IdentityKey.</summary>
        public byte[] Sign(byte[] data)
        {
            lock (syncRoot)
                return OmemoPrimitives.Ed25519Sign(identity.IdentitySeed, data);
        }

        public bool HasSession(string bareJid, int deviceId) => LoadSession(bareJid, deviceId) != null;

        public byte[] GetSessionIdentityKey(string bareJid, int deviceId) => LoadSession(bareJid, deviceId)?.RemoteIdentityKey;

        public void DeleteSession(string bareJid, int deviceId)
        {
            lock (syncRoot)
                store.SetValue(SessionKey(bareJid, deviceId), null);
        }

        public IEnumerable<int> GetSessionDeviceIds(string bareJid)
        {
            string prefix = keyPrefix + "session|" + Normalize(bareJid) + "|";
            foreach (var key in store.GetKeys(prefix))
            {
                if (Int32.TryParse(key.Substring(prefix.Length), out int id))
                    yield return id;
            }
        }

        /// <summary>
        /// Actively builds a session from a bundle using X3DH (§4.2, §5.4).
        /// </summary>
        /// <exception cref="CryptographicException">The bundle is invalid or its signature does
        /// not verify.</exception>
        public void BuildSession(string bareJid, int deviceId, OmemoBundle bundle)
        {
            bundle.ThrowIfNull("bundle");
            if (bundle.IdentityKeyPublic == null || bundle.IdentityKeyPublic.Length != OmemoPrimitives.KeySize)
                throw new CryptographicException("Bundle has an invalid IdentityKey.");
            if (bundle.SignedPreKeyPublic == null || bundle.SignedPreKeyPublic.Length != OmemoPrimitives.KeySize)
                throw new CryptographicException("Bundle has an invalid signed PreKey.");
            if (!OmemoPrimitives.Ed25519Verify(bundle.IdentityKeyPublic, bundle.SignedPreKeyPublic, bundle.SignedPreKeySignature))
                throw new CryptographicException("Bundle signed PreKey signature does not verify.");

            var preKeys = (bundle.PreKeys ?? new Dictionary<int, byte[]>())
                .Where(p => p.Key > 0 && p.Value != null && p.Value.Length == OmemoPrimitives.KeySize)
                .ToList();
            if (preKeys.Count == 0)
                throw new CryptographicException("Bundle contains no usable PreKeys.");

            // A random PreKey is selected (§5.4).
            var preKey = preKeys[RandomIndex(preKeys.Count)];

            lock (syncRoot)
            {
                OmemoPrimitives.GenerateX25519KeyPair(out byte[] ekPrivate, out byte[] ekPublic);
                byte[] ikPrivate = OmemoPrimitives.Ed25519SeedToX25519Private(identity.IdentitySeed);
                byte[] remoteIk = OmemoPrimitives.Ed25519PublicToX25519(bundle.IdentityKeyPublic);

                byte[] sk = DeriveSharedSecret(
                    OmemoPrimitives.X25519Agreement(ikPrivate, bundle.SignedPreKeyPublic),
                    OmemoPrimitives.X25519Agreement(ekPrivate, remoteIk),
                    OmemoPrimitives.X25519Agreement(ekPrivate, bundle.SignedPreKeyPublic),
                    OmemoPrimitives.X25519Agreement(ekPrivate, preKey.Value));

                byte[] ad = OmemoPrimitives.Concat(identity.IdentityPublic, bundle.IdentityKeyPublic);

                var session = new OmemoSession
                {
                    Ratchet = OmemoDoubleRatchet.InitializeActive(sk, ad, bundle.SignedPreKeyPublic),
                    RemoteIdentityKey = bundle.IdentityKeyPublic.ToArray(),
                    EphemeralKey = ekPublic,
                    Initiator = true,
                    KeyExchangePending = true,
                    PendingPreKeyId = (uint)preKey.Key,
                    PendingSignedPreKeyId = (uint)bundle.SignedPreKeyId
                };
                SaveSession(bareJid, deviceId, session);
            }
        }

        /// <summary>
        /// Encrypts key material (48 bytes, or 32 zero bytes for empty messages) for one device.
        /// </summary>
        /// <returns>The serialized OMEMOKeyExchange or OMEMOAuthenticatedMessage.</returns>
        public byte[] Encrypt(string bareJid, int deviceId, byte[] plaintext, out bool isKeyExchange)
        {
            plaintext.ThrowIfNull("plaintext");
            lock (syncRoot)
            {
                var session = LoadSession(bareJid, deviceId) ??
                    throw new InvalidOperationException("No OMEMO session with " + bareJid + ":" + deviceId + ".");

                var message = session.Ratchet.Encrypt(plaintext);
                byte[] result;
                isKeyExchange = session.KeyExchangePending;
                if (isKeyExchange)
                {
                    // Repeat the X3DH header until the peer confirms the session (§4.3).
                    result = new OmemoKeyExchangeProto
                    {
                        PkId = session.PendingPreKeyId,
                        SpkId = session.PendingSignedPreKeyId,
                        Ik = identity.IdentityPublic,
                        Ek = session.EphemeralKey,
                        Message = message
                    }.Serialize();
                }
                else
                {
                    result = message.Serialize();
                }

                SaveSession(bareJid, deviceId, session);
                return result;
            }
        }

        /// <summary>
        /// Decrypts a &lt;key&gt; element addressed to this device (§5.6).
        /// </summary>
        public OmemoDecryptResult Decrypt(string bareJid, int deviceId, bool isKeyExchange, byte[] data)
        {
            data.ThrowIfNull("data");
            lock (syncRoot)
            {
                return isKeyExchange
                    ? DecryptKeyExchange(bareJid, deviceId, OmemoKeyExchangeProto.Parse(data))
                    : DecryptAuthenticated(bareJid, deviceId, OmemoAuthenticatedMessageProto.Parse(data));
            }
        }

        /// <summary>Records that a heartbeat was sent for the current remote ratchet key.</summary>
        public void MarkHeartbeatSent(string bareJid, int deviceId, byte[] ratchetKey)
        {
            lock (syncRoot)
            {
                var session = LoadSession(bareJid, deviceId);
                if (session == null)
                    return;
                session.HeartbeatRatchetKey = ratchetKey;
                SaveSession(bareJid, deviceId, session);
            }
        }

        /// <summary>
        /// Computes the fingerprint: the IdentityKey in its Curve25519 form as lowercase hex (§8).
        /// </summary>
        public static string Fingerprint(byte[] ed25519IdentityKey)
        {
            if (ed25519IdentityKey == null)
                return null;
            byte[] curve = OmemoPrimitives.Ed25519PublicToX25519(ed25519IdentityKey);
            var sb = new StringBuilder(curve.Length * 2);
            foreach (byte b in curve)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        #region Legacy OMEMO (eu.siacs.conversations.axolotl)

        /// <summary>The IdentityKey in Curve25519 form, as used by legacy OMEMO.</summary>
        public byte[] LegacyIdentityKey { get { lock (syncRoot) return OmemoPrimitives.Ed25519PublicToX25519(identity.IdentityPublic); } }

        /// <summary>
        /// The legacy bundle: the same signed PreKey and PreKeys, with the signature over the
        /// serialized (0x05-prefixed) signed PreKey and a Curve25519 IdentityKey. Keys are 32 bytes
        /// here and gain the type byte when serialized to XML.
        /// </summary>
        public OmemoBundle GetLegacyBundle()
        {
            lock (syncRoot)
            {
                var spk = identity.CurrentSignedPreKey;
                return new OmemoBundle
                {
                    SignedPreKeyId = spk.Id,
                    SignedPreKeyPublic = spk.PublicKey.ToArray(),
                    SignedPreKeySignature = OmemoPrimitives.Ed25519Sign(identity.IdentitySeed, OmemoLegacySession.Encode(spk.PublicKey)),
                    IdentityKeyPublic = OmemoPrimitives.Ed25519PublicToX25519(identity.IdentityPublic),
                    PreKeys = identity.PreKeys.Values.ToDictionary(p => p.Id, p => p.PublicKey.ToArray())
                };
            }
        }

        public bool HasLegacySession(string bareJid, int deviceId) => LoadLegacySession(bareJid, deviceId) != null;

        public byte[] GetLegacySessionIdentityKey(string bareJid, int deviceId) => LoadLegacySession(bareJid, deviceId)?.RemoteIdentityKey;

        public void DeleteLegacySession(string bareJid, int deviceId)
        {
            lock (syncRoot)
                store.SetValue(LegacySessionKey(bareJid, deviceId), null);
        }

        public IEnumerable<int> GetLegacySessionDeviceIds(string bareJid)
        {
            string prefix = keyPrefix + "legacy-session|" + Normalize(bareJid) + "|";
            foreach (var key in store.GetKeys(prefix))
            {
                if (Int32.TryParse(key.Substring(prefix.Length), out int id))
                    yield return id;
            }
        }

        /// <summary>Builds a libsignal session from a legacy bundle (SessionBuilder.process).</summary>
        /// <exception cref="CryptographicException">The bundle is invalid.</exception>
        public void BuildLegacySession(string bareJid, int deviceId, OmemoBundle bundle)
        {
            bundle.ThrowIfNull("bundle");
            if (bundle.IdentityKeyPublic == null || bundle.IdentityKeyPublic.Length != OmemoPrimitives.KeySize ||
                bundle.SignedPreKeyPublic == null || bundle.SignedPreKeyPublic.Length != OmemoPrimitives.KeySize)
                throw new CryptographicException("Legacy bundle is incomplete.");
            if (!OmemoPrimitives.Curve25519Verify(bundle.IdentityKeyPublic, OmemoLegacySession.Encode(bundle.SignedPreKeyPublic), bundle.SignedPreKeySignature))
                throw new CryptographicException("Legacy bundle signed PreKey signature does not verify.");

            var preKeys = (bundle.PreKeys ?? new Dictionary<int, byte[]>())
                .Where(p => p.Key > 0 && p.Value != null && p.Value.Length == OmemoPrimitives.KeySize)
                .ToList();
            if (preKeys.Count == 0)
                throw new CryptographicException("Legacy bundle contains no usable PreKeys.");
            var preKey = preKeys[RandomIndex(preKeys.Count)];

            lock (syncRoot)
            {
                var session = OmemoLegacySession.InitializeActive(
                    OmemoPrimitives.Ed25519SeedToX25519Private(identity.IdentitySeed),
                    OmemoPrimitives.Ed25519PublicToX25519(identity.IdentityPublic),
                    bundle.IdentityKeyPublic, bundle.SignedPreKeyPublic, preKey.Value, (uint)preKey.Key, (uint)bundle.SignedPreKeyId);
                SaveLegacySession(bareJid, deviceId, session);
            }
        }

        public byte[] EncryptLegacy(string bareJid, int deviceId, byte[] plaintext, out bool isPreKey)
        {
            plaintext.ThrowIfNull("plaintext");
            lock (syncRoot)
            {
                var session = LoadLegacySession(bareJid, deviceId) ??
                    throw new InvalidOperationException("No legacy OMEMO session with " + bareJid + ":" + deviceId + ".");
                byte[] result = session.Encrypt(plaintext, (uint)identity.RegistrationId, out isPreKey);
                SaveLegacySession(bareJid, deviceId, session);
                return result;
            }
        }

        /// <summary>Decrypts a legacy &lt;key&gt; element (SessionCipher.decrypt).</summary>
        public OmemoDecryptResult DecryptLegacy(string bareJid, int deviceId, bool isPreKey, byte[] data)
        {
            data.ThrowIfNull("data");
            lock (syncRoot)
            {
                var existing = LoadLegacySession(bareJid, deviceId);
                if (!isPreKey)
                {
                    if (existing == null)
                        return new OmemoDecryptResult { NoSession = true };
                    return DecryptLegacyWith(bareJid, deviceId, existing, data, false);
                }

                var message = OmemoLegacySession.PreKeySignalMessage.Parse(data);

                // Already set up from this PreKey message: only process the bundled message.
                if (existing != null && !existing.Initiator && existing.AliceBaseKey != null &&
                    OmemoPrimitives.FixedTimeEquals(existing.AliceBaseKey, message.BaseKey))
                {
                    return DecryptLegacyWith(bareJid, deviceId, existing, message.Message, true);
                }

                var spk = identity.SignedPreKeys.FirstOrDefault(k => (uint)k.Id == message.SignedPreKeyId) ??
                    throw new CryptographicException("PreKey message references an unknown signed PreKey.");

                OmemoIdentityState.PreKey preKey = null;
                bool fromBundle = false;
                if (message.PreKeyId != null)
                {
                    fromBundle = identity.PreKeys.TryGetValue((int)message.PreKeyId.Value, out preKey);
                    if (!fromBundle)
                        preKey = identity.RetiredPreKeys.FirstOrDefault(p => (uint)p.Id == message.PreKeyId.Value);
                    if (preKey == null)
                        throw new CryptographicException("PreKey message references an unknown PreKey.");
                }

                var session = OmemoLegacySession.InitializePassive(
                    OmemoPrimitives.Ed25519SeedToX25519Private(identity.IdentitySeed),
                    OmemoPrimitives.Ed25519PublicToX25519(identity.IdentityPublic),
                    spk.PrivateKey, spk.PublicKey, preKey?.PrivateKey, message.IdentityKey, message.BaseKey);
                session.ConsumedPreKeyId = preKey?.Id ?? 0;

                var result = DecryptLegacyWith(bareJid, deviceId, session, message.Message, true);

                if (fromBundle)
                {
                    identity.PreKeys.Remove(preKey.Id);
                    identity.RetiredPreKeys.Add(preKey);
                    while (identity.RetiredPreKeys.Count > MaxRetiredPreKeys)
                        identity.RetiredPreKeys.RemoveAt(0);
                    FillPreKeys();
                    SaveIdentity();
                    result.BundleChanged = true;
                }
                result.NewSession = true;
                result.ReplacedSession = existing != null;
                return result;
            }
        }

        private OmemoDecryptResult DecryptLegacyWith(string bareJid, int deviceId, OmemoLegacySession session, byte[] signalMessage, bool viaPreKey)
        {
            byte[] plaintext;
            try
            {
                plaintext = session.Decrypt(signalMessage);
            }
            catch (OmemoDuplicateMessageException)
            {
                return new OmemoDecryptResult { Duplicate = true, RemoteIdentityKey = session.RemoteIdentityKey };
            }

            if (!viaPreKey && session.ConsumedPreKeyId != 0)
            {
                identity.RetiredPreKeys.RemoveAll(p => p.Id == session.ConsumedPreKeyId);
                session.ConsumedPreKeyId = 0;
                SaveIdentity();
            }

            SaveLegacySession(bareJid, deviceId, session);
            return new OmemoDecryptResult
            {
                Plaintext = plaintext,
                RemoteIdentityKey = session.RemoteIdentityKey,
                ViaKeyExchange = viaPreKey
            };
        }

        private OmemoLegacySession LoadLegacySession(string bareJid, int deviceId)
        {
            byte[] data = store.GetValue(LegacySessionKey(bareJid, deviceId));
            return data == null ? null : OmemoLegacySession.Deserialize(data);
        }

        private void SaveLegacySession(string bareJid, int deviceId, OmemoLegacySession session) =>
            store.SetValue(LegacySessionKey(bareJid, deviceId), session.Serialize());

        private string LegacySessionKey(string bareJid, int deviceId) => keyPrefix + "legacy-session|" + Normalize(bareJid) + "|" + deviceId;

        #endregion

        /// <summary>Lowercase hex of a Curve25519 key (the fingerprint form, §8).</summary>
        public static string CurveFingerprint(byte[] curveKey)
        {
            if (curveKey == null)
                return null;
            var sb = new StringBuilder(curveKey.Length * 2);
            foreach (byte b in curveKey)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        internal static string Normalize(string bareJid) => (bareJid ?? string.Empty).Trim().ToLowerInvariant();

        private OmemoDecryptResult DecryptKeyExchange(string bareJid, int deviceId, OmemoKeyExchangeProto kex)
        {
            if (kex.Ik.Length != OmemoPrimitives.KeySize || kex.Ek.Length != OmemoPrimitives.KeySize)
                throw new CryptographicException("Key exchange contains invalid keys.");

            // A repeated key exchange for an existing session only carries a new message (§4.3).
            var existing = LoadSession(bareJid, deviceId);
            if (existing != null && !existing.Initiator && existing.EphemeralKey != null &&
                OmemoPrimitives.FixedTimeEquals(existing.EphemeralKey, kex.Ek) &&
                OmemoPrimitives.FixedTimeEquals(existing.RemoteIdentityKey, kex.Ik))
            {
                return DecryptWithSession(bareJid, deviceId, existing, kex.Message, true);
            }

            var spk = identity.SignedPreKeys.FirstOrDefault(s => (uint)s.Id == kex.SpkId) ??
                throw new CryptographicException("Key exchange references an unknown signed PreKey.");

            // Key exchanges without a (known) PreKey MUST be rejected (§4.2).
            bool fromBundle = identity.PreKeys.TryGetValue((int)kex.PkId, out var preKey);
            if (!fromBundle)
                preKey = identity.RetiredPreKeys.FirstOrDefault(p => (uint)p.Id == kex.PkId);
            if (kex.PkId == 0 || preKey == null)
                throw new CryptographicException("Key exchange references an unknown PreKey.");

            byte[] ikPrivate = OmemoPrimitives.Ed25519SeedToX25519Private(identity.IdentitySeed);
            byte[] remoteIk = OmemoPrimitives.Ed25519PublicToX25519(kex.Ik);

            byte[] sk = DeriveSharedSecret(
                OmemoPrimitives.X25519Agreement(spk.PrivateKey, remoteIk),
                OmemoPrimitives.X25519Agreement(ikPrivate, kex.Ek),
                OmemoPrimitives.X25519Agreement(spk.PrivateKey, kex.Ek),
                OmemoPrimitives.X25519Agreement(preKey.PrivateKey, kex.Ek));

            byte[] ad = OmemoPrimitives.Concat(kex.Ik, identity.IdentityPublic);

            var session = new OmemoSession
            {
                Ratchet = OmemoDoubleRatchet.InitializePassive(sk, ad, spk.PrivateKey, spk.PublicKey),
                RemoteIdentityKey = kex.Ik.ToArray(),
                EphemeralKey = kex.Ek.ToArray(),
                Initiator = false,
                ConsumedPreKeyId = preKey.Id
            };

            // Throws on failure, before any state is replaced.
            var result = DecryptWithSession(bareJid, deviceId, session, kex.Message, true);

            if (fromBundle)
            {
                identity.PreKeys.Remove(preKey.Id);
                identity.RetiredPreKeys.Add(preKey);
                while (identity.RetiredPreKeys.Count > MaxRetiredPreKeys)
                    identity.RetiredPreKeys.RemoveAt(0);
                FillPreKeys();
                SaveIdentity();
                result.BundleChanged = true;
            }

            result.NewSession = true;
            result.ReplacedSession = existing != null;
            return result;
        }

        private OmemoDecryptResult DecryptAuthenticated(string bareJid, int deviceId, OmemoAuthenticatedMessageProto message)
        {
            var session = LoadSession(bareJid, deviceId);
            if (session == null)
                return new OmemoDecryptResult { NoSession = true };
            return DecryptWithSession(bareJid, deviceId, session, message, false);
        }

        private OmemoDecryptResult DecryptWithSession(string bareJid, int deviceId, OmemoSession session, OmemoAuthenticatedMessageProto message, bool viaKeyExchange)
        {
            byte[] plaintext;
            OmemoMessageProto header;
            bool usedSkipped;
            try
            {
                plaintext = session.Ratchet.Decrypt(message, out header, out usedSkipped);
            }
            catch (CryptographicException) when (IsDuplicate(session, message))
            {
                return new OmemoDecryptResult { Duplicate = true, RemoteIdentityKey = session.RemoteIdentityKey };
            }

            var result = new OmemoDecryptResult
            {
                Plaintext = plaintext,
                RemoteIdentityKey = session.RemoteIdentityKey,
                ViaKeyExchange = viaKeyExchange
            };

            // A message received through this session confirms our key exchange (§4.3).
            if (session.Initiator && session.KeyExchangePending)
                session.KeyExchangePending = false;

            // A non-key-exchange message proves the peer finished the key exchange, so the
            // PreKey that session was built from is no longer needed (§5.6).
            if (!viaKeyExchange && session.ConsumedPreKeyId != 0)
            {
                identity.RetiredPreKeys.RemoveAll(p => p.Id == session.ConsumedPreKeyId);
                session.ConsumedPreKeyId = 0;
                SaveIdentity();
            }

            if (!usedSkipped && header.N >= HeartbeatThreshold &&
                (session.HeartbeatRatchetKey == null || !OmemoPrimitives.FixedTimeEquals(session.HeartbeatRatchetKey, header.DhPub)))
            {
                result.HeartbeatRequired = true;
                result.RatchetKey = header.DhPub;
            }

            SaveSession(bareJid, deviceId, session);
            return result;
        }

        private static bool IsDuplicate(OmemoSession session, OmemoAuthenticatedMessageProto message)
        {
            try
            {
                var header = OmemoMessageProto.Parse(message.Message);
                return session.Ratchet.RemoteRatchetKey != null &&
                    OmemoPrimitives.FixedTimeEquals(header.DhPub, session.Ratchet.RemoteRatchetKey) &&
                    session.Ratchet.IsPastMessage(header.N);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static byte[] DeriveSharedSecret(params byte[][] dh)
        {
            // X3DH KDF: F = 32 0xFF bytes for X25519, zero salt, info "OMEMO X3DH".
            var f = Enumerable.Repeat((byte)0xFF, 32).ToArray();
            var ikm = OmemoPrimitives.Concat(new[] { f }.Concat(dh).ToArray());
            return OmemoPrimitives.HkdfSha256(ikm, new byte[32], x3dhInfo, 32);
        }

        private void LoadOrCreateIdentity()
        {
            byte[] data = store.GetValue(keyPrefix + "identity");
            if (data != null)
            {
                identity = OmemoIdentityState.Deserialize(data);
                if ((identity.IdentityPublic[31] & 0x80) == 0)
                {
                    if (identity.RegistrationId == 0)
                    {
                        identity.RegistrationId = NewRegistrationId();
                        SaveIdentity();
                    }
                    return;
                }
                // Keys created before legacy OMEMO support may not verify under libsignal:
                // keep the device id and replace the key material.
            }

            OmemoPrimitives.GenerateXeddsaCompatibleIdentity(out byte[] seed, out byte[] pub);
            identity = new OmemoIdentityState
            {
                DeviceId = identity?.DeviceId ?? OmemoPrimitives.RandomPositiveInt32(),
                DeviceIdAnnounced = identity?.DeviceIdAnnounced ?? false,
                RegistrationId = NewRegistrationId(),
                IdentitySeed = seed,
                IdentityPublic = pub
            };
            foreach (var key in store.GetKeys(keyPrefix + "session|").Concat(store.GetKeys(keyPrefix + "legacy-session|")).ToList())
                store.SetValue(key, null);
            AddSignedPreKey(DateTime.UtcNow);
            FillPreKeys();
            SaveIdentity();
        }

        private static int NewRegistrationId() => (int)(BitConverter.ToUInt32(OmemoPrimitives.RandomBytes(4), 0) % 16380) + 1;

        private void AddSignedPreKey(DateTime utcNow)
        {
            OmemoPrimitives.GenerateX25519KeyPair(out byte[] priv, out byte[] pub);
            int id;
            do
                id = OmemoPrimitives.RandomPositiveInt32();
            while (identity.SignedPreKeys.Any(s => s.Id == id));

            identity.SignedPreKeys.Add(new OmemoIdentityState.SignedPreKey
            {
                Id = id,
                PrivateKey = priv,
                PublicKey = pub,
                Signature = OmemoPrimitives.Ed25519Sign(identity.IdentitySeed, pub),
                CreatedUtc = utcNow
            });
        }

        private void FillPreKeys()
        {
            while (identity.PreKeys.Count < PreKeyTarget)
            {
                int id;
                do
                    id = OmemoPrimitives.RandomPositiveInt32();
                while (identity.PreKeys.ContainsKey(id) || identity.RetiredPreKeys.Any(p => p.Id == id));

                OmemoPrimitives.GenerateX25519KeyPair(out byte[] priv, out byte[] pub);
                identity.PreKeys[id] = new OmemoIdentityState.PreKey { Id = id, PrivateKey = priv, PublicKey = pub };
            }
        }

        private void SaveIdentity() => store.SetValue(keyPrefix + "identity", identity.Serialize());

        private OmemoSession LoadSession(string bareJid, int deviceId)
        {
            byte[] data = store.GetValue(SessionKey(bareJid, deviceId));
            return data == null ? null : OmemoSession.Deserialize(data);
        }

        private void SaveSession(string bareJid, int deviceId, OmemoSession session) =>
            store.SetValue(SessionKey(bareJid, deviceId), session.Serialize());

        private string SessionKey(string bareJid, int deviceId) => keyPrefix + "session|" + Normalize(bareJid) + "|" + deviceId;

        private static int RandomIndex(int count) =>
            (int)(BitConverter.ToUInt32(OmemoPrimitives.RandomBytes(4), 0) % (uint)count);
    }

    internal sealed class OmemoDecryptResult
    {
        /// <summary>The decrypted key material (48 bytes, or 32 bytes for empty messages).</summary>
        public byte[] Plaintext { get; set; }

        public byte[] RemoteIdentityKey { get; set; }

        /// <summary>A new session was built from an OMEMOKeyExchange.</summary>
        public bool NewSession { get; set; }

        /// <summary>The message was an OMEMOKeyExchange; the peer waits for a response (§4.3).</summary>
        public bool ViaKeyExchange { get; set; }

        /// <summary>The new session replaced an existing one.</summary>
        public bool ReplacedSession { get; set; }

        /// <summary>A PreKey was consumed; the bundle must be republished (§5.6).</summary>
        public bool BundleChanged { get; set; }

        /// <summary>The counter reached 53 on a new ratchet key; a heartbeat must be sent (§6).</summary>
        public bool HeartbeatRequired { get; set; }

        public byte[] RatchetKey { get; set; }

        /// <summary>An OMEMOAuthenticatedMessage arrived but no session exists.</summary>
        public bool NoSession { get; set; }

        /// <summary>The message was already decrypted before; the failure must be ignored (§6).</summary>
        public bool Duplicate { get; set; }
    }
}
