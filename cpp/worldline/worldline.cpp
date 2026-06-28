#include "worldline.h"

#include <algorithm>
#include <iterator>
#include <vector>

#include "world/cheaptrick.h"
#include "world/codec.h"
#include "world/d4c.h"
#include "world/dio.h"
#include "world/synthesis.h"
#include "worldline/classic/resampler.h"
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

DLL_API int F0(float* samples, int length, int fs, double frame_period,
               int method, double** f0) {
  std::unique_ptr<worldline::F0Estimator> estimator;
  switch (method) {
    case -1: {
      int f0_length = GetSamplesForDIO(fs, length, frame_period);
      *f0 = new double[f0_length];
      return f0_length;
    }
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
  int f0_length = f0_vec.size();
  *f0 = new double[f0_length];
  std::copy(f0_vec.begin(), f0_vec.end(), *f0);
  return f0_length;
}

DLL_API int DecodeMgc(int f0_length, double* mgc, int mgc_size, int fft_size,
                      int fs, double** spectrogram) {
  int sp_size = fft_size / 2 + 1;
  double** mgc2d = to2d(mgc, f0_length, mgc_size);
  *spectrogram = new double[f0_length * sp_size];
  double** sp2d = to2d(*spectrogram, f0_length, sp_size);
  DecodeSpectralEnvelope(mgc2d, f0_length, fs, fft_size, mgc_size, sp2d);
  delete[] sp2d;
  return sp_size;
}

DLL_API int DecodeBap(int f0_length, double* bap, int fft_size, int fs,
                      double** aperiodicity) {
  int bap_size = GetNumberOfAperiodicities(fs);
  int ap_size = fft_size / 2 + 1;
  double** bap2d = to2d(bap, f0_length, bap_size);
  *aperiodicity = new double[f0_length * ap_size];
  double** ap2d = to2d(*aperiodicity, f0_length, ap_size);
  DecodeAperiodicity(bap2d, f0_length, fs, fft_size, ap2d);
  delete[] ap2d;
  return ap_size;
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

DLL_API void WorldAnalysis(const AnalysisConfig* config, float* samples,
                           int num_samples, double** f0_out,
                           double** sp_env_out, double** ap_out,
                           int* num_frames) {
  std::vector<double> samples_vec;
  samples_vec.reserve(num_samples);
  std::copy(samples, samples + num_samples, std::back_inserter(samples_vec));
  auto f0_estimator = std::make_unique<worldline::PyinEstimator>();
  worldline::Model model(std::move(samples_vec), config->fs, config->frame_ms,
                         std::move(f0_estimator));
  model.BuildF0();
  model.BuildSp();
  model.BuildAp();
  *num_frames = model.f0().size();
  *f0_out = new double[*num_frames];
  int sp_size = config->fft_size / 2 + 1;
  *sp_env_out = new double[*num_frames * sp_size];
  *ap_out = new double[*num_frames * sp_size];
  std::copy(model.f0().begin(), model.f0().end(), *f0_out);
  for (int i = 0; i < *num_frames; ++i) {
    std::copy(model.sp()[i].begin(), model.sp()[i].end(),
              *sp_env_out + i * sp_size);
    std::copy(model.ap()[i].begin(), model.ap()[i].end(),
              *ap_out + i * sp_size);
  }
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
  // d4c_option.threshold = 0;
  D4C(samples_vec.data(), samples_vec.size(), config->fs, ts_vec.data(), f0_in,
      num_frames, config->fft_size, &d4c_option, ap_2d);

  delete[] sp_env_2d;
  delete[] ap_2d;
}

DLL_API int WorldSynthesis(double* const f0, int f0_length,
                           double* const mgc_or_sp, bool is_mgc, int mgc_size,
                           double* const bap_or_ap, bool is_bap, int fft_size,
                           double frame_period, int fs, double** y,
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

  int y_length =
      1 + static_cast<int>((f0_length - 1) * frame_period / 1000.0 * fs);
  *y = new double[y_length];

  if (gender != nullptr) {
    for (int i = 0; i < f0_length; ++i) {
      worldline::ShiftGender(sp[i], sp_size, (gender[i] - 0.5) * 200);
    }
  }

  std::vector<std::vector<double>> ten =
      worldline::vec2d(sp_size, f0_length, 1);
  if (tension != nullptr) {
    for (int i = 0; i < f0_length; ++i) {
      ten[i] = worldline::GetTensionCoefficients(
          f0[i], fs, (tension[i] - 0.5) * 200, sp_size);
    }
  }

  std::vector<double> bre(f0_length, 1);
  if (breathiness != nullptr) {
    for (int i = 0; i < f0_length; ++i) {
      bre[i] = breathiness[i] > 0.5 ? breathiness[i] * 4 : breathiness[i] * 2;
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
            ten_wrapper.data(), bre.data(), voi.data(), y_length, *y);

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

DLL_API int Resample(const SynthRequest* request, float** y) {
  auto resampler = std::make_unique<worldline::Resampler>(*request);
  std::vector<double> out = resampler->Resample();
  *y = new float[out.size()];
  std::copy(out.begin(), out.end(), *y);
  return out.size();
}

DLL_API PhraseSynth* PhraseSynthNew() { return new PhraseSynth(); }

DLL_API void PhraseSynthDelete(PhraseSynth* phrase_synth) {
  delete phrase_synth;
}

DLL_API void PhraseSynthAddRequest(PhraseSynth* phrase_synth,
                                   const SynthRequest* request, double pos_ms,
                                   double skip_ms, double length_ms,
                                   double fade_in_ms, double fade_out_ms,
                                   worldline::LogCallback logCallback) {
  phrase_synth->AddRequest(*request, pos_ms, skip_ms, length_ms, fade_in_ms,
                           fade_out_ms, logCallback);
}

DLL_API void PhraseSynthSetCurves(PhraseSynth* phrase_synth, double* f0,
                                  double* gender, double* tension,
                                  double* breathiness, double* voicing,
                                  int length,
                                  worldline::LogCallback logCallback) {
  phrase_synth->SetCurves(f0, gender, tension, breathiness, voicing, length,
                          logCallback);
}

DLL_API int PhraseSynthSynth(PhraseSynth* phrase_synth, float** y,
                             worldline::LogCallback logCallback) {
  std::vector<double> samples = phrase_synth->Synth(logCallback);
  int yLength = samples.size();
  *y = new float[samples.size()];
  std::copy(samples.begin(), samples.end(), *y);
  return yLength;
}
