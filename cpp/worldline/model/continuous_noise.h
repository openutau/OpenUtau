#ifndef WORLDLINE_MODEL_CONTINUOUS_NOISE_H_
#define WORLDLINE_MODEL_CONTINUOUS_NOISE_H_

#include <cstdint>
#include <vector>

namespace worldline {

// Continuous noise for Worldline-R1.1: white noise shaped frame by frame by
// STFT overlap-add, in place of WORLD's per-pulse noise segments.
//
// Spectra here are windowed periodograms on one grid: frame i centered at
// i * hop, symmetric Hann of 2 * hop samples, zero-padded to fft_size, stored
// row-major as num_frames x (fft_size / 2 + 1).

// Sum of the squared analysis window. WORLD's noise with spectrum S has
// per-sample variance S, so its periodogram is S * WindowPower(hop).
double WindowPower(int hop);

std::vector<double> StftPower(const double* y, int length, int hop,
                              int num_frames, int fft_size);

// In-place moving average over width bins in each row, edges replicated.
void SmoothFreq(std::vector<double>& power, int bins, int width);

// Noise of length samples whose periodogram at frame i matches target row i.
// Each frame's filter divides out the excitation's own spectrum, as
// measured^alpha * expected^(1 - alpha), with the measured periodogram
// smoothed over 3 bins: alpha = 1 flattens the noise's natural frame-to-frame
// fluctuation onto the target, alpha = 0 keeps it (as WORLD's noise does).
// The same seed gives the same noise.
std::vector<double> ContinuousNoise(const double* target, int num_frames,
                                    int hop, int length, const double* alpha,
                                    uint64_t seed, int fft_size);

}  // namespace worldline

#endif  // WORLDLINE_MODEL_CONTINUOUS_NOISE_H_
