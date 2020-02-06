using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Robust.MSBuild {
    public partial class StaticMethodTask {
        private static async Task<bool> CheckDownloadedVersion(string version, string outDir, string filename) {
            var path = Path.Combine(outDir, filename + ".version");
            await using var vfs = File.Open(path, FileMode.OpenOrCreate);

            if (vfs.Length <= 0)
                return false;

            using var vsr = new StreamReader(vfs);
            var lastVersion = vsr.ReadToEnd();

            return version == lastVersion;
        }
    }
}
