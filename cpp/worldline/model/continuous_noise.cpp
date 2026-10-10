#include "continuous_noise.h"

#include <algorithm>
#include <cmath>
#include <random>

#include "world/fft.h"

namespace worldline {

namespace {

constexpr double kPi = 3.14159265358979323846;

std::vector<double> Window(int hop) {
  int n = 2 * hop;
  std::vector<double> w(n);
  for (int i = 0; i < n; ++i) {
    w[i] = 0.5 - 0.5 * std::cos(2 * kPi * i / (n - 1));
  }
  return w;
}

// Forward and inverse real FFTs of one size on shared buffers.
class FrameFft {
 public:
  explicit FrameFft(int n)
      : n_(n), time_(n), spectrum_(new fft_complex[n / 2 + 1]) {
    forward_ = fft_plan_dft_r2c_1d(n, time_.data(), spectrum_, FFT_ESTIMATE);
    inverse_ = fft_plan_dft_c2r_1d(n, spectrum_, time_.data(), FFT_ESTIMATE);
  }
  ~FrameFft() {
    fft_destroy_plan(forward_);
    fft_destroy_plan(inverse_);
    delete[] spectrum_;
  }
  FrameFft(const FrameFft&) = delete;
  FrameFft& operator=(const FrameFft&) = delete;

  std::vector<double>& time() { return time_; }
  fft_complex* spectrum() { return spectrum_; }
  void Forward() { fft_execute(forward_); }
  // Unnormalized, as FFTW: the caller divides by n.
  void Inverse() { fft_execute(inverse_); }

 private:
  int n_;
  std::vector<double> time_;
  fft_complex* spectrum_;
  fft_plan forward_;
  fft_plan inverse_;
};

// Frame i's windowed samples of y into the middle of the zeroed time buffer.
void LoadFrame(const double* y, int length, int i, int hop,
               const std::vector<double>& w, std::vector<double>& time) {
  int nwin = static_cast<int>(w.size());
  int start = static_cast<int>(time.size()) / 2 - nwin / 2;
  int lo = i * hop - nwin / 2;
  std::fill(time.begin(), time.end(), 0.0);
  for (int j = 0; j < nwin; ++j) {
    int t = lo + j;
    if (t >= 0 && t < length) {
      time[start + j] = y[t] * w[j];
    }
  }
}

// Scales y so its total periodogram power over the target's loudest quarter
// of entries matches the target's; corrects the constant level change from
// the second window and the overlap-add.
void Relevel(std::vector<double>& y, const double* target, int num_frames,
             int hop, int fft_size) {
  int bins = fft_size / 2 + 1;
  size_t count = static_cast<size_t>(num_frames) * bins;
  if (count == 0) {
    return;
  }
  std::vector<double> sorted(target, target + count);
  size_t q = static_cast<size_t>(0.75 * (count - 1));
  std::nth_element(sorted.begin(), sorted.begin() + q, sorted.end());
  double threshold = sorted[q];
  auto got = StftPower(y.data(), static_cast<int>(y.size()), hop, num_frames,
                       fft_size);
  double want = 0, have = 0;
  for (size_t i = 0; i < count; ++i) {
    if (target[i] >= threshold) {
      want += target[i];
      have += got[i];
    }
  }
  if (want > 0 && have > 0) {
    double gain = std::sqrt(want / have);
    for (double& v : y) {
      v *= gain;
    }
  }
}

}  // namespace

double WindowPower(int hop) {
  double sum = 0;
  for (double v : Window(hop)) {
    sum += v * v;
  }
  return sum;
}

std::vector<double> StftPower(const double* y, int length, int hop,
                              int num_frames, int fft_size) {
  auto w = Window(hop);
  int bins = fft_size / 2 + 1;
  std::vector<double> power(static_cast<size_t>(num_frames) * bins);
  FrameFft fft(fft_size);
  for (int i = 0; i < num_frames; ++i) {
    LoadFrame(y, length, i, hop, w, fft.time());
    fft.Forward();
    const fft_complex* s = fft.spectrum();
    double* row = power.data() + static_cast<size_t>(i) * bins;
    for (int k = 0; k < bins; ++k) {
      row[k] = s[k][0] * s[k][0] + s[k][1] * s[k][1];
    }
  }
  return power;
}

void SmoothFreq(std::vector<double>& power, int bins, int width) {
  if (width <= 1 || bins <= 0) {
    return;
  }
  int pad = width / 2;
  std::vector<double> row(bins + 2 * pad);
  size_t frames = power.size() / bins;
  for (size_t i = 0; i < frames; ++i) {
    double* p = power.data() + i * bins;
    for (int k = 0; k < static_cast<int>(row.size()); ++k) {
      row[k] = p[std::clamp(k - pad, 0, bins - 1)];
    }
    double sum = 0;
    for (int k = 0; k < width; ++k) {
      sum += row[k];
    }
    for (int k = 0; k < bins; ++k) {
      p[k] = sum / width;
      if (k + width < static_cast<int>(row.size())) {
        sum += row[k + width] - row[k];
      }
    }
  }
}

std::vector<double> ContinuousNoise(const double* target, int num_frames,
                                    int hop, int length, const double* alpha,
                                    uint64_t seed, int fft_size) {
  int bins = fft_size / 2 + 1;
  auto w = Window(hop);
  double expected = WindowPower(hop);
  double log_expected = std::log(expected);

  // Box-Muller on a fixed engine, so the noise doesn't depend on the standard
  // library's normal_distribution.
  std::mt19937_64 engine(seed);
  auto uniform = [&engine]() {  // (0, 1)
    return ((engine() >> 11) + 0.5) * (1.0 / 9007199254740992.0);
  };
  std::vector<double> excitation(length);
  for (int i = 0; i < length; i += 2) {
    double r = std::sqrt(-2 * std::log(uniform()));
    double theta = 2 * kPi * uniform();
    excitation[i] = r * std::cos(theta);
    if (i + 1 < length) {
      excitation[i + 1] = r * std::sin(theta);
    }
  }

  constexpr int kFade = 16;
  std::vector<double> fade(fft_size, 1.0);
  for (int j = 0; j < kFade; ++j) {
    fade[j] = static_cast<double>(j) / kFade;
    fade[fft_size - 1 - j] = 1 - static_cast<double>(kFade - 1 - j) / kFade;
  }

  // Buffer index j of frame i is sample i * hop - fft_size / 2 + j; the
  // overlap-add buffer is offset by fft_size / 2.
  std::vector<double> ola(static_cast<size_t>(length) + fft_size);
  std::vector<double> measured(bins);
  FrameFft fft(fft_size);
  for (int i = 0; i < num_frames; ++i) {
    LoadFrame(excitation.data(), length, i, hop, w, fft.time());
    fft.Forward();
    fft_complex* s = fft.spectrum();
    for (int k = 0; k < bins; ++k) {
      measured[k] = s[k][0] * s[k][0] + s[k][1] * s[k][1];
    }
    SmoothFreq(measured, bins, 3);
    double a = alpha[i];
    const double* t = target + static_cast<size_t>(i) * bins;
    for (int k = 0; k < bins; ++k) {
      double reference =
          a >= 1   ? measured[k] + 1e-12
          : a <= 0 ? expected
                   : std::exp(a * std::log(measured[k] + 1e-12) +
                              (1 - a) * log_expected);
      double g = std::sqrt(t[k] / (reference + 1e-12));
      s[k][0] *= g;
      s[k][1] *= g;
    }
    s[0][1] = 0;
    s[bins - 1][1] = 0;
    fft.Inverse();
    const auto& time = fft.time();
    size_t lo = static_cast<size_t>(i) * hop;
    if (lo >= ola.size()) {
      break;
    }
    size_t count = std::min(static_cast<size_t>(fft_size), ola.size() - lo);
    for (size_t j = 0; j < count; ++j) {
      ola[lo + j] += time[j] / fft_size * fade[j];
    }
  }
  std::vector<double> y(length);
  std::copy(ola.begin() + fft_size / 2, ola.begin() + fft_size / 2 + length,
            y.begin());
  Relevel(y, target, num_frames, hop, fft_size);
  return y;
}

}  // namespace worldline
