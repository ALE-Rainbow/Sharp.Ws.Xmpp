# UI Guidelines for Sharp.Ws.Xmpp (.NET MAUI)

## 1. Purpose
Define consistent, accessible, and performant UI standards for an XMPP messaging application built with `.NET MAUI`, aligned with modern C#/.NET engineering practices.

---

## 2. Core UX Principles
- **Fast conversation flow**: Sending/reading messages must feel immediate.
- **Clarity over decoration**: Prioritize readable content and status indicators.
- **Predictable navigation**: Keep chat navigation, settings, contacts, and account screens consistent.
- **Mobile-first ergonomics**: Touch targets and spacing optimized for phones first, then desktop/tablet adaptation.

---

## 3. Platform + Design Standards
- Follow **.NET MAUI** UI patterns using `Shell`, `ContentPage`, `CollectionView`, `DataTemplate`, `VisualStateManager`.
- Apply **Fluent-inspired** principles (clear hierarchy, meaningful motion, typography consistency) while respecting native platform conventions.
- Meet **WCAG 2.2 AA** accessibility targets.
- Ensure localization and bidirectional text support from initial implementation.
- Follow **MVVM** pattern 

---

## 4. Information Architecture
Recommended top-level navigation:
1. **Chats**
2. **Contacts**
3. **Calls** (if supported)
4. **Settings**

Use:
- `Shell` for primary routing
- Deep links for direct chat opening (`xmpp://chat/{jid}` style mapping if implemented)
- Back behavior that always preserves unsent draft text where feasible

---

## 5. Messaging UI Standards

### 5.1 Conversation List
- Show: avatar, display name, last message preview, timestamp, unread badge, mute/pin states.
- Sort primarily by latest message timestamp.
- Use clear visual distinction for unread vs read chats.

### 5.2 Chat Screen
- Message bubbles:
  - Sent and received must be visually distinct.
  - Support text, links, emojis, and media placeholders.
- Metadata:
  - Timestamp on each message or grouped blocks.
  - Delivery/read status indicator for outgoing messages.
- Composer:
  - Persistent input at bottom.
  - `Send` enabled only when valid content exists.
  - Attachment, emoji, and optional voice actions grouped consistently.
- Typing and presence:
  - Show subtle typing indicator.
  - Presence states should be visible but not visually noisy.

### 5.3 Group Chat (MUC)
- Clearly show sender identity for each incoming message.
- Mention highlights (`@user`) must be visually emphasized.
- Distinguish system events (join/leave/topic changes) from normal messages.

---

## 6. XMPP-Specific UX Expectations
Align visible states with XMPP behavior (from relevant XMPP RFC/XEP features in use):
- Connection state: `Connecting`, `Online`, `Reconnecting`, `Offline`.
- Message state: `Pending`, `Sent`, `Delivered`, `Read`, `Failed`.
- Retries and errors must provide actionable options (`Retry`, `Edit`, `Delete` where valid).
- Preserve message order and display delayed/offline messages with correct timestamps.
- Surface trust/security indicators for TLS/auth/session issues in a user-friendly way.

---

## 7. Accessibility Requirements
- Support screen readers with meaningful `AutomationProperties.Name` and hints.
- Minimum touch target: **44x44 dp**.
- Respect dynamic text scaling and OS accessibility settings.
- Color contrast at least WCAG AA:
  - Normal text: **4.5:1**
  - Large text: **3:1**
- Never rely on color alone for status (include icon/label).

---

## 8. Visual System

### 8.1 Typography
- Use platform-default readable font stacks unless branding requires custom fonts.
- Define semantic text styles:
  - `Title`, `Subtitle`, `Body`, `Caption`, `Meta`.

### 8.2 Spacing + Layout
- Use a spacing scale (example): `4, 8, 12, 16, 24, 32`.
- Keep consistent margins/padding across pages.
- Prefer adaptive layouts with `Grid` and breakpoints for tablet/desktop.

### 8.3 Theming
- Support light and dark themes.
- Centralize color tokens and semantic resources (`Primary`, `Surface`, `Error`, `Success`, etc.).
- Use `AppThemeBinding` and resource dictionaries for theme switching.

---

## 9. Performance & Responsiveness
- Use `CollectionView` virtualization for chat lists and message lists.
- Use incremental loading for history and media thumbnails.
- Avoid blocking UI thread; all network and storage operations must be async.
- Use placeholders/skeletons for content loading >150ms.
- Keep animation subtle and short (typically 100–250ms).

---

## 10. Error Handling UX
- Distinguish:
  - transient network issue
  - auth/session issue
  - permanent send failure
- Use non-blocking banners/snackbars for transient issues.
- Reserve modal dialogs for destructive or account-critical actions.
- Provide clear recovery actions (`Reconnect`, `Retry`, `Sign in again`).

---

## 11. .NET / C# Industry Standards (UI-Relevant)

### 11.1 Coding Standards
- Enable nullable reference types: `<Nullable>enable</Nullable>`.
- Treat warnings as errors in CI for stable branches.
- Adopt `.editorconfig` with Microsoft naming/style rules.
- Use SDK analyzers (`Microsoft.CodeAnalysis.NetAnalyzers`) at recommended severity.

### 11.2 MVVM & Binding
- Use MVVM consistently (`View` + `ViewModel` separation).
- Prefer `CommunityToolkit.Mvvm` for source-generated observable properties/commands.
- Use compiled bindings (`x:DataType`) to improve performance and type safety.
- Keep code-behind minimal and UI-only.

### 11.3 Async and Threading
- Use `async/await` end-to-end.
- Avoid `.Result` / `.Wait()` on async tasks.
- Marshal UI updates to main thread only when required.
- Support cancellation tokens for long-running operations.

### 11.4 Logging & Diagnostics
- Use structured logging (`ILogger`) with event IDs for key flows:
  - connect/disconnect
  - send/receive
  - retry/failure
- Do not log sensitive message content by default.
- Add opt-in diagnostic modes for protocol troubleshooting.

### 11.5 Security Basics
- Use secure storage for tokens/secrets.
- Validate TLS and certificate handling correctly.
- Sanitize and safely render untrusted content (links, previews, markup).

---

## 12. Testing Requirements
- Unit test ViewModels and message state transitions.
- UI test critical flows:
  - open chat
  - send message
  - reconnect after offline
  - theme switch
  - accessibility labels present
- Add regression tests for message ordering and duplicate prevention.

---

## 13. Definition of Done for UI Features
A UI story is complete when:
1. UX behavior is documented.
2. Accessibility checks pass.
3. Light/dark themes verified.
4. Localization-ready strings used.
5. Performance budget met on target devices.
6. Unit/UI tests added and passing.
7. Telemetry/logging added for key failure points.

---

## 14. Versioning and Governance
- Keep this document versioned with the codebase.
- Update guidelines when:
  - new MAUI platform capabilities are adopted
  - XMPP feature scope changes
  - accessibility/legal standards change
- Enforce via PR checklist and automated analyzers in CI.