using System;
using System.Reflection;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Build.Framework;

namespace Robust.MSBuild {

    [PublicAPI]
    public partial class StaticMethodTask : Microsoft.Build.Utilities.Task {

        [Required]
        public string Name { get; set; }

        public ITaskItem[] Arguments { get; set; }

        public override bool Execute() {
            var mi = typeof(StaticMethodTask).GetMethod(Name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (mi == null)
                return false;

            var pis = mi.GetParameters();
            var argLen = pis.Length;
            var args = new object[argLen];
            for (var i = 0; i < argLen; ++i) {
                var pi = pis[i];
                ref var arg = ref args[i];
                if (typeof(ITaskItem).IsAssignableFrom(pi.ParameterType)) {
                    arg = Arguments[i];
                }
                else if (pi.ParameterType == typeof(string)) {
                    arg = Arguments[i].ItemSpec;
                }
                else if (pi.ParameterType.IsPrimitive) {
                    arg = ((IConvertible) Arguments[i].ItemSpec)
                        .ToType(pi.ParameterType, null);
                }
            }

            var result = mi.Invoke(null, args);

            if (mi.ReturnType == typeof(bool))
                return (bool) result;

            if (typeof(Task<bool>).IsAssignableFrom(mi.ReturnType)) {
                return ((Task<bool>) result).GetAwaiter().GetResult();
            }

            // ReSharper disable once InvertIf
            if (typeof(Task).IsAssignableFrom(mi.ReturnType)) {
                ((Task) result).GetAwaiter().GetResult();
                return true;
            }

            return true;
        }

    }

}
