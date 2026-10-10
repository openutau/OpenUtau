#include "continuous_noise.h"

#include <cmath>
#include <vector>

#include "gtest/gtest.h"

namespace worldline {
namespace {

// WORLD's noise with a flat spectrum S has per-sample variance S: the shaped
// noise must match that level whatever alpha is.
TEST(ContinuousNoiseTest, MatchesWorldNoiseLevel) {
  const int hop = 441;
  const int fft_size = 2048;
  const int bins = fft_size / 2 + 1;
  const int num_frames = 400;
  const int length = (num_frames - 1) * hop;
  const double s = 1e-4;
  std::vector<double> target(num_frames * bins, s * WindowPower(hop));
  for (double a : {1.0, 0.5, 0.0}) {
    std::vector<double> alpha(num_frames, a);
    auto y = ContinuousNoise(target.data(), num_frames, hop, length,
                             alpha.data(), 7, fft_size);
    ASSERT_EQ(y.size(), length);
    double sum = 0;
    int lo = length / 10, hi = length * 9 / 10;
    for (int i = lo; i < hi; ++i) sum += y[i] * y[i];
    double db = 10 * std::log10(sum / (hi - lo) / s);
    EXPECT_NEAR(db, 0.0, 0.5) << "alpha=" << a;
  }
}

TEST(ContinuousNoiseTest, SameSeedSameNoise) {
  const int hop = 220, fft_size = 2048, bins = fft_size / 2 + 1;
  const int num_frames = 50, length = (num_frames - 1) * hop;
  std::vector<double> target(num_frames * bins, 1.0);
  std::vector<double> alpha(num_frames, 0.5);
  auto a = ContinuousNoise(target.data(), num_frames, hop, length,
                           alpha.data(), 3, fft_size);
  auto b = ContinuousNoise(target.data(), num_frames, hop, length,
                           alpha.data(), 3, fft_size);
  auto c = ContinuousNoise(target.data(), num_frames, hop, length,
                           alpha.data(), 4, fft_size);
  EXPECT_EQ(a, b);
  EXPECT_NE(a, c);
}

TEST(ContinuousNoiseTest, SmoothFreqAveragesWithReplicatedEdges) {
  std::vector<double> p = {0, 3, 6, 9};
  SmoothFreq(p, 4, 3);
  EXPECT_DOUBLE_EQ(p[0], 1);  // (0 + 0 + 3) / 3
  EXPECT_DOUBLE_EQ(p[1], 3);
  EXPECT_DOUBLE_EQ(p[2], 6);
  EXPECT_DOUBLE_EQ(p[3], 8);  // (6 + 9 + 9) / 3
}

}  // namespace
}  // namespace worldline
