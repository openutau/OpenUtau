using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using OpenUtau.Core.Util;
using OpenUtau.Core.Util.nnmnkwii.io.hts;
//reference: https://github.com/r9y9/nnmnkwii/blob/master/nnmnkwii/frontend/merlin.py

namespace OpenUtau.Core.Util.nnmnkwii.frontend {
    public class merlin {
        //TODO:Should subphone_features be an enum?
        static Dictionary<string, int> frame_feature_size_dict = new Dictionary<string, int>
        {
            {"full",9},
            {"state_only",1 },
            {"frame_only",1 },
            {"uniform_state",2 },
            {"minimal_phoneme",3 },
            {"coarse_coding",4 },
        };

        public static int get_frame_feature_size(string subphone_features = "full") {
            if (subphone_features == null) {
                return 0;
            }
            subphone_features = subphone_features.Trim().ToLower();
            if (subphone_features == "none") {
                //TODO:raise ValueError("subphone_features = 'none' is deprecated, use None instead")
                throw new Exception("subphone_features = 'none' is deprecated, use None instead");
            }
            if (frame_feature_size_dict.TryGetValue(subphone_features, out var result)) {
                return result;
            } else {
                //TODO:raise ValueError("Unknown value for subphone_features: %s" % (subphone_features))
                throw new Exception($"Unknown value for subphone_features: {subphone_features}");
            }
        }

        public static List<int> pattern_matching_binary(
            Dictionary<int, Tuple<string, List<Regex>>> binary_dict, string label) {
            int dict_size = binary_dict.Count;
            var lab_binary_vector = Enumerable.Repeat(0, dict_size).ToList();

            foreach (int i in Enumerable.Range(0, dict_size)) {
                //ignored code: Always true
                //if isinstance(current_question_list, tuple):
                var current_question_list = binary_dict[i].Item2;
                var binary_flag = 0;
                foreach (var current_compiled in current_question_list) {
                    var ms = current_compiled.Match(label);
                    if (ms.Success) {
                        binary_flag = 1;
                        break;
                    }
                }
                lab_binary_vector[i] = binary_flag;
            }
            return lab_binary_vector;
        }

        public static float[] pattern_matching_continous_position(
            Dictionary<int, Tuple<string, Regex>> numeric_dict, string label) {
            int dict_size = numeric_dict.Count;
            var lab_continuous_vector = Enumerable.Repeat(0f,dict_size).ToArray();
            foreach (int i in Enumerable.Range(0, dict_size)) {
                //ignored code: Always true
                //if isinstance(current_compiled, tuple):

                var current_compiled = numeric_dict[i].Item2;
                //# NOTE: newer version returns tuple of (name, question)

                //ignore code:
                //if isinstance(current_compiled, tuple):
                //  current_compiled = current_compiled[1]
                float continuous_value;
                if (current_compiled.ToString().Contains("([-\\d]+)")) {
                    continuous_value = -50.0f;
                } else {
                    continuous_value = -1.0f;
                }

                var ms = current_compiled.Match(label);
                if (ms.Success) {
                    string note = ms.Groups[1].Value;
                    if (HTS.NameToTone(note)>0) {
                        continuous_value = HTS.NameToTone(note);
                    } else if (note.StartsWith("p")) {
                        continuous_value = int.Parse(note[1..]);
                    } else if (note.StartsWith("m")) {
                        continuous_value = -int.Parse(note[1..]);
                    } else if (float.TryParse(note, out float num)) {
                        continuous_value = num;
                    }
                    
                }
                lab_continuous_vector[i] = continuous_value;
            }
            return lab_continuous_vector;
        }

        public static List<List<float>> load_labels_with_phone_alignment(
            HTSLabelFile hts_labels,
            Dictionary<int, Tuple<string, List<Regex>>> binary_dict,
            Dictionary<int, Tuple<string, Regex>> numeric_dict,
            string subphone_features = null,
            bool add_frame_features = false,
            int frame_shift = 50000
            ) {
            int dict_size = binary_dict.Count + numeric_dict.Count;
            int frame_feature_size = get_frame_feature_size(subphone_features);
            int dimension = frame_feature_size + dict_size;
            int dimx;
            if (add_frame_features) {
                dimx = hts_labels.num_frames();
            } else {
                dimx = hts_labels.num_phones();
            }
            int label_feature_index = 0;

            //matrix size: dimx*dimension
            var label_feature_matrix = new List<List<float>>();
            if (subphone_features == "coarse_coding") {
                throw new NotImplementedException();
                //TODO:compute_coarse_coding_features()
            }
            foreach (int idx in Enumerable.Range(0, hts_labels.Count)) {
                var label = hts_labels[idx];
                var frame_number = label.end_time / frame_shift - label.start_time / frame_shift;
                //label_binary_vector = pattern_matching_binary(binary_dict, full_label)
                var label_vector = pattern_matching_binary(binary_dict, label.context).Select(x => (float)x).ToList();

                var label_continuous_vector = pattern_matching_continous_position(numeric_dict, label.context);
                //label_vector = np.concatenate(
                label_vector.AddRange(label_continuous_vector);

                /*TODO:
                 if subphone_features == "coarse_coding":
                    cc_feat_matrix = extract_coarse_coding_features_relative(
                        cc_features, frame_number)
                 */
                if (add_frame_features) {
                    throw new NotImplementedException();
                    //TODO
                } else if (subphone_features == null) {
                    label_feature_matrix.Add(label_vector);
                }
            }
            //#omg
            /*
             if label_feature_index == 0:
            raise ValueError(
                "Combination of subphone_features and add_frame_features is not supported: {}, {}".format(
                    subphone_features, add_frame_features
                    ))
             */
            if (label_feature_matrix.Count == 0) {
                throw new Exception("Combination of subphone_features and add_frame_features is not supported: "
                    + $"{subphone_features}, {add_frame_features}");
            }
            return label_feature_matrix;
        }

        public static List<List<float>> linguistic_features(
            HTSLabelFile hts_labels,
            Dictionary<int, Tuple<string, List<Regex>>> binary_dict,
            Dictionary<int, Tuple<string, Regex>> numeric_dict,
            string subphone_features = null,
            bool add_frame_features = false,
            int frame_shift = 50000
            ) {
            /*
             Linguistic features from HTS-style full-context labels.

    This converts HTS-style full-context labels to it's numeric representation
    given feature extraction regexes which should be constructed from
    HTS-style question set. The input full-context must be aligned with
    phone-level or state-level.　

    .. note::
        The implementation is adapted from Merlin, but no internal algorithms are
        changed. Unittests ensure this can get same results with Merlin
        for several typical settings.

    Args:
        hts_label (hts.HTSLabelFile): Input full-context label file
        binary_dict (dict): Dictionary used to extract binary features
        numeric_dict (dict): Dictionary used to extrract continuous features
        subphone_features (dict): Type of sub-phone features. According
          to the Merlin's source code, None, ``full``, ``state_only``,
          ``frame_only``, ``uniform_state``, ``minimal_phoneme`` and
          ``coarse_coding`` are supported. **However**, None, ``full`` (for state
          alignment) and ``coarse_coding`` (phone alignment) are only tested in
          this library. Default is None.
        add_frame_features (dict): Whether add frame-level features or not.
          Default is False.
        frame_shift (int) : Frame shift of alignment in 100ns units.

    Returns:
        numpy.ndarray: Numpy array representation of linguistic features.

    Examples:
        For state-level labels

        >>> from nnmnkwii.frontend import merlin as fe
        >>> from nnmnkwii.io import hts
        >>> from nnmnkwii.util import example_label_file, example_question_file
        >>> labels = hts.load(example_label_file(phone_level=False))
        >>> binary_dict, numeric_dict = hts.load_question_set(example_question_file())
        >>> features = fe.linguistic_features(labels, binary_dict, numeric_dict,
        ...     subphone_features="full", add_frame_features=True)
        >>> features.shape
        (615, 425)
        >>> features = fe.linguistic_features(labels, binary_dict, numeric_dict,
        ...     subphone_features=None, add_frame_features=False)
        >>> features.shape
        (40, 416)

        For phone-level labels

        >>> from nnmnkwii.frontend import merlin as fe
        >>> from nnmnkwii.io import hts
        >>> from nnmnkwii.util import example_label_file, example_question_file
        >>> labels = hts.load(example_label_file(phone_level=True))
        >>> binary_dict, numeric_dict = hts.load_question_set(example_question_file())
        >>> features = fe.linguistic_features(labels, binary_dict, numeric_dict,
        ...     subphone_features="coarse_coding", add_frame_features=True)
        >>> features.shape
        (615, 420)
        >>> features = fe.linguistic_features(labels, binary_dict, numeric_dict,
        ...     subphone_features=None, add_frame_features=False)
        >>> features.shape
        (40, 416)
             */
            if (hts_labels.is_state_alignment_label()) {

                throw new NotImplementedException();
                //TODO:load_labels_with_state_alignment
            } else {
                return load_labels_with_phone_alignment(
                    hts_labels,
                    binary_dict,
                    numeric_dict,
                    subphone_features,
                    add_frame_features,
                    frame_shift
                    );
            }
        }
    }
}
