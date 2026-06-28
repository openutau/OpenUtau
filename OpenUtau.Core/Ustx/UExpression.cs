using System;
using System.Diagnostics;
using System.Linq;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    public enum UExpressionType : int {
        Numerical = 0,
        Options = 1,
        Curve = 2,
    }

    /// <summary>
    /// Specifications of expressions managed by projects and tracks
    /// </summary>
    public class UExpressionDescriptor : IEquatable<UExpressionDescriptor> {
        public string name;
        public string abbr;
        public UExpressionType type;
        public float min;
        public float max;
        public float defaultValue;
        public float? _customDefaultValue = null; // made public for inclusion in YAML
        public bool isFlag;
        public string flag;
        public string[] options;
        public bool skipOutputIfDefault = false;
        [YamlIgnore]
        public float CustomDefaultValue {
            get => _customDefaultValue ?? defaultValue;
            set {
                if (value == defaultValue) {
                    _customDefaultValue = null;
                } else {
                    _customDefaultValue = value;
                }
            }
        }

        /// <summary>
        /// Constructor for Yaml deserialization
        /// </summary>
        public UExpressionDescriptor() { }

        /// <summary>
        /// For Numerical/Curve
        /// </summary>
        public UExpressionDescriptor(string name, string abbr, float min, float max, float defaultValue, string flag = "", float? customDefaultValue = null, bool skipOutputIfDefault = false) {
            this.name = name;
            this.abbr = abbr.ToLower();
            this.min = min;
            this.max = max;
            this.defaultValue = Math.Clamp(defaultValue, min, max);
            isFlag = !string.IsNullOrEmpty(flag);
            this.flag = flag;
            this.CustomDefaultValue = Math.Clamp(customDefaultValue ?? defaultValue, min, max);
            this.skipOutputIfDefault = skipOutputIfDefault;
        }

        /// <summary>
        /// For Options
        /// </summary>
        public UExpressionDescriptor(string name, string abbr, bool isFlag, string[] options) {
            this.name = name;
            this.abbr = abbr.ToLower();
            type = UExpressionType.Options;
            min = 0;
            max = options.Length - 1;
            this.isFlag = isFlag;
            this.options = options;
        }

        public UExpression Create() {
            return new UExpression(this) {
                value = defaultValue,
            };
        }

        public UExpressionDescriptor Clone() {
            return new UExpressionDescriptor() {
                name = name,
                abbr = abbr,
                type = type,
                min = min,
                max = max,
                defaultValue = defaultValue,
                CustomDefaultValue = CustomDefaultValue,
                isFlag = isFlag,
                flag = flag,
                options = (string[])options?.Clone(),
                skipOutputIfDefault = skipOutputIfDefault,
            };
        }

        public override string ToString() => $"{abbr.ToUpper()}: {name}";

        public bool Equals(UExpressionDescriptor other) {
            return this.name == other.name &&
                this.abbr == other.abbr &&
                this.type == other.type &&
                this.min == other.min &&
                this.max == other.max &&
                this.defaultValue == other.defaultValue &&
                this.CustomDefaultValue == other.CustomDefaultValue &&
                this.isFlag == other.isFlag &&
                this.flag == other.flag &&
                ((this.options == null && other.options == null) || this.options.SequenceEqual(other.options) &&
                this.skipOutputIfDefault == other.skipOutputIfDefault);
        }
    }

    /// <summary>
    /// Value for each phoneme
    /// </summary>
    public class UExpression {
        [YamlIgnore] public UExpressionDescriptor descriptor;

        private float _value;

        public int? index;
        public string abbr;
        public float value {
            get => _value;
            set => _value = descriptor == null ? value
                : abbr == Format.Ustx.CLR ? value
                : Math.Min(descriptor.max, Math.Max(descriptor.min, value));
        }

        /// <summary>
        /// Constructor for Yaml deserialization
        /// </summary>
        public UExpression() { }

        public UExpression(UExpressionDescriptor descriptor) {
            Trace.Assert(descriptor != null);
            this.descriptor = descriptor;
            abbr = descriptor.abbr;
        }

        public UExpression(string abbr) {
            this.abbr = abbr;
        }

        public UExpression Clone() {
            return new UExpression(descriptor) {
                index = index,
                value = value,
            };
        }

        public override string ToString() => $"{abbr.ToUpper()}: {value}";
    }
}
