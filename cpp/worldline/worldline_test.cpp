#include "worldline.h"

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
