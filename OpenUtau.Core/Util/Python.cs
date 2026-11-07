using System;
using System.Text.RegularExpressions;

namespace OpenUtau.Core.Util.nnmnkwii.python {
    public class AssertionError : Exception {

        public AssertionError() : base() { }

        public AssertionError(string message) : base(message) {
        }

        public AssertionError(string message, Exception innerException)
            : base(message, innerException) {
        }
    }

    public static class PythonAssert {
        public static void Assert(bool expression) {
            if (!expression) {
                throw new AssertionError();
            }
        }

        public static void Assert(bool expression, string message) {
            if (!expression) {
                throw new AssertionError(message);
            }
        }
    }

    //python string methods that C# doesn't have
    public static class PythonString {
        //python "".split() without parameter
        public static string[] split(string str) {
            return Regex.Split(str, @" +");
        }
    }
}
