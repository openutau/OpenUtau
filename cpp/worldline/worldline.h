#ifndef WORLDLINE_WORLDLINE_H_
#define WORLDLINE_WORLDLINE_H_

#include "world/common.h"
#include "world/constantnumbers.h"
#include "world/matlabfunctions.h"
#include "worldline/phrase_synth.h"
#include "worldline/synth_request.h"

#if defined(_MSC_VER)
#define DLL_API __declspec(dllexport)
#elif defined(__GNUC__)
#define DLL_API __attribute__((visibility("default")))
#endif

using worldline::PhraseSynth;

extern "C" {

DLL_API int F0(float* samples, int length, int fs, double frame_period,
               int method, double** f0);

DLL_API int DecodeMgc(int f0_length, double* mgc, int mgc_size, int fft_size,
                      int fs, double** spectrogram);

DLL_API int DecodeBap(int f0_length, double* bap, int fft_size, int fs,
                      double** aperiodicity);

struct AnalysisConfig {
  int fs;
  int hop_size;
  int fft_size;
  float f0_floor;
  double frame_ms;
};

DLL_API void InitAnalysisConfig(AnalysisConfig* config, int fs, int hop_size,
                                int fft_size);

DLL_API void WorldAnalysis(const AnalysisConfig* config, float* samples,
                           int num_samples, double** f0_out,
                           double** sp_env_out, double** ap_out,
                           int* num_frames);

DLL_API void WorldAnalysisF0In(const AnalysisConfig* config, float* samples,
                               int num_samples, double* f0_in, int num_frames,
                               double* sp_env_out, double* ap_out);

// gender: [0, 1] default 0.5
// tension: [0, 1] default 0.5
// breathiness: [0, 1] default 0.5
// voicing: [0, 1] default 1
DLL_API int WorldSynthesis(double* const f0, int f0_length,
                           double* const mgc_or_sp, bool is_mgc, int mgc_size,
                           double* const bap_or_ap, bool is_bap, int fft_size,
                           double frame_period, int fs, double** y,
                           double* const gender, double* const tension,
                           double* const breathiness, double* const voicing);

DLL_API int Resample(const SynthRequest* request, float** y);

DLL_API PhraseSynth* PhraseSynthNew();

DLL_API void PhraseSynthDelete(PhraseSynth* phrase_synth);

DLL_API void PhraseSynthAddRequest(PhraseSynth* phrase_synth,
                                   const SynthRequest* request, double pos_ms,
                                   double skip_ms, double length_ms,
                                   double fade_in_ms, double fade_out_ms,
                                   worldline::LogCallback logCallback);

DLL_API void PhraseSynthSetCurves(PhraseSynth* phrase_synth, double* f0,
                                  double* gender, double* tension,
                                  double* breathiness, double* voicing,
                                  int length,
                                  worldline::LogCallback logCallback);

DLL_API int PhraseSynthSynth(PhraseSynth* phrase_synth, float** y,
                             worldline::LogCallback logCallback);
}

#endif  // WORLDLINE_WORLDLINE_H_
