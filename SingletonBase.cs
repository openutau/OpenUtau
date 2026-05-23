using System;

namespace OpenUtau.Core.Util {
    public abstract class SingletonBase<T> where T : class {
        private static readonly Lazy<T> inst = new Lazy<T>(
            () => (T)Activator.CreateInstance(typeof(T), true),
            isThreadSafe: true);
        public static T Inst => inst.Value;
    }
}
