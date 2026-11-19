// Ported from ciglet (https://github.com/Sleepwalking/ciglet): cig_lfmodel_from_rd,
// lfparam_from_lfmodel, cig_lfmodel_spectrum and cig_fzero, translated to C#.
//
// ciglet
// ===
//
// Copyright (c) 2016-2019, Kanru Hua
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the following conditions are met:
//
// 1. Redistributions of source code must retain the above copyright notice, this
// list of conditions and the following disclaimer.
//
// 2. Redistributions in binary form must reproduce the above copyright notice,
// this list of conditions and the following disclaimer in the documentation and/or
// other materials provided with the distribution.
//
// 3. Neither the name of the copyright holder nor the names of its contributors
// may be used to endorse or promote products derived from this software without
// specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
// ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
// ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Numerics;

namespace OpenUtau.Core.Analysis;

/// <summary>
/// The Liljencrants-Fant glottal flow derivative model: its parameters from Rd (Fant 1995,
/// Huber &amp; Roebel 2014) and its spectrum (Doval &amp; d'Alessandro 1997), as ciglet has them.
/// </summary>
public static class LfModel {
    /// <summary>LF timing parameters as fractions of the period T0, and the excitation Ee.</summary>
    public readonly record struct Model(double tp, double te, double ta, double T0, double Ee);

    public static Model FromRd(double rd, double T0, double Ee) {
        double Rap = rd < 0.21 ? 1e-6 : (rd < 2.7 ? (-1.0 + 4.8 * rd) / 100.0 : 0.323 / rd);
        double OQupp = 1.0 - 1.0 / (2.17 * rd);
        double Rkp, Rgp;
        if (rd < 2.7) {
            Rkp = (22.4 + 11.8 * rd) / 100.0;
            Rgp = 0.25 * Rkp / ((0.11 * rd) / (0.5 + 1.2 * Rkp) - Rap);
        } else {
            Rgp = 9.3552e-3 + 596e-2 / (7.96 - 2.0 * OQupp);
            Rkp = 2.0 * Rgp * OQupp - 1.0428;
        }
        double tp = 1.0 / (2.0 * Rgp);
        double te = tp * (Rkp + 1.0);
        return new Model(tp, te, Rap, T0, Ee);
    }

    struct LfParam {
        public double T0, Te, Tp, Ta, wg, sin_wgTe, cos_wgTe, e, A, a, E0, scale;
    }

    static double EFunc(double x, in LfParam p) {
        return 1.0 - Math.Exp((p.Te - p.T0) * x) - p.Ta * x;
    }

    static double AFunc(double x, in LfParam p) {
        double C = p.wg * p.wg * p.sin_wgTe * p.A - p.wg * p.cos_wgTe;
        return p.sin_wgTe * p.A * x * x + p.sin_wgTe * x + p.wg * Math.Exp(-x * p.Te) + C;
    }

    static double ADeriv(double x, in LfParam p) {
        return 2 * p.sin_wgTe * p.A * x + p.sin_wgTe - p.wg * p.Te * Math.Exp(-x * p.Te);
    }

    static double NewtonSearch(in LfParam p) {
        double a = 0;
        for (int i = 0; i < 8; i++) {
            a -= AFunc(a, p) / ADeriv(a, p);
        }
        return a;
    }

    static LfParam ParamFromModel(Model model) {
        double scale = 1.0;
        const double maxHz = 800.0;
        double T0 = model.T0;
        if (T0 < 1.0 / maxHz) {
            scale = 1.0 / T0 / maxHz;
            T0 = 1.0 / maxHz;
        }
        var ret = new LfParam {
            T0 = T0,
            Te = T0 * model.te,
            Tp = T0 * model.tp,
            Ta = T0 * model.ta,
            a = 0,
            scale = scale,
        };
        ret.wg = Math.PI / ret.Tp;
        ret.sin_wgTe = Math.Sin(ret.wg * ret.Te);
        ret.cos_wgTe = Math.Cos(ret.wg * ret.Te);
        var p0 = ret;
        double e = FZero(x => EFunc(x, p0), 1.0, 2.0 / (ret.Ta + 1e-9));
        double eTeT0 = Math.Exp(e * (ret.Te - ret.T0));
        ret.A = (1.0 - eTeT0) / (e * e * ret.Ta) + (ret.Te - ret.T0) * eTeT0 / (e * ret.Ta);
        ret.e = e;
        ret.a = NewtonSearch(ret);
        ret.E0 = -model.Ee / (Math.Exp(ret.a * ret.Te) * ret.sin_wgTe);
        return ret;
    }

    /// <summary>Magnitudes of the glottal flow derivative spectrum at freq (Hz).</summary>
    public static double[] Spectrum(Model model, ReadOnlySpan<double> freq) {
        var p = ParamFromModel(model);
        double e = p.e, a = p.a, wg = p.wg, E0 = p.E0;
        double sin_wgTe = p.sin_wgTe, cos_wgTe = p.cos_wgTe;
        double Te = p.Te, Ta = p.Ta, T0 = p.T0;
        double e1eTa = e * (1.0 - e * Ta);
        var magn = new double[freq.Length];
        for (int i = 0; i < freq.Length; i++) {
            double Omega = 2.0 * Math.PI * freq[i] / p.scale;
            var asubipif = new Complex(a, -Omega);
            var P1 = new Complex(E0, 0) / (asubipif * asubipif + new Complex(wg * wg, 0));
            var P2 = new Complex(wg, 0)
                + Complex.Exp(asubipif * Te) * (asubipif * sin_wgTe - new Complex(wg * cos_wgTe, 0));
            var P3 = model.Ee * Complex.Exp(new Complex(0, -Omega * Te))
                / (new Complex(0, e * Ta * Omega) * new Complex(e, Omega));
            Complex P4;
            if (e1eTa < 1.0) {  // approximated, for numerical stability
                P4 = new Complex(0, -e * Ta * Omega);
            } else {
                P4 = e1eTa * (Complex.One - Complex.Exp(new Complex(0, -Omega * (T0 - Te))))
                    - new Complex(0, e * Ta * Omega);
            }
            var G = P1 * P2 + P3 * P4;
            double m = G.Magnitude;
            magn[i] = double.IsNaN(m) ? 0 : m;
        }
        return magn;
    }

    /// <summary>Brent's root finding on [xmin, xmax].</summary>
    static double FZero(Func<double, double> func, double xmin, double xmax) {
        const double eps = 1e-8;
        double a = xmin, b = xmax, c, s, d = 0;
        double fa = func(a), fb = func(b), fc, fs;
        if (fa * fb >= 0) {
            return (a + b) / 2.0;
        }
        if (Math.Abs(fa) < Math.Abs(fb)) {
            c = b; b = a; a = c;
            fc = fb; fb = fa; fa = fc;
        } else {
            c = a;
            fc = fa;
        }
        bool mflag = true;
        while (Math.Abs(fb) > eps && Math.Abs(a - b) > eps) {
            if (fa != fc && fb != fc) {
                s = a * fb * fc / (fa - fb) / (fa - fc)
                    + b * fa * fc / (fb - fa) / (fb - fc)
                    + c * fa * fb / (fc - fa) / (fc - fb);  // inverse quadratic interpolation
            } else {
                s = b - fb * (b - a) / (fb - fa);  // secant
            }
            if ((s < (3.0 * a + b) / 4.0 || s > b) ||
                (mflag && Math.Abs(s - b) >= Math.Abs(b - c) * 0.5) ||
                (!mflag && Math.Abs(s - b) >= Math.Abs(c - d) * 0.5) ||
                (mflag && Math.Abs(b - c) < eps) ||
                (!mflag && Math.Abs(c - d) < eps)) {
                s = (a + b) / 2.0;
                if (s == b || s == a) {
                    break;  // maximum precision reached
                }
                mflag = true;
            } else {
                mflag = false;
            }
            fs = func(s);
            d = c; c = b;
            fc = fb;
            if (fa * fs < 0) {
                b = s;
                fb = fs;
            } else {
                a = s;
                fa = fs;
            }
            if (Math.Abs(fa) < Math.Abs(fb)) {
                double x = b; b = a; a = x;
                x = fb; fb = fa; fa = x;
            }
        }
        return b;
    }
}
