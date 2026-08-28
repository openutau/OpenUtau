using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace OpenUtau.Core.Util {
    public class ScalerLine {
        public float xmin;
        public float scale;

        public void inverse_transform(IList<float> xs) {
            //In-place transform a bunch of vectors
            for (int i = 0; i < xs.Count; i++) {
                xs[i] = xs[i] / this.scale + this.xmin;
            }
        }
    }

    public class Scaler : List<ScalerLine> {

        public static Scaler load(string path,Encoding encoding = null) {
            //Encoding is UTF-8 by default
            if (encoding == null) {
                encoding = Encoding.UTF8;
            }
            return JsonConvert.DeserializeObject<Scaler>(
                File.ReadAllText(path, encoding));
        }

        public void transform(IList<float> x) {
            //In-place transform a vector
            for(int i = 0; i < this.Count; i++) {
                x[i] = (x[i] - this[i].xmin) * this[i].scale;
            }
        }

        
        public void transform(IEnumerable<IList<float>> xs) {
            //In-place transform a bunch of vectors
            foreach(IList<float> x in xs) {
                transform(x);
            }
        }

        public float[] transformed(IEnumerable<float> x) {
            //transform a vector into a new vector
            return Enumerable.Zip(this, x, (scalerLine, xLine) => (xLine - scalerLine.xmin) * scalerLine.scale).ToArray();
        }

        public float[][] transformed(IEnumerable<IEnumerable<float>> xs) {
            //transform a bunch of vectors into new ones
            return xs.Select(transformed).ToArray();
        }


        public void inverse_transform(IList<float> x) {
            for (int i = 0; i < this.Count; i++) {
                x[i] = x[i] / this[i].scale + this[i].xmin;
            }
        }

        public void inverse_transform(IEnumerable<IList<float>> xs) {
            //In-place transform a bunch of vectors
            foreach (IList<float> x in xs) {
                inverse_transform(x);
            }
        }
    }
}
