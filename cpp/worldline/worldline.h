#ifndef WORLDLINE_WORLDLINE_H_
#define WORLDLINE_WORLDLINE_H_

#include "world/common.h"
#include "world/constantnumbers.h"
#include "world/matlabfunctions.h"

#if defined(_MSC_VER)
#define DLL_API __declspec(dllexport)
#elif defined(__GNUC__)
#define DLL_API __attribute__((visibility("default")))
#endif

extern "C" {

// Upper bound on the frames F0 writes. The caller sizes its buffer with this.
DLL_API int F0FrameCount(int length, int fs, double frame_period, int method);

// Writes the f0 contour into the caller-owned f0_out, which must hold
// F0FrameCount() doubles. Returns the number of frames written.
// method: -1 fills the frame count with silence, 1 harvest, 2 pyin, else dio.
DLL_API int F0(const float* samples, int length, int fs, double frame_period,
               int method, double* f0_out);

// spectrogram must hold f0_length * (fft_size / 2 + 1) doubles.
DLL_API void DecodeMgc(int f0_length, double* mgc, int mgc_size, int fft_size,
                       int fs, double* spectrogram);

// aperiodicity must hold f0_length * (fft_size / 2 + 1) doubles.
DLL_API void DecodeBap(int f0_length, double* bap, int fft_size, int fs,
                       double* aperiodicity);

struct AnalysisConfig {
  int fs;
  int hop_size;
  int fft_size;
  float f0_floor;
  double frame_ms;
};

DLL_API void InitAnalysisConfig(AnalysisConfig* config, int fs, int hop_size,
                                int fft_size);

DLL_API void WorldAnalysisF0In(const AnalysisConfig* config, float* samples,
                               int num_samples, double* f0_in, int num_frames,
                               double* sp_env_out, double* ap_out);

// Number of samples WorldSynthesis writes into y for the given contour.
DLL_API int WorldSynthesisSampleCount(int f0_length, double frame_period,
                                      int fs);

// y must hold WorldSynthesisSampleCount() samples.
// gender: [0, 1] default 0.5
// tension: [0, 1] default 0.5
// breathiness: [0, 1] default 0.5
// voicing: [0, 1] default 1
DLL_API int WorldSynthesis(double* const f0, int f0_length,
                           double* const mgc_or_sp, bool is_mgc, int mgc_size,
                           double* const bap_or_ap, bool is_bap, int fft_size,
                           double frame_period, int fs, double* y,
                           double* const gender, double* const tension,
                           double* const breathiness, double* const voicing);
}

#endif  // WORLDLINE_WORLDLINE_H_
