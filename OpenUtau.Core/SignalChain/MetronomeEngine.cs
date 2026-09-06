using System;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.SignalChain {
    class MetronomeEngine : ISignalSource {
        private const int SampleRate = 44100;
        private const int Channels = 2;
        private const int MetronomeAttackMs = 5;
        private const int MetronomeBeatReleaseMs = 10;
        private const int MetronomeBarReleaseMs = 10;
        private const double AccentFrequencyOffset = 880;

        private readonly ToneGenerator toneGenerator = new ToneGenerator(PlaybackManager.GetMetronomeGain());
        private readonly MetronomeScheduler scheduler = new MetronomeScheduler();
        private readonly object lockObj = new object();
        private TimeAxis? playbackTimeAxis;
        private float lastGain = float.NaN;

        private static double MetronomeBarFreq => Preferences.Default.MetronomeHighFrequency;
        private static double MetronomeBeatFreq => Preferences.Default.MetronomeLowFrequency;
        private static double MetronomeBarAccentFreq => Preferences.Default.MetronomeHighFrequency + AccentFrequencyOffset;
        private static double MetronomeBeatAccentFreq => Preferences.Default.MetronomeLowFrequency + AccentFrequencyOffset;

        public bool Enabled { get; private set; }

        public MetronomeEngine() {
            UpdateGain();
        }

        public bool IsReady(int position, int count) {
            return toneGenerator.IsReady(position, count);
        }

        public int Mix(int position, float[] buffer, int offset, int count) {
            UpdateGain();
            ScheduleBuffer(position, count);
            return toneGenerator.Mix(position, buffer, offset, count);
        }

        public void SetEnabled(bool enabled, TimeAxis? timeAxis = null, int tick = -1) {
            lock (lockObj) {
                Enabled = enabled;
                if (!enabled) {
                    Stop();
                    return;
                }
                if (timeAxis != null && tick >= 0) {
                    playbackTimeAxis = timeAxis;
                    scheduler.Reset(timeAxis, tick);
                }
            }
        }

        public void Stop() {
            lock (lockObj) {
                toneGenerator.EndAllTones();
                scheduler.Clear();
                playbackTimeAxis = null;
            }
        }

        public void StartPlayback(TimeAxis timeAxis, int tick) {
            ArgumentNullException.ThrowIfNull(timeAxis);
            lock (lockObj) {
                toneGenerator.EndAllTones();
                playbackTimeAxis = timeAxis;
                if (Enabled) {
                    scheduler.Reset(timeAxis, tick);
                } else {
                    scheduler.Clear();
                }
            }
        }

        public void UpdateSchedule(TimeAxis timeAxis, int tick) {
            ArgumentNullException.ThrowIfNull(timeAxis);
            lock (lockObj) {
                if (!Enabled) {
                    scheduler.Clear();
                    return;
                }
                playbackTimeAxis = timeAxis;
                scheduler.Reset(timeAxis, tick);
            }
        }

        private void ScheduleBuffer(int position, int count) {
            lock (lockObj) {
                if (!Enabled || playbackTimeAxis == null || !scheduler.IsScheduled) {
                    return;
                }

                double bufferStartMs = position * 1000.0 / (SampleRate * Channels);
                double bufferEndMs = (position + count) * 1000.0 / (SampleRate * Channels);
                while (scheduler.IsScheduled && scheduler.NextMs < bufferStartMs) {
                    scheduler.Advance(playbackTimeAxis);
                }
                while (scheduler.IsScheduled && scheduler.NextMs < bufferEndMs) {
                    int startSampleOffset = (int)Math.Round((scheduler.NextMs - bufferStartMs) * SampleRate / 1000.0);
                    PlayClick(scheduler.NextBeat == 0, startSampleOffset);
                    scheduler.Advance(playbackTimeAxis);
                }
            }
        }

        private void PlayClick(bool accent, int startSampleOffset = 0) {
            if (accent) {
                toneGenerator.StartTones(
                    startSampleOffset,
                    (MetronomeBarFreq, MetronomeAttackMs, MetronomeBarReleaseMs),
                    (MetronomeBarAccentFreq, MetronomeAttackMs, MetronomeBeatReleaseMs));
                toneGenerator.EndTones(MetronomeBarFreq, MetronomeBarAccentFreq);
            } else {
                toneGenerator.StartTones(
                    startSampleOffset,
                    (MetronomeBeatFreq, MetronomeAttackMs, MetronomeBeatReleaseMs),
                    (MetronomeBeatAccentFreq, MetronomeAttackMs, MetronomeBeatReleaseMs / 2));
                toneGenerator.EndTones(MetronomeBeatFreq, MetronomeBeatAccentFreq);
            }
        }

        private void UpdateGain() {
            float gain = PlaybackManager.GetMetronomeGain();
            if (gain != lastGain) {
                lastGain = gain;
                toneGenerator.SetGain(gain);
            }
        }
    }
}
