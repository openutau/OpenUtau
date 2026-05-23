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

        public static List<int> GetSnapDivs(int resolution) {
            var result = new List<int>();
            int div = 4;
            int ticks = resolution * 4 / div;
            result.Add(div);
            while (ticks % 2 == 0) {
                ticks /= 2;
                div *= 2;
                result.Add(div);
            }
            div = 6;
            ticks = resolution * 4 / div;
            result.Add(div);
            while (ticks % 2 == 0) {
                ticks /= 2;
                div *= 2;
                result.Add(div);
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
