using System;

namespace VocalShaper.World
{
    public static class World
    {
        /// <summary>
        /// 合成
        /// </summary>
        /// <param name="vsVocoder">声码器</param>
        /// <param name="result">结果</param>
        /// <param name="sp">源sp</param>
        /// <param name="ap">源ap</param>
        /// <param name="fs">源采样率</param>
        /// <param name="fftSize">源fft大小</param>
        /// <param name="f0">音高</param>
        /// <param name="tension">张力</param>
        /// <param name="breathiness">气声</param>
        /// <param name="voicing">发声</param>
        /// <param name="gender">性别</param>
        /// <param name="phase">谐波相位</param>
        /// <param name="noiseIndex">噪声索引</param>
        /// <returns>谐波相位与噪声索引</returns>
        public static (double, int) Synthesis(this VSVocoder vsVocoder,
            out double[] result,
            double[,] sp,
            double[,] ap,
            int fs,
            int fftSize,
            double[] f0,
            double[] tension,
            double[] breathiness,
            double[] voicing,
            double[] gender,
            double phase = 0,
            int noiseIndex = 0)
        {
            double f2i = (double)fftSize / fs;
            return vsVocoder.Synthesis(out result,
                (i, f) =>
                {
                    double fi = f * f2i;
                    int floorIndex = (int)fi;
                    int ceillingIndex = floorIndex + 1;
                    return Math.Sqrt(VSMath.LineLerp(sp[i, floorIndex], sp[i, ceillingIndex], fi - floorIndex));
                },
                (i, f) =>
                {
                    double fi = f * f2i;
                    int floorIndex = (int)fi;
                    int ceillingIndex = floorIndex + 1;
                    double ratio = fi - floorIndex;
                    return Math.Sqrt(VSMath.LineLerp(sp[i, floorIndex], sp[i, ceillingIndex], ratio)) * VSMath.LineLerp(ap[i, floorIndex], ap[i, ceillingIndex], ratio);
                },
                f0,
                tension,
                breathiness,
                voicing,
                gender,
                phase,
                noiseIndex);
        }
    }
}
