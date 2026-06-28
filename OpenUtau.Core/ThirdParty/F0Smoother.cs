using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace ThirdParty {
    /// <summary>
    /// F0Smoother is a class that provides methods to smoothen and repair F0 (fundamental frequency) values in a list.
    /// <para>
    /// This is a C# implementation of SimpleEnunu's f0_smoother.py, modified for OpenUtau.
    /// </para>
    /// <para>
    /// Original Python implementation:
    /// https://github.com/oatsu-gh/SimpleEnunu/blob/main/extensions/f0_smoother.py
    /// </para>
    /// <para>
    /// Copyright (c) 2022 oatsu
    /// </para>
    /// </summary>
    public class F0Smoother {
        private const int SmoothenWidth = 6;
        private const double DetectThreshold = 0.6;
        private const double IgnoreThreshold = 0.01;
        public List<int> SmoothenWidthList { get; set; } = new List<int>();
        public List<double> DetectThresholdList { get; set; } = new List<double>();
        public List<double> IgnoreThresholdList { get; set; } = new List<double>();

        public F0Smoother(List<double> f0List) {
            SmoothenWidthList = Enumerable.Repeat(SmoothenWidth, f0List.Count).ToList();
            DetectThresholdList = Enumerable.Repeat(DetectThreshold, f0List.Count).ToList();
            IgnoreThresholdList = Enumerable.Repeat(IgnoreThreshold, f0List.Count).ToList();
        }

        public List<double> RepairSuddenZeroF0(List<double> f0List) {
            var newF0List = new List<double>(f0List);
            try {
                for (int i = 1; i < f0List.Count - 2; i++) {
                    if (f0List[i] == 0 && f0List[i - 1] != 0 && f0List[i + 1] != 0) {
                        newF0List[i] = (f0List[i - 1] + f0List[i + 1]) / 2.0;
                    }
                }
            } catch (Exception e) {
                Log.Error($"Error in RepairSuddenZeroF0.:{e.Message}");
                throw;
            }
            return newF0List;
        }

        public List<double> RepairJaggyF0(List<double> f0List, double ignoreThreshold) {
            var newF0List = new List<double>(f0List);
            var indices = new List<int>();
            try {
                for (int i = 2; i < f0List.Count - 2; i++) {
                    if (f0List[i - 1] == 0 || f0List[i] == 0 || f0List[i + 1] == 0 || f0List[i + 2] == 0)
                        continue;
                    double delta1 = f0List[i + 1] - f0List[i];
                    double delta3 = f0List[i + 2] - f0List[i - 1];
                    if (delta3 == 0) continue;
                    if (Math.Abs(delta1) < ignoreThreshold) continue;
                    if (delta1 * delta3 < 0)
                        indices.Add(i);
                }
                foreach (var idx in indices) {
                    newF0List[idx - 1] = 0.75 * newF0List[idx - 2] + 0.25 * newF0List[idx + 2];
                    newF0List[idx] = 0.5 * newF0List[idx - 2] + 0.5 * newF0List[idx + 2];
                    newF0List[idx + 1] = 0.25 * newF0List[idx - 2] + 0.75 * f0List[idx + 2];
                }
            } catch (Exception e) {
                Log.Error($"Error in RepairJaggyF0.:{e.Message}");
                throw;
            }
            return newF0List;
        }

        public List<double> GetSmoothenedF0List(List<double> f0List) {
            var f0Copy = new List<double>(f0List);
            var rapidIndices = GetRapidF0ChangeIndices(f0Copy);
            var adjustedWidths = GetAdjustedWidths(f0Copy, rapidIndices);
            var targetF0List = GetTargetF0List(f0Copy, rapidIndices, adjustedWidths);

            try {
                for (int k = 0; k < rapidIndices.Count; k++) {
                    int idx = rapidIndices[k];
                    int width = adjustedWidths[k];
                    double targetF0 = targetF0List[k];
                    if (width <= 0) continue;
                    for (int i = 0; i < width; i++) {
                        double ratioOriginal = Math.Cos(Math.PI * ((width - i) / (2.0 * width + 1)));
                        double ratioTarget = 1 - ratioOriginal;
                        int left = idx - i;
                        int right = idx + i + 1;
                        if (left >= 0 && left < f0Copy.Count)
                            f0Copy[left] = ratioTarget * targetF0 + ratioOriginal * f0Copy[left];
                        if (right >= 0 && right < f0Copy.Count)
                            f0Copy[right] = ratioTarget * targetF0 + ratioOriginal * f0Copy[right];
                    }
                }
            } catch (Exception e) {
                Log.Error($"Error in GetSmoothenedF0List.:{e.Message}");
                throw;
            }
            return f0Copy;
        }

        private List<int> GetRapidF0ChangeIndices(List<double> f0List) {
            var indices = new List<int>();
            try {
                for (int i = 1; i < f0List.Count - 2; i++) {
                    if (f0List[i - 1] == 0 || f0List[i] == 0 || f0List[i + 1] == 0 || f0List[i + 2] == 0)
                        continue;
                    double delta1 = f0List[i + 1] - f0List[i];
                    double delta3 = f0List[i + 2] - f0List[i - 1];
                    if (delta3 == 0) continue;
                    if (Math.Abs(delta1) < IgnoreThresholdList[i]) continue;
                    if (delta1 / delta3 > DetectThresholdList[i])
                        indices.Add(i);
                }
            } catch (Exception e) {
                Log.Error($"Error in GetRapidF0ChangeIndices.:{e.Message}");
                throw;
            }
            return indices;
        }

        private List<int> ReduceIndices(List<int> indices) {
            var result = new List<int>(indices);
            try {
                for (int i = 0; i < result.Count - 1; i++) {
                    int delta = result[i + 1] - result[i];
                    if (delta == 1) {
                        result[i] = -1;
                        result[i + 1] -= 1;
                    } else if (delta == 2) {
                        result[i] = -1;
                        result[i + 1] -= 1;
                    } else if (delta == 3) {
                        result[i] = -1;
                        result[i + 1] -= 2;
                    }
                }
            } catch (Exception e) {
                Log.Error($"Error in ReduceIndices.:{e.Message}");
                throw;
            }
            return result.Where(idx => idx >= 0).ToList();
        }

        private List<int> GetAdjustedWidths(List<double> f0List, List<int> rapidF0ChangeIndices) {
            var adjustedWidths = new List<int>();
            int len = f0List.Count;
            try {
                for (int i = 0; i < rapidF0ChangeIndices.Count; i++) {
                    var idx = rapidF0ChangeIndices[i];
                    int width = SmoothenWidthList[idx];
                    while ((idx - width) < 0 || (idx + width + 1) > len)
                        width--;
                    while (width > 0 && f0List.Skip(idx - width).Take(width * 2 + 2).Any(f0 => f0 == 0))
                        width--;
                    adjustedWidths.Add(width);
                }
            } catch (Exception e) {
                Log.Error($"Error in GetAdjustedWidths.:{e.Message}");
                throw;
            }
            return adjustedWidths;
        }

        private List<double> GetTargetF0List(List<double> f0List, List<int> rapidF0ChangeIndices, List<int> adjustedWidths) {
            var targetF0List = new List<double>();
            try {
                for (int i = 0; i < rapidF0ChangeIndices.Count; i++) {
                    int idx = rapidF0ChangeIndices[i];
                    int width = adjustedWidths[i];
                    int idxL = Math.Max(idx - width, 0);
                    int idxR = Math.Min(idx + width + 1, f0List.Count - 1);
                    double f0Left = f0List[idxL];
                    double f0Right = f0List[idxR];
                    double targetF0 = (f0Left + f0Right) / 2.0;
                    targetF0List.Add(targetF0);
                }
            } catch (Exception e) {
                Log.Error($"Error in GetTargetF0List.:{e.Message}");
                throw;
            }
            return targetF0List;
        }
    }
}
