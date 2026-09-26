#include "model.h"

#include <algorithm>
#include <memory>

#include "world/cheaptrick.h"
#include "world/constantnumbers.h"
#include "world/d4c.h"
#include "world/dio.h"
#include "world/synthesis.h"
#include "worldline/common/vec_utils.h"

namespace worldline {

Model::Model(std::vector<double> samples, int fs, double frame_ms,
             std::unique_ptr<F0Estimator> f0_estimator)
    : samples_(std::move(samples)),
      fs_(fs),
      frame_ms_(frame_ms),
      f0_estimator_(std::move(f0_estimator)) {}
Model::Model(int fs, double frame_ms, int fft_size)
    : fs_(fs), frame_ms_(frame_ms), fft_size_(fft_size) {}

void Model::BuildF0() {
  f0_estimator_->Estimate(samples_, fs_, frame_ms_, &f0_, &ts_);
}

void Model::BuildSp() {
  CheapTrickOption ct_option;
  InitializeCheapTrickOption(fs_, &ct_option);
  fft_size_ = ct_option.fft_size;
  sp_ = vec2d(fft_size_ / 2 + 1, f0_.size(), 0);
  std::vector<double*> sp_wrapper = vec2d_wrapper(sp_);
  CheapTrick(samples_.data(), samples_.size(), fs_, ts_.data(), f0_.data(),
             f0_.size(), &ct_option, sp_wrapper.data());
}

void Model::BuildAp() {
  D4COption d4c_option;
  InitializeD4COption(&d4c_option);
  d4c_option.threshold = 0;
  ap_ = vec2d(fft_size_ / 2 + 1, f0_.size(), 0);
  std::vector<double*> ap_wrapper = vec2d_wrapper(ap_);
  D4C(samples_.data(), samples_.size(), fs_, ts_.data(), f0_.data(), f0_.size(),
      fft_size_, &d4c_option, ap_wrapper.data());
}

void Model::SynthParams(std::vector<std::vector<double>>* tension,
                        std::vector<double>* breathiness,
                        std::vector<double>* voicing) {
  *tension = vec2d(sp_[0].size(), f0_.size(), 1);
  *breathiness = std::vector<double>(f0_.size(), 1);
  *voicing = std::vector<double>(f0_.size(), 1);
}

void Model::Synth(std::vector<std::vector<double>>& tension,
                  std::vector<double>& breathiness,
                  std::vector<double>& voicing) {
  int y_len = static_cast<int>(fs_ * (f0_.size() - 1) * frame_ms_ / 1000.0) + 1;
  std::vector<double> y = std::vector<double>(y_len);
  std::vector<double*> sp_wrapper = vec2d_wrapper(sp_);
  std::vector<double*> ap_wrapper = vec2d_wrapper(ap_);
  std::vector<double*> tension_wrapper = vec2d_wrapper(tension);
  Synthesis(f0_.data(), f0_.size(), sp_wrapper.data(), ap_wrapper.data(),
            fft_size_, frame_ms_, fs_, tension_wrapper.data(),
            breathiness.data(), voicing.data(), y_len, y.data());
  samples_ = std::move(y);
}

void Model::Trim(int start, int length) {
  int start_samples = static_cast<int>(frame_ms_ * start * fs_ / 1000.0);
  int length_samples = static_cast<int>(frame_ms_ * length * fs_ / 1000.0);
  samples_.erase(samples_.begin(), samples_.begin() + start_samples);
  samples_.erase(samples_.begin() + length_samples, samples_.end());
  if (f0_.size() > 0) {
    f0_.erase(f0_.begin(), f0_.begin() + start);
    f0_.erase(f0_.begin() + length, f0_.end());
  }
  if (ts_.size() > 0) {
    ts_.erase(ts_.begin(), ts_.begin() + start);
    ts_.erase(ts_.begin() + length, ts_.end());
  }
  double t0 = ts_[0];
  for (int i = 0; i < ts_.size(); ++i) {
    ts_.data()[i] -= t0;
  }
  if (sp_.size() > 0) {
    sp_.erase(sp_.begin(), sp_.begin() + start);
    sp_.erase(sp_.begin() + length, sp_.end());
  }
  if (ap_.size() > 0) {
    ap_.erase(ap_.begin(), ap_.begin() + start);
    ap_.erase(ap_.begin() + length, ap_.end());
  }
}

void Model::Remap(const std::vector<double>& mapping) {
  std::vector<double> new_f0;
  std::vector<std::vector<double>> new_sp;
  std::vector<std::vector<double>> new_ap;
  new_f0.reserve(mapping.size());
  new_sp.reserve(mapping.size());
  new_ap.reserve(mapping.size());
  for (double p : mapping) {
    double pos = p / frame_ms_;
    int idx = static_cast<int>(pos);
    double t = pos - idx;
    int i0 = std::min(idx, (int)f0_.size() - 1);
    int i1 = std::min(idx + 1, (int)f0_.size() - 1);
    new_f0.push_back(f0_[i0] * (1.0 - t) + f0_[i1] * t);
    new_sp.push_back(vec_lerp(sp_[i0], sp_[i1], t));
    new_ap.push_back(vec_lerp(ap_[i0], ap_[i1], t));
  }
  f0_ = std::move(new_f0);
  sp_ = std::move(new_sp);
  ap_ = std::move(new_ap);
}

double Model::GetVoicedRatio() {
  int voiced = std::count_if(f0_.begin(), f0_.end(), [](double f) {
    return f > world::kFloorF0StoneMask;
  });
  return (double)voiced / f0_.size();
}

int Model::MsToSamples(double ms) { return static_cast<int>(ms * fs_ / 1000); }

}  // namespace worldline
