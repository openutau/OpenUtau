#include "harvest_estimator.h"

#include <cmath>
#include <vector>

#include "world/constantnumbers.h"
#include "world/harvest.h"
#include "worldline/f0/f0_estimator.h"

namespace worldline {

static double avg_f0(double* data, int count) {
  int non_zeros = 0;
  double sum = 0;
  for (int i = 0; i < count; ++i) {
    if (data[i] > world::kFloorF0StoneMask) {
      non_zeros++;
      sum += data[i];
    }
  }
  if (non_zeros == 0) {
    return 0;
  }
  return sum / non_zeros;
}

void HarvestEstimator::Estimate(const std::vector<double>& samples, int fs,
                                double frame_ms, std::vector<double>* f0,
                                std::vector<double>* time_axis) {
  int f0_len = GetSamplesForHarvest(fs, samples.size(), frame_ms);
  *f0 = std::vector<double>(f0_len);
  *time_axis = std::vector<double>(f0_len);

  HarvestOption option;
  InitializeHarvestOption(&option);
  option.frame_period = frame_ms;
  Harvest(samples.data(), samples.size(), fs, &option, time_axis->data(),
          f0->data());
}

}  // namespace worldline
