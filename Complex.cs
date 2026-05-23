using System;

namespace VocalShaper
{
    internal struct Complex
    {
        double a;
        double b;
        public double Real
        {
            get { return a; }
            set { a = value; }
        }
        public double Imaginary
        {
            get { return b; }
            set { b = value; }
        }
        public double Norm => Math.Sqrt(a * a + b * b);
        public Complex(double a, double b)
        {
            this.a = a;
            this.b = b;
        }
        public Complex Conjugate()
        {
            return new Complex(a, -b);
        }
        public static Complex operator +(Complex c1, Complex c2)
        {
            return new Complex(c1.a + c2.a, c1.b + c2.b);
        }
        public static Complex operator -(Complex c1, Complex c2)
        {
            return new Complex(c1.a - c2.a, c1.b - c2.b);
        }
        public static Complex operator *(Complex c1, Complex c2)
        {
            return new Complex(c1.a * c2.a - c1.b * c2.b, c1.b * c2.a + c1.a * c2.b);
        }
        public static Complex operator *(float d, Complex c2)
        {
            return new Complex(d * c2.a, d * c2.b);
        }
        public static Complex operator *(Complex c2, double d)
        {
            return new Complex(d * c2.a, d * c2.b);
        }
        public static Complex operator /(Complex c1, Complex c2)
        {
            return c1 * c2.Conjugate();
        }
        public static Complex operator ^(Complex c, int n)
        {
            if (n < -1)
            {
                return c.Conjugate() ^ (-n);
            }
            else if (n == -1)
            {
                return c.Conjugate();
            }
            else if (n == 0)
            {
                return new Complex(1, 0);
            }
            else if (n == 1)
            {
                return c;
            }
            else
            {
                Complex tmp = c;
                for (int i = 2; i <= n; i++)
                {
                    tmp *= c;
                }
                return tmp;
            }
        }
    }
}
