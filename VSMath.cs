using System;
using System.Collections.Generic;

namespace VocalShaper
{
    internal static class VSMath
    {

        /// <summary>
        /// 获取不大于n的最大2的N次方的整数
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        public static int Max2N(int n)
        {
            int tmp = -1;
            do
            {
                tmp++;
            }
            while (Math.Pow(2, tmp) <= n);
            return (int)Math.Pow(2, tmp - 1);
        }

        /// <summary>
        /// 获取不小于n的最小2的N次方的整数
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        public static int Min2N(int n)
        {
            int tmp = 0;
            while (Math.Pow(2, tmp) < n)
            {
                tmp++;
            }
            return (int)Math.Pow(2, tmp);
        }

        /// <summary>
        /// 线性插值
        /// </summary>
        /// <param name="y1"></param>
        /// <param name="y2"></param>
        /// <param name="ratio"></param>
        /// <returns></returns>
        public static double LineLerp(double y1, double y2, double ratio)
        {
            return y1 + (y2 - y1) * ratio;
        }

        /// <summary>
        /// 布莱克曼窗
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        internal static double[] BlackmanWin(int length)
        {
            double[] tmp = new double[length];
            for (int i = 0; i < length; i++)
            {
                tmp[i] = 0.42f - 0.5f * Math.Cos(2 * Math.PI * i / (length - 1)) + 0.08f * Math.Cos(4 * Math.PI * i / (length - 1));
            }
            return tmp;
        }

        /// <summary>
        /// 余弦窗
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        internal static double[] CosWin(int length)
        {
            double[] tmp = new double[length];
            for (int i = 0; i < length; i++)
            {
                tmp[i] = 0.5f - 0.5f * Math.Cos(2 * Math.PI * i / (length - 1));
            }
            return tmp;
        }

        internal static Complex[] FFT(IList<double> data, Complex[] W)
        {
            int n = data.Count;
            Complex[] output = new Complex[n];
            double[] rlist = new double[n];
            int total_m = 0;
            int Rpos(int num)
            {
                int outNum = 0;
                int bits = 0;
                int _i = n;
                int dataNum = num;
                while (_i != 0)
                {
                    _i /= 2;
                    bits++;
                }
                for (int i = 0; i < bits - 1; i++)
                {
                    outNum = outNum << 1;
                    outNum = outNum | ((dataNum >> i) & 1);
                }
                total_m = bits - 1;
                return outNum;
            }
            for (int _ = 0; _ < n; _++)
            {
                rlist[_] = data[Rpos(_)];
                output[_] = new Complex(rlist[_], 0);
            }
            int split;
            int numeach;
            Complex temp, temp2;
            for (int m = 0; m < total_m; m++)
            {
                split = n / (int)Math.Pow(2, m + 1);
                numeach = n / split;
                for (int _ = 0; _ < split; _++)
                {
                    for (int __ = 0; __ < numeach / 2; __++)
                    {
                        temp = output[_ * numeach + __];
                        temp2 = output[_ * numeach + __ + numeach / 2] * W[__ * (int)Math.Pow(2, total_m - m - 1)];
                        output[_ * numeach + __] = temp + temp2;
                        output[_ * numeach + __ + numeach / 2] = temp - temp2;
                    }
                }
            }
            return output;
        }

        internal static double[] IFFT(Complex[] data, Complex[] _W)
        {
            int n = data.Length;
            Complex[] output = new Complex[n];
            Complex[] rlist = new Complex[n];
            int total_m = 0;
            int Rpos(int num)
            {
                int outNum = 0;
                int bits = 0;
                int _i = n;
                int dataNum = num;
                while (_i != 0)
                {
                    _i = _i / 2;
                    bits++;
                }
                for (int i = 0; i < bits - 1; i++)
                {
                    outNum = outNum << 1;
                    outNum = outNum | ((dataNum >> i) & 1);
                }
                total_m = bits - 1;
                return outNum;
            }
            for (int _ = 0; _ < n; _++)
            {
                rlist[_] = data[Rpos(_)];
                output[_] = rlist[_];
            }
            int split;
            int numeach;
            Complex temp, temp2;
            for (int m = 0; m < total_m; m++)
            {
                split = n / (int)Math.Pow(2, m + 1);
                numeach = n / split;
                for (int _ = 0; _ < split; _++)
                {
                    for (int __ = 0; __ < numeach / 2; __++)
                    {
                        temp = output[_ * numeach + __];
                        temp2 = output[_ * numeach + __ + numeach / 2] * _W[__ * (int)Math.Pow(2, total_m - m - 1)];
                        output[_ * numeach + __] = temp + temp2;
                        output[_ * numeach + __ + numeach / 2] = temp - temp2;
                    }
                }
            }
            double[] outputN = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (output[i].Real > -output[i].Imaginary)
                {
                    outputN[i] = output[i].Norm / n;
                }
                else
                {
                    outputN[i] = -output[i].Norm / n;
                }
            }
            return outputN;
        }

        /// <summary>
        /// 生成随机白噪声
        /// </summary>
        /// <param name="g"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        internal static double[] Noise(double g, int length)
        {
            double[] noise = new double[length];
            Random random = new Random();
            for (int i = 0; i < length; i++)
            {
                noise[i]=(float)(random.NextDouble() - 0.5) * 2 * g;
            }
            return noise;
        }
    }
}
