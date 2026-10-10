using System;
using System.Linq;

namespace OpenUtau.Core.Analysis;

/// <summary>
/// Tension as the glottal source's shape parameter Rd (the LF model, <see cref="LfModel"/>):
/// Rd per frame fitted to the harmonic amplitudes, and the per-harmonic gains of moving it.
/// Lower Rd is a tenser, brighter source. The first harmonic is the reference: an edit
/// leaves it as is and reshapes the rest.
/// </summary>
public static class GlottalRd {
    public const double MinRd = 0.02;
    public const double MaxRd = 3.0;
    /// <summary>The highest harmonic frequency the fit looks at.</summary>
    public const double MaxFitHz = 8000;
    const int MaxFitHarmonics = 80;
    const int GridSize = 64;
    const double LipRadiusCm = 1.5;

    static readonly double[] grid = Enumerable.Range(0, GridSize)
        .Select(i => MinRd + (MaxRd - MinRd) * i / (GridSize - 1)).ToArray();
    // Glottal flow power per harmonic for each grid Rd. Over harmonic numbers the LF shape
    // doesn't depend on f0 (below the model's 800 Hz limit), so one f0 serves all.
    static readonly double[][] flowPower = grid.Select(rd => {
        var shape = FlowShape(rd, 200, MaxFitHarmonics);
        return shape.Select(v => v * v).ToArray();
    }).ToArray();

    /// <summary>The Rd for a tension value (-100..100): +100 halves Rd, -100 doubles it.</summary>
    public static double TenseRd(double rd, double tension) {
        return Math.Clamp(rd * Math.Pow(2, -tension / 100), MinRd, MaxRd);
    }

    /// <summary>
    /// Glottal flow amplitudes (the LF derivative over frequency) at harmonics 1..n of f0,
    /// relative to the first.
    /// </summary>
    public static double[] FlowShape(double rd, double f0, int n) {
        var freq = new double[n];
        for (int k = 0; k < n; k++) {
            freq[k] = (k + 1) * f0;
        }
        var shape = LfModel.Spectrum(LfModel.FromRd(rd, 1.0 / f0, 1.0), freq);
        double first = shape[0];
        for (int k = 0; k < n; k++) {
            shape[k] = first > 0 ? shape[k] / (k + 1) / first : 1;
        }
        return shape;
    }

    /// <summary>
    /// Lip radiation of a piston in a baffle: |j w L R / (R + j w L)|, with R and L the
    /// radiation resistance and inductance for the lip radius.
    /// </summary>
    public static double LipGain(double hz) {
        double r = 128.0 / (9.0 * Math.PI * Math.PI);
        double l = 8.0 * LipRadiusCm / 100.0 / (3.0 * Math.PI * 340.0);
        double w = 2 * Math.PI * hz;
        double wl = w * l;
        // |j w L R| / |R + j w L|
        return wl * r / Math.Sqrt(r * r + wl * wl);
    }

    /// <summary>
    /// The Rd whose glottal flow spectrum best matches the amplitudes of harmonics 1..n of
    /// f0, after removing lip radiation: Itakura-Saito distance on power, with the model's
    /// gain set by the first harmonic, minimized over an Rd grid and refined between points.
    /// </summary>
    public static double Fit(ReadOnlySpan<double> amplitudes, double f0) {
        int n = Math.Min(amplitudes.Length, MaxFitHarmonics);
        if (n < 2) {
            return 1.0;
        }
        var power = new double[n];
        for (int k = 0; k < n; k++) {
            double a = amplitudes[k] / LipGain((k + 1) * f0);
            power[k] = a * a + 1e-20;
        }
        var distance = new double[GridSize];
        for (int g = 0; g < GridSize; g++) {
            var model = flowPower[g];
            double gain = power[0] / model[0];
            double sum = 0;
            for (int k = 0; k < n; k++) {
                double ratio = power[k] / (model[k] * gain + 1e-30);
                sum += ratio - Math.Log(ratio) - 1;
            }
            distance[g] = sum / n;
        }
        int best = 0;
        for (int g = 1; g < GridSize; g++) {
            if (distance[g] < distance[best]) {
                best = g;
            }
        }
        double index = best;
        if (best > 0 && best < GridSize - 1) {
            double y0 = distance[best - 1], y1 = distance[best], y2 = distance[best + 1];
            double curvature = y0 - 2 * y1 + y2;
            if (curvature > 0) {
                index = best + Math.Clamp(0.5 * (y0 - y2) / curvature, -0.5, 0.5);
            }
        }
        return MinRd + (MaxRd - MinRd) * index / (GridSize - 1);
    }

    /// <summary>
    /// An Rd track with unvoiced frames filled from the nearest voiced ones, smoothed by a
    /// moving average of window frames. All-unvoiced gives 1.
    /// </summary>
    public static double[] Smooth(double[] rd, bool[] voiced, int window) {
        int n = rd.Length;
        var filled = new double[n];
        var known = Enumerable.Range(0, n).Where(i => voiced[i]).ToArray();
        if (known.Length == 0) {
            Array.Fill(filled, 1.0);
            return filled;
        }
        int j = 0;
        for (int i = 0; i < n; i++) {
            while (j + 1 < known.Length && known[j + 1] <= i) {
                j++;
            }
            if (i <= known[0]) {
                filled[i] = rd[known[0]];
            } else if (i >= known[^1]) {
                filled[i] = rd[known[^1]];
            } else {
                int a = known[j], b = known[j + 1];
                filled[i] = rd[a] + (rd[b] - rd[a]) * (i - a) / (b - a);
            }
        }
        if (window <= 1) {
            return filled;
        }
        var smoothed = new double[n];
        int half = window / 2;
        for (int i = 0; i < n; i++) {
            double sum = 0;
            for (int w = -half; w < window - half; w++) {
                sum += filled[Math.Clamp(i + w, 0, n - 1)];
            }
            smoothed[i] = sum / window;
        }
        return smoothed;
    }

    /// <summary>Amplitude gains of harmonics 1..n of f0 when Rd moves from rd to rd2 (the first is 1).</summary>
    public static double[] Gains(double rd, double rd2, double f0, int n) {
        var from = FlowShape(rd, f0, n);
        var to = FlowShape(rd2, f0, n);
        var gains = new double[n];
        for (int k = 0; k < n; k++) {
            gains[k] = from[k] > 0 ? to[k] / from[k] : 1;
        }
        return gains;
    }

    /// <summary>
    /// The gain at a frequency from per-harmonic gains of f0: log-linear between harmonics,
    /// 1 below the first, the last one above the last.
    /// </summary>
    public static double GainAt(double[] gains, double f0, double hz) {
        double k = hz / f0 - 1;
        if (k <= 0 || gains.Length == 0) {
            return 1;
        }
        if (k >= gains.Length - 1) {
            return gains[^1];
        }
        int i = (int)k;
        double t = k - i;
        return Math.Exp(Math.Log(Math.Max(gains[i], 1e-9)) * (1 - t) + Math.Log(Math.Max(gains[i + 1], 1e-9)) * t);
    }
}
