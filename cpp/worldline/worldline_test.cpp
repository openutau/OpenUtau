#include "worldline.h"

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

extern "C" {
DLL_API int F0(float* samples, int length, int fs, double frame_period,
               int method, double** f0);
}

TEST(WorldlineTest, TestF0) {
#if defined(_MSC_VER)
  HMODULE handle = LoadLibrary("worldline.dll");
  EXPECT_THAT(handle, testing::NotNull());
#else
  dlopen("libworldline", RTLD_LAZY);
#endif

  double* f0 = nullptr;
  EXPECT_EQ(F0(nullptr, 0, 44100, 10, 0, &f0), 0);
  EXPECT_THAT(f0, testing::NotNull());
  delete[] f0;
}
