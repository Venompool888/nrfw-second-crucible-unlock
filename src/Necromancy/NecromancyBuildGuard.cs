using System;
using System.IO;

namespace Necromancy
{
    // Hash-free build guard (2026-10-19 no-hash policy).
    // Verifies Steam appmanifest_1371980.acf buildid == 22928553 and non-empty GameAssembly.dll / global-metadata.dat
    // before any Harmony or native hooks are installed.
    internal static class NecromancyBuildGuard
    {
        internal static void Verify(string modsDirectory, string userDataDirectory, Action<string> log)
        {
            string gameRoot = ResolveGameRoot(modsDirectory, userDataDirectory)
                ?? throw new InvalidOperationException("Cannot resolve game root directory.");
            string steamApps = Directory.GetParent(gameRoot)?.Parent?.FullName
                ?? throw new InvalidOperationException("Cannot resolve Steam library directory.");
            string manifestPath = Path.Combine(steamApps, "appmanifest_1371980.acf");
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("Steam manifest missing: " + manifestPath);

            string manifestText = File.ReadAllText(manifestPath);
            if (!NecromancyPolicy.IsExpectedSteamBuild(manifestText, NecromancyPolicy.ExpectedSteamBuildId))
            {
                string found = NecromancyPolicy.TryParseManifestBuildId(manifestText, out string actual) ? actual : "<unreadable>";
                throw new InvalidOperationException("Requires Steam build " + NecromancyPolicy.ExpectedSteamBuildId + " (found " + found + ").");
            }

            long gameAssemblyBytes = VerifyFileNonEmpty(Path.Combine(gameRoot, "GameAssembly.dll"));
            long metadataBytes = VerifyFileNonEmpty(Path.Combine(gameRoot, "NoRestForTheWicked_Data", "il2cpp_data", "Metadata", "global-metadata.dat"));
            log?.Invoke("build guard passed: Steam=" + NecromancyPolicy.ExpectedSteamBuildId +
                        "; GameAssembly.dll=" + gameAssemblyBytes + "B; global-metadata.dat=" + metadataBytes + "B (no-hash)");
        }

        private static string ResolveGameRoot(string modsDirectory, string userDataDirectory)
        {
            if (!string.IsNullOrWhiteSpace(modsDirectory))
            {
                string parent = Directory.GetParent(Path.GetFullPath(modsDirectory))?.FullName;
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) return parent;
            }
            if (!string.IsNullOrWhiteSpace(userDataDirectory))
            {
                string parent = Directory.GetParent(Path.GetFullPath(userDataDirectory))?.FullName;
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) return parent;
            }
            return null;
        }

        private static long VerifyFileNonEmpty(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new FileNotFoundException("Required game file missing: " + info.Name, path);
            if (info.Length <= 0)
                throw new InvalidOperationException("Required game file is empty: " + info.Name);
            return info.Length;
        }
    }
}
