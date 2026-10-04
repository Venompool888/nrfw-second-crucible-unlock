using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CrucibleUnlock
{
    internal static class BuildGuard
    {
        public const string BuildId = "22928553";
        private const string GameAssemblyHash = "5b00ee90833b1be2ea73e01cb83e710e09e86199d12ac769be3c0e82add8b4bb";
        private const string MetadataHash = "4c8dfe07e5f5178f8eefd3d079412ec164eba5da798efd2a2ae03aa37dc79502";

        // Read-only guard. Never update these constants automatically for a new build.
        public static void Verify(string modsDirectory, Action<string> log)
        {
            string gameRoot = Directory.GetParent(Path.GetFullPath(modsDirectory))?.FullName
                ?? throw new InvalidOperationException("Cannot resolve the game directory.");
            string steamApps = Directory.GetParent(gameRoot)?.Parent?.FullName
                ?? throw new InvalidOperationException("Cannot resolve the Steam library.");
            string manifest = File.ReadAllText(Path.Combine(steamApps, "appmanifest_1371980.acf"));
            Match build = Regex.Match(manifest, "\"buildid\"\\s+\"([0-9]+)\"");
            if (!build.Success || build.Groups[1].Value != BuildId)
                throw new InvalidOperationException("Runtime unlock requires Steam build " + BuildId + ".");
            VerifyHash(Path.Combine(gameRoot, "GameAssembly.dll"), GameAssemblyHash);
            VerifyHash(Path.Combine(gameRoot, "NoRestForTheWicked_Data", "il2cpp_data", "Metadata", "global-metadata.dat"), MetadataHash);
            log("[runtime-unlock] build guard passed: Steam=" + BuildId + "; GameAssembly and metadata SHA256 match");
        }

        private static void VerifyHash(string path, string expected)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            string actual = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new InvalidOperationException("Build guard hash mismatch: " + Path.GetFileName(path));
        }
    }
}
