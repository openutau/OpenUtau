using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using System.Collections.Concurrent;

namespace OpenUtau.Core {
    public class SineGenerator : ISampleProvider {
        public WaveFormat WaveFormat => waveFormat;
        private WaveFormat waveFormat;

        private readonly double attackSampleCount;
        private readonly double releaseSampleCount;
        private int startSampleOffset;

        public double freq { get; set; }

        private int position;
        private int releasePosition = 0;
        private float gain = 1;

        public bool isActive { get; private set; } = true;
        public bool isPlaying { get; private set; } = true;

        public SineGenerator(double freq, float gain, int attackMs = 25, int releaseMs = 25) {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            this.freq = freq;
            this.gain = gain;
            position = 0;

            // Number of samples the attack & release fades take
            attackSampleCount = (attackMs / 1000.0f) * waveFormat.SampleRate;
            releaseSampleCount = (releaseMs / 1000.0f) * waveFormat.SampleRate;
        }

        public SineGenerator(double freq, float gain, int attackMs, int releaseMs, int startSampleOffset)
            : this(freq, gain, attackMs, releaseMs) {
            this.startSampleOffset = Math.Max(0, startSampleOffset);
        }

        public void SetGain(float gain) {
            this.gain = gain;
        }

        public int Read(float[] buffer, int offset, int count) {
            // Duplicate sample across two channels
            for (int i = 0; i < count / 2; i++) {
                float sample = i < startSampleOffset ? 0 : GetNextSample();
                buffer[offset + (i * 2)] += (float)sample * gain;
                buffer[offset + (i * 2) + 1] += (float)sample * gain;
            }
            startSampleOffset = Math.Max(0, startSampleOffset - count / 2);
            return count;
        }

        private float GetNextSample() {
            double delta = 2 * Math.PI * freq / waveFormat.SampleRate;
            double sample = Math.Sin(position * delta);

            // Calculate attack envelope
            sample *= Math.Clamp(position / attackSampleCount, 0, 1);

            // Calculate release envelope
            double releaseEnvelope = 1;
            if (!isActive) {
                releaseEnvelope = Math.Clamp(1.0f - ((position - releasePosition) / releaseSampleCount), 0, 1);
            }
            sample *= releaseEnvelope;

            if (releaseEnvelope < double.Epsilon) {
                // Stop sampling this generator if release is completed
                // Instance will be cleaned up later
                isPlaying = false;
            }

            position++;
            return (float)sample * gain;
        }

        public void Stop() {
            if (!isActive) return;

            isActive = false;
            releasePosition = position;
        }
    }

    public class ToneGenerator : ISignalSource {
        private Dictionary<double, SineGenerator> activeFrequencies = new Dictionary<double, SineGenerator>();
        private List<SineGenerator> inactiveFrequencies = new List<SineGenerator>();
        private float gain = 0.4f;

        private readonly object _lockObj = new object();

        public ToneGenerator() {}

        public ToneGenerator(float gain) {
            this.gain = gain;
        }

        public void SetGain(float gain) {
            this.gain = gain;
            lock (_lockObj) {
                foreach (var generator in activeFrequencies.Values) {
                    generator.SetGain(gain);
                }
                foreach (var generator in inactiveFrequencies) {
                    generator.SetGain(gain);
                }
            }
        }

        public bool IsReady(int position, int count) {
            return true;
        }

        public int Mix(int position, float[] buffer, int offset, int count) {
            lock (_lockObj) {
                foreach (var freqEntry in activeFrequencies) {
                    if (freqEntry.Value.isPlaying) {
                        freqEntry.Value.Read(buffer, offset, count);
                    }
                }
                foreach (var generator in inactiveFrequencies) {
                    if (generator.isPlaying) {
                        generator.Read(buffer, offset, count);
                    }
                }
            }

            return position + count;
        }
        public void StartTone(double freq) {
            lock (_lockObj) {
                if (activeFrequencies.ContainsKey(freq)) {
                    if (activeFrequencies[freq].isActive) {
                        // Don't cut off tone to replace with the same frequency
                        // Should never happen
                        return;
                    }
                }
                activeFrequencies[freq] = new SineGenerator(freq, gain);
            }
        }

        public void StartTone(double freq, int attackMs, int releaseMs, int startSampleOffset) {
            lock (_lockObj) {
                if (activeFrequencies.ContainsKey(freq)) {
                    if (activeFrequencies[freq].isActive) {
                        return;
                    }
                }
                activeFrequencies[freq] = new SineGenerator(freq, gain, attackMs, releaseMs, startSampleOffset);
            }
        }

        public void StartTones(int startSampleOffset, params (double freq, int attackMs, int releaseMs)[] tones) {
            foreach (var tone in tones) {
                StartTone(tone.freq, tone.attackMs, tone.releaseMs, startSampleOffset);
            }
        }

        public void EndTones(params double[] freqs) {
            foreach (var freq in freqs) {
                EndTone(freq);
            }
        }

        public void EndTone(double freq) {

            lock (_lockObj) {
                if (activeFrequencies.ContainsKey(freq)) {
                    activeFrequencies[freq].Stop();
                    // Move to inactive frequencies list
                    inactiveFrequencies.Add(activeFrequencies[freq]);
                    activeFrequencies.Remove(freq);
                }
            }
            CleanupTones();
        }

        public void EndAllTones() {
            lock (_lockObj) {
                foreach (var tone in activeFrequencies.Values) {
                    tone.Stop();
                    inactiveFrequencies.Add(tone);
                }
                activeFrequencies.Clear();
            }

            CleanupTones();
        }

        private void CleanupTones() {
            lock (_lockObj) {
                inactiveFrequencies.RemoveAll(gen => !gen.isPlaying);
            }
        }
    }

    class PlaybackMix : ISignalSource {
        private readonly ISignalSource masterSource;
        private readonly ISignalSource overlaySource;
        private volatile bool masterExhausted;

        // True once the master source stops producing audio, release tails included.
        // The overlay can always produce sound, so the mix itself never reports
        // end-of-stream and playback has to be stopped by play position instead.
        public bool MasterExhausted => masterExhausted;

        public PlaybackMix(ISignalSource masterSource, ISignalSource overlaySource) {
            this.masterSource = masterSource;
            this.overlaySource = overlaySource;
        }

        public bool IsReady(int position, int count) {
            return masterSource.IsReady(position, count) && overlaySource.IsReady(position, count);
        }

        public int Mix(int position, float[] buffer, int offset, int count) {
            int masterPos = masterSource.Mix(position, buffer, offset, count);
            int overlayPos = overlaySource.Mix(position, buffer, offset, count);
            masterExhausted = masterPos <= position;
            return Math.Max(masterPos, overlayPos);
        }
    }

    public class PlaybackManager : SingletonBase<PlaybackManager>, ICmdSubscriber {
        public ConcurrentDictionary<string, (int trackNo, double posMs, float[] samples, DateTime renderTime)> LiveWaveformCache = new ConcurrentDictionary<string, (int, double, float[], DateTime)>();
        public bool IsWaveformBlanked { get; set; } = false;
        private PlaybackManager() {
            DocManager.Inst.AddSubscriber(this);
            try {
                Directory.CreateDirectory(PathManager.Inst.CachePath);
                RenderEngine.ReleaseSourceTemp();
            } catch (Exception e) {
                Log.Error(e, "Failed to release source temp.");
            }

            toneGenerator = new ToneGenerator();
            metronomeEngine = new MetronomeEngine();
            metronomeEngine.SetEnabled(Preferences.Default.Metronome);
            editingMix = new MasterAdapter(toneGenerator);
        }

        public readonly ToneGenerator toneGenerator;
        private readonly MetronomeEngine metronomeEngine;
        List<Fader> faders;
        MasterAdapter masterMix;
        MasterAdapter editingMix;
        
        double startMs;
        public int StartTick => DocManager.Inst.Project.timeAxis.MsPosToTickPos(startMs);
        CancellationTokenSource renderCancellation;
        UVoicePart preRenderFocusPart;
        int preRenderFocusTick = -1;

        // Active playback range captured when playback starts. Its end remains a stop
        // boundary even when looping is disabled.
        private int playbackRangeStartTick = 0;
        private int playbackRangeEndTick = -1;
        // Tick where the current playback must end, -1 when unbounded (empty project).
        private int playbackEndTick = -1;
        private PlaybackMix playbackMix;
        private bool loopProjectOnPlaybackEnd;

        public Audio.IAudioOutput AudioOutput { get; set; } = new Audio.DummyAudioOutput();
        public bool OutputActive => AudioOutput.PlaybackState == PlaybackState.Playing;
        public bool StartingToPlay { get; private set; }
        public bool PlayingMaster { get; private set; }
        public bool LoopPlayback { get; set; }
        public bool Metronome {
            get => metronomeEngine.Enabled;
            set {
                if (metronomeEngine.Enabled == value) {
                    return;
                }
                Preferences.Default.Metronome = value;
                Preferences.Save();
                metronomeEngine.SetEnabled(
                    value,
                    PlayingMaster ? DocManager.Inst.Project.timeAxis : null,
                    PlayingMaster ? DocManager.Inst.playPosTick : -1);
            }
        }

        public void PlayTestSound() {
            masterMix = null;
            PlayingMaster = false;
            AudioOutput.Stop();
            AudioOutput.Init(new SignalGenerator(44100, 1).Take(TimeSpan.FromSeconds(1)));
            AudioOutput.Play();
        }

        public void PlayMetronomeClick() {
            masterMix = null;
            PlayingMaster = false;
            toneGenerator.EndAllTones();
            AudioOutput.Stop();
            AudioOutput.Init(new MixingSampleProvider(new[] {
                CreateMetronomePreviewTone(Preferences.Default.MetronomeHighFrequency, TimeSpan.Zero),
                CreateMetronomePreviewTone(Preferences.Default.MetronomeLowFrequency, TimeSpan.FromMilliseconds(300)),
            }) {
                ReadFully = true,
            });
            AudioOutput.Play();
        }

        private static ISampleProvider CreateMetronomePreviewTone(double frequency, TimeSpan delay) {
            return new OffsetSampleProvider(new SineGenerator(frequency, GetMetronomePreviewGain(), 5, 80)) {
                DelayBy = delay,
                Take = TimeSpan.FromMilliseconds(120),
            };
        }

        private static float GetMetronomePreviewGain() {
            return MathF.Sqrt(Math.Clamp(Preferences.Default.MetronomeVolume / 100f, 0f, 1f));
        }

        public static float GetMetronomeGain() {
            return GetMetronomePreviewGain();
        }

        public void PlayTone(double freq) {
            if (!Core.Util.Preferences.Default.PlayTone) {
                return;
            }
            toneGenerator.StartTone(freq);

            // If nothing is playing, start editing mix
            if (!OutputActive) {
                AudioOutput.Stop();
                AudioOutput.Init(editingMix);
                AudioOutput.Play();
            }
        }

        public void EndTone(double freq) {
            toneGenerator.EndTone(freq);
        }

        public void EndAllTones() {
            toneGenerator.EndAllTones();
        }

        public void PlayFile(string file) {
            masterMix = null;
            if (AudioOutput.PlaybackState == PlaybackState.Playing) {
                AudioOutput.Stop();
            }
            try{
                var playSound = Wave.OpenFile(file);
                AudioOutput.Init(playSound.ToSampleProvider());
            } catch (Exception ex) {
                Log.Error(ex, $"Failed to load sample {file}.");
                return;
            }
            AudioOutput.Play();
        } 

        public void PlayOrPause(int tick = -1, int endTick = -1, int trackNo = -1) {
            if (PlayingMaster) {
                PausePlayback();
            } else {
                int rangeStart = DocManager.Inst.rangeStartTick;
                int rangeEnd = DocManager.Inst.rangeEndTick;
                if (endTick == -1 && rangeEnd > rangeStart) {
                    int playPos = DocManager.Inst.playPosTick;
                    playbackRangeStartTick = rangeStart;
                    playbackRangeEndTick = rangeEnd;
                    loopProjectOnPlaybackEnd = false;
                    Play(
                        DocManager.Inst.Project,
                        tick: tick == -1
                            ? ((playPos >= rangeStart && playPos < rangeEnd) ? playPos : rangeStart)
                            : tick,
                        endTick: rangeEnd,
                        trackNo: trackNo);
                } else {
                    playbackRangeEndTick = -1;
                    // Explicit bounds are used for one-shot previews such as Alt+Space
                    // on selected notes; they must not fall through to project looping.
                    loopProjectOnPlaybackEnd = endTick == -1;
                    Play(
                        DocManager.Inst.Project,
                        tick: tick == -1 ? DocManager.Inst.playPosTick : tick,
                        endTick: endTick,
                        trackNo: trackNo);
                }
            }
        }

        public void Play(UProject project, int tick, int endTick = -1, int trackNo = -1) {
            playbackEndTick = endTick == -1 ? project.EndTick : endTick;
            if (AudioOutput.PlaybackState == PlaybackState.Paused) {
                PlayingMaster = true;
                metronomeEngine.StartPlayback(project.timeAxis, DocManager.Inst.playPosTick);
                AudioOutput.Play();
                return;
            }
            AudioOutput.Stop();
            StartingToPlay = true;
            PlayingMaster = true;
            Render(project, tick, endTick, trackNo);
        }

        public void StopPlayback() {
            AudioOutput.Stop();
            StartingToPlay = false;
            masterMix = null;
            PlayingMaster = false;
            metronomeEngine.Stop();
            playbackRangeEndTick = -1;
            playbackEndTick = -1;
            playbackMix = null;
            loopProjectOnPlaybackEnd = false;
        }

        public void PausePlayback() {
            AudioOutput.Pause();
            StartingToPlay = false;
            PlayingMaster = false;
            metronomeEngine.Stop();
            playbackRangeEndTick = -1;
            loopProjectOnPlaybackEnd = false;
        }

        private void StartPlayback(double startMs, MasterAdapter masterAdapter) {
            toneGenerator.EndAllTones();
            this.startMs = startMs;
            metronomeEngine.StartPlayback(DocManager.Inst.Project.timeAxis, StartTick);
            var start = TimeSpan.FromMilliseconds(startMs);
            Log.Information($"StartPlayback at {start}");
            masterMix = masterAdapter;
            AudioOutput.Stop();
            AudioOutput.Init(masterMix);
            AudioOutput.Play();
            // Cleared only after playback actually starts. Until then UpdatePlayPos
            // would see Stopped + PlayingMaster and stop the playback it is starting.
            StartingToPlay = false;
        }

        private void Render(UProject project, int tick, int endTick, int trackNo) {
            Task.Run(() => {
                try {
                    LiveWaveformCache.Clear();
                    IsWaveformBlanked = false;
                    
                    Task.Factory.StartNew(() => {
                        DocManager.Inst.ExecuteCmd(new WaveformReadyNotification());
                    }, CancellationToken.None, TaskCreationOptions.None, DocManager.Inst.MainScheduler);

                    RenderEngine engine = new RenderEngine(project, startTick: tick, endTick: endTick, trackNo: trackNo);
                    var result = engine.RenderMixdown(DocManager.Inst.MainScheduler, ref renderCancellation, wait: false);
                    playbackMix = new PlaybackMix(result.Item1, metronomeEngine);
                    var playbackAdapter = new MasterAdapter(playbackMix);
                    playbackAdapter.SetPosition((int)(project.timeAxis.TickPosToMsPos(tick) * 44100 / 1000) * 2);
                    faders = result.Item2;
                    StartPlayback(project.timeAxis.TickPosToMsPos(tick), playbackAdapter);

                    Task.Factory.StartNew(() => {
                        DocManager.Inst.ExecuteCmd(new WaveformReadyNotification());
                    }, CancellationToken.None, TaskCreationOptions.None, DocManager.Inst.MainScheduler);
                } catch (Exception e) {
                    Log.Error(e, "Failed to render.");
                    StopPlayback();
                    var customEx = new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>", e);
                    Task.Factory.StartNew(() => {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                    }, CancellationToken.None, TaskCreationOptions.None, DocManager.Inst.MainScheduler);
                }
            });
        }

        public void UpdatePlayPos() {
            if (AudioOutput != null && AudioOutput.PlaybackState == PlaybackState.Playing && PlayingMaster && masterMix != null) {

                double ms = (AudioOutput.GetPosition() / sizeof(float) - masterMix.Waited / 2) * 1000.0 / 44100;
                int tick = DocManager.Inst.Project.timeAxis.MsPosToTickPos(startMs + ms);
                if (playbackRangeEndTick > 0 && tick >= playbackRangeEndTick) {
                    HandlePlaybackBoundary(hasPlaybackRange: true);
                    return;
                }
                if (playbackRangeEndTick <= 0 &&
                    playbackEndTick > 0 &&
                    tick >= playbackEndTick &&
                    (playbackMix == null || playbackMix.MasterExhausted)) {
                    HandlePlaybackBoundary(hasPlaybackRange: false);
                    return;
                }
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(tick, masterMix.IsWaiting));
            } else if (AudioOutput != null &&
                AudioOutput.PlaybackState == PlaybackState.Stopped &&
                PlayingMaster &&
                !StartingToPlay) {
                HandlePlaybackBoundary(hasPlaybackRange: playbackRangeEndTick > 0);
            }
        }

        private void HandlePlaybackBoundary(bool hasPlaybackRange) {
            if (LoopPlayback) {
                if (hasPlaybackRange) {
                    Play(
                        DocManager.Inst.Project,
                        tick: playbackRangeStartTick,
                        endTick: playbackRangeEndTick);
                    return;
                }
                if (loopProjectOnPlaybackEnd && DocManager.Inst.Project.EndTick > 0) {
                    Play(DocManager.Inst.Project, tick: 0);
                    return;
                }
            }
            if (hasPlaybackRange) {
                int endTick = playbackRangeEndTick;
                StopPlayback();
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(endTick));
            } else {
                StopPlayback();
            }
        }

        public static float DecibelToVolume(double db) {
            return (db <= -24) ? 0 : (float)MusicMath.DecibelToLinear((db < -16) ? db * 2 + 16 : db);
        }

        // Exporting mixdown
        public async Task RenderMixdown(UProject project, string exportPath) {
            await Task.Run(() => {
                try {
                    RenderEngine engine = new RenderEngine(project);
                    var projectMix = engine.RenderMixdown(DocManager.Inst.MainScheduler, ref renderCancellation, wait: true).Item1;
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Exporting to {exportPath}."));

                    CheckFileWritable(exportPath);
                    WaveFileWriter.CreateWaveFile16(exportPath, new ExportAdapter(projectMix));
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Exported to {exportPath}."));
                } catch (IOException ioe) {
                    var customEx = new MessageCustomizableException($"Failed to export {exportPath}.", $"<translate:errors.failed.export>: {exportPath}", ioe);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Failed to export {exportPath}."));
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to render.", $"<translate:errors.failed.render>: {exportPath}", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Failed to render."));
                }
            });
        }

        // Exporting each tracks
        public async Task RenderToFiles(UProject project, string exportPath) {
            await Task.Run(() => {
                string file = "";
                try {
                    RenderEngine engine = new RenderEngine(project);
                    var trackMixes = engine.RenderTracks(DocManager.Inst.MainScheduler, ref renderCancellation);
                    for (int i = 0; i < trackMixes.Count; ++i) {
                        if (trackMixes[i] == null || i >= project.tracks.Count || project.tracks[i].Muted) {
                            continue;
                        }
                        file = PathManager.Inst.GetExportPath(exportPath, project.tracks[i]);
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Exporting to {file}."));

                        CheckFileWritable(file);
                        WaveFileWriter.CreateWaveFile16(file, new ExportAdapter(trackMixes[i]).ToMono(1, 0));
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Exported to {file}."));
                    }
                } catch (IOException ioe) {
                    var customEx = new MessageCustomizableException($"Failed to export {file}.", $"<translate:errors.failed.export>: {file}", ioe);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Failed to export {file}."));
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Failed to render."));
                }
            });
        }

        private void CheckFileWritable(string filePath) {
            if (!File.Exists(filePath)) {
                return;
            }
            using (FileStream fp = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) {
                return;
            }
        }

        void SchedulePreRender() {
            Log.Information("SchedulePreRender");
            var engine = new RenderEngine(
                DocManager.Inst.Project,
                focusPart: preRenderFocusPart,
                focusTick: preRenderFocusTick);
            engine.PreRenderProject(ref renderCancellation);
        }

        #region ICmdSubscriber

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is SeekPlayPosTickNotification) {
                var _cmd = cmd as SeekPlayPosTickNotification;
                StopPlayback();
                int tick = _cmd!.playPosTick;
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(tick, false, _cmd.pause));
            } else if (cmd is VolumeChangeNotification) {
                var _cmd = cmd as VolumeChangeNotification;
                if (faders != null && faders.Count > _cmd.TrackNo) {
                    faders[_cmd.TrackNo].Scale = DecibelToVolume(_cmd.Volume);
                }
            } else if (cmd is PanChangeNotification) {
                var _cmd = cmd as PanChangeNotification;
                if (faders != null && faders.Count > _cmd!.TrackNo) {
                    faders[_cmd.TrackNo].Pan = (float)_cmd.Pan;
                }
            } else if (cmd is BpmCommand ||
                cmd is TimeSignatureCommand ||
                cmd is AddTempoChangeCommand ||
                cmd is DelTempoChangeCommand ||
                cmd is AddTimeSigCommand ||
                cmd is DelTimeSigCommand) {
                if (PlayingMaster && Metronome) {
                    metronomeEngine.UpdateSchedule(DocManager.Inst.Project.timeAxis, DocManager.Inst.playPosTick);
                }
            } else if (cmd is LoadProjectNotification) {
                StopPlayback();
                renderCancellation?.Cancel();
                LiveWaveformCache.Clear();
                DocManager.Inst.ExecuteCmd(new WaveformReadyNotification());
                preRenderFocusPart = null;
                preRenderFocusTick = -1;
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(0));
            } else if (cmd is LoadPartNotification loadPart) {
                preRenderFocusPart = loadPart.part as UVoicePart;
                preRenderFocusTick = loadPart.tick;
            } else if (cmd is FocusNoteNotification focusNote) {
                preRenderFocusPart = focusNote.part as UVoicePart;
                preRenderFocusTick = focusNote.part?.position + focusNote.note.position ?? preRenderFocusTick;
            } else if (cmd is SetPlayPosTickNotification setPlayPosTick) {
                preRenderFocusTick = setPlayPosTick.playPosTick;
            } else if (cmd is PreRenderNotification preRender) {
                if (preRender.part is UVoicePart voicePart) {
                    preRenderFocusPart = voicePart;
                }
                if (preRender.focusTick >= 0) {
                    preRenderFocusTick = preRender.focusTick;
                }
            }
            if (cmd is PreRenderNotification || cmd is LoadProjectNotification) {
                if (Util.Preferences.Default.PreRender) {
                    SchedulePreRender();
                }
            }
        }

        #endregion
    }
    public class WaveformReadyNotification : UNotification {
        public override string ToString() => "Waveform rendered and ready";
    }
}
