using System;

namespace OpenUtau.Core.SignalChain.Effects {
    /// <summary>
    /// Freeverb -- public-domain Schroeder/Moorer reverb by Jezar at Dreampoint.
    /// The C reference impl is ~300 lines; this is a faithful C# port.
    /// Tunings are designed for 44.1 kHz mono input but we run a stereo input
    /// (downmixed to a mono sum for the comb/allpass network, then spread back
    /// out via the L/R offset of <see cref="StereoSpread"/> samples).
    /// </summary>
    public class Freeverb : IEffect {
        // Constants -- straight from Jezar's reference implementation.
        private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        private static readonly int[] AllpassTuning = { 556, 441, 341, 225 };
        private const int StereoSpread = 23;
        private const float FixedGain = 0.015f;
        private const float ScaleDamp = 0.4f;
        private const float ScaleRoom = 0.28f;
        private const float OffsetRoom = 0.7f;

        private readonly int channels;
        private readonly int sampleRate;
        private readonly CombFilter[] combsL;
        private readonly CombFilter[] combsR;
        private readonly AllpassFilter[] apsL;
        private readonly AllpassFilter[] apsR;

        // Pre-delay line on the mono input feeding the comb network.  Dry
        // signal bypasses it, so the direct sound still arrives on time.
        // Sized for the maximum supported pre-delay (200 ms) so we never
        // re-allocate at Configure time.
        private const int MaxPreDelayMs = 200;
        private readonly float[] preDelayBuf;
        private int preDelayIdx;
        private int preDelaySamples;

        private float width = 1.0f;
        private float wet = 0.3f;
        private float dry = 0.7f;
        private bool bypassed = true;

        public bool IsBypassed => bypassed;

        public Freeverb(int sampleRate = 44100, int channels = 2) {
            this.channels = channels;
            this.sampleRate = sampleRate;
            double scale = sampleRate / 44100.0;
            combsL = new CombFilter[CombTuning.Length];
            combsR = new CombFilter[CombTuning.Length];
            for (int i = 0; i < CombTuning.Length; i++) {
                combsL[i] = new CombFilter((int)(CombTuning[i] * scale));
                combsR[i] = new CombFilter((int)((CombTuning[i] + StereoSpread) * scale));
            }
            apsL = new AllpassFilter[AllpassTuning.Length];
            apsR = new AllpassFilter[AllpassTuning.Length];
            for (int i = 0; i < AllpassTuning.Length; i++) {
                apsL[i] = new AllpassFilter((int)(AllpassTuning[i] * scale));
                apsR[i] = new AllpassFilter((int)((AllpassTuning[i] + StereoSpread) * scale));
                apsL[i].Feedback = 0.5f;
                apsR[i].Feedback = 0.5f;
            }
            int maxDelay = Math.Max(1, sampleRate * MaxPreDelayMs / 1000);
            preDelayBuf = new float[maxDelay];
        }

        public void Configure(double roomsize, double damp, double width, double wet, double dry, double preDelayMs = 0.0) {
            this.width = (float)Math.Clamp(width, 0.0, 1.0);
            this.wet = (float)Math.Max(0.0, wet);
            this.dry = (float)Math.Max(0.0, dry);
            float fb = (float)(roomsize * ScaleRoom + OffsetRoom);
            float d1 = (float)(damp * ScaleDamp);
            foreach (var c in combsL) { c.Feedback = fb; c.SetDamp(d1); }
            foreach (var c in combsR) { c.Feedback = fb; c.SetDamp(d1); }
            // Clamp pre-delay to [0, MaxPreDelayMs].  preDelaySamples == 0 means
            // the delay line is skipped entirely (no per-sample buffer touch).
            double pdMs = Math.Clamp(preDelayMs, 0.0, MaxPreDelayMs);
            preDelaySamples = (int)Math.Round(pdMs * sampleRate / 1000.0);
            if (preDelaySamples >= preDelayBuf.Length) preDelaySamples = preDelayBuf.Length - 1;
            // Reverb with non-zero wet is never truly bypassed; treat tiny wet as off.
            bypassed = this.wet < 1e-4;
        }

        public void Reset() {
            foreach (var c in combsL) c.Reset();
            foreach (var c in combsR) c.Reset();
            foreach (var a in apsL) a.Reset();
            foreach (var a in apsR) a.Reset();
            Array.Clear(preDelayBuf, 0, preDelayBuf.Length);
            preDelayIdx = 0;
        }

        public void Process(float[] buffer, int offset, int count) {
            if (bypassed) {
                return;
            }
            if (channels != 2) {
                // Only the stereo path is implemented (matches MasterAdapter).
                return;
            }
            int frames = count / 2;
            float wet1 = wet * (width / 2f + 0.5f);
            float wet2 = wet * ((1f - width) / 2f);
            float d = dry;
            int pdLen = preDelaySamples;
            int pdBufLen = preDelayBuf.Length;

            for (int i = 0; i < frames; i++) {
                int idx = offset + i * 2;
                float inL = buffer[idx];
                float inR = buffer[idx + 1];
                float input = (inL + inR) * FixedGain;

                // Pre-delay: write current sum, read sample from pdLen frames
                // ago.  When pdLen == 0 we skip the buffer entirely.
                if (pdLen > 0) {
                    preDelayBuf[preDelayIdx] = input;
                    int readIdx = preDelayIdx - pdLen;
                    if (readIdx < 0) readIdx += pdBufLen;
                    input = preDelayBuf[readIdx];
                    preDelayIdx++;
                    if (preDelayIdx == pdBufLen) preDelayIdx = 0;
                }

                float outL = 0f;
                float outR = 0f;
                // 8 parallel comb filters
                for (int k = 0; k < combsL.Length; k++) {
                    outL += combsL[k].Process(input);
                    outR += combsR[k].Process(input);
                }
                // 4 series allpass filters
                for (int k = 0; k < apsL.Length; k++) {
                    outL = apsL[k].Process(outL);
                    outR = apsR[k].Process(outR);
                }
                buffer[idx]     = outL * wet1 + outR * wet2 + inL * d;
                buffer[idx + 1] = outR * wet1 + outL * wet2 + inR * d;
            }
        }

        // ----- comb with one-pole low-pass in the feedback path -----
        private class CombFilter {
            private readonly float[] buf;
            private int idx;
            private float filtStore;
            private float damp1;
            private float damp2;

            public float Feedback { get; set; } = 0.5f;

            public CombFilter(int size) {
                buf = new float[Math.Max(1, size)];
            }

            public void SetDamp(float d) {
                damp1 = d;
                damp2 = 1f - d;
            }

            public float Process(float x) {
                float y = buf[idx];
                filtStore = y * damp2 + filtStore * damp1;
                buf[idx] = x + filtStore * Feedback;
                idx++;
                if (idx == buf.Length) idx = 0;
                return y;
            }

            public void Reset() {
                Array.Clear(buf, 0, buf.Length);
                filtStore = 0;
                idx = 0;
            }
        }

        // ----- standard Schroeder allpass section -----
        private class AllpassFilter {
            private readonly float[] buf;
            private int idx;
            public float Feedback { get; set; } = 0.5f;

            public AllpassFilter(int size) {
                buf = new float[Math.Max(1, size)];
            }

            public float Process(float x) {
                float bufOut = buf[idx];
                float y = -x + bufOut;
                buf[idx] = x + bufOut * Feedback;
                idx++;
                if (idx == buf.Length) idx = 0;
                return y;
            }

            public void Reset() {
                Array.Clear(buf, 0, buf.Length);
                idx = 0;
            }
        }
    }
}
