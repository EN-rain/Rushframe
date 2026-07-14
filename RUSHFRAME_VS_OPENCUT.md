# Rushframe vs OpenCut Codebase Comparison

Last reviewed: 2026-07-12

Repositories compared:

- Rushframe: `C:\Users\LENOVO\Desktop\Projectsss\Rushframe`
- OpenCut: `C:\Users\LENOVO\Desktop\Projectsss\opencut-github`

## Important Scope Note

The OpenCut repository contains two materially different products:

1. **Current `main` rewrite** — an early scaffold. The web app displays `hello world`, the desktop app displays a shell window, and the API has health/echo routes.
2. **`pre-rewrite` OpenCut Classic** — the previous functional browser editor with the substantial editing implementation.

This report separates implemented features from roadmap claims. OpenCut's advertised Editor API, plugin-first system, MCP server, headless mode, scripting tab, and unified Rust core are currently roadmap items in `main`, not completed features.

---

# 1. Executive Summary

## Against OpenCut current `main`

Rushframe is substantially ahead in actual editor functionality. Rushframe already has:

- a working Windows editor
- timeline tracks and editing commands
- multi-layer rendering
- media preview and FFmpeg export
- project persistence and autosave
- effects, masks, color controls, audio controls, and stabilization models
- local media intelligence
- a local agent/MCP workflow
- approval and audit controls
- automated and manual QA artifacts

OpenCut current `main` currently provides only project scaffolding for web, desktop, and API applications.

## Against OpenCut Classic (`pre-rewrite`)

The comparison is more balanced.

**OpenCut Classic is stronger in:**

- polished browser timeline interaction
- real-time GPU composition
- direct manipulation inside preview
- keyframe editing UX and graph curves
- multi-selection and group operations
- built-in fonts, stickers, shapes, and brand assets
- browser project migrations
- configurable keyboard actions
- cross-platform browser availability
- scenes and richer bookmarks

**Rushframe is stronger in:**

- Windows-native local editing workflow
- FFmpeg codec and container support
- professional media-processing breadth
- color correction, chroma key, stabilization, fades, pan, and crop modeling
- larger built-in effect catalog
- agent/MCP integration that exists today
- agent approvals, preview-only edits, audit records, and local-only bridge behavior
- deep scene/audio/transcript/media intelligence
- duplicate-take and editing-moment analysis
- local filesystem projects, recovery, and autosave
- legacy project import
- native C++ interop
- concrete manual QA and rendered-review artifacts

## Main strategic conclusion

Rushframe should not copy OpenCut wholesale. It should preserve its local desktop, FFmpeg, agent-safe architecture while borrowing OpenCut Classic's strongest interaction and rendering ideas:

- real-time GPU composition
- direct canvas manipulation
- graph-based keyframe editing
- multi-item selection and transforms
- action/keybinding registry
- integer-tick/rational-FPS time model
- project schema migration discipline
- modular asset-provider registries

---

# 2. Product Direction

| Area | Rushframe | OpenCut current rewrite | OpenCut Classic |
|---|---|---|---|
| Primary product | Local Windows video editor with agent workflow | Future cross-platform editor platform | Browser-first local video editor |
| Current usability | Functional editor | Scaffold only | Functional editor |
| Main UI | WPF/.NET 10 | React web shell + GPUI desktop shell | Next.js/React |
| Core media engine | FFmpeg/FFprobe | Planned Rust core | Rust/wgpu/WASM compositor + browser media APIs |
| Storage | Local files and app-data | Not implemented | IndexedDB + OPFS |
| Agent integration | Implemented local bridge and MCP-facing backend | Promised | Not implemented |
| Offline-first | Yes, except optional cloud visual AI | Not established | Mostly local, but has server/auth and remote asset integrations |
| Platform scope | Windows | Planned browser/desktop/mobile | Browser; desktop shell was early |

Rushframe's direction is narrower but more concrete. OpenCut's rewrite has a broader long-term architecture goal, but its current implementation does not yet validate those promises.

---

# 3. Architecture

## Rushframe

Rushframe uses explicit layers:

- `Rushframe.Domain` — canonical project model and edit commands
- `Rushframe.Application` — application use cases
- `Rushframe.Infrastructure` — persistence, caching, autosave, analysis
- `Rushframe.Desktop` — WPF UI and composition root
- `Rushframe.Media.Abstractions` — media contracts
- `Rushframe.Media.Native` — FFmpeg implementation
- `Rushframe.Native.Interop` and native C++ — optional low-level frame operations
- `Rushframe.LegacyImport` — old-format conversion
- `rushframe_intelligence` — Python media-analysis system

This is a conventional layered desktop architecture. The domain model is independent of WPF and FFmpeg, which is a strong foundation for agent commands and testing.

## OpenCut current rewrite

The intended design is:

- Rust as the single source of truth for non-UI logic
- replaceable web, desktop, and future mobile shells
- plugin-first editor API
- headless and MCP access

However, the checked-out implementation currently contains:

- a GPUI desktop scaffold
- a Vite/React hello-world page
- an Elysia Cloudflare health/echo API

The architecture is aspirational rather than demonstrated.

## OpenCut Classic

OpenCut Classic contains:

- React editor components
- TypeScript editor managers and command objects
- Rust crates for time, GPU, compositor, effects, and masks
- WASM bindings consumed by the browser
- browser persistence adapters and 30+ schema migrations
- browser renderer nodes for video, image, text, stickers, graphics, effects, and backgrounds

Its strongest architectural advantage is a real-time Rust/wgpu compositor used through WASM.

## Architecture verdict

| Topic | Winner | Reason |
|---|---|---|
| Current production completeness | Rushframe | Current OpenCut rewrite is only scaffolding |
| Cross-platform potential | OpenCut rewrite | Rust-core/UI-shell goal is more portable, once implemented |
| Domain isolation | Rushframe | Clear domain/application/infrastructure separation already exists |
| Real-time rendering architecture | OpenCut Classic | Rust/wgpu compositor is more suitable than pre-rendering preview files |
| Agent command architecture | Rushframe | Domain commands are already exposed through controlled local endpoints |
| Browser local-storage maturity | OpenCut Classic | Extensive OPFS/IndexedDB adapters and migrations |
| Native desktop integration | Rushframe | WPF, local files, FFmpeg, app data, native interop |

---

# 4. Timeline and Core Editing Functions

| Editing function | Rushframe | OpenCut Classic | Notes |
|---|---:|---:|---|
| Multi-track timeline | Yes | Yes | Both support layered timelines |
| Video clips | Yes | Yes | |
| Image clips | Yes | Yes | |
| Audio clips | Yes | Yes | |
| Text clips | Yes | Yes | |
| Music/voice-specific tracks | Yes | Generic audio tracks | Rushframe models semantic audio track types |
| Graphic elements | Model partially | Yes | OpenCut has rectangle/ellipse/polygon/star definitions |
| Stickers | Domain item exists, not end-to-end complete | Yes | OpenCut has flags, logos, and shapes |
| Adjustment/effect layers | Domain item exists, incomplete render path | Yes | OpenCut supports effect tracks/elements |
| Move clips | Yes | Yes | |
| Trim clips | Yes | Yes | |
| Split clips | Yes | Yes | OpenCut also has split-left and split-right actions |
| Delete | Yes | Yes | |
| Ripple delete/editing | Yes | Yes | OpenCut Classic's interval-diff ripple algorithm is more mature |
| Duplicate | Yes | Yes | |
| Copy/paste | Yes | Yes | OpenCut can also paste media directly from system clipboard |
| Undo/redo | Yes | Yes | Both use command objects/history |
| Snapping | Yes | Yes | OpenCut has richer snap sources and temporary Shift bypass |
| Markers/bookmarks | Markers | Rich bookmarks | OpenCut bookmarks support notes, colors, and durations |
| Multiple scenes/sequences | Sequences | Scenes | Both model more than one composition |
| Multi-item selection | Limited compared with OpenCut | Yes | OpenCut supports group move and resize |
| Track mute | Yes | Yes | |
| Track visibility | Yes | Yes | |
| Track solo | Modeled | No clear support | Rushframe model includes it |
| Track lock | Modeled | No clear support | Rushframe model includes it |
| Item lock | Modeled | No clear support | |
| Source audio separation | Extract-audio workflow | Explicit toggle/extract/recover | OpenCut provides a direct action |

## Timeline verdict

OpenCut Classic currently has the more polished manual timeline interaction system. Rushframe has a broader professional domain model, but some modeled capabilities are not yet surfaced or rendered end to end.

---

# 5. Preview and Composition

## Rushframe

Rushframe now has two preview modes:

- source-media preview
- cached composed-timeline preview rendered through the FFmpeg export graph

This fixes multi-layer correctness because timeline preview uses the same composition logic as export. It can include:

- overlapping videos and images
- text layers
- track ordering
- transforms
- opacity
- blend modes
- mixed audio
- supported masks/effects/color processing

The tradeoff is latency: after an edit, Rushframe may need to regenerate a low-resolution preview file.

## OpenCut Classic

OpenCut Classic uses a browser canvas plus Rust/wgpu/WASM compositor. It supports real-time composition and interactive overlays, including:

- move, scale, and rotate handles
- preview hit-testing
- snapping guides
- text editing overlay
- mask handles
- preview zoom and pan
- interactive selection outlines

## Preview verdict

OpenCut Classic has the superior interaction model and lower-latency composition architecture. Rushframe has stronger output parity because preview and export share FFmpeg logic, but it needs a real-time compositor to achieve professional editing responsiveness.

## Recommended Rushframe direction

Introduce a composition abstraction that can target:

1. a real-time preview renderer, preferably GPU-backed
2. FFmpeg final export

Both targets should consume the same evaluated scene graph so they cannot drift in behavior.

---

# 6. Transform, Animation, and Keyframes

| Capability | Rushframe | OpenCut Classic |
|---|---:|---:|
| Position | Yes | Yes |
| Independent X/Y scale | Domain supports X/Y | Yes, exposed in UI |
| Rotation | Yes | Yes |
| Anchor | Modeled | Param-driven |
| Opacity | Yes | Yes |
| Direct preview manipulation | Not yet comparable | Yes |
| Keyframe model | Yes | Yes |
| Multiple animated properties per element | Current item has only one `AnimatedProperty` | Yes |
| Keyframe lanes | No complete UI found | Yes |
| Graph editor | No | Yes |
| Bezier curve editing | Tangents modeled, evaluation incomplete | Yes |
| Copy/paste keyframes | No complete workflow found | Yes |
| Effect-parameter keyframes | Not end-to-end | Yes |
| Group transforms | Limited | Yes |

Rushframe's current keyframe implementation is incomplete:

- `TimelineItem` contains a single optional animated property rather than a collection/channel set.
- Bezier tangent fields exist, but Bezier evaluation currently falls back to linear interpolation.
- No full timeline lane/graph editor was found.
- FFmpeg export does not currently evaluate keyframes.

OpenCut Classic is decisively ahead in animation UX and evaluation.

---

# 7. Effects, Color, Masks, and Transitions

## Effects

### Rushframe catalog

Rushframe registers effects including:

- monochrome
- sepia
- blur
- brightness
- contrast
- vignette
- noise reduction
- motion blur
- glitch
- film grain
- food pop
- night lift
- nature green
- spark flash
- love soft
- lens punch
- smooth motion

Its FFmpeg renderer supports many, but not every registered effect. `noise_reduction`, `motion_blur`, and `glitch` appear registered without matching final-render cases.

### OpenCut Classic catalog

OpenCut Classic has a modular effect registry and effect-track architecture, but the actual default catalog found in source contains only Blur.

### Effect verdict

- **Catalog breadth:** Rushframe
- **GPU effect architecture and parameter animation:** OpenCut Classic
- **End-to-end consistency:** neither is complete; both need registry-to-renderer contract tests

## Color correction

Rushframe supports modeled and rendered controls such as:

- brightness
- contrast
- saturation
- exposure contribution
- tint/hue
- black and white

OpenCut Classic has rich color-picking and blend functionality, but no comparable clip-level grading system was found.

**Winner: Rushframe.**

## Chroma key and stabilization

Rushframe models and renders:

- chroma key
- stabilization/deshake

OpenCut Classic has no equivalent implementation found.

**Winner: Rushframe.**

## Masks

Rushframe domain mask shapes:

- rectangle
- ellipse
- linear
- mirror
- star
- polygon
- split

However, the FFmpeg exporter currently handles only a rectangle-like mask by applying crop. Other mask shapes are modeled but not rendered.

OpenCut Classic supports interactive masks including:

- split
- rectangle
- ellipse
- star
- heart
- diamond
- cinematic bars
- feather and inversion
- preview handles

Its unpublished next changelog also references text and custom Bezier masks.

**Winner today: OpenCut Classic**, because its mask features are more end-to-end.

## Transitions

Rushframe models:

- cross dissolve
- slide
- zoom
- blur
- wipe
- whip pan
- mask transition

It also has transition edit commands and UI selection. However, the current FFmpeg timeline renderer does not consume the sequence transition list, so these are not complete final-output features.

OpenCut Classic has a Transitions tab, but its UI explicitly says the transitions view is coming soon. No actual transition timeline model was found.

**Verdict:** Rushframe has the stronger foundation, but neither currently has a complete transition workflow in the inspected code.

---

# 8. Audio

| Audio capability | Rushframe | OpenCut Classic |
|---|---:|---:|
| Multiple audio tracks | Yes | Yes |
| Music and voice track semantics | Yes | No dedicated kinds |
| Audio from video clips | Mixed during export | Supported and separable |
| Volume | Yes | Yes |
| Pan | Yes | No clear equivalent |
| Mute | Yes | Yes |
| Fade in/out | Yes | No complete equivalent found |
| Speed | Yes | Yes |
| Maintain pitch | Not explicit in model/UI | Yes |
| Waveforms | Media derivative support | Strong RMS waveform UI |
| Audio extraction | Yes | Source-audio separation action |
| Beat/music analysis | Yes, Python intelligence | No comparable deep analysis |
| Semantic sound events | Optional | No |
| Speaker diarization | Optional | No |

OpenCut Classic offers better waveform and maintain-pitch interaction. Rushframe has broader audio-processing and analysis capabilities.

---

# 9. Text, Fonts, Graphics, Stickers, and Built-in Assets

## Rushframe assets

Rushframe currently includes:

- application icon assets
- user-imported local media
- QA output videos, projects, screenshots, and review artifacts

It does not currently include a large built-in creative asset catalog.

## OpenCut Classic assets

OpenCut Classic includes or exposes:

- 1,000+ font choices through a font atlas/sprite system
- system fonts
- 271 country/organization flag SVGs
- logo stickers
- geometric shapes
- graphic definitions for rectangle, ellipse, polygon, and star
- platform-guide resources
- brand assets and downloadable logos
- remote Freesound search for sound effects

## Asset verdict

**OpenCut Classic is much stronger in built-in creative assets.**

## Rushframe recommendation

Add a local asset-provider abstraction rather than hard-coding a large catalog into editor logic. Each asset should include:

- stable ID
- local path or packaged resource
- type
- source/provider
- license and attribution requirements
- commercial-use status
- dimensions/duration
- checksum/version

Remote downloading should remain opt-in and controlled. Rushframe's existing product rule against arbitrary website downloading should not be weakened.

---

# 10. Media Import and Codec Handling

## Rushframe

- local file import
- FFprobe metadata detection
- FFmpeg proxies, thumbnails, waveforms, extraction, and export
- relinking offline media
- local filesystem paths
- codec/container support determined largely by FFmpeg

## OpenCut Classic

- browser file picker, drag-and-drop, and clipboard paste
- file-type validation
- browser quota checks before persistence
- thumbnail and metadata extraction
- warnings for browser-incompatible codecs such as HEVC
- browser OPFS storage

## Verdict

- **Codec/container reliability:** Rushframe
- **Import UX:** OpenCut Classic
- **Storage-capacity guardrails:** OpenCut Classic
- **Offline media relinking:** Rushframe

Rushframe should adopt OpenCut's preflight checks and clearer import warnings while retaining FFmpeg as the media compatibility layer.

---

# 11. Export

## Rushframe

- FFmpeg-based timeline export
- H.264 video and AAC audio
- custom dimensions and portrait/landscape presets
- cancellation support in media process execution
- final output written directly to local filesystem
- agent render endpoint with approval
- current configured output-duration limit: 180 seconds

## OpenCut Classic

- MP4 and WebM
- low, medium, high, and very-high quality
- optional output FPS
- optional audio
- AVC or VP9 video
- AAC or Opus audio
- browser WebCodecs/Mediabunny export
- cancellation and progress
- output returned as browser buffer/download

## Verdict

OpenCut offers a richer export settings surface. Rushframe offers more robust local processing and fewer browser memory/codec constraints.

Rushframe should add:

- codec/container presets
- quality/bitrate or CRF presets
- audio-only export
- frame sequence export
- transparent export where supported
- render-range/in-out export
- explicit FPS control
- hardware encoder detection with safe software fallback

---

# 12. Project Persistence, Recovery, and Migration

## Rushframe

- `.rushframe` local project files
- project repository abstraction
- autosave snapshots
- startup recovery
- migration backups
- legacy project import
- media relinking

## OpenCut Classic

- IndexedDB project metadata
- OPFS media files
- storage quota checks
- persistence requests to reduce browser eviction risk
- malformed-record checks
- rollback when media persistence partially fails
- more than 30 project schema migration steps
- extensive migration fixture tests

## Verdict

- **Desktop recovery and portable local files:** Rushframe
- **Schema migration discipline:** OpenCut Classic

Rushframe should introduce an explicit project schema version and sequential migration pipeline with fixture tests for every historical version.

---

# 13. Agent, MCP, Automation, and Guardrails

## Rushframe implemented agent workflow

Rushframe currently exposes a local editor bridge with:

- loopback-only listener (`127.0.0.1`)
- health endpoint
- timeline-state endpoint
- edit endpoint
- render endpoint
- audit endpoint
- preview-only edit validation
- user approval enabled by default
- edit commands executed through undo/redo
- autosave after accepted edits
- audit entries for success, rejection, and failure
- a maximum output duration
- local media-intelligence context and search tools

This is a real implementation, not only a roadmap item.

## OpenCut

Current OpenCut documentation promises:

- Editor API
- plugins
- MCP
- headless mode
- scripting

No corresponding implementation was found in either current `main` or the inspected Classic tag.

## Agent guardrail verdict

**Rushframe is substantially ahead.**

## Important Rushframe agent-security gaps

Rushframe should still improve the bridge:

1. **Add authentication.** Loopback-only is useful but insufficient against another local process or malicious webpage exploiting local services. Generate a per-session bearer token or named-pipe capability.
2. **Enforce timeline revisions.** The current bridge returns timeline state but does not appear to require a `base_version` or revision on edit requests. Stale-agent edits can overwrite assumptions.
3. **Restrict output paths.** Agent render currently accepts an arbitrary local output path after approval. Add an allowed output-root policy and explicit overwrite confirmation.
4. **Validate request size and content type.** Limit JSON body size and require expected methods/content types.
5. **Persist audit records.** The current audit log is in memory and capped at 200 entries.
6. **Add granular permissions.** Separate read timeline, propose edit, mutate project, analyze media, and render capabilities.
7. **Protect locked tracks/items.** Agent command construction should enforce lock state consistently.
8. **Require approval for destructive changes regardless of caller flags.** A caller should not be able to set `require_approval=false` unless the user explicitly enabled a trusted automation mode.

---

# 14. Web/Application Security Guardrails

OpenCut Classic is stronger in web-facing security because it contains a web application and remote API surface:

- schema validation with Zod
- request rate limiting through Upstash
- auth trusted-origin configuration
- email/password authentication
- input limits and enum validation
- output-response validation
- security reporting policy
- browser storage quota validation
- unsupported codec warnings
- accessibility and strict TypeScript/lint instructions

Rushframe has a smaller network surface and protects the Gemini key using local secret protection, but it needs stronger local-bridge authentication and payload controls.

These guardrail categories are different:

- **Agent/runtime mutation safety:** Rushframe wins.
- **Internet-facing API safety:** OpenCut Classic wins.
- **Coding-style/a11y instructions:** OpenCut has stricter documented rules.

---

# 15. Media Intelligence and AI

## Rushframe

Rushframe's local intelligence pipeline can generate:

- technical media metadata
- scene boundaries
- sampled scene frames
- word-timed transcript
- audio measurements
- music analysis
- editing moments
- hook potential scores
- duplicate-take groups
- optional OCR
- optional precise word alignment
- optional speaker diarization
- optional semantic sound events
- optional Gemini or local Qwen visual understanding
- SQLite FTS search
- optional semantic embeddings
- bounded agent-context JSON

## OpenCut Classic

OpenCut Classic includes local browser transcription using Hugging Face Transformers in a Web Worker, with:

- model-loading progress
- quantized model execution
- cancellation
- timestamped transcript segments
- caption generation
- transcript-file import

## Verdict

OpenCut has a good focused caption workflow. Rushframe is much more advanced in cross-modal editing intelligence and agent context.

---

# 16. Testing and QA

## Rushframe

- domain tests
- desktop tests
- media/FFmpeg tests
- legacy-import tests
- Python intelligence tests
- architecture tests
- persistence/recovery tests
- QA harness
- PowerShell UI automation
- TRX execution results
- defect report
- rendered videos and frame captures for manual review

## OpenCut Classic

- approximately 38 test files
- strong unit tests for timeline placement, animation, masks, ripple behavior, keybindings, audio logic, and storage migrations
- migration fixtures for many schema versions
- CI workflow

## Verdict

- **Manual/render QA evidence:** Rushframe
- **Fine-grained frontend algorithm tests:** OpenCut Classic
- **Migration regression testing:** OpenCut Classic
- **Native media integration testing:** Rushframe

Rushframe should add more deterministic interaction-level tests for snapping, group editing, animation evaluation, transitions, and renderer parity.

---

# 17. Extensibility and Plugins

## Rushframe

Rushframe already has lightweight registries and interfaces:

- effect registry
- media abstractions
- edit-command interface
- local agent bridge
- intelligence providers

However, it does not yet have a formal third-party plugin SDK or sandbox.

## OpenCut

OpenCut current README promises first-class plugins and an Editor API, but no plugin runtime or permission system was found.

OpenCut Classic's definition/provider registries are good internal extensibility patterns:

- effects registry
- graphics registry
- sticker provider registry
- renderer node architecture
- action registry

## Recommendation

Rushframe should build an internal extension contract before calling it a plugin system:

- signed manifest
- declared capabilities
- versioned API
- no arbitrary process execution by default
- isolated asset/effect/action providers
- explicit filesystem and network permissions
- deterministic serialization IDs
- uninstall/missing-plugin behavior
- render fallback or clear failure

---

# 18. Important Implemented-vs-Modeled Gaps in Rushframe

Rushframe's domain model is broader than its complete runtime behavior. These should not be advertised as finished until preview, export, persistence, UI, undo, and tests all agree.

## Current gaps found

- transitions exist in the model and commands but are not used by FFmpeg timeline export
- keyframes exist but are not evaluated by export
- Bezier interpolation fields exist but evaluation falls back to linear
- only one animated property is stored per timeline item
- multiple mask shapes exist, but export currently handles only a rectangle/crop case
- sticker and adjustment-layer item kinds exist but are not included in the current visual export selection
- text outline and shadow are modeled but not included in current FFmpeg text rendering
- some registered effects have no final-render implementation
- agent edits do not appear to enforce a base timeline revision
- audit records are not durable

OpenCut Classic also has incomplete areas, especially transitions and effect breadth, but Rushframe should be strict about distinguishing model coverage from completed feature coverage.

---

# 19. What Rushframe Should Copy or Adapt

## Priority 0 — correctness and architectural risks

1. Complete transition rendering and preview parity.
2. Replace the single animated property with typed animation channels.
3. Evaluate animation consistently in preview and export.
4. Complete all mask shapes or disable unsupported ones in UI/export.
5. Enforce a registry contract so every listed effect has preview/export support.
6. Add agent-session authentication and timeline revision checks.
7. Add renderer capability tests for every modeled feature.

## Priority 1 — major editor UX improvements

1. GPU-backed real-time compositor.
2. Direct move/scale/rotate controls in preview.
3. Preview snapping guides.
4. Keyframe lanes and Bezier graph editor.
5. Multi-item selection, group move, and group resize.
6. Richer waveform rendering and draggable volume line.
7. Action registry with user-configurable shortcuts.
8. Rich bookmarks with notes, colors, and durations.
9. Explicit schema-version migration pipeline with fixtures.
10. Integer-tick time and rational frame-rate model.

## Priority 2 — creative and ecosystem improvements

1. Local sticker/shape/graphic providers.
2. Licensed font-provider system.
3. Platform safe-area/layout guides.
4. Background color, gradient, and blur editor.
5. More export formats and quality presets.
6. Optional licensed sound-library provider with attribution tracking.
7. Internal extension manifests and capability declarations.

---

# 20. What Rushframe Should Not Copy

- Do not replace local filesystem projects with browser OPFS.
- Do not make cloud services mandatory for core editing.
- Do not add arbitrary URL/social-media download behavior.
- Do not expose a plugin runtime before permissions and sandbox rules exist.
- Do not rely exclusively on browser codec support.
- Do not copy the current OpenCut rewrite's roadmap claims without implementations.
- Do not split one source of truth across C#, Python, JavaScript, and native code without versioned contracts.
- Do not move agent prompting into the editor UI; keep the editor as controlled preview/edit execution surface.

---

# 21. Recommended Combined Architecture for Rushframe

A strong future Rushframe architecture would keep its current domain and agent model while adopting selected OpenCut ideas:

```text
Rushframe Domain
  Project / Sequence / Tracks / Items
  Commands / Undo / Revisions
  Animation channels / evaluated scene graph
            |
            +--> Real-time GPU preview renderer
            |
            +--> FFmpeg final renderer
            |
            +--> Headless render service
            |
            +--> Agent proposal and validation layer

Asset Providers
  Local uploads
  Packaged shapes/stickers/fonts
  Optional licensed remote providers

Agent Gateway
  Session authentication
  Capability permissions
  Revision checking
  Preview-only proposals
  User approval
  Durable audit log
```

This would combine:

- Rushframe's local reliability, FFmpeg breadth, agent safety, and intelligence
- OpenCut Classic's GPU responsiveness, interaction design, animation UX, and provider registries

---

# 22. Final Scorecard

Scores represent the inspected code today, not future promises. `10` means strong and substantially implemented.

| Category | Rushframe | OpenCut current rewrite | OpenCut Classic |
|---|---:|---:|---:|
| Current usable editor | 7 | 1 | 8 |
| Timeline interaction UX | 6 | 1 | 9 |
| Real-time preview | 4 | 1 | 9 |
| Final render/media compatibility | 8 | 1 | 6 |
| Animation/keyframe UX | 3 | 1 | 9 |
| Effects catalog | 7 | 1 | 3 |
| Masks end-to-end | 3 | 1 | 8 |
| Color/chroma/stabilization | 8 | 1 | 3 |
| Audio editing | 7 | 1 | 7 |
| Built-in creative assets | 2 | 1 | 9 |
| Project recovery | 8 | 1 | 7 |
| Schema migration maturity | 4 | 1 | 9 |
| Agent/MCP implementation | 9 | 1 | 1 |
| Agent mutation guardrails | 8 | 1 | 1 |
| Web/API security guardrails | 4 | 3 | 8 |
| Media intelligence | 10 | 1 | 5 |
| Cross-platform reach | 3 | 2 | 8 |
| Test breadth | 8 | 3 | 8 |
| Manual QA evidence | 9 | 1 | 4 |
| Extensibility foundation | 6 | 2 | 8 |

## Overall assessment

- **Best current agent-oriented local editor foundation:** Rushframe
- **Best mature manual-editing UX reference:** OpenCut Classic
- **Best future cross-platform ambition:** OpenCut rewrite, but not yet implemented
- **Best path for Rushframe:** retain its architecture and selectively absorb OpenCut Classic patterns rather than porting or replacing the application
