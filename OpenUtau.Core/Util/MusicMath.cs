using System;
using System.Collections.Generic;

namespace OpenUtau.Core {
    public static class MusicMath {
        private static readonly double a = Math.Pow(2, 1.0 / 12);

        public enum KeyColor { White, Black }

        public static readonly Tuple<string, KeyColor>[] KeysInOctave = {
            Tuple.Create("C", KeyColor.White),
            Tuple.Create("C#", KeyColor.Black),
            Tuple.Create("D", KeyColor.White),
            Tuple.Create("D#", KeyColor.Black),
            Tuple.Create("E", KeyColor.White),
            Tuple.Create("F", KeyColor.White),
            Tuple.Create("F#", KeyColor.Black),
            Tuple.Create("G", KeyColor.White),
            Tuple.Create("G#", KeyColor.Black),
            Tuple.Create("A", KeyColor.White),
            Tuple.Create("A#", KeyColor.Black),
            Tuple.Create("B" , KeyColor.White),
        };

        public static readonly Dictionary<string, int> NameInOctave = new Dictionary<string, int> {
            { "C", 0 }, { "C#", 1 }, { "Db", 1 },
            { "D", 2 }, { "D#", 3 }, { "Eb", 3 },
            { "E", 4 },
            { "F", 5 }, { "F#", 6 }, { "Gb", 6 },
            { "G", 7 }, { "G#", 8 }, { "Ab", 8 },
            { "A", 9 }, { "A#", 10 }, { "Bb", 10 },
            { "B", 11 },
        };

        public static readonly string[] Solfeges = { 
            "do",
            "",
            "re",
            "",
            "mi",
            "fa",
            "",
            "sol",
            "",
            "la",
            "",
            "ti",
        };

        public static readonly string[] NumberedNotations = {
            "1",
            "",
            "2",
            "",
            "3",
            "4",
            "",
            "5",
            "",
            "6",
            "",
            "7",
        };

        public static string GetToneName(int noteNum) {
            return noteNum < 0 ? string.Empty : KeysInOctave[noteNum % 12].Item1 + (noteNum / 12 - 1).ToString();
        }

        public static int NameToTone(string name) {
            if (name.Length < 2) {
                return -1;
            }
            var str = name.Substring(0, (name[1] == '#' || name[1] == 'b') ? 2 : 1);
            var num = name.Substring(str.Length);
            if (!int.TryParse(num, out int octave)) {
                return -1;
            }
            if (!NameInOctave.TryGetValue(str, out int inOctave)) {
                return -1;
            }
            return 12 * (octave + 1) + inOctave;
        }

        public static bool IsBlackKey(int noteNum) {
            return KeysInOctave[noteNum % 12].Item2 == KeyColor.Black;
        }

        public static bool IsCenterKey(int noteNum) {
            return noteNum % 12 == 0;
        }

        public static double[] zoomRatios = { 4.0, 2.0, 1.0, 1.0 / 2, 1.0 / 4, 1.0 / 8, 1.0 / 16, 1.0 / 32, 1.0 / 64 };

        public static double getZoomRatio(double quarterWidth, int beatPerBar, int beatUnit, double minWidth) {
            int i;

            switch (beatUnit) {
                case 2: i = 0; break;
                case 4: i = 1; break;
                case 8: i = 2; break;
                case 16: i = 3; break;
                default: throw new Exception("Invalid beat unit.");
            }

            if (beatPerBar % 4 == 0) {
                i--; // level below bar is half bar, or 2 beatunit
            }
            // else // otherwise level below bar is beat unit

            if (quarterWidth * beatPerBar * 4 <= minWidth * beatUnit) {
                return beatPerBar / beatUnit * 4;
            } else {
                while (i + 1 < zoomRatios.Length && quarterWidth * zoomRatios[i + 1] > minWidth) {
                    i++;
                }

                return zoomRatios[i];
            }
        }

        const double ep = 0.001;

        public static double SinEasingInOut(double x0, double x1, double y0, double y1, double x) {
            if(x1 - x0 < ep){
                return y1;
            }
            return y0 + (y1 - y0) * (1 - Math.Cos((x - x0) / (x1 - x0) * Math.PI)) / 2;
        }

        public static double SinEasingInOutX(double x0, double x1, double y0, double y1, double y) {
            return Math.Acos(1 - (y - y0) * 2 / (y1 - y0)) / Math.PI * (x1 - x0) + x0;
        }

        public static double SinEasingIn(double x0, double x1, double y0, double y1, double x) {
            if(x1 - x0 < ep){
                return y1;
            }
            return y0 + (y1 - y0) * (1 - Math.Cos((x - x0) / (x1 - x0) * Math.PI / 2));
        }

        public static double SinEasingInX(double x0, double x1, double y0, double y1, double y) {
            return Math.Acos(1 - (y - y0) / (y1 - y0)) / Math.PI * 2 * (x1 - x0) + x0;
        }

        public static double SinEasingOut(double x0, double x1, double y0, double y1, double x) {
            if(x1 - x0 < ep){
                return y1;
            }
            return y0 + (y1 - y0) * Math.Sin((x - x0) / (x1 - x0) * Math.PI / 2);
        }

        public static double SinEasingOutX(double x0, double x1, double y0, double y1, double y) {
            return Math.Asin((y - y0) / (y1 - y0)) / Math.PI * 2 * (x1 - x0) + x0;
        }

        public static double Linear(double x0, double x1, double y0, double y1, double x) {
            if(x1 - x0 < ep){
                return y1;
            }
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }

        public static double LinearX(double x0, double x1, double y0, double y1, double y) {
            return (y - y0) / (y1 - y0) * (x1 - x0) + x0;
        }

        public static double InterpolateShape(double x0, double x1, double y0, double y1, double x, Ustx.PitchPointShape shape) {
            switch (shape) {
                case Ustx.PitchPointShape.io: return SinEasingInOut(x0, x1, y0, y1, x);
                case Ustx.PitchPointShape.sp: return SinEasingInOut(x0, x1, y0, y1, x);
                case Ustx.PitchPointShape.i: return SinEasingIn(x0, x1, y0, y1, x);
                case Ustx.PitchPointShape.o: return SinEasingOut(x0, x1, y0, y1, x);
                default: return Linear(x0, x1, y0, y1, x);
            }
        }

        public static double InterpolateShapeX(double x0, double x1, double y0, double y1, double y, Ustx.PitchPointShape shape) {
            switch (shape) {
                case Ustx.PitchPointShape.io: return SinEasingInOutX(x0, x1, y0, y1, y);
                case Ustx.PitchPointShape.sp: return SinEasingInOutX(x0, x1, y0, y1, y);
                case Ustx.PitchPointShape.i: return SinEasingInX(x0, x1, y0, y1, y);
                case Ustx.PitchPointShape.o: return SinEasingOutX(x0, x1, y0, y1, y);
                default: return LinearX(x0, x1, y0, y1, y);
            }
        }

        public static double DecibelToLinear(double db) {
            return Math.Pow(10, db / 20);
        }

        public static double LinearToDecibel(double v) {
            return Math.Log10(v) * 20;
        }

        public static double ToneToFreq(int tone) {
            return 440.0 * Math.Pow(a, tone - 69);
        }

        public static double ToneToFreq(double tone) {
            return 440.0 * Math.Pow(a, tone - 69);
        }

        public static double FreqToTone(double freq) {
            return Math.Log(freq / 440.0, a) + 69;
        }

        public static Dictionary<string, int> GetSnapDivs() {
            var result = new Dictionary<string, int>();
            // Straight
            int div = 4;
            while (div <= 128) {
                result.Add(div.ToString(), div);
                div *= 2;
            }
            // Triplet
            div = 4;
            int triDiv = 6;
            while (div <= 128) {
                result.Add($"{div} T", triDiv);
                div *= 2;
                triDiv *= 2;
            }
            // Swing
            div = 4;
            while (div <= 16) {
                result.Add($"{div} Sw", div + 1);
                div *= 2;
            }
            return result;
        }

        public static void GetSnapUnit(
            int resolution, double minTicks, bool triplet,
            out int ticks, out int div) {
            div = triplet ? 6 : 4;
            ticks = resolution * 4 / div;
            while (ticks % 2 == 0 && ticks / 2 >= minTicks) {
                ticks /= 2;
                div *= 2;
            }
        }

        public static int GetSnappedTick(int resolution, int tickInPart, int partPosition, int snapDiv, int swing, int roundMode = 0) {
            double snapUnit = resolution * 4 / snapDiv;
            if (swing == 0) {
                if (roundMode == 1) {
                    return (int)(Math.Round(tickInPart / snapUnit) * snapUnit);
                } else if (roundMode == 2) {
                    return (int)(Math.Ceiling(tickInPart / snapUnit) * snapUnit);
                } else {
                    return (int)(Math.Floor(tickInPart / snapUnit) * snapUnit);
                }
            }

            double absoluteTick = (double)tickInPart + partPosition;
            double maxSwingOffset = snapUnit / 3.0;
            double normalizedSwing = Math.Max(0, Math.Min(100, swing)) / 100.0;
            double swingOffset = maxSwingOffset * normalizedSwing;

            Func<long, double> getActualGridTick = (i) => {
                double pos = i * snapUnit;
                if (i % 2 != 0) { // On the offbeat
                    pos += swingOffset;
                }
                return pos;
            };

            long baseIndex = (long)Math.Floor(absoluteTick / snapUnit);
            long bestIndex = baseIndex;

            if (roundMode == 1) {
                // Round
                double minDiff = double.MaxValue;
                for (long i = baseIndex - 2; i <= baseIndex + 2; i++) {
                    double actualTick = getActualGridTick(i);
                    double diff = Math.Abs(absoluteTick - actualTick);
                    if (diff < minDiff) {
                        minDiff = diff;
                        bestIndex = i;
                    }
                }
            } else if (roundMode == 2) {
                // Ceiling
                bestIndex = long.MinValue;
                for (long i = baseIndex; i <= baseIndex + 2; i++) {
                    if (getActualGridTick(i) >= absoluteTick) {
                        bestIndex = i;
                        break;
                    }
                }
                if (bestIndex == long.MinValue) bestIndex = baseIndex + 1;
            } else {
                // Floor
                bestIndex = long.MinValue;
                for (long i = baseIndex + 1; i >= baseIndex - 1; i--) {
                    if (getActualGridTick(i) <= absoluteTick) {
                        bestIndex = i;
                        break;
                    }
                }
                if (bestIndex == long.MinValue) bestIndex = baseIndex - 1;
            }

            double snappedAbsoluteTick = getActualGridTick(bestIndex);
            int snappedAbsoluteInt = (int)Math.Round(snappedAbsoluteTick);
            return snappedAbsoluteInt - partPosition;
        }

        public static int GetEffectiveSnapUnit(int resolution, int startTick, int partPosition, int snapDiv, int swing) {
            if (snapDiv <= 0 || resolution <= 0)
                return resolution * 4 / snapDiv;

            double baseSnapUnit = (double)(resolution * 4) / snapDiv;
            if (swing == 0) return (int)baseSnapUnit;

            double absoluteTick = (double)startTick + partPosition;
            double maxSwingOffset = baseSnapUnit / 3.0;
            double normalizedSwing = Math.Max(0, Math.Min(100, swing)) / 100.0;
            double swingOffset = maxSwingOffset * normalizedSwing;

            Func<long, double> getActualGridTick = (i) => {
                double pos = i * baseSnapUnit;
                if (i % 2 != 0) { // On the offbeat
                    pos += swingOffset;
                }
                return pos;
            };

            long baseIndex = (long)Math.Floor(absoluteTick / baseSnapUnit);
            long bestIndex = baseIndex;
            double minDiff = double.MaxValue;

            for (long i = baseIndex - 2; i <= baseIndex + 2; i++) {
                double actualTick = getActualGridTick(i);
                double diff = Math.Abs(absoluteTick - actualTick);
                if (diff < minDiff) {
                    minDiff = diff;
                    bestIndex = i;
                }
            }

            if (bestIndex % 2 != 0) {
                return (int)Math.Round(baseSnapUnit - swingOffset);
            } else {
                return (int)Math.Round(baseSnapUnit + swingOffset);
            }
        }

        public static double TempoMsToTick(double tempo, double ms) {
            return (tempo * 480 * ms) / (60.0 * 1000.0);
        }

        public static double TempoTickToMs(double tempo, int tick) {
            return (60.0 * 1000.0 * tick) / (tempo * 480);
        }

        public static (float, float) PanToChannelVolumes(float pan) {
            float angle = (Math.Clamp(pan, -100f, 100f) + 100f) / 200f * (float)(Math.PI / 2);
            return ((float)Math.Cos(angle), (float)Math.Sin(angle));
        }
    }
}
