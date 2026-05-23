using System;

namespace VocalShaper
{
    public class VSVocoder
    {
        //采样率
        readonly int samplesPerSec;
        //采样率的一半
        readonly int halfSameplesPerSec;
        //winLen下单位频率
        readonly double unitFrequency;
        //fft窗长
        readonly int winLen;
        //fft窗长的一半
        readonly int halfWinLen;
        //跳步长
        readonly int hopSize;
        //帧长
        readonly int frameSize;
        //ifft结果与需要的三角窗起始位置的差值
        readonly int ifftOffset;
        /// <summary>
        /// 合成数据前面多出来的长度
        /// </summary>
        /// <returns></returns>
        public int LerpLen()
        {
            return hopSize;
        }

        /// <summary>
        /// 声码器
        /// </summary>
        /// <param name="fs">合成目标采样率</param>
        /// <param name="frameMs">帧长度/毫秒</param>
        /// <param name="noiseTimeSec">预合成噪声长度/秒</param>
        public VSVocoder(int fs, double frameMs, double noiseTimeSec = 5)
        {
            samplesPerSec = fs;
            halfSameplesPerSec = samplesPerSec / 2;
            hopSize = (int)(frameMs / 1000 * samplesPerSec);
            frameSize = 2 * hopSize;
            halfWinLen = VSMath.Min2N(hopSize);
            winLen = 2 * halfWinLen;
            unitFrequency = samplesPerSec / (double)winLen;
            ifftOffset = halfWinLen - hopSize;

            //初始化sin
            sin = new double[samplesPerSec];
            for (int i = 0; i < samplesPerSec; i++)
            {
                sin[i] = 0.125 * Math.Sqrt(2) * Math.Sin(2 * Math.PI * i / samplesPerSec);
            }

            //初始化旋转因子
            W = new Complex[winLen];
            for (int _ = 0; _ < winLen; _++)
            {
                W[_] = new Complex(Math.Cos(2 * Math.PI / winLen), -Math.Sin(2 * Math.PI / winLen)) ^ _;
            }
            _W = new Complex[winLen];
            for (int _ = 0; _ < winLen; _++)
            {
                _W[_] = new Complex(Math.Cos(2 * Math.PI / winLen), -Math.Sin(2 * Math.PI / winLen)) ^ (-_);
            }

            //初始化窗函数
            BlackmanWindow = VSMath.BlackmanWin(winLen);
            Blackman2FrameTriangleWin = new double[frameSize];
            for (int i = 0; i < hopSize; i++)
            {
                Blackman2FrameTriangleWin[i] = (double)i / hopSize / BlackmanWindow[i + ifftOffset];
            }
            for (int i = hopSize; i < frameSize; i++)
            {
                Blackman2FrameTriangleWin[i] = (double)(frameSize - i) / hopSize / BlackmanWindow[i + ifftOffset];
            }

            //初始化噪声
            int noiseCount = (int)Math.Ceiling(noiseTimeSec * samplesPerSec / hopSize);
            int noiseLen = noiseCount * hopSize;
            var noiseData = VSMath.Noise(Math.Sqrt(3), noiseLen);
            Noises = new Complex[noiseCount][];
            for (int i = 0; i < noiseCount; i++)
            {
                double[] x = new double[winLen];
                int offset = i * hopSize;
                for (int j = 0; j < winLen; j++)
                {
                    x[j] = BlackmanWindow[j] * noiseData[(j + offset) % noiseLen];
                }
                Noises[i] = VSMath.FFT(x, W);
            }

        }

        /// <summary>
        /// 合成
        /// </summary>
        /// <param name="result">结果</param>
        /// <param name="HarmonicEnvelope">获取谐波包络</param>
        /// <param name="NoiseEnvelope">获取谐波包络</param>
        /// <param name="f0">音高</param>
        /// <param name="tension">张力</param>
        /// <param name="breathiness">气声</param>
        /// <param name="voicing">发声</param>
        /// <param name="gender">性别</param>
        /// <param name="phase">谐波相位</param>
        /// <param name="noiseIndex">噪声索引</param>
        /// <returns>谐波相位与噪声索引</returns>
        public (double, int) Synthesis(
            out double[] result,
            GetEnvelope HarmonicEnvelope,
            GetEnvelope NoiseEnvelope,
            double[] f0,
            double[] tension,
            double[] breathiness,
            double[] voicing,
            double[] gender,
            double phase = 0,
            int noiseIndex = 0)
        {
            //初始化
            int frameCount = f0.Length;
            int resultLen = frameCount * hopSize;
            result = new double[resultLen];

            double lastf;
            double nextf;
            //合成
            for (int i = 0; i < frameCount - 2; i++)
            {
                int offset = i * hopSize;

                double f = f0[i + 1];
                if (f != 0)//合成谐波
                {
                    //计算参数值
                    double[] ten = Tension(tension[i + 1], f);
                    double voc = Voicing(voicing[i + 1]);
                    double[] gen = Gender(gender[i + 1], f, ten.Length);

                    double[] g = new double[ten.Length];

                    for (int h = 0; h < ten.Length; h++) {
                        g[h] = HarmonicEnvelope(i + 1, gen[h]) * ten[h];
                    }

                    //合成前半段谐波
                    lastf = f0[i];
                    nextf = f0[i + 1];
                    if (lastf == 0) {
                        lastf = nextf;
                    }
                    for (int j = 0; j < hopSize; j++) {
                        f = VSMath.LineLerp(lastf, nextf, (double)j / hopSize);
                        double y = 0;
                        while (phase >= samplesPerSec) {
                            phase -= samplesPerSec;
                        }
                        for (int h = 0; h < ten.Length; h++) {
                            y += g[h] * Sin((h + 1) * phase);
                        }
                        phase += f;
                        result[offset + j] += y * voc * j / hopSize;
                    }

                    double nextPhase = phase;

                    //合成后半段谐波
                    lastf = f0[i + 1];
                    nextf = f0[i + 2];
                    if (nextf == 0) {
                        nextf = lastf;
                    }
                    for (int j = hopSize; j < frameSize; j++) {
                        f = VSMath.LineLerp(lastf, nextf, (double)j / hopSize - 1);
                        double y = 0;
                        while (phase >= samplesPerSec) {
                            phase -= samplesPerSec;
                        }
                        for (int h = 0; h < ten.Length; h++) {
                            y += g[h] * Sin((h + 1) * phase);
                        }
                        phase += f;
                        result[offset + j] += y * voc * (frameSize - j) / hopSize;
                    }

                    phase = nextPhase;
                }

                //合成噪声
                double bre = Breathiness(breathiness[i + 1]);
                double[] genN = Gender(gender[i + 1], unitFrequency, halfWinLen);
                Complex[] noise = new Complex[winLen];
                while (noiseIndex >= Noises.Length) noiseIndex -= Noises.Length;
                noise[0] = new Complex(0,0); //Noises[noiseIndex][0];
                for (int h = 1; h < halfWinLen; h++)
                {
                    double y = NoiseEnvelope(i + 1, genN[h - 1]);
                    int mirrorIndex = winLen - h;
                    noise[h] = Noises[noiseIndex][h] * y;
                    noise[mirrorIndex] = Noises[noiseIndex][mirrorIndex] * y;
                }
                noise[halfWinLen] = new Complex(0, 0); //Noises[noiseIndex][halfWinLen];
                var ifft = VSMath.IFFT(noise, _W);
                for (int j = 0; j < frameSize; j++)
                {
                    result[offset + j] += ifft[ifftOffset + j] * Blackman2FrameTriangleWin[j] * bre;
                }

                noiseIndex++;
            }


            return (phase, noiseIndex);
        }

        readonly double[] Blackman2FrameTriangleWin;
        readonly double[] BlackmanWindow;

        readonly Complex[] W;
        readonly Complex[] _W;

        readonly double[] sin;
        /// <summary>
        /// 快速sin
        /// </summary>
        /// <param name="x">相位与采样率的乘积</param>
        /// <returns></returns>
        double Sin(double x)
        {
            return sin[(int)(x % samplesPerSec)];
        }

        readonly Complex[][] Noises;

        #region 参数

        //const int MaxHarmonicCount = 128;
        const double tenBaseSensitivity = 6;
        const double tenPk = 0.1;
        const double tenPb = 1.7;
        //const double tenPkDrop = -0.1;
        const double tenN2Gain = -11;
        const double tenNStep = -1;
        /// <summary>
        /// 张力
        /// </summary>
        /// <param name="v"></param>
        /// <param name="f0"></param>
        /// <returns></returns>
        double[] Tension(double v, double f0)
        {
            int n = (int)(halfSameplesPerSec / f0);//int n = Math.Min((int)(samplesPerSec / 2 / f0), MaxHarmonicCount);
            double[] ten = new double[n];
            v = (v - 0.5) * 2;

            if (v == 0)
            {
                for (int i = 0; i < n; i++)
                {
                    ten[i] = 1;
                }
            }
            else if (v < 0)
            {
                ten[0] = Math.Pow(10, -v * tenBaseSensitivity / 20);
                for (int i = 1; i < n; i++)
                {
                    ten[i] = Math.Pow(10, -v * (tenN2Gain + tenNStep * (i - 1)) / 20);
                }
            }
            else
            {
                ten[0] = Math.Pow(10, -v * tenBaseSensitivity / 20);
                for (int i = 1; i < n; i++)
                {
                    ten[i] = Math.Pow(10, v * Math.Log10(Math.Max(0.5, tenPb - tenPk * Math.Abs(i - 10))));
                }
            }

            return ten;
        }

        const double breathinessSensitivity = 12;
        /// <summary>
        /// 气声
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        double Breathiness(double v)
        {
            return Math.Pow(10, (v - 0.5) * 2 * breathinessSensitivity / 20);
        }

        /// <summary>
        /// 发声
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        double Voicing(double v)
        {
            return v;
        }

        const double genR = 1.6f;
        const double genHigh = 8000;
        const double genLow = 5000;
        /// <summary>
        /// 性别
        /// </summary>
        /// <param name="v"></param>
        /// <param name="f0"></param>
        /// <returns></returns>
        double[] Gender(double v, double f0, int n)
        {
            double[] gen = new double[n];
            double fn = 0;
            int i = 0;
            v = (v - 0.5) * 2;

            if (v == 0)
            {
                for (; i < n; i++)
                {
                    fn += f0;
                    gen[i] = fn;
                }
            }
            else
            {
                for (; i < n; i++)
                {
                    fn += f0;
                    if (fn < genLow) gen[i] = fn * Math.Pow(genR, v);
                    else
                    {
                        fn -= f0;
                        break;
                    }
                }
                for (; i < n; i++)
                {
                    fn += f0;
                    if (fn > genHigh)
                    {
                        fn -= f0;
                        break;
                    }
                    gen[i] = genHigh - (genHigh - Math.Pow(genR, v) * genLow) / (genHigh - genLow) * (genHigh - fn);
                }
                for (; i < n; i++)
                {
                    fn += f0;
                    gen[i] = fn;
                }
            }
            return gen;
        }
        #endregion

        public delegate double GetEnvelope(int frameIndex, double f);

    }
}
