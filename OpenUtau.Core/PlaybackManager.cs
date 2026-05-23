using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Core.Format;
using Serilog;

namespace OpenUtau.Core {
    public class SineGenerator : ISampleProvider {
        public WaveFormat WaveFormat => waveFormat;
        private WaveFormat waveFormat;

        private readonly double attackSampleCount;
        private readonly double releaseSampleCount;

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

        public int Read(float[] buffer, int offset, int count) {
            // Duplicate sample across two channels
            for (int i = 0; i < count / 2; i++) {
                float sample = GetNextSample();
                buffer[offset + (i * 2)] += (float)sample * gain;
                buffer[offset + (i * 2) + 1] += (float)sample * gain;
            }
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
        private readonly float gain = 0.4f;

        private readonly object _lockObj = new object();

        public ToneGenerator() {}

        public ToneGenerator(float gain) {
            this.gain = gain;
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
            if (activeFrequencies.ContainsKey(freq)) {
                if (activeFrequencies[freq].isActive) {
                    // Don't cut off tone to replace with the same frequency
                    // Should never happen
                    return;
                }
            }

            lock (_lockObj) {
                activeFrequencies[freq] = new SineGenerator(freq, gain);
            }
        }

        public void EndTone(double freq) {
            if (activeFrequencies.ContainsKey(freq)) {
                activeFrequencies[freq].Stop();

                lock (_lockObj) {
                    // Move to inactive frequencies list
                    inactiveFrequencies.Add(activeFrequencies[freq]);
                    activeFrequencies.Remove(freq);
                }
            }

            CleanupTones();
        }

        public void EndAllTones() {
            foreach (var tone in activeFrequencies) {
                tone.Value.Stop();

                lock (_lockObj) {
                    // Move to inactive frequencies list
                    inactiveFrequencies.Add(tone.Value);
                    activeFrequencies.Remove(tone.Key);
                }
            }


            CleanupTones();
        }

        private void CleanupTones() {
            lock (_lockObj) {
                inactiveFrequencies.RemoveAll(gen => !gen.isPlaying);
            }
        }
    }

    public class PlaybackManager : SingletonBase<PlaybackManager>, ICmdSubscriber {
        private PlaybackManager() {
            DocManager.Inst.AddSubscriber(this);
            try {
                Directory.CreateDirectory(PathManager.Inst.CachePath);
                RenderEngine.ReleaseSourceTemp();
            } catch (Exception e) {
                Log.Error(e, "Failed to release source temp.");
            }

            toneGenerator = new ToneGenerator();
            editingMix = new MasterAdapter(toneGenerator);
        }

        public readonly ToneGenerator toneGenerator;
        List<Fader> faders;
        MasterAdapter masterMix;
        MasterAdapter editingMix;
        
        double startMs;
        public int StartTick => DocManager.Inst.Project.timeAxis.MsPosToTickPos(startMs);
        CancellationTokenSource renderCancellation;

        public Audio.IAudioOutput AudioOutput { get; set; } = new Audio.DummyAudioOutput();
        public bool OutputActive => AudioOutput.PlaybackState == PlaybackState.Playing;
        public bool StartingToPlay { get; private set; }
        public bool PlayingMaster { get; private set; }

        public void PlayTestSound() {
            masterMix = null;
            PlayingMaster = false;
            AudioOutput.Stop();
            AudioOutput.Init(new SignalGenerator(44100, 1).Take(TimeSpan.FromSeconds(1)));
            AudioOutput.Play();
        }

        public void PlayTone(double freq) {
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
                Play(
                    DocManager.Inst.Project,
                    tick: tick == -1 ? DocManager.Inst.playPosTick : tick,
                    endTick: endTick,
                    trackNo: trackNo);
            }
        }

        public void Play(UProject project, int tick, int endTick = -1, int trackNo = -1) {
            if (AudioOutput.PlaybackState == PlaybackState.Paused) {
                PlayingMaster = true;
                AudioOutput.Play();
                return;
            }
            AudioOutput.Stop();
            Render(project, tick, endTick, trackNo);
            StartingToPlay = true;
            PlayingMaster = true;
        }

        public void StopPlayback() {
            AudioOutput.Stop();
            PlayingMaster = false;
        }

        public void PausePlayback() {
            AudioOutput.Pause();
            PlayingMaster = false;
        }

        private void StartPlayback(double startMs, MasterAdapter masterAdapter) {
            toneGenerator.EndAllTones();

            this.startMs = startMs;
            var start = TimeSpan.FromMilliseconds(startMs);
            Log.Information($"StartPlayback at {start}");
            masterMix = masterAdapter;
            AudioOutput.Stop();
            AudioOutput.Init(masterMix);
            AudioOutput.Play();
        }

        private void Render(UProject project, int tick, int endTick, int trackNo) {
            Task.Run(() => {
                try {
                    RenderEngine engine = new RenderEngine(project, startTick: tick, endTick: endTick, trackNo: trackNo);
                    var result = engine.RenderProject(DocManager.Inst.MainScheduler, ref renderCancellation);
                    faders = result.Item2;
                    StartingToPlay = false;
                    StartPlayback(project.timeAxis.TickPosToMsPos(tick), result.Item1);
                } catch (Exception e) {
                    Log.Error(e, "Failed to render.");
                    StopPlayback();
                    var customEx = new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                }
            });
        }

        public void UpdatePlayPos() {
            if (AudioOutput != null && AudioOutput.PlaybackState == PlaybackState.Playing && PlayingMaster) {
                double ms = (AudioOutput.GetPosition() / sizeof(float) - masterMix.Waited / 2) * 1000.0 / 44100;
                int tick = DocManager.Inst.Project.timeAxis.MsPosToTickPos(startMs + ms);
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(tick, masterMix.IsWaiting));
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
            var engine = new RenderEngine(DocManager.Inst.Project);
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
            } else if (cmd is LoadProjectNotification) {
                StopPlayback();
                renderCancellation?.Cancel();
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(0));
            }
            if (cmd is PreRenderNotification || cmd is LoadProjectNotification) {
                if (Util.Preferences.Default.PreRender) {
                    SchedulePreRender();
                }
            }
        }

        #endregion
    }
}
