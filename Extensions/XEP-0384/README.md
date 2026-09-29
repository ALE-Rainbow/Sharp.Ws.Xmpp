# OMEMO (XEP-0384) Extension

Implements OMEMO Encryption for `Sharp.Xmpp` in both versions still in use:

- OMEMO 0.9 (`urn:xmpp:omemo:2`), the current XEP-0384.
- Legacy OMEMO 0.3 (`eu.siacs.conversations.axolotl`), used by Conversations and other libsignal
  based clients. It is on by default and can be turned off with `XmppClient.OmemoLegacyEnabled`.

Both versions share one identity key, one device id and one PreKey pool, so a device shows the
same fingerprint in both. Each contact device is encrypted for in the newest version it
announces. When a chat has devices of both kinds, one message carries both `<encrypted/>`
elements. Trust decisions are stored per fingerprint and apply to both versions.

## What is implemented

- **Crypto (§4):** X3DH with Ed25519 IdentityKeys and X25519 (BouncyCastle), the Double Ratchet with
  the OMEMO KDF parameters, the protobuf wire format, skipped message keys (MAX_SKIP 1000) and the
  payload cipher (HKDF "OMEMO Payload", AES-256-CBC, truncated HMAC). Details were cross-checked
  against python-twomemo, the reference implementation.
- **Setup and announcing (§5.1, §5.3):** a stable random device id (checked against the account's
  device list), 100 PreKeys topped up after use, signed PreKey rotation every 7 days (the previous one
  is kept), and publishing with `publish-options` (`access_model=open`, `max_items=max`) plus the
  XEP-0060 §8.2 reconfigure fallback. The existing device list is fetched first so other clients stay
  on it, and the device re-announces itself if another client removes it.
- **Discovery (§5.2):** advertises `urn:xmpp:omemo:2:devices+notify`, caches device lists from PEP
  events, and fetches lists that were not seen yet on this connection before sending.
- **Sending (§5.5):** SCE envelope with random `<rpad/>`, `<time/>`, `<from/>` and `<to/>`. Keys are
  encrypted for all trusted devices of the recipient and of the own account. The message also gets
  a fallback body, an EME hint and a `<store/>` hint. Nothing is sent in plaintext while OMEMO is on:
  `SendMessage` throws `OmemoSendException` instead.
- **Receiving (§5.6):** key exchanges build or replace sessions (repeated key exchanges are
  recognised by their ephemeral key), and consumed PreKeys trigger a bundle republish. The envelope
  affixes are validated. Failures replace the body with a warning and raise `OmemoDecryptionFailed`,
  while duplicates are ignored. Carbons and MAM results are decrypted in place.
- **Empty messages (§6):** an automatic reply completes a key exchange, a heartbeat is sent at
  counter 53, and sessions are repaired when a message arrives from a device with no session.
- **Trust (§8):** trust is stored per contact and IdentityKey fingerprint (`Undecided`, `Trusted`,
  `Verified`, `Distrusted`). Only trusted and verified devices are encrypted for. Fingerprints use the Curve25519
  form, formatted as 8×8 hex groups. `OmemoMessageInfo.FromMessage` exposes each received message's
  sender device and trust.
- **Opt-out (§5.7), session reset (§6), unpublish (§6) and group chats (§5.8):** the member, admin
  and owner lists combined with occupant real JIDs, and a mandatory `<to/>` affix.

## Legacy OMEMO details

- The session layer mirrors libsignal-protocol-java (protocol version 3): the "WhisperText",
  "WhisperRatchet" and "WhisperMessageKeys" KDFs, `SignalMessage`/`PreKeySignalMessage` with
  8-byte MACs over both identity keys, up to 5 receiver chains, and 2000 stored message keys.
- The device list is at `eu.siacs.conversations.axolotl.devicelist`. Bundles are at
  `eu.siacs.conversations.axolotl.bundles:<device id>` (item `current`), with 0x05-prefixed keys.
- The payload is AES-128-GCM with a 12-byte IV, and the 16-byte tag travels with the key, as
  Conversations does. Key transport messages acknowledge PreKey messages.
- The identity key is generated so that its signatures verify under libsignal's Curve25519
  signature check. Identities created by the first OMEMO 2-only build are replaced once.

## Usage

```csharp
Environment.SetEnvironmentVariable(OmemoConstants.DbPathEnvVar, "/path/omemo.db3"); // persistent keys
client.EnableOmemo(true);
client.OmemoDeviceLabel = "MyApp on Android";
client.Connect();
client.PublishOmemoBundle();              // after every login

try { client.SendMessage(message); }
catch (OmemoSendException ex) when (ex.Failure == OmemoSendFailure.UntrustedDevices)
{
    // Show ex.UndecidedDevices[i].FormattedFingerprint, then:
    client.SetOmemoTrust(device.Jid, device.DeviceId, OmemoTrustLevel.Verified);
}
```

State (identity, PreKeys, sessions, trust and opt-outs) is kept per account in the SQLite file.
Deleting it creates a new device identity.

## Known limits

- MIX group chats are not covered (the spec only defines MUC).
- Interoperability was checked against the spec, python-twomemo (OMEMO 0.9), and the
  libsignal-protocol-java and Conversations sources (legacy), but not yet live against another client.
- Legacy OMEMO only carries the message body. Opt-out (§5.7) exists only in OMEMO 0.9.
