using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FftFlat;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OpenUtau.Core.Analysis;

public class HnsepConfig {
    public string model = "model.onnx";
    public int sample_rate = 44100;
    public int n_fft = 2048;
    public int hop_length = 512;
}

/// <summary>
/// Harmonic/noise separation with the hnsep network (yxlllc/vocal-remover,
/// MIT, derived from tsurumeso/vocal-remover). The network predicts a
/// complex mask on an STFT; the STFT and ISTFT here reproduce torch.stft /
/// torch.istft (periodic Hann, center=True with zero padding) so only the
/// network runs in ONNX. The model ships as the "hnsep_240512" package:
/// hnsep.yaml (<see cref="HnsepConfig"/>) plus the ONNX file, whose input
/// and output are [1, 2 (re, im), n_fft/2+1, frames].
/// </summary>
public sealed class Hnsep {
    public const string PackageId = "hnsep_240512";
    const int SegmentFrames = 32;  // the network wants frames in multiples of 32

    static readonly object loadLock = new object();
    static Hnsep? instance;

    readonly InferenceSession session;
    readonly HnsepConfig config;
    readonly double[] window;
    readonly bool isCpuRunner;
    readonly object runLock = new object();

    public int SampleRate => config.sample_rate;

    public Hnsep(string modelPath, HnsepConfig config) {
        this.config = config;
        session = Onnx.getInferenceSession(modelPath);
        isCpuRunner = Onnx.IsCpuRunner();
        window = PeriodicHann(config.n_fft);
    }

    /// <summary>The installed package's model, loaded once. Throws when the package is missing.</summary>
    public static Hnsep Instance {
        get {
            lock (loadLock) {
                return instance ??= Load();
            }
        }
    }

    static Hnsep Load() {
        string? dir = PackageManager.Inst.GetInstalledPath(PackageId);
        string? configPath = dir == null ? null : Path.Combine(dir, "hnsep.yaml");
        if (configPath == null || !File.Exists(configPath)) {
            throw new MissingPackageException(PackageId);
        }
        var config = Yaml.DefaultDeserializer.Deserialize<HnsepConfig>(File.ReadAllText(configPath));
        return new Hnsep(Path.Combine(dir!, config.model), config);
    }

    internal static double[] PeriodicHann(int n) {
        var w = new double[n];
        for (int i = 0; i < n; i++) {
            w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n);
        }
        return w;
    }

    /// <summary>
    /// The harmonic part of x (same length, at <see cref="SampleRate"/>); the noise part is x minus it.
    /// Callers run it from several threads, which a CPU session allows and a GPU session
    /// (DirectML, CoreML, ...) doesn't, so a GPU run is locked.
    /// </summary>
    public float[] Harmonic(float[] x) => Separate(x, config.n_fft, config.hop_length, window, input => {
        if (isCpuRunner) return Run(input);
        lock (runLock) return Run(input);
    });

    float[] Run(DenseTensor<float> input) {
        using var dmlScope = Onnx.EnterDmlScope();
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(session.InputNames[0], input) });
        return results.First().AsTensor<float>().ToDenseTensor().Buffer.ToArray();
    }

    /// <summary>
    /// STFT, mask, ISTFT. predictMask maps the [1, 2, bins, frames] spectrum to a
    /// complex mask of the same layout (the network, or a stand-in in tests).
    /// </summary>
    internal static float[] Separate(float[] x, int nfft, int hop, double[] window,
        Func<DenseTensor<float>, float[]> predictMask) {
        int bins = nfft / 2 + 1;
        int T = x.Length;

        // Pad to a whole number of network segments, as hnsep's predict_fromaudio.
        int seg = SegmentFrames * hop;
        int t1 = T + hop;
        int tPad = seg * ((t1 - 1) / seg + 1) - t1;
        int left = tPad / 2 / hop * hop;
        int length = T + tPad;
        int frames = 1 + length / hop;

        // Centered STFT: n_fft/2 zeros on both sides of the padded signal.
        var padded = new double[length + nfft];
        for (int i = 0; i < T; i++) {
            padded[nfft / 2 + left + i] = x[i];
        }
        // Spectrum kept for masking; the network input is [1, 2 (re, im), bins, frames].
        var spectrum = new Complex[frames * bins];
        var input = new DenseTensor<float>(new[] { 1, 2, bins, frames });
        var inputMemory = input.Buffer;
        Parallel.For(0, frames, () => new FrameScratch(nfft), (m, _, s) => {
            for (int i = 0; i < nfft; i++) {
                s.buffer[i] = padded[m * hop + i] * window[i];
            }
            var frame = s.fft.Forward(s.buffer);
            frame.CopyTo(spectrum.AsSpan(m * bins, bins));
            var span = inputMemory.Span;
            for (int k = 0; k < bins; k++) {
                span[k * frames + m] = (float)frame[k].Real;
                span[(bins + k) * frames + m] = (float)frame[k].Imaginary;
            }
            return s;
        }, _ => { });

        float[] mask = predictMask(input);

        // Masked ISTFT: inverse frames in parallel, then overlap-add in order,
        // divided by the summed squared window.
        var frameOut = new double[frames * nfft];
        Parallel.For(0, frames, () => new FrameScratch(nfft), (m, _, s) => {
            var frame = MemoryMarshal.Cast<double, Complex>(s.buffer.AsSpan());
            for (int k = 0; k < bins; k++) {
                frame[k] = spectrum[m * bins + k] * new Complex(mask[k * frames + m], mask[(bins + k) * frames + m]);
            }
            // DC and Nyquist are real for a real signal.
            frame[0] = frame[0].Real;
            frame[bins - 1] = frame[bins - 1].Real;
            var time = s.fft.Inverse(frame);
            int o = m * nfft;
            for (int i = 0; i < nfft; i++) {
                frameOut[o + i] = time[i] * window[i];
            }
            return s;
        }, _ => { });
        var y = new double[length + nfft];
        var wsum = new double[length + nfft];
        for (int m = 0; m < frames; m++) {
            int o = m * nfft;
            int p = m * hop;
            for (int i = 0; i < nfft; i++) {
                y[p + i] += frameOut[o + i];
                wsum[p + i] += window[i] * window[i];
            }
        }
        var h = new float[T];
        for (int i = 0; i < T; i++) {
            int j = nfft / 2 + left + i;
            h[i] = wsum[j] > 1e-11 ? (float)(y[j] / wsum[j]) : 0f;
        }
        return h;
    }

    /// <summary>
    /// Per-thread FFT (numpy's sign convention, inverse divided by n) and its
    /// in-place buffer: n samples, or n/2+1 complex bins.
    /// </summary>
    sealed class FrameScratch {
        public readonly RealFourierTransform fft;
        public readonly double[] buffer;

        public FrameScratch(int nfft) {
            fft = new RealFourierTransform(nfft);
            buffer = new double[nfft + 2];
        }
    }
}
