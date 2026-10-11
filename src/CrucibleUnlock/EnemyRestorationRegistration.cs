using System;
using System.Collections.Generic;
using System.Linq;

namespace CrucibleUnlock
{
    public enum EnemyAssetRole { View, Weapon, Host, Controller, Npc }
    public sealed class EnemyAssetDefinition
    {
        public EnemyRestorationProfile Profile;
        public EnemyAssetRole Role;
        public long Guid, SourceGuid;
        public string Path;
    }
    // Native operations are isolated here so the real orchestration is testable
    // without loading IL2CPP game assemblies. The shipped backend implements ALL operations.
    public interface IEnemyRestorationBackend
    {
        object Find(long guid);
        object Clone(long sourceGuid, EnemyAssetRole role);
        object CreateView();
        void Configure(object asset, EnemyAssetDefinition definition);
        bool Matches(object asset, EnemyAssetDefinition definition);
        void Register(object asset, EnemyAssetDefinition definition);
        void Remove(EnemyAssetDefinition definition);
    }
    public static class EnemyRestorationRegistration
    {
        public static bool KeepAction(long primary, IEnumerable<long> combos, IEnumerable<long> blocked)
        {
            var excluded = new HashSet<long>(blocked);
            return !excluded.Contains(primary) && !combos.Any(excluded.Contains);
        }

        public static List<EnemyAssetDefinition> Definitions(EnemyRestorationCatalog catalog)
        {
            var result = new List<EnemyAssetDefinition>();
            foreach (var p in catalog.Profiles)
            {
                void Add(EnemyAssetRole role, long guid, long source, string suffix) => result.Add(new EnemyAssetDefinition
                    { Profile = p, Role = role, Guid = guid, SourceGuid = source, Path = p.Path(suffix) });
                Add(EnemyAssetRole.View, p.ViewGuid, 0, "view");
                if (p.BlockedActions.Length != 0) Add(EnemyAssetRole.Weapon, p.PrivateWeaponGuid, p.WeaponGuid, "weapon");
                Add(EnemyAssetRole.Host, p.HostGuid, p.HostTemplateGuid, "host");
                Add(EnemyAssetRole.Controller, p.ControllerGuid, p.ControllerTemplateGuid, "controller");
                Add(EnemyAssetRole.Npc, p.NpcGuid, p.NpcTemplateGuid, "npc");
            }
            return result.OrderBy(d => d.Role).ThenBy(d => d.Profile.Index).ToList();
        }

        public static List<EnemyAssetDefinition> Register(EnemyRestorationCatalog catalog, IEnemyRestorationBackend backend)
        {
            var definitions = Definitions(catalog);
            var existing = definitions.Select(d => backend.Find(d.Guid)).ToArray();
            if (existing.Any(x => x != null))
            {
                if (existing.All(x => x != null) && definitions.Select((d,i) => backend.Matches(existing[i],d)).All(x => x))
                    return definitions;
                throw new InvalidOperationException("Enemy asset GUID collision or incomplete existing registration; no assets replaced.");
            }
            // Build and validate the ENTIRE graph before the first native AddAsset.
            var prepared = new List<object>();
            foreach (var d in definitions)
            {
                var asset = d.Role == EnemyAssetRole.View ? backend.CreateView() : backend.Clone(d.SourceGuid,d.Role);
                backend.Configure(asset,d);
                if (!backend.Matches(asset,d)) throw new InvalidOperationException("Prepared enemy graph mismatch: " + d.Path);
                prepared.Add(asset);
            }
            var attempted = new List<EnemyAssetDefinition>();
            try
            {
                for (int i=0;i<definitions.Count;i++)
                {
                    // AddAsset can throw after insertion/loading; include that slot in rollback.
                    attempted.Add(definitions[i]);
                    backend.Register(prepared[i],definitions[i]);
                    var found = backend.Find(definitions[i].Guid);
                    if (found == null || !backend.Matches(found,definitions[i]))
                        throw new InvalidOperationException("Enemy registration readback failed: " + definitions[i].Path);
                }
                return definitions;
            }
            catch (Exception installError)
            {
                var failures = new List<Exception> { installError };
                for (int i=attempted.Count-1;i>=0;i--)
                {
                    try { backend.Remove(attempted[i]); } catch (Exception error) { failures.Add(error); }
                }
                if (failures.Count > 1) throw new AggregateException("Enemy registration and rollback failed.",failures);
                throw;
            }
        }
    }
}
