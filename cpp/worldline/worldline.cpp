#include "worldline.h"

#include <algorithm>
#include <cmath>
#include <iterator>
#include <memory>
#include <vector>

#include "world/cheaptrick.h"
#include "world/codec.h"
#include "world/d4c.h"
#include "world/dio.h"
#include "world/synthesis.h"
#include "worldline/common/vec_utils.h"
#include "worldline/f0/dio_estimator.h"
#include "worldline/f0/dio_ss_estimator.h"
#include "worldline/f0/f0_estimator.h"
#include "worldline/f0/harvest_estimator.h"
#include "worldline/f0/pyin_estimator.h"
#include "worldline/model/effects.h"

static double** to2d(double* const arr, int length, int width) {
  double** arr2d = new double*[length];
  for (int i = 0; i < length; ++i) {
    arr2d[i] = arr + i * width;
  }
  return arr2d;
}

DLL_API int F0FrameCount(int length, int fs, double frame_period, int method) {
  if (length <= 0) {
    return 0;
  }
  // DIO, Harvest and the -1 placeholder all use the WORLD frame grid.
  int count = GetSamplesForDIO(fs, length, frame_period);
  if (method == 2) {
    // pyin steps by round(fs * frame_period / 1000) samples, which the rounding
    // can make slightly shorter than frame_period.
    int nhop =
        std::max(1, static_cast<int>(std::lround(fs * frame_period / 1000.0)));
    count = std::max(count, length / nhop);
  }
  return count;
}

DLL_API int F0(const float* samples, int length, int fs, double frame_period,
               int method, double* f0_out) {
  // Check if there's an issue with the WAV file
  if (length <= 0 || samples == nullptr || f0_out == nullptr) {
    return 0;
  }
  // Clamp to the count the caller sized its buffer with, so that a disagreement
  // between the two exports cannot write past the end of it.
  int capacity = F0FrameCount(length, fs, frame_period, method);
  // -1 asks for the frame count only: the caller fills the silent contour in.
  if (method == -1) {
    std::fill(f0_out, f0_out + capacity, 0);
    return capacity;
  }
  std::unique_ptr<worldline::F0Estimator> estimator;
  switch (method) {
    case 1:
      estimator = std::make_unique<worldline::HarvestEstimator>();
      break;
    case 2:
      estimator = std::make_unique<worldline::PyinEstimator>();
      break;
    default:
      estimator = std::make_unique<worldline::DioEstimator>();
      break;
  }
  std::vector<double> samples_vec(length, 0);
  std::copy(samples, samples + length, samples_vec.begin());
  std::vector<double> f0_vec;
  std::vector<double> ts_vec;
  estimator->Estimate(samples_vec, fs, frame_period, &f0_vec, &ts_vec);
  int f0_length = std::min(capacity, static_cast<int>(f0_vec.size()));
  std::copy(f0_vec.begin(), f0_vec.begin() + f0_length, f0_out);
  std::fill(f0_out + f0_length, f0_out + capacity, 0);
  return f0_length;
}

DLL_API void DecodeMgc(int f0_length, double* mgc, int mgc_size, int fft_size,
                       int fs, double* spectrogram) {
  int sp_size = fft_size / 2 + 1;
  double** mgc2d = to2d(mgc, f0_length, mgc_size);
  double** sp2d = to2d(spectrogram, f0_length, sp_size);
  DecodeSpectralEnvelope(mgc2d, f0_length, fs, fft_size, mgc_size, sp2d);
  delete[] sp2d;
}

DLL_API void DecodeBap(int f0_length, double* bap, int fft_size, int fs,
                       double* aperiodicity) {
  int bap_size = GetNumberOfAperiodicities(fs);
  int ap_size = fft_size / 2 + 1;
  double** bap2d = to2d(bap, f0_length, bap_size);
  double** ap2d = to2d(aperiodicity, f0_length, ap_size);
  DecodeAperiodicity(bap2d, f0_length, fs, fft_size, ap2d);
  delete[] ap2d;
}

void InitAnalysisConfig(AnalysisConfig* config, int fs, int hop_size,
                        int fft_size) {
  config->fs = fs;
  config->hop_size = hop_size;
  config->fft_size = fft_size;
  config->f0_floor = (float)GetF0FloorForCheapTrick(fs, fft_size);
  config->frame_ms = static_cast<double>(config->hop_size) * 1000.0 /
                     static_cast<double>(config->fs);
}

DLL_API void WorldAnalysisF0In(const AnalysisConfig* config, float* samples,
                               int num_samples, double* f0_in, int num_frames,
                               double* sp_env_out, double* ap_out) {
  std::vector<double> samples_vec;
  samples_vec.reserve(num_samples);
  std::copy(samples, samples + num_samples, std::back_inserter(samples_vec));
  std::vector<double> ts_vec;
  ts_vec.reserve(num_frames);
  for (int i = 0; i < num_frames; ++i) {
    ts_vec.push_back(i * config->frame_ms / 1000.0);
  }

  int sp_size = config->fft_size / 2 + 1;
  double** sp_env_2d = to2d(sp_env_out, num_frames, sp_size);
  double** ap_2d = to2d(ap_out, num_frames, sp_size);

  CheapTrickOption ct_option;
  InitializeCheapTrickOption(config->fs, &ct_option);
  ct_option.f0_floor = config->f0_floor;
  ct_option.fft_size = config->fft_size;
  CheapTrick(samples_vec.data(), samples_vec.size(), config->fs, ts_vec.data(),
             f0_in, num_frames, &ct_option, sp_env_2d);

  D4COption d4c_option;
  InitializeD4COption(&d4c_option);
  d4c_option.threshold = 0;
  D4C(samples_vec.data(), samples_vec.size(), config->fs, ts_vec.data(), f0_in,
      num_frames, config->fft_size, &d4c_option, ap_2d);

  delete[] sp_env_2d;
  delete[] ap_2d;
}

DLL_API int WorldSynthesisSampleCount(int f0_length, double frame_period,
                                      int fs) {
  if (f0_length <= 0) {
    return 0;
  }
  return 1 + static_cast<int>((f0_length - 1) * frame_period / 1000.0 * fs);
}

DLL_API int WorldSynthesis(double* const f0, int f0_length,
                           double* const mgc_or_sp, bool is_mgc, int mgc_size,
                           double* const bap_or_ap, bool is_bap, int fft_size,
                           double frame_period, int fs, double* y,
                           double* const gender, double* const tension,
                           double* const breathiness, double* const voicing) {
  int bap_size = GetNumberOfAperiodicities(fs);
  int sp_size = fft_size / 2 + 1;

  double** sp = nullptr;
  if (is_mgc) {
    double** mgc2d = to2d(mgc_or_sp, f0_length, mgc_size);
    sp = new double*[f0_length];
    for (int i = 0; i < f0_length; ++i) {
      sp[i] = new double[sp_size];
    }
    DecodeSpectralEnvelope(mgc2d, f0_length, fs, fft_size, mgc_size, sp);
    delete[] mgc2d;
  } else {
    sp = to2d(mgc_or_sp, f0_length, sp_size);
  }

  double** ap = nullptr;
  if (is_bap) {
    double** bap2d = to2d(bap_or_ap, f0_length, bap_size);
    ap = new double*[f0_length];
    for (int i = 0; i < f0_length; ++i) {
      ap[i] = new double[sp_size];
    }
    DecodeAperiodicity(bap2d, f0_length, fs, fft_size, ap);
    delete[] bap2d;
  } else {
    ap = to2d(bap_or_ap, f0_length, sp_size);
  }

  int y_length = WorldSynthesisSampleCount(f0_length, frame_period, fs);

  if (gender != nullptr) {
    for (int i = 0; i < f0_length; ++i) {
      worldline::ShiftGender(sp[i], sp_size,
                             std::lround((gender[i] - 0.5) * 200));
    }
  }

  std::vector<std::vector<double>> ten =
      worldline::vec2d(sp_size, f0_length, 1);
  if (tension != nullptr) {
    for (int i = 0; i < f0_length; ++i) {
      ten[i] = worldline::GetTensionCoefficients(
          f0[i], fs, std::lround((tension[i] - 0.5) * 200), sp_size);
    }
  }

  std::vector<double> bre(f0_length, 1);
  if (breathiness != nullptr) {
    for (int i = 0; i < f0_length; ++i) {
      // Linear gain on the aperiodic part, continuous at 0.5 (= 1, unmodified):
      // [0, 0.5] -> [0, 1], (0.5, 1] -> (1, 3].
      bre[i] =
          breathiness[i] > 0.5 ? breathiness[i] * 4 - 1 : breathiness[i] * 2;
    }
  }

  std::vector<double> voi(f0_length, 1);
  if (voicing != nullptr) {
    for (int i = 0; i < f0_length; ++i) {
      voi[i] = voicing[i];
    }
  }

  auto ten_wrapper = worldline::vec2d_wrapper(ten);
  Synthesis(f0, f0_length, sp, ap, fft_size, frame_period, fs,
            ten_wrapper.data(), bre.data(), voi.data(), y_length, y);

  if (is_mgc) {
    for (int i = 0; i < f0_length; ++i) {
      delete[] sp[i];
    }
  }
  delete[] sp;

  if (is_bap) {
    for (int i = 0; i < f0_length; ++i) {
      delete[] ap[i];
    }
  }
  delete[] ap;

  return y_length;
}
