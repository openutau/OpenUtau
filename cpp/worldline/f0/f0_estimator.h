#ifndef WORLDLINE_F0_F0_ESTIMATOR_H_
#define WORLDLINE_F0_F0_ESTIMATOR_H_

#include <vector>

namespace worldline {

class F0Estimator {
 public:
  virtual void Estimate(const std::vector<double>& samples, int fs,
                        double frame_ms, std::vector<double>* f0,
                        std::vector<double>* time_axis) = 0;
virtual ~F0Estimator() {}
};

}  // namespace worldline

#endif  // WORLDLINE_F0_F0_ESTIMATOR_H_
