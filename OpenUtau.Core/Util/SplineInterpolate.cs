using System;

namespace OpenUtau.Core {
    public class CubicSplineSegment {
        private readonly double x0, y0, x1, y1;
        private readonly double a, b, c, d;

        public CubicSplineSegment(double x_1, double y_1, double x0, double y0, double x1, double y1, double x2, double y2) {
            this.x0 = x0;
            this.y0 = y0;
            this.x1 = x1;
            this.y1 = y1;

            // Catmull-Rom
            double m0 = (y1 - y_1) * (x1 - x0) / (x1 - x_1);
            double m1 = (y2 - y0) * (x1 - x0) / (x2 - x0);

            this.a = 2 * y0 - 2 * y1 + m0 + m1;
            this.b = -3 * y0 + 3 * y1 - 2 * m0 - m1;
            this.c = m0;
            this.d = y0;
        }

        public double GetY(double x) {
            if (x <= x0) return y0;
            if (x >= x1) return y1;

            double t = (x - x0) / (x1 - x0);
            double t2 = t * t;

            // at^3 + bt^2 + ct + d
            return ((a * t + b) * t + c) * t + d;
        }

        public double GetX(double y) {
            if (y <= Math.Min(y0, y1)) return y == y0 ? x0 : x1;
            if (y >= Math.Max(y0, y1)) return y == y1 ? x1 : x0;

            double t = (y - y0) / (y1 - y0);

            for (int i = 0; i < 5; i++)
            {
                // f(t) = at^3 + bt^2 + ct + d - y
                double ft = ((a * t + b) * t + c) * t + (d - y);
                // f'(t) = 3at^2 + 2bt + c
                double dft = (3 * a * t + 2 * b) * t + c;

                if (Math.Abs(dft) < 1e-6) break; // End when the tilt is close to zero

                t -= ft / dft;
                t = Math.Clamp(t, 0, 1);
            }

            return x0 + t * (x1 - x0);
        }
    }
}
