using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using OpenUtau.Core.Util.nnmnkwii.python;
//reference: https://github.com/r9y9/nnmnkwii/blob/master/nnmnkwii/io/hts.py

namespace OpenUtau.Core.Util.nnmnkwii.io.hts {
    public class HTSLabel {
        public int start_time = 0;
        public int end_time = 0;
        public string context = "";

        public HTSLabel(int start_time, int end_time, string context) {
            this.start_time = start_time;
            this.end_time = end_time;
            this.context = context;
        }
    }

    public class HTSLabelFile : IEnumerable<HTSLabel> // IList<HTSLabel>
    {
        //storaged data
        public List<int> start_times;
        public List<int> end_times;
        public List<string> contexts;
        int frame_shift;

        //constructor
        public HTSLabelFile(int frame_shift = 50000) {
            this.start_times = new List<int>();
            this.end_times = new List<int>();
            this.contexts = new List<string>();
            this.frame_shift = frame_shift;
        }

        ////IList properties

        //__len__
        public int Count {
            get {
                return start_times.Count;
            }
        }

        public bool isfixedsize = false;
        public bool IsReadOnly {
            get {
                return false;
            }
        }

        //TODO
        //public bool IsSynchronized => false;

        //__getitem__ L106 TODO:slicing
        public HTSLabel this[int index] {
            get {
                return new HTSLabel(
                    this.start_times[index],
                    this.end_times[index],
                    this.contexts[index]
                );
            }
            set {
                this.start_times[index] = value.start_time;
                this.end_times[index] = value.end_time;
                this.contexts[index] = value.context;
            }
        }

        //IList methods
        public void append(HTSLabel label, bool strict = true) {
            int start_time = label.start_time;
            int end_time = label.end_time;
            string context = label.context;
            if (strict) {
                //TODO:ValueError
                if (start_time >= end_time) {
                    throw new Exception($"end_time ({end_time}) must be larger than start_time ({start_time}).");
                }
                if (end_times.Count > 0 && start_time != end_times[-1]) {
                    throw new Exception($"start_time ({start_time}) must be equal to the last end_time ({end_times.Last()}).");
                }
            }
            start_times.Add(start_time);
            end_times.Add(end_time);
            contexts.Add(context);
        }
        public void Add(HTSLabel label) => append(label);

        public void Clear() {
            start_times.Clear();
            end_times.Clear();
            contexts.Clear();
        }

        public bool Contains(HTSLabel label) {
            return !(Enumerable.Range(0, Count)
                .Where(index => start_times[index] == label.start_time)
                .Where(index => end_times[index] == label.end_time)
                .Where(index => contexts[index] == label.context)
                .Any());
        }

        public void CopyTo(HTSLabel[] array, int index) {
            if (array == null) {
                throw new ArgumentNullException("array is null");
            }

            if (index < 0) {
                throw new ArgumentOutOfRangeException("negative index");
            }

            if (array.Length - index < Count) {
                throw new ArgumentException();
            }

            foreach (int i in Enumerable.Range(0, Count)) {
                array[index] = this[i];
            }
        }

        // Must implement GetEnumerator, which returns a new StreamReaderEnumerator.
        public IEnumerator<HTSLabel> GetEnumerator() {
            return new HTSLabelEnum(this);
        }

        // Must also implement IEnumerable.GetEnumerator, but implement as a private method.
        private IEnumerator GetEnumerator1() {
            return this.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator1();
        }

        public int IndexOf(HTSLabel label) {
            return Enumerable.Range(0, Count)
                .Where(index => start_times[index] == label.start_time)
                .Where(index => end_times[index] == label.end_time)
                .Where(index => contexts[index] == label.context)
                .DefaultIfEmpty(-1)
                .First();
        }

        public void Insert(int index, HTSLabel label) {
            start_times.Insert(index, label.start_time);
            end_times.Insert(index, label.end_time);
            contexts.Insert(index, label.context);
        }

        public void Remove(HTSLabel label) {
            int index = IndexOf(label);
            if (index >= 0) {
                RemoveAt(index);
            }
        }

        public void RemoveAt(int index) {
            start_times.RemoveAt(index);
            end_times.RemoveAt(index);
            contexts.RemoveAt(index);
        }

        //Other attributes and methods

        //__str__
        public override string ToString() {
            String[] lines = Enumerable.Range(0, Count)
                .Select(index => String.Format("{} {} {}",
                    this.start_times[index],
                    this.end_times[index],
                    this.contexts[index]))
                .ToArray();
            return String.Join("\n", lines);
        }

        //按时间单位取整
        public void round_() {
            start_times = start_times
                .Select(x => (int)Math.Round((float)x / frame_shift))
                .ToList();
            end_times = end_times
                .Select(x => (int)Math.Round((float)x / frame_shift))
                .ToList();
        }

        //TODO:set_durations



        public int[] silence_label_indices(Regex regex = null) {
            /*
            Returns silence label indices

            Args:
                regex (re(optional)): Compiled regex to find silence labels.

            Returns:
                1darray: Silence label indices
             */
            if (regex == null) {
                regex = new Regex(".*-sil+.*");
            }
            //if serveral matches exist in the same string, index will repeat several times
            return Enumerable.Range(0, contexts.Count)
                .SelectMany(index => Enumerable.Repeat(index, regex.Matches(contexts[index]).Count))
                .ToArray();
        }

        public int[] silence_phone_indices(Regex regex = null) {
            /*
            Returns phone-level frame indices

            Args:
                regex (re(optional)): Compiled regex to find silence labels.

            Returns:
                1darray: Silence label indices
             */
            if (regex == null) {
                regex = new Regex(".*-sil+.*");
            }
            return Enumerable.Range(0, contexts.Count)
                .Where(index => regex.Match(contexts[index]).Success).ToArray();
        }

        public int[] silence_frame_indices(Regex regex = null, int frame_shift = 50000) {
            /*
            Returns silence frame indices

            Similar to :func:`silence_label_indices`, but returns indices in frame-level.

            Args:
                regex (re(optional)): Compiled regex to find silence labels.

            Returns:
                1darray: Silence frame indices
             */
            if (regex == null) {
                regex = new Regex(".*-sil+.*");
            }
            var indices = silence_label_indices(regex);
            if (indices.Length == 0) {
                return new int[] { };
            }
            return indices
                .SelectMany(index => Enumerable.Range(
                    start_times[index] / frame_shift,
                    end_times[index] / frame_shift - start_times[index] / frame_shift)).ToArray();
        }

        public bool is_state_alignment_label() {
            return contexts[0][^1] == ']' && contexts[0][^3] == '[';
        }

        public int num_states() {
            /*
             Returnes number of states exclusing special begin/end states.
             */
            if (!is_state_alignment_label()) {
                return 1;
            }
            int initial_state_num = int.Parse(contexts[0][^2].ToString());
            int largest_state_num = initial_state_num;
            foreach (var label in contexts.Skip(1)) {
                int n = int.Parse(label[^2].ToString());
                if (n > largest_state_num) {
                    largest_state_num = n;
                } else {
                    break;
                }
            }
            return largest_state_num - initial_state_num + 1;
        }

        public int num_phones() {
            if (is_state_alignment_label()) {
                return Count / num_states();
            } else {
                return Count;
            }
        }

        public int num_frames(int frame_shift = 50000) {
            return end_times[^1] / frame_shift;
        }
    }

    public class HTSLabelEnum : IEnumerator<HTSLabel> {
        public HTSLabelFile _htsLabelFile;

        // Enumerators are positioned before the first element
        // until the first MoveNext() call.
        int position = -1;

        public HTSLabelEnum(HTSLabelFile list) {
            _htsLabelFile = list;
        }

        public bool MoveNext() {
            position++;
            return (position < _htsLabelFile.Count);
        }

        public void Reset() {
            position = -1;
        }

        object IEnumerator.Current {
            get {
                return Current;
            }
        }

        public HTSLabel Current {
            get {
                try {
                    return _htsLabelFile[position];
                } catch (IndexOutOfRangeException) {
                    throw new InvalidOperationException();
                }
            }
        }

        //reference: https://learn.microsoft.com/zh-cn/dotnet/api/system.collections.generic.ienumerable-1?view=netstandard-2.1
        //https://zhuanlan.zhihu.com/p/244894004
        private bool disposedValue = false;
        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (!this.disposedValue) {
                if (disposing) {
                    // Dispose of managed resources.
                }
                /*_current = null;
                if (_sr != null)
                {
                    _sr.Close();
                    _sr.Dispose();
                }*/
            }

            this.disposedValue = true;
        }
    }

    public static class hts {
        public static HTSLabelFile load(IEnumerable<string> lines) {
            /*
            Load HTS-style label file

            Args:
                lines (list): Content of label file. If not None, construct HTSLabelFile
                    directry from it instead of loading a file.

            Returns:
                labels (HTSLabelFile): Instance of HTSLabelFile.
            
            TODO
            Examples:
                >>> from nnmnkwii.io import hts
                >>> from nnmnkwii.util import example_label_file
                >>> labels = hts.load(example_label_file())
             */
            var labels = new HTSLabelFile();
            foreach (string line in lines) {
                if (line[0] == '#') {
                    continue;
                }
                //cols = line.strip().split()
                //split with multiple spaces
                var cols = PythonString.split(line.Trim());
                int start_time;
                int end_time;
                string context;
                if (cols.Length == 3) {
                    start_time = int.Parse(cols[0]);
                    end_time = int.Parse(cols[1]);
                    context = cols[2];
                } else if (cols.Length == 1) {
                    start_time = -1;
                    end_time = -1;
                    context = cols[0];
                } else {
                    //TODO: raise RuntimeError("Not supported for now")
                    throw (new Exception("Not supported for now"));
                }
                labels.start_times.Add(start_time);
                labels.end_times.Add(end_time);
                labels.contexts.Add(context);
            }
            return labels;
        }

        public static HTSLabelFile load(string path, Encoding encoding = null) {
            /*
            Load HTS-style label file

            Args:
                path (str): Path of file.
                encoding (System.Text.Encoding): Text File Encoding used to open the
                    file. Default is UTF-8

            Returns:
                labels (HTSLabelFile): Instance of HTSLabelFile.

            TODO
            Examples:
                >>> from nnmnkwii.io import hts
                >>> from nnmnkwii.util import example_label_file
                >>> labels = hts.load(example_label_file())
             */
            //The default encoding is UTF-8
            if (encoding == null) {
                encoding = Encoding.UTF8;
            }
            return load(File.ReadLines(path, encoding));
        }

        public static string wildcards2regex(
            string question,
            bool convert_number_pattern = false,
            bool convert_svs_pattern = true) {
            /*
            subphone_features
            Convert HTK-style question into regular expression for searching labels.
            If convert_number_pattern, keep the following sequences unescaped for
            extracting continuous values):
            (\d+)       -- handles digit without decimal point
            ([\d\.]+)   -- handles digits with and without decimal point
            ([-\d]+)    -- handles positive and negative numbers
             */

            //# handle HTK wildcards (and lack of them) at ends of label:
            string prefix = "";
            string postfix = "";
            if (question.Contains("*")) {
                if (!question.StartsWith("*")) {
                    prefix = "\\A";
                }
                if (!question.EndsWith("*")) {
                    postfix = "\\A";
                }
            }
            question = Regex.Escape(question.Trim(new char[] { '*' }))
                .Replace("\\*", ".*");//# convert remaining HTK wildcards * and ? to equivalent regex:
            question = prefix + question + postfix;

            if (convert_number_pattern) {
                question = question.Replace("\\(\\\\d\\+\\)", "(\\d+)")
                    .Replace("\\(\\[\\-\\\\d\\]\\+\\)", "([-\\d]+)")
                    .Replace("\\(\\[\\\\d\\\\\\.\\]\\+\\)", "([\\d\\.]+)");

            }
            //# NOTE: singing voice synthesis specific handling
            if (convert_svs_pattern) {
                question = question.Replace(
                    "\\(\\[A\\-Z\\]\\[b\\]\\?\\[0\\-9\\]\\+\\)",
                    "([A-Z][b]?[0-9]+)")
                    .Replace("\\(\\\\NOTE\\)", "([A-Z][b]?[0-9]+)")
                    .Replace("\\(\\[pm]\\\\d\\+\\)", "([pm]\\d+)");
            }
            return question;
        }

        public static Tuple<
                Dictionary<int, Tuple<string, List<Regex>>>,
                Dictionary<int, Tuple<string, Regex>>
            > load_question_set
            (
            string qs_file_name,
            bool append_hat_for_LL = true,
            bool convert_svs_pattern = true,
            Encoding encoding = null
            ) {
            /*
             Load HTS-style question and convert it to binary/continuous feature
            extraction regexes.

            This code was taken from Merlin.

            Args:
                qs_file_name (str): Input HTS-style question file path
                append_hat_for_LL (bool): Append ^ for LL regex search.
                    Note that the most left context is assumed to be phoneme identity
                    before the previous phoneme (i.e. LL-xx). This parameter should be False
                    for the HTS-demo_NIT-SONG070-F001 demo.
                convert_svs_pattern (bool): Convert SVS specific patterns.

            Returns:
                (binary_dict, numeric_dict): Binary/numeric feature extraction
                regexes.

            Examples:
                >>> from nnmnkwii.io import hts
                >>> from nnmnkwii.util import example_question_file
                >>> binary_dict, numeric_dict = hts.load_question_set(example_question_file())
             */
            if (encoding == null) {
                encoding = Encoding.UTF8;
            }
            int binary_qs_index = 0;
            int continuous_qs_index = 0;
            var binary_dict = new Dictionary<int, Tuple<string, List<Regex>>> { };
            var numeric_dict = new Dictionary<int, Tuple<string, Regex>> { };

            var LL = new Regex("LL-");

            foreach (string _line in File.ReadLines(qs_file_name, encoding)) {
                var line = _line.Replace("\n", "");
                if (line.Length <= 0 || line.StartsWith("#")) {
                    continue;
                }
                var name = PythonString.split(line)[1]
                    .Replace("\"", "")
                    .Replace("\'", "");
                var question_list = line.Split("{")[1]
                    .Split("}")[0].Trim().Split(",");
                var temp_list = line.Split(" ");
                var question_key = temp_list[1];
                if (temp_list[0] == "CQS") {
                    PythonAssert.Assert(question_list.Length == 1);
                    var processed_question = wildcards2regex(
                        question_list[0],
                        convert_number_pattern: true,
                        convert_svs_pattern: convert_svs_pattern);
                    numeric_dict[continuous_qs_index] = new Tuple<string, Regex>(
                        name,
                        new Regex(processed_question));
                    continuous_qs_index++;
                } else if (temp_list[0] == "QS") {
                    var re_list = new List<Regex>();
                    foreach (var temp_question in question_list) {
                        var processed_question = wildcards2regex(temp_question);
                        if (append_hat_for_LL
                            && LL.Match(question_key).Success
                            && processed_question[0] != '^') {
                            processed_question = "^" + processed_question;
                        }
                        var re = new Regex(processed_question);
                        re_list.Add(re);
                    }

                    binary_dict[binary_qs_index] = new Tuple<string, List<Regex>>(name, re_list);
                    binary_qs_index++;
                } else {
                    //raise RuntimeError("Not supported question format")
                    throw new Exception("Not supported question format");
                }
            }
            return new Tuple<
                    Dictionary<int, Tuple<string, List<Regex>>>,
                    Dictionary<int, Tuple<string, Regex>>
                    >(binary_dict, numeric_dict);
        }
    }
}
