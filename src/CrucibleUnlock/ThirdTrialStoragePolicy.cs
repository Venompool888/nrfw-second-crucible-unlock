using System;
using System.Collections.Generic;
namespace CrucibleUnlock
{
    public interface IThirdTrialStorageBackend
    {
        void Preflight(EnemyRestorationProfile profile);
        void EnsureDependencies(EnemyRestorationProfile profile);
        object BuildAttachments(EnemyRestorationProfile profile);
        void EnsureAttachments(EnemyRestorationProfile profile, object attachments);
        bool IsReady(EnemyRestorationProfile profile);
    }
    public static class ThirdTrialStoragePolicy
    {
        public static void Ensure(EnemyRestorationCatalog catalog, IThirdTrialStorageBackend backend)
        {
            if (catalog?.Profiles == null || catalog.Profiles.Length != 10 || backend == null)
                throw new InvalidOperationException("Third trial requires all ten profiles and a storage backend.");
            var ids = new HashSet<long>();
            foreach (var p in catalog.Profiles)
                if (p == null || !ids.Add(p.NpcGuid)) throw new InvalidOperationException("Duplicate third-trial NPC.");
            foreach (var p in catalog.Profiles) backend.Preflight(p);
            foreach (var p in catalog.Profiles) backend.EnsureDependencies(p);
            var prepared = new List<object>();
            foreach (var p in catalog.Profiles) prepared.Add(backend.BuildAttachments(p));
            for (int i = 0; i < catalog.Profiles.Length; i++)
                backend.EnsureAttachments(catalog.Profiles[i], prepared[i]);
            foreach (var p in catalog.Profiles)
                if (!backend.IsReady(p)) throw new InvalidOperationException("Third trial runtime cache readback failed: " + p.Key);
            // Native caches have no supported removal API. On an exception the caller
            // keeps only these unused private entries and seals the third entrance.
            // Never dispose backing assets while a cache can still reference them.
        }
    }
}
