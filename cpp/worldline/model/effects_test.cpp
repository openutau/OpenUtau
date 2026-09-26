#include "effects.h"

#include <vector>

#include "gtest/gtest.h"

namespace worldline {
namespace {

// A flat envelope must stay flat no matter how it is stretched.
TEST(ShiftGenderTest, FlatSpectrumStaysFlat) {
  const int width = 1025;
  for (int value : {-100, -50, -15, -1, 1, 15, 50, 100}) {
    std::vector<double> sp(width, 1.0);
    ShiftGender(sp.data(), width, value);
    for (int i = 0; i < width; ++i) {
      ASSERT_DOUBLE_EQ(sp[i], 1.0) << "value=" << value << " bin=" << i;
    }
  }
}

// Output bin i samples the input envelope at bin i * 2^(value / 100).
TEST(ShiftGenderTest, SamplesInputAtScaledBin) {
  const int width = 1025;
  for (int value : {-100, -50, -15, 15, 50, 100}) {
    std::vector<double> sp(width);
    for (int i = 0; i < width; ++i) sp[i] = i;
    ShiftGender(sp.data(), width, value);
    double ratio = std::pow(2, value * 0.01);
    for (int i = 0; i < width; ++i) {
      double expected = std::min(i * ratio, width - 1.0);
      ASSERT_NEAR(sp[i], expected, 1e-9) << "value=" << value << " bin=" << i;
    }
  }
}

TEST(ShiftGenderTest, FramesOverloadMatchesPointerOverload) {
  const int width = 513;
  std::vector<double> a(width);
  for (int i = 0; i < width; ++i) a[i] = 1.0 + (i % 7);
  std::vector<std::vector<double>> frames = {a, a};
  ShiftGender(a.data(), width, -15);
  ShiftGender(frames, -15);
  for (int i = 0; i < width; ++i) {
    ASSERT_DOUBLE_EQ(frames[0][i], a[i]);
    ASSERT_DOUBLE_EQ(frames[1][i], a[i]);
  }
}

}  // namespace
}  // namespace worldline
