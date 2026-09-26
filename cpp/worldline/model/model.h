#ifndef WORLDLINE_MODEL_MODEL_H_
#define WORLDLINE_MODEL_MODEL_H_

#include <memory>
#include <vector>

#include "worldline/f0/f0_estimator.h"

namespace worldline {

class Model {
 public:
  Model(std::vector<double> samples, int fs, double frame_ms,
        std::unique_ptr<F0Estimator> f0_estimator);
  Model(int fs, double frame_ms, int fft_size);

  void BuildF0();
  void BuildSp();
  void BuildAp();

  void SynthParams(std::vector<std::vector<double>>* tension,
                   std::vector<double>* breathiness,
                   std::vector<double>* voicing);
  void Synth(std::vector<std::vector<double>>& tension,
             std::vector<double>& breathiness, std::vector<double>& voicing);

  void Trim(int start, int length);
  void Remap(const std::vector<double>& frame_positions);

  double GetVoicedRatio();
  int MsToSamples(double ms);

  std::vector<double>& samples() { return samples_; }
  int fs() { return fs_; }
  double total_ms() { return samples_.size() * 1000.0 / fs_; }

  double frame_ms() { return frame_ms_; }

  std::vector<double>& f0() { return f0_; }
  std::vector<double>& ts() { return ts_; }

  int fft_size() { return fft_size_; }
  std::vector<std::vector<double>>& sp() { return sp_; }
  std::vector<std::vector<double>>& ap() { return ap_; }

 private:
  std::vector<double> samples_;
  int fs_;

  std::unique_ptr<F0Estimator> f0_estimator_;
  double frame_ms_;

  std::vector<double> f0_;
  std::vector<double> ts_;

  int fft_size_;
  std::vector<std::vector<double>> sp_;
  std::vector<std::vector<double>> ap_;
};

}  // namespace worldline

#endif  // WORLDLINE_MODEL_MODEL_H_
