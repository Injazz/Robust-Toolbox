using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Robust.MSBuild {
    public partial class StaticMethodTask {
        private const string _glfwUriBase =
            "https://github.com/space-wizards/build-dependencies/raw/master/natives/glfw/";

        public static async Task<bool> DownloadGlfw(
            string version, string platform, string outDir
        ) {
            var filename = platform switch {
                "windows" => "glfw3.dll",
                "macos" => "libglfw.3.dylib",
                "linux" => "libglfw.so.3",
                _ => throw new PlatformNotSupportedException(platform)
            };

            if (await CheckDownloadedVersion(version, outDir, filename))
                return true;

            await using var fs = File.Create(Path.Combine(outDir, filename));

            using var client = new HttpClient {
                BaseAddress = new Uri($"{_glfwUriBase}{version}/")
            };

            var s = await client.GetStreamAsync(filename);

            await s.CopyToAsync(fs);
            return true;
        }
    }
}
