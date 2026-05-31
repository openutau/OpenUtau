using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Core.Analysis;

public static class AudioSlicer {
    static int sample_rate = 44100;
    static float threshold = 0.02f;
    static int hop_size = 441;
    static int win_size = 1764;
    static int min_length = 100;
    static int min_interval = 20;
    static int max_sil_kept = 10;

    public static int SampleRate => sample_rate;

    public struct Chunk {
        public double offsetMs; //position of this slice in the audio file in milliseconds
        public float[] samples;

        public Chunk(double offsetMs, float[] samples) {
            this.offsetMs = offsetMs;
            this.samples = samples;
        }

        public Chunk(float[] originalSamples, int startIndex, int endIndex) {
            samples = originalSamples[startIndex..endIndex];
            offsetMs = (double)startIndex * (1000.0 / sample_rate);
        }
    }

    static double[] get_rms(
        float[] samples,
        int frame_length = 2048,
        int hop_length = 512
    ) {
        //reference: https://github.com/openvpi/audio-slicer/blob/main/slicer2.py#L5
        //y = np.pad(samples, padding, mode="constant")
        float[] y = new float[samples.Length + frame_length];
        Array.Copy(samples, 0, y, frame_length / 2, samples.Length);
        for (int i = 0; i < y.Length; i++) {
            y[i] = y[i] * y[i];
        }

        int output_size = samples.Length / hop_length;
        return Enumerable.Range(0, output_size)
            .Select(i => Math.Sqrt(y[(i * hop_length)..(i * hop_length + frame_length)].Average()))
            .ToArray();
    }

    static int argmin(this double[] array) {
        //numpy's argmin function
        return Array.IndexOf(array, array.Min());
    }

    public static List<Chunk> Slice(float[] samples) {
        //reference: https://github.com/openvpi/audio-slicer/blob/main/slicer2.py#L68
        if ((samples.Length + hop_size - 1) / hop_size <= min_length) {
            return new List<Chunk> { new Chunk(0, samples) };
        }

        var rms_list = get_rms(
            samples,
            frame_length: win_size,
            hop_length: hop_size
        );
        var sil_tags = new List<Tuple<int, int>>();
        int silence_start = -1; //here -1 means none
        int clip_start = 0;
        foreach (int i in Enumerable.Range(0, rms_list.Length)) {
            var rms = rms_list[i];
            //Keep looping while frame is silent.
            if (rms < threshold) {
                //Record start of silent frames.
                if (silence_start < 0) {
                    silence_start = i;
                }

                continue;
            }

            //Keep looping while frame is not silent and silence start has not been recorded.
            if (silence_start < 0) {
                continue;
            }

            //Clear recorded silence start if interval is not enough or clip is too short
            var is_leading_silence = silence_start == 0 && i > max_sil_kept;
            var need_slice_middle = i - silence_start >= min_interval && i - clip_start >= min_length;
            if (!is_leading_silence && !need_slice_middle) {
                silence_start = -1;
                continue;
            }

            //Need slicing. Record the range of silent frames to be removed.
            if (i - silence_start <= max_sil_kept) {
                var pos = rms_list[silence_start..(i + 1)].argmin() + silence_start;
                if (silence_start == 0) {
                    sil_tags.Add(Tuple.Create(0, pos));
                } else {
                    sil_tags.Add(Tuple.Create(pos, pos));
                }

                clip_start = pos;
            } else if (i - silence_start <= max_sil_kept * 2) {
                var pos = rms_list[(i - max_sil_kept)..(silence_start + max_sil_kept + 1)].argmin();
                pos += i - max_sil_kept;
                var pos_l = rms_list[silence_start..(silence_start + max_sil_kept + 1)].argmin() + silence_start;
                var pos_r = rms_list[(i - max_sil_kept)..(i + 1)].argmin() + i - max_sil_kept;
                if (silence_start == 0) {
                    sil_tags.Add(Tuple.Create(0, pos_r));
                    clip_start = pos_r;
                } else {
                    sil_tags.Add(Tuple.Create(Math.Min(pos_l, pos), Math.Max(pos_r, pos)));
                    clip_start = Math.Max(pos_r, pos);
                }
            } else {
                var pos_l = rms_list[silence_start..(silence_start + max_sil_kept + 1)].argmin() + silence_start;
                var pos_r = rms_list[(i - max_sil_kept)..(i + 1)].argmin() + i - max_sil_kept;
                if (silence_start == 0) {
                    sil_tags.Add(Tuple.Create(0, pos_r));
                } else {
                    sil_tags.Add(Tuple.Create(pos_l, pos_r));
                }

                clip_start = pos_r;
            }

            silence_start = -1;
        }

        //Deal with trailing silence.
        var total_frames = rms_list.Length;
        if (silence_start >= 0 && total_frames - silence_start >= min_interval) {
            var silence_end = Math.Min(total_frames, silence_start + max_sil_kept);
            var pos = rms_list[silence_start..(silence_end + 1)].argmin() + silence_start;
            sil_tags.Add(Tuple.Create(pos, total_frames + 1));
        }

        //Apply and return slices.
        if (sil_tags.Count == 0) {
            return new List<Chunk> { new Chunk(0, samples) };
        } else {
            var chunks = new List<Chunk>();
            if (sil_tags[0].Item1 > 0) {
                chunks.Add(new Chunk(
                    samples,
                    0,
                    sil_tags[0].Item1 * hop_size
                ));
            }

            foreach (var i in Enumerable.Range(0, sil_tags.Count - 1)) {
                chunks.Add(new Chunk(
                    samples,
                    sil_tags[i].Item2 * hop_size,
                    sil_tags[i + 1].Item1 * hop_size
                ));
            }

            if (sil_tags[^1].Item2 < total_frames) {
                chunks.Add(new Chunk(
                    samples,
                    sil_tags[^1].Item2 * hop_size,
                    total_frames * hop_size
                ));
            }

            return chunks;
        }
    }
}
