using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Midi;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class MidiInViewModel {
        private static MidiIn? midiIn;
        private NotesViewModel notesViewModel;
        private UVoicePart? part => notesViewModel.Part;
        public bool WaitingMidiInput { get; private set; } = false;
        public bool StepIn { get; private set; } = true;

        private int activeTone = 0;
        private UNote? recordingNote;
        private CancellationTokenSource? _noteExpandCts;
        private int ExpandIntervalMs = 300; // 更新間隔＝入力感度 (ミリ秒) Prefs

        public MidiInViewModel(NotesViewModel notesViewModel) {
            this.notesViewModel = notesViewModel;
        }

        public void InitDevice() {
            try {
                int deviceCount = MidiIn.NumberOfDevices;
                if (deviceCount == 0) {
                    DocManager.Inst.ExecuteCmd(new ToastNotification("Pianoroll", "No MIDI input device was found.", "No MIDI input device was found.")); // Todo
                    StopMidiInput();
                    return;
                }

                Dictionary<int, string> deviceDict = new Dictionary<int, string>();
                for (int i = 0; i < deviceCount; i++) {
                    deviceDict.Add(i, MidiIn.DeviceInfo(i).ProductName);
                }
                int midiInDeviceNumber = deviceCount - 1;
                // preferenceを参照
                SelectMidiDevice(midiInDeviceNumber);

            } catch (Exception e) {
                Log.Error(e, "Failed to open MIDI device");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                StopMidiInput();
            }
        }

        public void SelectMidiDevice(int deviceNumber) {
            StopMidiInput();
            if (deviceNumber < 0 || deviceNumber >= MidiIn.NumberOfDevices) {
                throw new ArgumentOutOfRangeException(nameof(deviceNumber), "Invalid MIDI device number.");
            }

            midiIn = new MidiIn(deviceNumber);
            midiIn.MessageReceived += MidiIn_MessageReceived;
            midiIn.ErrorReceived += MidiIn_ErrorReceived;
            midiIn.Start();
            WaitingMidiInput = true;

            string deviceName = MidiIn.DeviceInfo(deviceNumber).ProductName;
            Log.Debug($"MIDI input device selected: {deviceName}");
            // preference保存
        }

        public void StopMidiInput() {
            if (midiIn != null) {
                midiIn.Stop();
                midiIn.Dispose();
                midiIn = null;
            }
            WaitingMidiInput = false;
        }

        public void ToggleStepIn() {
            NoteOff(activeTone);
            StepIn = !StepIn;
        }

        private void MidiIn_MessageReceived(object? sender, MidiInMessageEventArgs arg) {
            try {
                if (!WaitingMidiInput) return;

                MidiCommandCode commandCode = arg.MidiEvent.CommandCode;
                switch (commandCode) {
                    case MidiCommandCode.NoteOn:
                        NoteOnEvent noteOn = (NoteOnEvent)arg.MidiEvent;
                        if (noteOn.Velocity > 0) {
                            Log.Debug($"[MIDI NOTE ON] Channel: {noteOn.Channel}, Pitch: {noteOn.NoteNumber} ({noteOn.NoteName}), Velocity: {noteOn.Velocity}");
                            NoteOn(noteOn.NoteNumber);
                        } else {
                            Log.Debug($"[MIDI NOTE OFF (Velocity=0)] Channel: {noteOn.Channel}, Pitch: {noteOn.NoteNumber} ({noteOn.NoteName}), Velocity: {noteOn.Velocity}");
                            NoteOff(noteOn.NoteNumber);
                        }
                        break;
                    case MidiCommandCode.NoteOff:
                        NoteEvent noteOff = (NoteEvent)arg.MidiEvent;

                        Log.Debug($"[MIDI NOTE OFF] Channel: {noteOff.Channel}, Pitch: {noteOff.NoteNumber} ({noteOff.NoteName})");
                        NoteOff(noteOff.NoteNumber);
                        break;
                    case MidiCommandCode.ControlChange:
                        ControlChangeEvent controlChange = (ControlChangeEvent)arg.MidiEvent;
                        Log.Debug($"[MIDI CONTROL CHANGE] Channel: {controlChange.Channel}, Controller: {controlChange.Controller}, Value: {controlChange.ControllerValue}");
                        break;
                    default:
                        Log.Debug($"[MIDI OTHER MESSAGES] Command: {commandCode}");
                        break;
                }
            } catch (Exception e) {
                Log.Error(e, "Error processing MIDI message");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        private void NoteOn(int tone) {
            if (part == null) return;

            // Monitor
            if (activeTone != tone) {
                NoteOff(activeTone);
            }
            PlaybackManager.Inst.PlayTone(MusicMath.ToneToFreq(tone));
            activeTone = tone;

            var playhead = DocManager.Inst.playPosTick;
            if (playhead < part.position || part.End <= playhead) {
                DocManager.Inst.ExecuteCmd(new ToastNotification("Pianoroll", "The playhead is not within the range of the part.", "The playhead is not within the range of the part.")); // Todo
                return;
            }
            playhead = playhead - part.position;

            if (PlaybackManager.Inst.PlayingMaster) {
                // MIDI Recording
                // Todo

            } else if (StepIn) {
                // Step Input
                var project = DocManager.Inst.Project;
                var SnapDiv = notesViewModel.SnapDiv;
                int snapUnit = project.resolution * 4 / SnapDiv;
                int snappedTick = (int)Math.Floor((double)playhead / snapUnit) * snapUnit;

                UNote note = project.CreateNote(tone, snappedTick, snapUnit);
                recordingNote = note;

                // Extend note while holding the key
                _noteExpandCts?.Cancel();
                _noteExpandCts = new CancellationTokenSource();
                var token = _noteExpandCts.Token;
                Task.Run(async () => {
                    try {
                        while (!token.IsCancellationRequested) {
                            await Task.Delay(ExpandIntervalMs, token);

                            DocManager.Inst.PostOnUIThread(() => {
                                if (recordingNote != null) {
                                    DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(part, recordingNote, snapUnit));
                                    DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(note.End + part.position));
                                }
                            });
                        }
                    } catch (TaskCanceledException) {
                        // Do nothing when the key is released
                    }
                }, token);

                // Create new note
                DocManager.Inst.PostOnUIThread(() => {
                    DocManager.Inst.StartUndoGroup("command.note.add");
                    DocManager.Inst.ExecuteCmd(new AddNoteCommand(part, note));
                    DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(note.End + part.position));
                });
            }
        }

        private void NoteOff(int tone) {
            // Monitor
            PlaybackManager.Inst.EndTone(MusicMath.ToneToFreq(tone));
            activeTone = 0;

            if (_noteExpandCts != null && recordingNote != null && recordingNote.tone == tone) {
                _noteExpandCts.Cancel();
                _noteExpandCts = null;
                DocManager.Inst.PostOnUIThread(DocManager.Inst.EndUndoGroup);
                recordingNote = null;
            }
        }

        private void MidiIn_ErrorReceived(object? sender, MidiInMessageEventArgs e) {
            DocManager.Inst.ExecuteCmd(new ToastNotification("Pianoroll", "MIDI input error", $"MIDI input error: {e.RawMessage}"));
        }
    }
}
