#include "worldline.h"

#include <cmath>
#include <vector>

#include "gmock/gmock.h"
#include "gtest/gtest.h"

#if defined(_MSC_VER)
#include <windows.h>
#else
#include <dlfcn.h>
#endif

#if defined(_MSC_VER)
#define DLL_IMPORT __declspec(dllimport)
#elif defined(__GNUC__)
#define DLL_IMPORT __attribute__((visibility("default")))
#endif

TEST(WorldlineTest, TestF0) {
#if defined(_MSC_VER)
  HMODULE handle = LoadLibrary("worldline.dll");
  EXPECT_THAT(handle, testing::NotNull());
#else
  dlopen("libworldline", RTLD_LAZY);
#endif

  EXPECT_EQ(F0(nullptr, 0, 44100, 10, 0, nullptr), 0);
  EXPECT_EQ(F0FrameCount(0, 44100, 10, 0), 0);

  // The caller owns the buffer and sizes it with F0FrameCount.
  std::vector<float> samples(44100, 0);
  for (int method : {-1, 0, 2}) {
    int count = F0FrameCount(samples.size(), 44100, 10, method);
    EXPECT_GT(count, 0);
    std::vector<double> f0(count, -1);
    int written =
        F0(samples.data(), samples.size(), 44100, 10, method, f0.data());
    EXPECT_THAT(written, testing::AllOf(testing::Gt(0), testing::Le(count)));
    EXPECT_THAT(f0.back(), testing::Not(testing::DoubleEq(-1)));
  }
}

// Unvoiced, flat sp and fully aperiodic: the output is the noise half alone,
// at WORLD's noise level (per-sample variance sp), and the inputs are kept.
TEST(WorldlineTest, ContinuousNoiseSynthesisLevel) {
  const int fs = 44100, hop = 220, fft_size = 2048;
  const int sp_size = fft_size / 2 + 1, frames = 400;
  const double s = 1e-4;
  std::vector<double> f0(frames, 0);
  std::vector<double> sp(frames * sp_size, s);
  std::vector<double> harmonic_sp(frames * sp_size, s);
  std::vector<double> ap(frames * sp_size, 1.0);
  std::vector<double> stretch(frames, 1.0);
  std::vector<double> gender(frames, 0.5), tension(frames, 0.5);
  std::vector<double> breathiness(frames, 0.5), voicing(frames, 1.0);
  double frame_period = hop * 1000.0 / fs;
  int length = WorldSynthesisSampleCount(frames, frame_period, fs);
  std::vector<double> y(length);
  EXPECT_EQ(WorldSynthesisContinuousNoise(
                f0.data(), frames, sp.data(), harmonic_sp.data(), ap.data(),
                stretch.data(), fft_size, hop, fs, 1, y.data(), gender.data(),
                tension.data(), breathiness.data(), voicing.data()),
            length);
  double sum = 0;
  int lo = length / 10, hi = length * 9 / 10;
  for (int i = lo; i < hi; ++i) sum += y[i] * y[i];
  EXPECT_NEAR(10 * std::log10(sum / (hi - lo) / s), 0.0, 0.5);
  EXPECT_THAT(sp, testing::Each(s));
  EXPECT_THAT(harmonic_sp, testing::Each(s));
  EXPECT_THAT(ap, testing::Each(1.0));
}
