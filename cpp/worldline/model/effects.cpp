#include "effects.h"

#include <algorithm>
#include <cmath>
#include <iostream>
#include <numeric>
#include <vector>

#include "spline.h"

namespace worldline {

// Output bin i samples the input envelope at bin i * ratio, linearly
// interpolating between indexes[i] and indexes[i] + 1 with weights[i].
static void GenderWeights(std::vector<int>& indexes,
                          std::vector<double>& weights, int width,
                          double ratio) {
  for (int i = 0; i < width; ++i) {
    double p = std::min(i * ratio, width - 1.0);
    int i1 = std::min(static_cast<int>(std::floor(p)), width - 2);
    indexes[i] = i1;
    weights[i] = p - i1;
  }
}

void ShiftGender(std::vector<std::vector<double>>& sp, int value) {
  double ratio = std::pow(2, value * 0.01);
  int width = sp.empty() ? 0 : sp[0].size();
  if (ratio == 1 || ratio <= 0 || width < 2) {
    return;
  }
  std::vector<int> indexes(width);
  std::vector<double> weights(width);
  GenderWeights(indexes, weights, width, ratio);
  for (auto& frame : sp) {
    std::vector<double> buffer = frame;
    for (int i = 0; i < width; ++i) {
      int i1 = indexes[i];
      double t = weights[i];
      frame[i] = buffer[i1] * (1 - t) + buffer[i1 + 1] * t;
    }
  }
}

void ShiftGender(double* sp, int width, int value) {
  double ratio = std::pow(2, value * 0.01);
  if (ratio == 1 || ratio <= 0 || width < 2) {
    return;
  }
  std::vector<int> indexes(width);
  std::vector<double> weights(width);
  GenderWeights(indexes, weights, width, ratio);
  std::vector<double> temp(width);
  std::copy(sp, sp + width, temp.begin());
  for (int i = 0; i < width; ++i) {
    int i1 = indexes[i];
    double t = weights[i];
    sp[i] = temp[i1] * (1 - t) + temp[i1 + 1] * t;
  }
}

static void Logspace(std::vector<double>& vec, int i0, int i1, double power,
                     double v0, double v1) {
  double delta = (v1 - v0) / (i1 - i0);
  double v = v0;
  for (int i = i0; i < i1; ++i) {
    vec[i] = std::pow(power, v);
    v += delta;
  }
}

std::vector<double> GetTensionCoefficients1(double f0, int fs, int value,
                                            int width) {
  std::vector<double> envelope(width);
  int p1 = static_cast<int>(75.0 / 1024 * width);
  int p2 = static_cast<int>(450.0 / 1024 * width);
  int p3 = static_cast<int>(525.0 / 1024 * width);
  int p4 = static_cast<int>(585.0 / 1024 * width);
  double factor = value * 0.01;
  Logspace(envelope, 0, p1, 8, 0 * factor, 1 * factor);
  Logspace(envelope, p1, p2, 8, 1 * factor, 2 * factor);
  Logspace(envelope, p2, p3, 8, 2 * factor, 0 * factor);
  Logspace(envelope, p3, p4, 8, 0 * factor, -0.75 * factor);
  Logspace(envelope, p4, width, 8, -0.75 * factor, -0.75 * factor);
  return envelope;
}

std::vector<double> GetTensionCoefficients(double f0, int fs, int value,
                                           int width) {
  std::vector<double> envelope(width, 1);
  if (f0 < 50) {
    return envelope;
  }
  double v = value * 0.01;
  double s0 = -1.5 * v;
  double s1 = v < 0 ? 4 * v : 2 * v;
  std::vector<double> px;
  std::vector<double> py;
  double f0_bins = f0 / (fs / 2) * width;
  int x = 0;
  px.push_back(x * f0_bins);
  py.push_back(s0);
  x++;
  px.push_back(x * f0_bins);
  py.push_back(s0);
  x += 3;
  while (x * f0_bins < 250) {
    px.push_back(x * f0_bins);
    py.push_back(s1);
    x++;
  }
  while (x * f0_bins < 350) {
    x++;
  }
  while (x * f0_bins < width + f0_bins) {
    px.push_back(x * f0_bins);
    py.push_back(0);
    x++;
  }
  tk::spline spline(px, py);
  for (int i = 0; i < width; ++i) {
    envelope[i] = std::exp(spline(i));
  }
  return envelope;
}

void AutoGain(std::vector<double>& samples, double src_max, double out_max,
              double voiced_ratio, int volume, int peakComp) {
  // weighs between max of full audio file and max of synthed section
  // based on voiced ratio of synthed section
  // to avoid overamplifying consonants.
  double weight = 1.0 / (1.0 + exp(5.0 - 10.0 * voiced_ratio));
  double max = out_max * weight + src_max * (1.0 - weight);
  double gain = volume * 0.01;
  double auto_gain = max == 0 ? 1.0 : std::pow(0.5 / max, peakComp * 0.01);
  if (auto_gain * gain != 1) {
    for (int i = 0; i < samples.size(); ++i) {
      samples[i] = samples[i] * auto_gain * gain;
    }
  }
}

}  // namespace worldline