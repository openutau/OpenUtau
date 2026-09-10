# OpenUtau DAW Integration API

**Version 1.2 — Specification**

This document specifies the integration contract between OpenUtau and digital audio
workstation (DAW) plugins. It allows an OpenUtau project to be rendered into one or more
DAW tracks in real time, with project, track and audio state synchronized incrementally
and the DAW's transport reflected back into OpenUtau.

The key words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are to be
interpreted as described in RFC 2119.

| | |
|---|---|
| Current version | `1.2` |
| Transport | TCP, loopback only |
| Control plane | newline-delimited JSON over UTF-8 |
| Data plane | length-prefixed raw binary frames |
| Audio format | raw `float32` PCM, 44.1 kHz, stereo, interleaved, little-endian |
| Reference client | `OpenUtau.Core/DawIntegration/` (this repository) |
| Reference plugin | [KakaruHayate/openutau-vst-bridge](https://github.com/KakaruHayate/openutau-vst-bridge) (VST3/CLAP) |

---

## 1. Scope and Goals

The API synchronizes three kinds of state from OpenUtau into a DAW plugin — the project
document, track configuration, and rendered part audio — and carries transport feedback
(position, playing state, tempo) from the DAW back to OpenUtau.

Design principles:

- **Minimal surface in OpenUtau.** OpenUtau exposes a small, stable local API. Everything
  plugin- or UI-shaped lives in the plugin project.
- **Two-tier framing.** A JSON control plane that stays text-debuggable, and a binary data
  plane for audio. **Audio never travels inside JSON** — no base64, no embedded encoding.
- **Incremental by default.** Only changed state is pushed; audio is transferred on demand
  by content hash.
- **Pull-based audio.** The plugin requests audio it is missing; the request/response shape
  gives the plugin backpressure.
- **Localhost trust only.** No authentication; anything running as the same user may connect.

Non-goals in this version:

- Remote or cross-machine connections.
- ARA (Audio Random Access) tempo-map integration.
- Bidirectional tempo-map synchronization.
- **Audio format negotiation.** 44.1 kHz stereo is a hard limit of OpenUtau's engine
  (`WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)` in `PlaybackManager`); the wire format
  is fixed to match and is not negotiated.
- MIDI input into OpenUtau (reserved for a future version, §6.3).

## 2. Roles and Topology

| Role | Process | Responsibility |
|---|---|---|
| **Server** | DAW plugin (VST3, AU, CLAP, …) | Listens on `127.0.0.1:<port>`, publishes a discovery file, answers requests, renders received audio into the DAW track |
| **Client** | OpenUtau | Scans for discovery files, connects, pushes project/track/audio state |

Connection direction is **plugin = TCP server, OpenUtau = TCP client**. OpenUtau never
listens; the plugin never connects. One active connection per plugin instance.

This inversion is deliberate: a plugin instance discovered through its own listening port
maps one-to-one onto a DAW track the user has just added, with no pairing UI on either
side.

## 3. Transport

- **Protocol:** TCP, loopback only (`127.0.0.1`).
- **Port:** dynamic, chosen by the plugin at bind time.
- **Framing:** two planes share one socket, distinguished on receive by the first bytes of
  a line (§5).
- **Ordering:** TCP ordering, plus a client-side write mutex. The protocol defines no
  sequence numbers; a sender MUST serialize its own writes per connection.

### 3.1 Timing Constants

| Item | Value | Owner |
|---|---|---|
| `init` handshake timeout | 5 s | OpenUtau |
| Control-plane request timeout | 10 s | OpenUtau |
| Heartbeat send interval | 5 s | Plugin |
| Heartbeat liveness check | every 2 s | OpenUtau |
| Heartbeat dead threshold | 15 s without any message | OpenUtau |
| Reconnect backoff | 500 ms, 1 s, 2 s, then give up | OpenUtau |

## 4. Service Discovery and Version Negotiation

### 4.1 Discovery File

The plugin publishes one JSON file per instance:

- **Path:** `%TEMP%/OpenUtau/PluginServers/<name>.json` — the per-user temporary directory on
  Windows and macOS. On Linux, where `TMPDIR` may resolve to a shared directory such as `/tmp`,
  OpenUtau enforces the trust boundary of §11 before scanning or publishing (owner-only
  directory permissions; a directory it does not own is refused).
- **Schema:**

```json
{
  "port": 52341,
  "name": "OpenUtau Bridge (Track 1)",
  "apiVersion": "1.2"
}
```

| Field | Type | Meaning |
|---|---|---|
| `port` | integer | The TCP port the instance listens on |
| `name` | string | Human-readable instance name (shown in OpenUtau's UI) |
| `apiVersion` | string | Protocol version the instance implements, `"major.minor"` |

Rules:

- The plugin **MUST** (re)write the file whenever it binds or re-binds its port, and
  **MUST** delete it on shutdown.
- File names **MUST** be unique per instance (e.g. `<plugin name> <port>.json`) so that
  concurrent instances do not collide.
- OpenUtau **MUST** scan the directory for `*.json`, probe each advertised port by
  attempting `bind(127.0.0.1:<port>)` — if the bind succeeds, the advertised server is
  gone — and delete stale files whose probes succeed.

### 4.2 Version Negotiation

- `apiVersion` is carried in the discovery file **and** echoed in the `init` response
  (§6.1), so an implementation that advertises one version and speaks another is caught.
- **Major mismatch** → OpenUtau **MUST** refuse the connection and inform the user that
  the plugin is incompatible and needs updating.
- **Minor skew** → connect. The newer side **MUST** restrict itself to messages and
  fields present in the older minor (§10).

## 5. Message Framing

### 5.1 Control Plane (Line-Based JSON)

Every control message is one line: UTF-8 `<header> <json>\n`. The JSON payload may be
empty (the header is still followed by one space and an empty JSON document, e.g. `{}`).

| Header | Direction | Meaning |
|---|---|---|
| `request:<uuid>:<kind>` | OpenUtau → Plugin | A request; the plugin **MUST** reply with `response:<uuid>` |
| `response:<uuid>` | Plugin → OpenUtau | The reply to a request |
| `notification:<kind>` | either | Fire-and-forget; no reply exists |
| `close` | OpenUtau → Plugin | Bare string, no payload; clean teardown |

Every response carries a `DawResult` envelope:

```json
{ "success": true, "data": { }, "error": null }
```

- On success `data` holds the response payload defined per message.
- On failure `success` is `false`, `error` carries a human-readable string, and `data` is
  `null`.

A receiver **MUST** answer an unknown `request:` kind or malformed JSON with a failed
envelope rather than dropping the connection, and **MUST** log-and-ignore an unknown
`notification:` kind.

### 5.2 Data Plane (Binary Audio Frames)

```
audio <hash> <length>\n
<length bytes of raw audio>
```

- `hash` — the XXH64 hash of the payload bytes, **serialized as a decimal string**
  (e.g. `13507256038857166760`). 64-bit hashes **MUST NOT** be serialized as JSON numbers
  anywhere in the protocol: values above 2^53 exceed the safe-integer range of
  `double`-based JSON parsers. In a data-plane frame header the hash appears unquoted, as
  plain text outside JSON.
- `length` — decimal byte count. The receiver **MUST** read exactly `length` bytes after
  the header line; the frame does not end at the line break. A length above **268 435 456**
  (256 MiB, ≈ 12.7 minutes of 44.1 kHz stereo `float32`) **MUST** be refused as a protocol
  error, never allocated: the length is peer-controlled. Senders **MUST NOT** emit frames
  above this bound.
- Plane discrimination on receive: a line starting with `audio ` is a data-frame header;
  any other line is a control line.

## 6. Message Reference

All JSON payloads below show the inner document that follows the header. `<uuid>` values
are opaque correlation strings.

### 6.1 OpenUtau → Plugin

#### `init` (request)

```json
{ "ustx": "<full USTX project document>" }
```

- Sent exactly once, immediately after connecting; the full project is the baseline for
  all later incremental updates.
- `ustx` is OpenUtau's native USTX document, which is YAML — byte-identical to what
  OpenUtau writes into a `.ustx` file. A plugin may persist it or re-parse it with any
  YAML reader. It is deliberately **not** a JSON projection of the in-memory project
  object, which would be lossy.
- **Response `data`:** `{ "apiVersion": "1.2" }` — the version the plugin implements.
- OpenUtau is the sole owner of the project: the baseline travels outward only and is
  never echoed back.

#### `updateUstx` (notification)

```json
{ "ustx": "<full USTX project document>" }
```

- The whole USTX document, resent per change (debounced, §7). Atomic replacement: the
  receiver **MUST** discard its previous project state on receipt.

#### `updatePartLayout` (request)

```json
{
  "parts": [
    { "trackNo": 0, "startMs": 1200.0, "endMs": 8400.0,
      "audioHash": "13507256038857166760" }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `trackNo` | integer | Zero-based index into the last `updateTracks` list |
| `startMs` | number | Part start on the shared timeline, milliseconds |
| `endMs` | number | Part end, same coordinate system |
| `audioHash` | string | XXH64 (decimal) of that part's rendered audio |

- The receiver deduplicates against audio it already holds. It **SHOULD** additionally
  verify a received frame's `length` against its own hash computation as a cheap
  integrity cross-check.
- **Response `data`:** `{ "missingAudios": ["13507256038857166760", …] }` — the hashes the
  receiver still needs, each a decimal string. An empty array means everything is cached.

#### `updateTracks` (notification)

```json
{ "tracks": [
    { "name": "Singer 1", "volume": 0.0, "pan": 0.0, "muted": false,
      "singer": "Kikyo", "engine": "DIFFSINGER" }
] }
```

| Field | Type | Meaning |
|---|---|---|
| `name` | string | Track name |
| `volume` | number | Track volume in **decibels**, `0` = unity, as stored by OpenUtau |
| `pan` | number | Track pan in OpenUtau's scale, **-100…+100**, `0` = centre |
| `muted` | bool | The **effective** mute — solo already resolved against the project |
| `singer` | string | **v1.2** — track singer's display name; empty string when none assigned |
| `engine` | string | **v1.2** — render engine key (`CLASSIC`, `WORLDLINE-R`, `DIFFSINGER`, `ENUNU`, `VOGEN`, `VOICEVOX`, …); empty string when no usable renderer |

Notes:

- `volume`, `pan` and `muted` are passed through **unconverted**, for peers that want
  OpenUtau's mixer state. They do **not** govern the audio on the wire (§8): the audio is
  pre-fader, and the DAW owns gain, pan, mute and solo. A muted track still renders and
  ships part audio; `muted` is never a request to omit audio.
- `singer` and `engine` are informational (track pickers, info windows). They do not
  affect audio, and receivers that do not care may ignore them. Both were added in 1.2;
  1.1 receivers ignore them implicitly (§10).

#### `updateProjectInfo` (notification) — since 1.1

```json
{ "name": "my song", "saved": true }
```

- What a plugin's UI may show about the project. `name` is the project file's stem; an
  unsaved project reports `saved` `false` and an empty `name`.
- Carries no state the mixer or renderer needs; a plugin may ignore it.

### 6.2 Plugin → OpenUtau

#### `getAudio` (request) — audio pull

```json
{ "hash": "13507256038857166760" }
```

- Requests the rendered audio for a hash advertised in `updatePartLayout`.
- **The response is a data-plane frame**, not a JSON envelope:

```
audio 13507256038857166760 3528000\n<3528000 raw bytes>
```

- Payload encoding (fixed, engine-bound): **raw `float32` PCM, 44.1 kHz, stereo,
  interleaved, little-endian**. No compression, no base64. Mono mixes are upmixed to
  stereo by OpenUtau before transmission.
- The plugin **SHOULD** pull missing hashes sequentially; this version allows **one
  outstanding `getAudio` per connection**.
- The plugin is the requester by design: pulling rather than being pushed to is what gives
  the plugin backpressure over a slow DAW track.

#### `ping` (notification)

```json
{}
```

- Sent every 5 s. Any message from the peer also counts as liveness; `ping` exists for
  otherwise-idle connections.

#### `playbackStarted` (notification)

```json
{}
```

- Sent on the DAW transport's play rising edge. OpenUtau flushes all pending debounced
  updates (§7) before playback begins, so the plugin plays against current state.

#### `playhead` (notification) — since 1.1

```json
{ "positionMs": 12500.0, "playing": false }
```

| Field | Type | Meaning |
|---|---|---|
| `positionMs` | number | DAW transport position, absolute milliseconds on the shared timeline — the same coordinate system as `updatePartLayout`'s `startMs` |
| `playing` | bool | DAW transport state |

- **One-way towards OpenUtau.** The received position overwrites OpenUtau's playhead,
  converted to ticks on the project's own time axis. There is no reverse direction, reply,
  or acknowledgement.
- Moves smaller than **5 ticks** at the destination are ignored as jitter.
- Pacing is the sender's choice; receivers **MUST** tolerate any pacing. The reference
  plugin sends state changes immediately, every 100 ms while playing, and only when a
  parked playhead moves more than 50 ms.

#### `bpm` (notification) — since 1.1

```json
{ "bpm": 137.5 }
```

- The DAW project's tempo, sent when it changes. OpenUtau uses this only as a guard —
  warning the user, once per distinct value outside a **±0.5 BPM** tolerance, that bars
  will misalign. It never retempo-maps the project (no tempo-map sync in this version).

### 6.3 MIDI Input (Reserved — Not in This Version)

A future message family (e.g. `notification:midiNotes`, `request:recordMidi`) is reserved.
Implementations **MUST NOT** invent wire shapes for this direction under the v1 namespace.

## 7. Synchronization Semantics

- OpenUtau observes its project's command stream and pushes changes while connected.
- **Debounce windows:**

| Message group | Debounce |
|---|---|
| `updateUstx`, `updateTracks`, `updateProjectInfo` | 1 s |
| `updatePartLayout` (+ the audio pulls it triggers) | 5 s |

- **Playback flush:** on `playbackStarted`, all debounce queues flush before playback
  begins.
- **Full sync:** after every (re)connect, in this order, serialized so that one update is
  in flight per connection:

```
updateUstx → updateTracks → updateProjectInfo → updatePartLayout (+ audio pulls)
```

## 8. Audio Path Contract

- The wire audio is **pre-fader**: OpenUtau's `volume`, `pan` and `muted` fields are not
  applied to it. The DAW owns gain, pan, mute and solo, so the dry signal entering its
  effects chain stays stable while the vocal performance is edited.
- Pre-fader output is scaled by a **constant output trim of √0.5 (≈ 0.7071, −3.01 dB) per
  channel**. OpenUtau pans constant-power, so its own playback of a centred track puts
  cos(π/4) on each channel; a receiver that bypassed panning without this trim would sit a
  systematic 3 dB above the level the performance was tuned against. The trim is **not**
  mixer state and never follows `volume`, `pan` or `muted`.
- Receivers **MUST NOT** expect the mixer fields to describe the signal they receive.

## 9. Connection Lifecycle

```
plugin binds :port, writes discovery file (with apiVersion)
        │
OpenUtau scans discovery dir → probe port → TCP connect
        │
request:init (5 s timeout) → full USTX baseline + version check
        │
steady state: debounced updates; audio pulled by hash on demand
        │
DAW plays   → notification:playbackStarted → flush pending updates
DAW moves   → notification:playhead / notification:bpm
        │
disconnect (error) → backoff ×3 → re-init + full sync
disconnect (user)  → optional final update → "close" → teardown
```

## 10. Compatibility and Versioning Policy

- **Append-only.** New minor versions add messages, fields and kinds; they **MUST NOT**
  change the meaning of existing fields.
- **Kind namespaces.** A semantically changed message gets a new kind with a version
  suffix (e.g. `updatePartLayoutV2`) rather than an in-place change.
- **Unknown-field tolerance.** Receivers **MUST** ignore fields they do not know; this is
  what makes minor skew safe (§4.2).
- Version identification lives in the discovery file and the `init` response (§4.2).
- **1.1 → 1.2:** `updateTracks` gained the optional per-track `singer` and `engine`
  informational fields. Nothing else changed; 1.1 implementations interoperate unchanged.
  The newer side **MUST** omit these two fields when the peer negotiated a minor below 2.

## 11. Security and Trust Model

- Loopback only, dynamic port, no authentication. Any local process running as the same
  user can read the project document and rendered audio. This matches the trust model of
  comparable bridge tools (e.g. ACE Studio's bridge) and is accepted for this version.
- Mitigations: random high port, owner-only discovery directory (on Unix the discovery
  directory is tightened to mode 0700 before scanning and publishing, and a directory this
  user does not own is refused outright), published files carry mode 0600, no cross-machine path.
- Caveat: on a shared-`/tmp` Linux system, a file planted in the discovery directory *before*
  OpenUtau first tightens it could still be scanned. Do not run OpenUtau in multi-user shared
  sessions.

## 12. Error Handling

| Condition | Required behavior |
|---|---|
| Control request timeout (10 s) | Treat the connection as dead: disconnect and reconnect |
| Malformed control line | Log a warning; keep the connection |
| Unknown `request:` kind | Failed `DawResult` envelope; keep the connection |
| Unknown `notification:` kind | Log and ignore |
| Data frame truncated (stream ends before `length` bytes) | Protocol error: disconnect |
| Frame `length` above the 256 MiB bound | Protocol error: refuse, do not allocate |
| Non-user-initiated disconnect | Reconnect with backoff 500 ms / 1 s / 2 s, then notify the user |
| User-initiated disconnect | Optional final update, then the bare `close` line, then teardown |

## 13. Implementation Checklist for Plugin Authors

A conforming plugin:

1. Binds a dynamic TCP port on `127.0.0.1` and publishes the discovery file (§4.1),
   deleting it on shutdown.
2. Implements the control-plane framing and the `DawResult` envelope (§5.1), answering
   unknown requests with a failed envelope instead of dropping the connection.
3. Implements the data-plane framing (§5.2), including the decimal-string hash rule and
   the 256 MiB length bound, and answers `getAudio` with a frame, not an envelope (§6.2).
4. Handles `init` (version echo), `updateUstx` (atomic replacement), `updatePartLayout`
   (hash dedup + `missingAudios`), and tolerates `updateTracks`/`updateProjectInfo`.
5. Sends `ping` every 5 s; sends `playbackStarted` on the DAW's play edge; optionally
   sends `playhead` and `bpm`.
6. Treats the wire audio as pre-fader 44.1 kHz stereo `float32` already trimmed by √0.5
   per channel (§8).
7. Handles reconnection gracefully: any non-user-initiated disconnect is followed by
   OpenUtau's re-`init` and a full sync (§9).
8. Cleans up its discovery file when the host unloads it.

## 14. Conformance Testing

- **OpenUtau side (unit + conformance):** framing, request/response/timeout, heartbeat and
  debounce-flush are covered by `OpenUtau.Test/Core/DawIntegration/`. A loopback test
  plugin (`DawTestPlugin`) plays the plugin half over the shipping transport code, and a
  conformance suite drives `init → updateTracks → updateProjectInfo → updatePartLayout →
  getAudio → playbackStarted` (plus the 1.1 `playhead`/`bpm`) against the real manager
  through a real discovery directory.
- **Plugin side:** an independent test client that replays recorded transcripts —
  including binary frames — against the plugin and asserts responses. Once the two sides
  share no code, the transcript harness is the only honest contract test, and plugin
  authors are encouraged to build one.

## Appendix A — Constants Summary

| Constant | Value | Where defined |
|---|---|---|
| Audio sample format | `float32`, interleaved, little-endian | §5.2, §6.2 |
| Sample rate | 44 100 Hz (fixed, not negotiated) | §1 |
| Channels | 2 (mono mixes upmixed) | §6.2 |
| Output trim | √0.5 per channel (−3.01 dB), constant | §8 |
| Hash function | XXH64, serialized as a decimal string | §5.2 |
| Maximum frame length | 268 435 456 bytes (256 MiB) | §5.2 |
| Discovery directory | `%TEMP%/OpenUtau/PluginServers/` | §4.1 |
| `init` timeout | 5 s | §3.1 |
| Request timeout | 10 s | §3.1 |
| Heartbeat interval | 5 s | §3.1 |
| Liveness check | every 2 s | §3.1 |
| Dead threshold | 15 s | §3.1 |
| Reconnect backoff | 500 ms, 1 s, 2 s | §3.1 |
| Playhead jitter floor | 5 ticks | §6.2 |
| BPM mismatch tolerance | ±0.5 | §6.2 |
| Protocol version | `1.2` | §4.2 |

## Appendix B — Design Notes

For reviewers evaluating the shape of the API; not part of the conformance contract.

- **Audio never travels in JSON.** A base64-in-JSON encoding costs +33 % before any
  compression, compresses poorly on PCM, and forces the whole message to be materialized
  in memory. The length-prefixed binary plane keeps the control plane readable and the
  audio path streaming-friendly, with a fixed, engine-bound format instead of a
  negotiation.
- **Pull-based audio.** Making the plugin request audio by hash gives it backpressure: a
  slow DAW track simply pulls slower, instead of OpenUtau flooding a socket ahead of the
  consumer.
- **XXH64, as decimal strings.** Fast enough to hash every rendered frame, and collisions
  are negligible; the decimal-string rule exists because 64-bit values overflow the
  2^53 safe-integer range of `double`-based JSON parsers. The receiver's `length`
  cross-check is a deliberately cheap integrity backstop.
- **Plugin as server.** An instance that listens and advertises itself maps one-to-one
  onto a DAW track the user just added — no pairing step, no broker process, and OpenUtau
  stays a pure client with no listening surface of its own.
