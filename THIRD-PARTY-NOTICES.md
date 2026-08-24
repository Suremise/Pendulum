# Third-Party Notices

Pendulum is built on the following open-source software. None of it is modified —
each is used as published, via NuGet package references or as a separately
downloaded, user-installed engine/model (Whisper, Piper). This file lists what's
in use and under what license; each project's own repository is the authoritative
source for its full license text.

## Referenced via NuGet (compiled into Pendulum)

| Library | License | Author / Project |
|---|---|---|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT | .NET Foundation and Contributors |
| [WPF-UI](https://github.com/lepoco/wpfui) | MIT | Leszek Pomianowski and WPF UI Contributors |
| [NAudio](https://github.com/naudio/NAudio) | MIT | Mark Heath and NAudio Contributors |
| [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) | MIT | Sergey Kazakov (HavenDV) |
| [System.Speech](https://github.com/dotnet/runtime) | MIT | .NET Foundation and Contributors |
| [Whisper.net](https://github.com/sandrohanea/whisper.net) (+ Whisper.net.Runtime) | MIT | Sandro Hanea |
| [whisper.cpp](https://github.com/ggml-org/whisper.cpp) (native library wrapped by Whisper.net) | MIT | Georgi Gerganov and Contributors |

## Downloaded separately by the user (not bundled with Pendulum)

These are optional, offline speech engines/models the user opts into via
Settings — Pendulum never downloads or bundles them itself; the user fetches
them from the links Settings provides.

| Component | License | Source |
|---|---|---|
| Whisper ggml models | MIT (OpenAI Whisper model weights) | [huggingface.co/ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp) |
| Piper (portable Windows engine) | MIT | [github.com/rhasspy/piper](https://github.com/rhasspy/piper) |
| Piper voices | MIT (individual voices may carry their own attribution from their source dataset — see each voice's MODEL_CARD) | [huggingface.co/rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices) |

## Fonts / icons

Panel and button icons use glyphs from **Segoe Fluent Icons** / **Segoe MDL2
Assets**, system fonts included with Windows. They're referenced by codepoint
at render time, not bundled with the app.
