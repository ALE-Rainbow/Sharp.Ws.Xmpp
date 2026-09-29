# Sharp.Ws.Xmpp — Project Overview

## Summary
`Sharp.Ws.Xmpp` is a .NET library for communicating with XMPP servers over **WebSocket**.  
It focuses on instant messaging and presence scenarios, with support for core XMPP protocol features and common extensions.

## Goals
- Provide a simple, documented API for XMPP client development.
- Support modern XMPP usage over WebSocket (TCP stream support removed).
- Offer production-friendly authentication and messaging capabilities.
- Provide end-to-end message encryption using OMEMO specifification as defined in the [XEP-0384](https://xmpp.org/extensions/xep-0384.html)

## Key Capabilities
- XMPP Core + XMPP IM support
- SASL authentication (PLAIN, DIGEST-MD5, SCRAM-SHA-1)
- Presence and instant messaging
- User profile/presence extensions (avatar, mood, tune, activity)
- File transfer support (SOCKS5 and in-band)
- In-band registration and simplified blocking

## Technology Context
Based on the current workspace configuration, the codebase includes projects targeting:
- `.NET Standard 2.0`
- `.NET Core 3.1`
- `.NET 10`

This likely reflects a compatibility strategy across legacy and modern .NET runtimes.

## Repository Notes
- Primary repository: `Sharp.Ws.Xmpp`
- License: MIT
- Project lineage and credits indicate evolution from `S22.Xmpp` and `Sharp.Xmpp`

## Typical Use Case
Applications that need to:
- Connect to XMPP servers via WebSocket
- Authenticate users
- Exchange messages
- Manage presence and related XMPP extension data

## Maintenance Considerations
- Validate package/docs links in `README.md` (some appear historical and may need refresh).
- Keep authentication mechanisms and protocol extension support aligned with current security expectations.
- Review target framework strategy periodically (especially `.NET Core 3.1` lifecycle status).

## Getting Started
1. Reference the library in your .NET solution.
2. Create and configure an XMPP client instance.
3. Connect/authenticate, then subscribe to messaging/presence events.
4. Add extension support only where required by your product scenario.