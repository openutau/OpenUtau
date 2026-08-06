using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Api;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.AgentBridge {
    /// <summary>In-process MCP request dispatcher and local-file bridge. The HTTP listener is owned by McpService.</summary>
    public static class BridgeCore {
        private const int MaxRequestBytes = 1024 * 1024;
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private static readonly object Gate = new();
        private static CancellationTokenSource? cancellation;
        private static Task? worker;
        private static string? bridgeDirectory;
        private static string sessionId = Guid.NewGuid().ToString("N");
        private static UProject? currentProject;
        private static readonly Dictionary<object, string> objectIds = new(ReferenceEqualityComparer.Instance);
        private static readonly object EventGate = new();
        private static readonly Queue<StateEvent> stateEvents = new();
        private const int StateEventCapacity = 256;
        private static long nextEventSequence;
        private static bool commandObserverAttached;
        private static readonly BridgeCommandObserver commandObserver = new();
        private static long nextObjectId;
        private static long revision = 1;
        private static Action<UVoicePart, int>? loadPartAction;
        public static object? MainWindow { get; set; }

        public static void SetLoadPartAction(Action<UVoicePart, int> action) {
            lock (Gate) {
                loadPartAction = action;
            }
        }

        public static void Start() {
            lock (Gate) {
                if (worker != null) return;
                bridgeDirectory = ResolveBridgeDirectory();
                Directory.CreateDirectory(bridgeDirectory);
                RecoverInterruptedRequest(bridgeDirectory);
                cancellation = new CancellationTokenSource();
                worker = Task.Run(() => RunAsync(bridgeDirectory, cancellation.Token));
                EnsureCommandObserver();
                Log.Information("OpenUtau Agent Bridge started at {BridgeDirectory}.", bridgeDirectory);
            }
        }

        public static void Stop() {
            lock (Gate) {
                cancellation?.Cancel();
                cancellation?.Dispose();
                cancellation = null;
                worker = null;
            }
        }

        internal static string ResolveBridgeDirectory() {
            var configured = Environment.GetEnvironmentVariable("OPENUTAU_AGENT_BRIDGE_DIR")?.Trim();
            return string.IsNullOrWhiteSpace(configured) ? Path.GetTempPath() : Path.GetFullPath(configured);
        }

        private static async Task RunAsync(string directory, CancellationToken token) {
            var nextHeartbeat = DateTimeOffset.MinValue;
            while (!token.IsCancellationRequested) {
                try {
                    if (DateTimeOffset.UtcNow >= nextHeartbeat) {
                        PublishStatus(directory, "running");
                        nextHeartbeat = DateTimeOffset.UtcNow.AddSeconds(1);
                    }
                    ProcessRequest(directory);
                } catch (Exception ex) {
                    Log.Error(ex, "OpenUtau Agent Bridge worker failure.");
                }
                try {
                    await Task.Delay(100, token).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    break;
                }
            }
            PublishStatus(directory, "stopped");
        }

        internal static void ProcessRequest(string directory) {
            var requestPath = Path.Combine(directory, BridgeProtocol.RequestFileName);
            var processingPath = Path.Combine(directory, BridgeProtocol.ProcessingFileName);
            if (!File.Exists(requestPath) || File.Exists(processingPath)) return;
            try {
                File.Move(requestPath, processingPath);
            } catch (IOException) {
                return;
            }

            object response;
            try {
                if (new FileInfo(processingPath).Length > MaxRequestBytes) throw new BridgeException("REQUEST_TOO_LARGE", "request exceeds 1 MiB");
                using var document = JsonDocument.Parse(File.ReadAllText(processingPath, Encoding.UTF8));
                response = DispatchOnUiThread(document.RootElement);
            } catch (BridgeException ex) {
                response = Failure("invalid-request", ex.Code, ex.Message);
            } catch (Exception ex) {
                Log.Warning(ex, "OpenUtau Agent Bridge request failed.");
                response = Failure("invalid-request", "HOST_ERROR", ex.Message);
            }
            WriteJsonAtomically(Path.Combine(directory, BridgeProtocol.ResponseFileName), response);
            try {
                File.Move(processingPath, $"{processingPath}.{Guid.NewGuid():N}.completed");
            } catch (IOException ex) {
                Log.Warning(ex, "OpenUtau Agent Bridge could not archive processed request.");
            }
        }
        /// <summary>Runs a v2 envelope through the UI-thread dispatcher.</summary>
        internal static object DispatchRequest(JsonElement request) {
            EnsureCommandObserver();
            return DispatchOnUiThread(request);
        }

        private static object DispatchOnUiThread(JsonElement request) {
            var completion = new ManualResetEventSlim();
            object? response = null;
            Exception? exception = null;
            var copy = request.Clone();
            DocManager.Inst.PostOnUIThread(() => {
                try { response = HandleRequest(copy); } catch (Exception ex) { exception = ex; } finally { completion.Set(); }
            });
            if (!completion.Wait(TimeSpan.FromSeconds(15))) throw new BridgeException("UI_TIMEOUT", "UI thread did not complete the request");
            if (exception != null) throw exception;
            return response!;
        }

        private static object HandleRequest(JsonElement request) {
            var id = GetString(request, "id") ?? "invalid-request";
            if (!request.TryGetProperty("v", out var version) || !version.TryGetInt32(out var wireVersion) || wireVersion != BridgeProtocol.Version) {
                return Failure(id, "PROTOCOL_VERSION_MISMATCH", "expected v2 envelope");
            }
            var action = GetString(request, "a");
            if (!BridgeProtocol.IsSupportedAction(action)) return Failure(id, "UNSUPPORTED_ACTION", action ?? "missing action");
            var payload = request.TryGetProperty("p", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
            try {
                return Success(id, action switch {
                    "ping" => new { pong = true, sessionToken = sessionId },
                    "get_project_info" => GetProjectInfo(),
                    "get_state_snapshot" => GetStateSnapshot(payload),
                    "get_state_events" => GetStateEvents(payload),
                    "get_bridge_diagnostics" => GetBridgeDiagnostics(),
                    "set_track_config" => SetTrackConfig(payload),
                    "set_track_singer" => SetTrackSinger(payload),
                    "create_part" => CreatePart(payload),
                    "add_notes_simple" => AddNotesSimple(payload),
                    "edit_note_simple" => EditNoteSimple(payload),
                    "delete_notes_simple" => DeleteNotesSimple(payload),
                    "playback" => Playback(payload),
                    "get_editor_state" => GetEditorState(),
                    "save_file" => SaveFile(payload),
                    "load_file" => LoadFile(payload),
                    "navigate_editor" => NavigateEditor(),
                    "open_piano_roll" => OpenPianoRoll(payload),
                    _ => throw new BridgeException("UNSUPPORTED_ACTION", action!),
                });
            } catch (BridgeException ex) {
                return Failure(id, ex.Code, ex.Message);
            }
        }

        private static object GetProjectInfo() {
            var project = DocManager.Inst.Project;
            RefreshProjectContext(project);
            return new {
                name = project.name, filePath = project.FilePath, saved = project.Saved, resolution = project.resolution,
                endTick = project.EndTick,
                tracks = project.tracks.Select((track, index) => new {
                    index, name = track.TrackName, singerId = track.Singer?.Id, singerName = track.Singer?.Name,
                    track.Mute, track.Solo, track.Volume, track.Pan,
                }).ToArray(),
                parts = project.parts.Select((part, index) => new { index, part.name, part.position, duration = part.Duration, part.trackNo, type = part.GetType().Name }).ToArray(),
            };
        }

        private static object GetStateSnapshot(JsonElement payload) {
            var project = DocManager.Inst.Project;
            RefreshProjectContext(project);
            var partOffset = Math.Max(0, OptionalInt(payload, "partOffset") ?? 0);
            var partLimit = Math.Clamp(OptionalInt(payload, "partLimit") ?? 100, 1, 100);
            var noteOffset = Math.Max(0, OptionalInt(payload, "noteOffset") ?? 0);
            var noteLimit = Math.Clamp(OptionalInt(payload, "noteLimit") ?? 500, 1, 500);
            var fromTick = OptionalInt(payload, "fromTick");
            var toTick = OptionalInt(payload, "toTick");
            if (fromTick is < 0 || toTick is < 0 || (fromTick.HasValue && toTick.HasValue && fromTick > toTick)) {
                throw new BridgeException("INVALID_PAYLOAD", "tick range must be non-negative and ordered");
            }
            var matchingParts = project.parts
                .Select((part, index) => (part, index))
                .Where(item => !fromTick.HasValue || item.part.End >= fromTick.Value)
                .Where(item => !toTick.HasValue || item.part.position <= toTick.Value)
                .ToArray();
            var page = matchingParts.Skip(partOffset).Take(partLimit).Select(item => SnapshotPart(item.part, item.index, noteOffset, noteLimit)).ToArray();
            var nextPartOffset = partOffset + page.Length;
            return new {
                sessionId,
                revision,
                guard = new { sessionId, revision },
                project = new {
                    id = GetObjectId(project, "project"), project.name, filePath = project.FilePath, project.Saved,
                    project.resolution, project.key, endTick = project.EndTick,
                    tempos = project.tempos.Select(tempo => new { tempo.position, tempo.bpm }).ToArray(),
                    timeSignatures = project.timeSignatures.Select(signature => new { signature.barPosition, signature.beatPerBar, signature.beatUnit }).ToArray(),
                     tracks = project.tracks.Select((track, index) => new {
                         id = GetObjectId(track, "track"), index, track.TrackName, track.TrackColor,
                         singerId = track.Singer?.Id, singerName = track.Singer?.Name, phonemizer = track.Phonemizer?.GetType().FullName,
                         renderer = track.RendererSettings.Renderer?.GetType().FullName, track.Mute, track.Solo, track.Volume, track.Pan,
                         expressions = track.GetSupportedExps(project).Select(SnapshotExpression).ToArray(),
                     }).ToArray(),
                    parts = page,
                },
                playback = new {
                    playPosTick = DocManager.Inst.playPosTick, rangeStartTick = DocManager.Inst.rangeStartTick,
                    rangeEndTick = DocManager.Inst.rangeEndTick, playing = PlaybackManager.Inst.PlayingMaster,
                    starting = PlaybackManager.Inst.StartingToPlay,
                },
                editor = new { page = ReadMainWindowPage() },
                pageInfo = new { partOffset, partLimit, totalParts = matchingParts.Length, nextPartOffset = nextPartOffset < matchingParts.Length ? nextPartOffset : (int?)null, noteOffset, noteLimit, fromTick, toTick },
            };
        }

        private static object GetStateEvents(JsonElement payload) {
            var afterSequence = OptionalLong(payload, "afterSequence") ?? 0;
            if (afterSequence < 0) throw new BridgeException("INVALID_PAYLOAD", "afterSequence must be non-negative");
            lock (EventGate) {
                var earliest = stateEvents.Count == 0 ? nextEventSequence + 1 : stateEvents.Peek().Sequence;
                var requiresSnapshot = afterSequence > 0 && afterSequence < earliest - 1;
                var events = requiresSnapshot
                    ? Array.Empty<StateEvent>()
                    : stateEvents.Where(stateEvent => stateEvent.Sequence > afterSequence).ToArray();
                return new {
                    sessionId,
                    revision,
                    latestSequence = nextEventSequence,
                    requiresSnapshot,
                    events = events.Select(stateEvent => new {
                        sequence = stateEvent.Sequence,
                        stateEvent.Revision,
                        stateEvent.Source,
                        stateEvent.Type,
                    }).ToArray(),
                };
            }
        }

        private static object GetBridgeDiagnostics() {
            lock (EventGate) {
                return new {
                    sessionId,
                    revision,
                    latestEventSequence = nextEventSequence,
                    bufferedEventCount = stateEvents.Count,
                    ipcDirectory = bridgeDirectory,
                    mcp = McpService.Status,
                };
            }
        }

        private static object SnapshotPart(UPart part, int index, int noteOffset, int noteLimit) {
            var voicePart = part as UVoicePart;
            var notes = voicePart?.notes.Skip(noteOffset).Take(noteLimit)
                .Select((note, noteIndex) => SnapshotNote(note, noteOffset + noteIndex)).ToArray();
            var totalNotes = voicePart?.notes.Count ?? 0;
            var nextNoteOffset = noteOffset + (notes?.Length ?? 0);
            return new {
                id = GetObjectId(part, "part"), index, kind = part.GetType().Name, part.name, part.comment,
                trackIndex = part.trackNo, part.position, duration = part.Duration, end = part.End, notes,
                totalNotes, nextNoteOffset = nextNoteOffset < totalNotes ? nextNoteOffset : (int?)null,
            };
        }

        internal static object SnapshotNote(UNote note, int index) {
            return new {
                id = GetObjectId(note, "note"), index, note.position, note.duration, end = note.End,
                note.tone, note.lyric, note.tuning, phonemizerOverride = note.PhonemizerOverride,
                pitch = new {
                    snapFirst = note.pitch?.snapFirst ?? false,
                    points = note.pitch?.data.Select(point => new {
                        x = point.X, y = point.Y, shape = point.shape.ToString(), point.autoCompleted,
                    }).ToArray() ?? Array.Empty<object>(),
                },
                vibrato = note.vibrato == null ? null : new {
                    note.vibrato.length, note.vibrato.period, note.vibrato.depth,
                    fadeIn = note.vibrato.@in, fadeOut = note.vibrato.@out,
                    note.vibrato.shift, note.vibrato.drift, note.vibrato.volLink,
                },
                phonemeExpressions = note.phonemeExpressions.Select(expression => new {
                    expression.index, expression.abbr, expression.value,
                }).ToArray(),
                phonemeOverrides = note.phonemeOverrides.Select(overrideValue => new {
                    overrideValue.index, overrideValue.phoneme, overrideValue.offset,
                    overrideValue.preutterDelta, overrideValue.overlapDelta,
                    overrideValue.attackTimeDelta, overrideValue.releaseTimeDelta,
                }).ToArray(),
            };
        }

        private static object SnapshotExpression(UExpressionDescriptor descriptor) {
            return new {
                descriptor.name, descriptor.abbr, type = descriptor.type.ToString(), descriptor.min, descriptor.max,
                descriptor.defaultValue, customDefaultValue = descriptor.CustomDefaultValue, descriptor.isFlag,
                descriptor.flag, options = descriptor.options ?? Array.Empty<string>(), descriptor.skipOutputIfDefault,
            };
        }

        private static void RefreshProjectContext(UProject project) {
            if (ReferenceEquals(currentProject, project)) return;
            currentProject = project;
            objectIds.Clear();
            nextObjectId = 0;
            sessionId = Guid.NewGuid().ToString("N");
            revision++;
            lock (EventGate) {
                stateEvents.Clear();
            }
        }

        private static void AdvanceRevision() => revision++;

        private static void EnsureCommandObserver() {
            if (commandObserverAttached) return;
            DocManager.Inst.AddSubscriber(commandObserver);
            commandObserverAttached = true;
        }

        private static void RecordCommandEvent(UCommand command, bool isUndo) {
            if (command.Silent) return;
            lock (EventGate) {
                var eventRevision = ++revision;
                stateEvents.Enqueue(new StateEvent(++nextEventSequence, eventRevision, isUndo ? "undo" : "execute", command.GetType().Name));
                while (stateEvents.Count > StateEventCapacity) stateEvents.Dequeue();
            }
        }

        private static string GetObjectId(object value, string kind) {
            if (objectIds.TryGetValue(value, out var id)) return id;
            id = $"{sessionId}:{kind}:{++nextObjectId}";
            objectIds.Add(value, id);
            return id;
        }

        private static object SetTrackConfig(JsonElement payload) {
            var project = DocManager.Inst.Project;
            var index = RequiredInt(payload, "trackIndex");
            if (index < 0 || index >= project.tracks.Count) throw new BridgeException("INVALID_TRACK", "trackIndex is outside the project");
            var track = project.tracks[index];
            if (GetString(payload, "name") is { Length: > 0 } name) {
                DocManager.Inst.StartUndoGroup("agentbridge.settrackconfig");
                try { DocManager.Inst.ExecuteCmd(new RenameTrackCommand(project, track, name)); DocManager.Inst.EndUndoGroup(); }
                catch { DocManager.Inst.RollBackUndoGroup(); throw; }
            }
            if (TryBool(payload, "mute", out var mute)) track.Mute = mute;
            if (TryBool(payload, "solo", out var solo)) track.Solo = solo;
            if (TryDouble(payload, "volume", out var volume)) track.Volume = Math.Clamp(volume, 0, 2);
            if (TryDouble(payload, "pan", out var pan)) track.Pan = Math.Clamp(pan, -1, 1);
            project.ValidateFull();
            AdvanceRevision();
            return new { trackIndex = index, name = track.TrackName, track.Mute, track.Solo, track.Volume, track.Pan };
        }

        private static object SetTrackSinger(JsonElement payload) {
            var project = DocManager.Inst.Project;
            var index = RequiredInt(payload, "trackIndex");
            if (index < 0 || index >= project.tracks.Count) throw new BridgeException("INVALID_TRACK", "trackIndex is outside the project");
            var singerId = GetString(payload, "singerId")?.Trim();
            if (string.IsNullOrEmpty(singerId) || singerId.Length > 512) throw new BridgeException("INVALID_PAYLOAD", "singerId must be a non-empty string up to 512 characters");
            if (!SingerManager.Inst.Singers.TryGetValue(singerId, out var singer)) throw new BridgeException("SINGER_NOT_FOUND", "singerId is not installed");

            var track = project.tracks[index];
            DocManager.Inst.StartUndoGroup("agentbridge.settracksinger");
            try {
                DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(project, track, singer));
                var preferredPhonemizer = !string.IsNullOrEmpty(singer.Id) &&
                    Preferences.Default.SingerPhonemizers.TryGetValue(singer.Id, out var configuredPhonemizer)
                    ? configuredPhonemizer
                    : null;
                if (!string.IsNullOrEmpty(preferredPhonemizer) &&
                    TryChangePhonemizer(track, preferredPhonemizer)) {
                } else if (!string.IsNullOrEmpty(singer.DefaultPhonemizer) &&
                    TryChangePhonemizer(track, singer.DefaultPhonemizer)) {
                } else if (!string.IsNullOrEmpty(preferredPhonemizer) ||
                    !string.IsNullOrEmpty(singer.DefaultPhonemizer)) {
                    throw new BridgeException("PHONEMIZER_UNAVAILABLE", "no configured phonemizer is available for singerId");
                }
                if (!singer.Found || singer.SingerType != track.RendererSettings.Renderer?.SingerType) {
                    var settings = singer.Found
                        ? new URenderSettings { renderer = Renderers.GetDefaultRenderer(singer.SingerType) }
                        : new URenderSettings();
                    DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(project, track, settings));
                }
                DocManager.Inst.EndUndoGroup();
            } catch {
                DocManager.Inst.RollBackUndoGroup();
                throw;
            }
            project.ValidateFull();
            AdvanceRevision();
            return new {
                trackIndex = index, singerId = singer.Id, singerName = singer.Name,
                phonemizer = track.Phonemizer?.GetType().FullName,
                renderer = track.RendererSettings.Renderer?.GetType().FullName,
            };
        }

        private static object CreatePart(JsonElement payload) {
            var project = DocManager.Inst.Project;
            var trackIndex = RequiredInt(payload, "trackIndex");
            if (trackIndex < 0 || trackIndex >= project.tracks.Count) throw new BridgeException("INVALID_TRACK", "trackIndex is outside the project");
            var position = Math.Max(0, RequiredInt(payload, "position"));
            var duration = Math.Max(1, RequiredInt(payload, "duration"));
            var part = new UVoicePart { trackNo = trackIndex, position = position, duration = duration, name = GetString(payload, "name") ?? "New Part" };
            DocManager.Inst.StartUndoGroup("agentbridge.createpart");
            try { DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part)); DocManager.Inst.EndUndoGroup(); }
            catch { DocManager.Inst.RollBackUndoGroup(); throw; }
            AdvanceRevision();
            return new { partIndex = project.parts.IndexOf(part), part.name, part.position, part.duration, part.trackNo };
        }

        private static object AddNotesSimple(JsonElement payload) {
            var project = DocManager.Inst.Project;
            var partIndex = RequiredInt(payload, "partIndex");
            if (partIndex < 0 || partIndex >= project.parts.Count || project.parts[partIndex] is not UVoicePart part) throw new BridgeException("INVALID_PART", "partIndex must identify a voice part");
            if (!payload.TryGetProperty("notes", out var notesValue) || notesValue.ValueKind != JsonValueKind.Array) throw new BridgeException("INVALID_PAYLOAD", "notes must be an array");
            var notes = new List<UNote>();
            foreach (var note in notesValue.EnumerateArray()) {
                var position = Math.Max(0, RequiredInt(note, "position"));
                var duration = Math.Max(1, RequiredInt(note, "duration"));
                var tone = Math.Clamp(RequiredInt(note, "tone"), 0, 127);
                var created = project.CreateNote(tone, position, duration);
                created.lyric = GetString(note, "lyric") ?? "a";
                notes.Add(created);
            }
            if (notes.Count == 0) throw new BridgeException("INVALID_PAYLOAD", "notes must contain at least one note");
            DocManager.Inst.StartUndoGroup("agentbridge.addnotes");
            try { DocManager.Inst.ExecuteCmd(new AddNoteCommand(part, notes)); DocManager.Inst.EndUndoGroup(); }
            catch { DocManager.Inst.RollBackUndoGroup(); throw; }
            AdvanceRevision();
            return new { partIndex, added = notes.Count, noteCount = part.notes.Count };
        }

        private static object EditNoteSimple(JsonElement payload) {
            var (partIndex, part) = RequiredVoicePart(payload);
            var noteIndex = RequiredInt(payload, "noteIndex");
            if (noteIndex < 0 || noteIndex >= part.notes.Count) throw new BridgeException("INVALID_NOTE", "noteIndex is outside the voice part");
            var note = part.notes.ElementAt(noteIndex);
            var position = OptionalInt(payload, "position");
            var duration = OptionalInt(payload, "duration");
            var tone = OptionalInt(payload, "tone");
            var lyric = GetString(payload, "lyric");
            if (position is < 0) throw new BridgeException("INVALID_PAYLOAD", "position must be non-negative");
            if (duration is < 1) throw new BridgeException("INVALID_PAYLOAD", "duration must be at least 1");
            if (tone is < 0 or > 127) throw new BridgeException("INVALID_PAYLOAD", "tone must be within 0..127");
            if (lyric?.Length > 256) throw new BridgeException("INVALID_PAYLOAD", "lyric must be at most 256 characters");
            if (position == null && duration == null && tone == null && lyric == null) throw new BridgeException("INVALID_PAYLOAD", "provide position, duration, tone, or lyric");

            DocManager.Inst.StartUndoGroup("agentbridge.editnote");
            try {
                if (position != null || tone != null) {
                    DocManager.Inst.ExecuteCmd(new MoveNoteCommand(part, note, (position ?? note.position) - note.position, (tone ?? note.tone) - note.tone));
                }
                if (duration != null) {
                    DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(part, note, duration.Value - note.duration));
                }
                if (lyric != null) {
                    DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(part, note, lyric));
                }
                DocManager.Inst.EndUndoGroup();
            } catch {
                DocManager.Inst.RollBackUndoGroup();
                throw;
            }
            AdvanceRevision();
            return new { partIndex, noteIndex, note.position, note.duration, note.tone, note.lyric };
        }

        private static object DeleteNotesSimple(JsonElement payload) {
            var project = DocManager.Inst.Project;
            RefreshProjectContext(project);
            var (partIndex, part) = ResolveDeletePart(project, payload);
            var notes = ResolveDeleteNotes(part, payload);
            if (notes.Count == 0) throw new BridgeException("INVALID_PAYLOAD", "provide noteIds or noteIndices");
            DocManager.Inst.StartUndoGroup("agentbridge.deletenotes");
            try { DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(part, notes)); DocManager.Inst.EndUndoGroup(); }
            catch { DocManager.Inst.RollBackUndoGroup(); throw; }
            AdvanceRevision();
            return new { partIndex, partId = GetObjectId(part, "part"), deleted = notes.Count, noteCount = part.notes.Count, revision };
        }

        private static (int partIndex, UVoicePart part) ResolveDeletePart(UProject project, JsonElement payload) {
            var partId = GetString(payload, "partId");
            if (partId == null) return RequiredVoicePart(payload);
            RequireCurrentGuard(payload);
            var matches = project.parts
                .Select((part, index) => (part, index))
                .Where(item => item.part is UVoicePart && GetObjectId(item.part, "part") == partId)
                .ToArray();
            if (matches.Length != 1) throw new BridgeException("AMBIGUOUS_TARGET", "partId is not available in the current project session");
            return (matches[0].index, (UVoicePart)matches[0].part);
        }

        private static List<UNote> ResolveDeleteNotes(UVoicePart part, JsonElement payload) {
            if (payload.TryGetProperty("noteIds", out var idsValue)) {
                RequireCurrentGuard(payload);
                if (idsValue.ValueKind != JsonValueKind.Array) throw new BridgeException("INVALID_PAYLOAD", "noteIds must be an array");
                var ids = idsValue.EnumerateArray().Select(item => item.GetString()).ToArray();
                if (ids.Length == 0 || ids.Length > 1024 || ids.Any(string.IsNullOrEmpty) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) {
                    throw new BridgeException("INVALID_PAYLOAD", "noteIds must contain 1..1024 distinct IDs");
                }
                var notes = part.notes.Where(note => ids.Contains(GetObjectId(note, "note"), StringComparer.Ordinal)).ToList();
                if (notes.Count != ids.Length) throw new BridgeException("AMBIGUOUS_TARGET", "one or more noteIds are no longer available; read a fresh snapshot");
                return notes;
            }
            if (!payload.TryGetProperty("noteIndices", out var value) || value.ValueKind != JsonValueKind.Array) throw new BridgeException("INVALID_PAYLOAD", "noteIndices must be an array");
            var indices = new HashSet<int>();
            foreach (var item in value.EnumerateArray()) {
                if (!item.TryGetInt32(out var index) || index < 0 || index >= part.notes.Count) throw new BridgeException("INVALID_NOTE", "noteIndices must identify notes in the voice part");
                indices.Add(index);
                if (indices.Count > 1024) throw new BridgeException("INVALID_PAYLOAD", "noteIndices supports up to 1024 notes");
            }
            if (indices.Count == 0) throw new BridgeException("INVALID_PAYLOAD", "noteIndices must contain at least one note");
            return indices.OrderBy(index => index).Select(index => part.notes.ElementAt(index)).ToList();
        }

        private static void RequireCurrentGuard(JsonElement payload) {
            if (GetString(payload, "sessionId") != sessionId || OptionalLong(payload, "revision") != revision) {
                throw new BridgeException("STALE_CONTEXT", "sessionId and revision must match a fresh state snapshot");
            }
        }

        private static object Playback(JsonElement payload) {
            var operation = GetString(payload, "operation") ?? "status";
            var tick = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("tick", out var tickValue) && tickValue.TryGetInt32(out var requestedTick) ? Math.Max(0, requestedTick) : DocManager.Inst.playPosTick;
            switch (operation) {
                case "play": PlaybackManager.Inst.PlayOrPause(tick: tick); break;
                case "pause": PlaybackManager.Inst.PausePlayback(); break;
                case "stop": PlaybackManager.Inst.StopPlayback(); break;
                case "seek": DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(tick)); break;
                case "status": break;
                default: throw new BridgeException("INVALID_PAYLOAD", "operation must be status, play, pause, stop, or seek");
            }
            return new { operation, tick = DocManager.Inst.playPosTick, playing = PlaybackManager.Inst.PlayingMaster };
        }

        private static object GetEditorState() => new { page = ReadMainWindowPage(), playPosTick = DocManager.Inst.playPosTick, rangeStartTick = DocManager.Inst.rangeStartTick, rangeEndTick = DocManager.Inst.rangeEndTick };

        private static object SaveFile(JsonElement payload) {
            var path = RequiredUstxPath(payload);
            Format.Ustx.Save(path, DocManager.Inst.Project);
            return new { path, saved = DocManager.Inst.Project.Saved };
        }

        private static object LoadFile(JsonElement payload) {
            var path = RequiredUstxPath(payload);
            if (!File.Exists(path)) throw new BridgeException("FILE_NOT_FOUND", "project file does not exist");
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(Format.Ustx.Load(path)));
            return new { path, loaded = true, name = DocManager.Inst.Project.name };
        }

        private static object NavigateEditor() {
            var part = DocManager.Inst.Project.parts.LastOrDefault(part => part is UVoicePart) as UVoicePart;
            if (part == null) throw new BridgeException("INVALID_PART", "project has no voice part");
            return ShowPianoRoll(part, part.position);
        }

        private static object OpenPianoRoll(JsonElement payload) {
            var (partIndex, part) = RequiredVoicePart(payload);
            var tick = OptionalInt(payload, "tick") ?? part.position;
            if (tick < 0) throw new BridgeException("INVALID_PAYLOAD", "tick must be non-negative");
            return ShowPianoRoll(part, tick, partIndex);
        }

        private static object ShowPianoRoll(UVoicePart part, int tick, int? partIndex = null) {
            var action = loadPartAction ?? throw new BridgeException("UI_UNAVAILABLE", "editor part loader is unavailable");
            action(part, tick);
            return new { navigated = true, page = 1, partIndex, tick };
        }

        private static int? ReadMainWindowPage() {
            dynamic? mainWindow = MainWindow;
            if (mainWindow?.DataContext is null) return null;
            dynamic viewModel = mainWindow.DataContext;
            return viewModel.Page;
        }

        private static string RequiredUstxPath(JsonElement payload) {
            var path = GetString(payload, "path");
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".ustx", StringComparison.OrdinalIgnoreCase)) throw new BridgeException("INVALID_PATH", "path must be an absolute .ustx path");
            return Path.GetFullPath(path);
        }
        private static (int partIndex, UVoicePart part) RequiredVoicePart(JsonElement payload) {
            var project = DocManager.Inst.Project;
            var partIndex = RequiredInt(payload, "partIndex");
            if (partIndex < 0 || partIndex >= project.parts.Count || project.parts[partIndex] is not UVoicePart part) throw new BridgeException("INVALID_PART", "partIndex must identify a voice part");
            return (partIndex, part);
        }
        private static int? OptionalInt(JsonElement value, string name) {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property)) return null;
            return property.TryGetInt32(out var result) ? result : throw new BridgeException("INVALID_PAYLOAD", $"{name} must be an integer");
        }
        private static long? OptionalLong(JsonElement value, string name) {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property)) return null;
            return property.TryGetInt64(out var result) ? result : throw new BridgeException("INVALID_PAYLOAD", $"{name} must be an integer");
        }
        private static bool TryChangePhonemizer(UTrack track, string phonemizerName) {
            try {
                var phonemizer = PhonemizerFactory.Get(phonemizerName)?.Create();
                if (phonemizer == null) return false;
                DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, track, phonemizer));
                return true;
            } catch (Exception e) {
                Log.Warning(e, "Agent Bridge could not load phonemizer {PhonemizerName}.", phonemizerName);
                return false;
            }
        }

        private static void PublishStatus(string directory, string state) => WriteJsonAtomically(Path.Combine(directory, BridgeProtocol.StatusFileName), new { v = BridgeProtocol.Version, state, bridgeVersion = BridgeProtocol.BridgeVersion, updatedAtEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionToken = sessionId, ipcDirectory = directory, projectFile = GetProjectFilePathOnUiThread() });
        private static string GetProjectFilePathOnUiThread() {
            var completion = new ManualResetEventSlim();
            string? filePath = null;
            DocManager.Inst.PostOnUIThread(() => { filePath = DocManager.Inst.Project.FilePath; completion.Set(); });
            if (!completion.Wait(TimeSpan.FromSeconds(15))) throw new BridgeException("UI_TIMEOUT", "UI thread did not provide project status");
            return filePath ?? string.Empty;
        }
        internal static void WriteJsonAtomically(string path, object value) { var temp = $"{path}.{Guid.NewGuid():N}.tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false)); File.Move(temp, path, true); }
        private static void RecoverInterruptedRequest(string directory) { var processing = Path.Combine(directory, BridgeProtocol.ProcessingFileName); if (File.Exists(processing)) { try { File.Move(processing, $"{processing}.{Guid.NewGuid():N}.interrupted"); } catch (IOException) { } } }

        private static object Success(string id, object result) => new { v = BridgeProtocol.Version, id, r = result };
        private static object Failure(string id, string code, string message) => new { v = BridgeProtocol.Version, id, e = new { code, message } };
        private static string? GetString(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        private static int RequiredInt(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.TryGetInt32(out var result) ? result : throw new BridgeException("INVALID_PAYLOAD", $"{name} must be an integer");
        private static bool TryBool(JsonElement value, string name, out bool result) {
            result = false;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property) || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            result = property.GetBoolean();
            return true;
        }
        private static bool TryDouble(JsonElement value, string name, out double result) { result = 0; return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.TryGetDouble(out result); }
        private sealed class BridgeException : Exception { public string Code { get; } public BridgeException(string code, string message) : base(message) { Code = code; } }
        private sealed class BridgeCommandObserver : ICmdSubscriber {
            public void OnNext(UCommand command, bool isUndo) => RecordCommandEvent(command, isUndo);
        }
        private sealed record StateEvent(long Sequence, long Revision, string Source, string Type);
    }
}
