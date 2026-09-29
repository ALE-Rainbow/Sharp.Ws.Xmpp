# Sharp.Ws.Xmpp Functional Requirements

## 1. Purpose

Define the functional requirements for a .NET XMPP client library focused on WebSocket-based communication, instant messaging, presence, and supported XMPP extensions.

## 2. Scope

The library shall provide client-side XMPP capabilities for applications that need:

- Connection and authentication to an XMPP server
- One-to-one and group messaging
- Presence and roster management
- File transfer
- Message/state synchronization and archive access
- Extension-based feature support (XEP and vendor-specific modules)

TCP stream transport is out of scope; WebSocket transport is supported.

## 3. Primary Actor

- **Application Developer** integrating `XmppClient` into a .NET application.

## 4. Functional Requirements

## 4.1 Connection and Session

- **FR-001**: The library shall allow constructing an XMPP client with hostname, credentials, port, TLS settings, and certificate validation callback.
- **FR-002**: The library shall support connecting to an XMPP server and optional resource binding.
- **FR-003**: The library shall expose connection state (`Connected`) and authentication state (`Authenticated`).
- **FR-004**: The library shall support explicit authentication with username/password after client creation.
- **FR-005**: The library shall support secure transport negotiation via TLS when enabled.
- **FR-006**: The library shall expose a callback hook for remote certificate validation.
- **FR-007**: The library shall support graceful shutdown and disposal of client resources.
- **FR-008**: The library shall expose a connection status change event.

## 4.2 WebSocket and Proxy

- **FR-009**: The library shall support enabling/disabling WebSocket transport.
- **FR-010**: The library shall allow configuring the WebSocket endpoint URI.
- **FR-011**: The library shall support web proxy configuration for outbound connections.

## 4.3 Messaging (1:1)

- **FR-012**: The library shall send chat messages to a target JID with body, subject, thread, type, and optional language.
- **FR-013**: The library shall support multilingual message payloads (multiple body/subject language variants).
- **FR-014**: The library shall support receiving incoming messages via an event.
- **FR-015**: The library shall support optional out-of-band metadata attachment on message send.
- **FR-016**: The library shall support chat state notifications and expose state change events.
- **FR-017**: The library shall support marking messages as received/read using delivery receipt semantics.
- **FR-018**: The library shall expose message delivery receipt events.

## 4.4 Presence and Roster

- **FR-019**: The library shall support setting default and active presence status (availability, message, priority, language).
- **FR-020**: The library shall expose incoming presence/status updates through events.
- **FR-021**: The library shall retrieve the authenticated user roster.
- **FR-022**: The library shall support adding contacts to roster and requesting subscriptions.
- **FR-023**: The library shall support removing contacts from roster.
- **FR-024**: The library shall expose roster update events.
- **FR-025**: The library shall expose subscription workflow events (request, approved, refused, unsubscribed).

## 4.5 IQ and Discovery

- **FR-026**: The library shall support asynchronous IQ get/set requests with callback handling.
- **FR-027**: The library shall support custom IQ request handling and custom IQ callbacks.
- **FR-028**: The library shall retrieve software version from a remote entity.
- **FR-029**: The library shall retrieve remote entity time.
- **FR-030**: The library shall retrieve supported features/extensions for a remote entity.
- **FR-031**: The library shall support entity ping and return round-trip timing.

## 4.6 Blocking and Privacy

- **FR-032**: The library shall support blocking and unblocking entities.
- **FR-033**: The library shall retrieve the current block list.
- **FR-034**: If native blocking is unavailable, the library shall fall back to privacy-list-based blocking logic.

## 4.7 Multi-User Chat (MUC)

- **FR-035**: The library shall discover public rooms from a chat service.
- **FR-036**: The library shall retrieve room information.
- **FR-037**: The library shall join and leave rooms.
- **FR-038**: The library shall send and receive room invitations and declines.
- **FR-039**: The library shall expose group chat events (subject updates, participant presence, invitation, room errors).
- **FR-040**: The library shall retrieve room occupants by affiliation/role (members, owners, moderators, visitors, bans, etc.).
- **FR-041**: The library shall support moderation actions (kick, voice request, room config changes).
- **FR-042**: The library shall support retrieval of room history based on history options.

## 4.8 File Transfer

- **FR-043**: The library shall support initiating file transfer by file path.
- **FR-044**: The library shall support initiating file transfer by stream.
- **FR-045**: The library shall support canceling active file transfers.
- **FR-046**: The library shall expose file transfer request callback for incoming transfers.
- **FR-047**: The library shall expose progress and abort events for file transfers.
- **FR-048**: The library shall support SI File Transfer with in-band and SOCKS5 bytestream options.

## 4.9 User Profile and PEP Features

- **FR-049**: The library shall support publishing and receiving user tune information.
- **FR-050**: The library shall support publishing and receiving user mood information.
- **FR-051**: The library shall support publishing and receiving user activity information.
- **FR-052**: The library shall support vCard avatar publish and retrieval.
- **FR-053**: The library shall expose avatar/vCard update notifications.

## 4.10 Message Archive and Carbons

- **FR-054**: The library shall support querying archived messages by date range and pagination cursor.
- **FR-055**: The library shall support deleting archived messages.
- **FR-056**: The library shall expose archive retrieval and archive-management result events.
- **FR-057**: The library shall load and support message carbons extension where available.

## 4.11 Stream Management and Reliability

- **FR-058**: The library shall support enabling stream management with optional resume support.
- **FR-059**: The library shall expose stream management state/events (resumed, failed).
- **FR-060**: The library shall allow reading and restoring stream resume context (resume ID and stanza counters).

## 4.12 Registration

- **FR-061**: The library shall support in-band account registration via callback-driven data collection.

## 4.13 Events and Error Signaling

- **FR-062**: The library shall expose runtime error events for unrecoverable conditions.
- **FR-063**: The library shall validate connected/authenticated preconditions before operations that require an active session.
- **FR-064**: The library shall throw meaningful exceptions for invalid input, state violations, and protocol/network failures.

## 4.14 Extension Loading

- **FR-065**: The library shall initialize and expose supported XMPP extension modules during client startup.
- **FR-066**: The library shall provide a central API surface (`XmppClient`) over underlying core and extension modules.

## 5. Acceptance Criteria (High-Level)

- All required operations can be executed through `XmppClient` public methods/properties/events.
- Connection/authentication lifecycle is observable and controllable.
- Messaging, presence, roster, MUC, and file transfer features are testable end-to-end against a compliant XMPP server.
- Extension-dependent features fail gracefully when unsupported by server/entity.
- Error and state validation behavior is deterministic and documented.