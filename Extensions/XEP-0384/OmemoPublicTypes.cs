using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// The OMEMO protocol version a device or message uses.
    /// </summary>
    public enum OmemoVersion
    {
        /// <summary>OMEMO 0.9 (urn:xmpp:omemo:2).</summary>
        Omemo2,
        /// <summary>Legacy OMEMO 0.3 (eu.siacs.conversations.axolotl), e.g. Conversations.</summary>
        Legacy
    }

    /// <summary>
    /// Trust decision for an OMEMO device's IdentityKey (XEP-0384 §8). Decisions are stored per
    /// fingerprint, so they apply to a device in both OMEMO versions.
    /// </summary>
    public enum OmemoTrustLevel
    {
        /// <summary>No decision yet. Messages are not encrypted for the device.</summary>
        Undecided,
        /// <summary>The user accepted the key without comparing fingerprints.</summary>
        Trusted,
        /// <summary>The user compared and confirmed the fingerprint.</summary>
        Verified,
        /// <summary>The user rejected the key. Messages are never encrypted for the device.</summary>
        Distrusted
    }

    /// <summary>
    /// Describes an OMEMO device of a contact or of the own account.
    /// </summary>
    public sealed class OmemoDeviceInfo
    {
        /// <summary>The bare JID that owns the device.</summary>
        public Jid Jid { get; internal set; }

        /// <summary>The OMEMO device id.</summary>
        public int DeviceId { get; internal set; }

        /// <summary>The OMEMO version the device announced itself with.</summary>
        public OmemoVersion Version { get; internal set; }

        /// <summary>The device label, only set when its signature verified (§5.3.1).</summary>
        public string Label { get; internal set; }

        /// <summary>
        /// The fingerprint (IdentityKey in Curve25519 form, lowercase hex), or null while the
        /// device's IdentityKey is not known yet.
        /// </summary>
        public string Fingerprint { get; internal set; }

        /// <summary>The fingerprint split into 8 groups of 8 characters, for display (§8).</summary>
        public string FormattedFingerprint => OmemoDeviceInfo.FormatFingerprint(Fingerprint);

        /// <summary>The trust decision for the device's IdentityKey.</summary>
        public OmemoTrustLevel Trust { get; internal set; }

        /// <summary>true if the device is the local device.</summary>
        public bool IsLocalDevice { get; internal set; }

        /// <summary>true if the device currently appears on the owner's device list.</summary>
        public bool Active { get; internal set; }

        /// <summary>
        /// Formats a hex fingerprint as 8 space-separated groups of 8 characters.
        /// </summary>
        /// <param name="fingerprint">A lowercase hex fingerprint.</param>
        /// <returns>The formatted fingerprint, or null if <paramref name="fingerprint"/> is null.</returns>
        public static string FormatFingerprint(string fingerprint)
        {
            if (fingerprint == null)
                return null;
            var groups = new List<string>();
            for (int i = 0; i < fingerprint.Length; i += 8)
                groups.Add(fingerprint.Substring(i, Math.Min(8, fingerprint.Length - i)));
            return String.Join(" ", groups);
        }
    }

    /// <summary>
    /// Why an OMEMO encrypted message could not be sent.
    /// </summary>
    public enum OmemoSendFailure
    {
        /// <summary>A recipient has no OMEMO devices, so the message would be sent in plaintext.</summary>
        NoDevices,
        /// <summary>A recipient has devices, but none of them is trusted yet.</summary>
        UntrustedDevices,
        /// <summary>The peer asked to stop using OMEMO and the user has not confirmed plaintext (§5.7).</summary>
        OptedOut,
        /// <summary>Sessions could not be built with any device of a recipient.</summary>
        SessionFailure,
        /// <summary>The group chat does not allow OMEMO (e.g. it is anonymous) or members are unknown (§5.8).</summary>
        GroupChatNotSupported
    }

    /// <summary>
    /// Thrown by SendMessage when OMEMO is enabled but the message cannot be encrypted. Nothing
    /// is sent in that case.
    /// </summary>
    [Serializable]
    public class OmemoSendException : XmppException
    {
        /// <summary>Initializes a new instance of the OmemoSendException class.</summary>
        /// <param name="failure">The reason.</param>
        /// <param name="recipient">The affected bare JID.</param>
        /// <param name="message">A description of the error.</param>
        /// <param name="devices">Devices awaiting a trust decision, if any.</param>
        public OmemoSendException(OmemoSendFailure failure, Jid recipient, string message, IEnumerable<OmemoDeviceInfo> devices = null)
            : base(message)
        {
            Failure = failure;
            Recipient = recipient;
            UndecidedDevices = (devices ?? Enumerable.Empty<OmemoDeviceInfo>()).ToList().AsReadOnly();
        }

        /// <summary>The reason the message could not be sent.</summary>
        public OmemoSendFailure Failure { get; }

        /// <summary>The recipient that caused the failure.</summary>
        public Jid Recipient { get; }

        /// <summary>Devices without a trust decision; set for <see cref="OmemoSendFailure.UntrustedDevices"/>.</summary>
        public IReadOnlyList<OmemoDeviceInfo> UndecidedDevices { get; }
    }

    /// <summary>
    /// Why an incoming OMEMO message could not be decrypted (§5.6).
    /// </summary>
    public enum OmemoReceiveFailure
    {
        /// <summary>The message was not encrypted for this device.</summary>
        NotEncryptedForThisDevice,
        /// <summary>There is no session with the sending device.</summary>
        NoSession,
        /// <summary>Decryption or authentication failed.</summary>
        DecryptionFailed,
        /// <summary>The SCE envelope was invalid or did not match the stanza.</summary>
        InvalidEnvelope
    }

    /// <summary>
    /// Provides data for the OmemoDecryptionFailed event.
    /// </summary>
    public class OmemoDecryptionFailedEventArgs : EventArgs
    {
        /// <summary>Initializes a new instance of the OmemoDecryptionFailedEventArgs class.</summary>
        public OmemoDecryptionFailedEventArgs(Jid from, int deviceId, OmemoReceiveFailure failure, Exception exception = null)
        {
            From = from;
            DeviceId = deviceId;
            Failure = failure;
            Exception = exception;
        }

        /// <summary>The sender of the message.</summary>
        public Jid From { get; }

        /// <summary>The sending device id.</summary>
        public int DeviceId { get; }

        /// <summary>The reason.</summary>
        public OmemoReceiveFailure Failure { get; }

        /// <summary>The underlying exception, if any.</summary>
        public Exception Exception { get; }
    }

    /// <summary>
    /// Provides data for the OmemoOptOutReceived event (§5.7).
    /// </summary>
    public class OmemoOptOutEventArgs : EventArgs
    {
        /// <summary>Initializes a new instance of the OmemoOptOutEventArgs class.</summary>
        public OmemoOptOutEventArgs(Jid jid, string reason)
        {
            Jid = jid;
            Reason = reason;
        }

        /// <summary>The bare JID that opted out.</summary>
        public Jid Jid { get; }

        /// <summary>The optional reason text.</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// Security details attached to a message that was decrypted with OMEMO.
    /// </summary>
    public sealed class OmemoMessageInfo
    {
        internal const string LocalNamespace = "urn:xmpp:omemo:2:sharp-xmpp-local";
        internal const string ElementName = "omemo-status";

        /// <summary>The sending device id.</summary>
        public int SenderDeviceId { get; private set; }

        /// <summary>The OMEMO version the message was encrypted with.</summary>
        public OmemoVersion Version { get; private set; }

        /// <summary>The trust level of the sending device at the time of decryption.</summary>
        public OmemoTrustLevel Trust { get; private set; }

        /// <summary>The fingerprint of the sending device.</summary>
        public string Fingerprint { get; private set; }

        /// <summary>
        /// Set when the message could not be decrypted; the body then holds a warning text.
        /// </summary>
        public OmemoReceiveFailure? Failure { get; private set; }

        /// <summary>true if the message was decrypted and the sender's device is trusted or verified.</summary>
        public bool IsTrusted => Failure == null && (Trust == OmemoTrustLevel.Trusted || Trust == OmemoTrustLevel.Verified);

        /// <summary>
        /// Returns the OMEMO details of a received message, or null if the message was not
        /// decrypted with OMEMO.
        /// </summary>
        /// <param name="message">A received message.</param>
        /// <exception cref="ArgumentNullException">The message parameter is null.</exception>
        public static OmemoMessageInfo FromMessage(Message message)
        {
            message.ThrowIfNull("message");
            var e = message.Data[ElementName, LocalNamespace];
            if (e == null)
                return null;

            Int32.TryParse(e.GetAttribute("sid"), out int sid);
            Enum.TryParse(e.GetAttribute("trust"), out OmemoTrustLevel trust);
            OmemoReceiveFailure? failure = null;
            if (Enum.TryParse(e.GetAttribute("failure"), out OmemoReceiveFailure f))
                failure = f;
            string fingerprint = e.GetAttribute("fingerprint");
            Enum.TryParse(e.GetAttribute("version"), out OmemoVersion version);
            return new OmemoMessageInfo
            {
                SenderDeviceId = sid,
                Version = version,
                Trust = trust,
                Fingerprint = String.IsNullOrEmpty(fingerprint) ? null : fingerprint,
                Failure = failure
            };
        }

        internal static XmlElement CreateElement(OmemoVersion version, int sid, OmemoTrustLevel trust, string fingerprint, OmemoReceiveFailure? failure)
        {
            var e = Xml.Element(ElementName, LocalNamespace)
                .Attr("version", version.ToString())
                .Attr("sid", sid.ToString())
                .Attr("trust", trust.ToString())
                .Attr("fingerprint", fingerprint ?? string.Empty);
            if (failure != null)
                e.Attr("failure", failure.ToString());
            return e;
        }
    }
}
