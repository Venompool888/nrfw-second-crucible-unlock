using System;
using System.Globalization;
using System.Threading;
using Il2CppQuantum;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        private static bool _bossEntranceEnabled;

        private sealed class ActiveEntrance
        {
            internal EntityRef Entity;
            internal EntranceRun Run;
            internal ActionData[] Actions;
            internal long Start;
            internal int Current;
            internal QBoolean? IgnoreDamage, AiDisabled;
        }

        private static bool CanStartEntrance(BossSession session, BossEntrance spec) =>
            _bossEntranceEnabled && !session.EntranceFailed && spec?.Enabled == true;

        private static unsafe ActionComponent* EntranceActions(Frame f, EntityRef entity) =>
            EntityRefExtensions.Has<ActionComponent>(entity, f) ? EntityRefExtensions.Get<ActionComponent>(entity, f) : null;

        private static string EntranceSeconds(long raw) => (raw / 65536.0).ToString("0.###", CultureInfo.InvariantCulture);

        private static unsafe void StartEntrance(Frame f, BossSession session, EntityRef entity, EntityRef owner, BossRuneRule rule)
        {
            try
            {
                if (session.Entrances.ContainsKey(entity.Raw) || !AiUtils.ExistsAlive(f, entity)) return;
                var spec = rule.Entrance;
                var actions = new ActionData[spec.MinionActions.Length];
                void Missing(long guid) => Write("entrance SKIP entity=" + entity.Raw + " rune=" + rule.Key + " unresolved action=" + guid);
                for (int i = 0; i < actions.Length; ++i)
                {
                    long guid = spec.MinionActions[i];
                    actions[i] = Asset<ActionData>(f, guid);
                    if (actions[i] == null) { Missing(guid); return; }
                }
                var player = spec.PlayerAction == 0 ? null : Asset<ActionData>(f, spec.PlayerAction);
                if (spec.PlayerAction != 0 && player == null) { Missing(spec.PlayerAction); return; }

                long now = f.TimeSinceStart.RawValue;
                var active = new ActiveEntrance { Entity = entity, Run = new EntranceRun(spec, now), Actions = actions, Start = now };
                session.Entrances.Add(entity.Raw, active);
                Write("entrance start entity=" + entity.Raw + " rune=" + rule.Key + " owner=" + owner.Raw + " actions=[" +
                    string.Join(",", spec.MinionActions) + "] player=" + spec.PlayerAction + " cut=" + EntranceSeconds(spec.PlayerCutRaw) + "s");

                // Has/Get<T> are generic interop calls: an instantiation the game never compiled throws at run time
                // (0.18.2 hit this with HealthComponent). These two are optional, so a failure must not cancel the entrance.
                var enabled = new QBoolean { Value = 1 };
                bool immune = false, paused = false;
                if (spec.Invincible && !session.EntranceImmunityFailed)
                    try
                    {
                        if (EntityRefExtensions.Has<DamageResolverComponent>(entity, f))
                        {
                            var damage = EntityRefExtensions.Get<DamageResolverComponent>(entity, f);
                            if (damage != null) { active.IgnoreDamage = damage->IgnoreDamage; damage->IgnoreDamage = enabled; immune = true; }
                        }
                    }
                    catch (Exception e) { session.EntranceImmunityFailed = true; Write("entrance invincible UNAVAILABLE " + e.GetType().Name + ": " + e.Message); }
                if (spec.PauseAi && !session.EntranceAiPauseFailed)
                    try
                    {
                        var ai = EntityRefExtensions.Has<AiControllerComponent>(entity, f) ? EntityRefExtensions.Get<AiControllerComponent>(entity, f) : null;
                        if (ai == null) Write("entrance: no AiControllerComponent on minion, AI not paused");
                        else { active.AiDisabled = ai->IsDisabled; ai->IsDisabled = enabled; paused = true; }
                    }
                    catch (Exception e) { session.EntranceAiPauseFailed = true; Write("entrance pause-ai UNAVAILABLE " + e.GetType().Name + ": " + e.Message); }
                Write("entrance guards entity=" + entity.Raw + " invincible=" + immune + " aiPaused=" + paused);

                bool running = ExecuteEntranceStep(f, active, active.Run.Start().Index, EntranceActions(f, entity), now);
                // The summoner's pose is cosmetic: a failure here must not cancel the minion's entrance.
                if (player != null && !session.EntrancePlayerFailed && owner.IsValid && owner.Raw == unchecked((ulong)Interlocked.Read(ref _heroRaw)))
                    try
                    {
                        var hero = EntranceActions(f, owner);
                        if (hero != null)
                        {
                            int result = ForceExecute(f, hero, player, out bool playing, out _);
                            Write("entrance player action=" + spec.PlayerAction + " -> " + result + " executingNow=" + playing);
                            // No timer unless it started: a later cut would hit the same gesture performed by the player.
                            if (playing) session.PlayerCuts.Add((owner, spec.PlayerAction, now, active.Run));
                        }
                    }
                    catch (Exception e) { session.EntrancePlayerFailed = true; Write("entrance player action UNAVAILABLE " + e.GetType().Name + ": " + e.Message); }
                if (!running) EndEntrance(f, session, active, active.Run.Next(now, true, false).Reason, now);
            }
            catch (Exception e) { DisableEntrances(f, session, e); }
        }

        private static unsafe bool ExecuteEntranceStep(Frame f, ActiveEntrance active, int index, ActionComponent* component, long now)
        {
            active.Current = index;
            var action = active.Actions[index];
            bool running = false;
            int result = component == null ? -1 : ForceExecute(f, component, action, out running, out _);
            active.Run.Executed(now, running);
            Write("entrance step " + (index + 1) + "/" + active.Actions.Length + " entity=" + active.Entity.Raw +
                " action=" + action.Guid.Value + " -> " + result + " executingNow=" + running);
            return running;
        }

        private static unsafe void TickEntrances(Frame f)
        {
            var session = _bossSession;
            if (session == null || session.EntranceFailed || (session.Entrances.Count == 0 && session.PlayerCuts.Count == 0)) return;
            try
            {
                if (BossSessionFor(f) != session) return;
                long now = f.TimeSinceStart.RawValue;
                foreach (var active in System.Linq.Enumerable.ToArray(session.Entrances.Values))   // EndEntrance removes entries
                {
                    var entity = active.Entity;
                    bool alive = EntityRefExtensions.Has<RaisedMinionComponent>(entity, f) && AiUtils.ExistsAlive(f, entity);
                    var actions = alive ? EntranceActions(f, entity) : null;
                    bool executing = actions != null && actions->IsExecutingAction(f, active.Actions[active.Current]);
                    var next = active.Run.Next(now, alive, executing);
                    if (next.Command == EntranceCommand.Execute && !ExecuteEntranceStep(f, active, next.Index, actions, now))
                        next = active.Run.Next(now, alive, false);
                    if (next.Command == EntranceCommand.Finish) EndEntrance(f, session, active, next.Reason, now);
                }
                for (int i = session.PlayerCuts.Count - 1; i >= 0; --i)
                {
                    var cut = session.PlayerCuts[i];
                    if (!cut.Run.PlayerCutDue(now)) continue;
                    session.PlayerCuts.RemoveAt(i);
                    try
                    {
                        bool bound = cut.Hero.Raw == unchecked((ulong)Interlocked.Read(ref _heroRaw));
                        var actions = bound ? EntranceActions(f, cut.Hero) : null;
                        var action = actions == null ? null : Asset<ActionData>(f, cut.Action);
                        bool abort = action != null && actions->IsExecutingAction(f, action);
                        if (abort) actions->Abort(f, ActionComponent.ActionLayer);
                        Write("entrance player cut +" + EntranceSeconds(now - cut.Start) + "s aborted=" + (abort ? "true" : "false"));
                    }
                    catch (Exception e) { session.EntrancePlayerFailed = true; Write("entrance player cut FAILED " + e.GetType().Name + ": " + e.Message); }
                }
            }
            catch (Exception e) { DisableEntrances(f, session, e); }
        }

        private static void EndEntrance(Frame f, BossSession session, ActiveEntrance active, string reason, long now)
        {
            RestoreEntrance(f, active);
            session.Entrances.Remove(active.Entity.Raw);
            Write("entrance end entity=" + active.Entity.Raw + " reason=" + reason + " elapsed=" + EntranceSeconds(now - active.Start) + "s");
        }

        private static unsafe void RestoreEntrance(Frame f, ActiveEntrance active)
        {
            var entity = active.Entity;
            try
            {
                if (active.IgnoreDamage.HasValue && EntityRefExtensions.Has<DamageResolverComponent>(entity, f))
                {
                    var damage = EntityRefExtensions.Get<DamageResolverComponent>(entity, f);
                    if (damage != null) damage->IgnoreDamage = active.IgnoreDamage.Value;
                }
            }
            finally
            {
                if (active.AiDisabled.HasValue && EntityRefExtensions.Has<AiControllerComponent>(entity, f))
                {
                    var ai = EntityRefExtensions.Get<AiControllerComponent>(entity, f);
                    if (ai != null) ai->IsDisabled = active.AiDisabled.Value;
                }
            }
        }

        private static void DisableEntrances(Frame f, BossSession session, Exception error)
        {
            session.EntranceFailed = true;
            foreach (var active in session.Entrances.Values)
                try { RestoreEntrance(f, active); }
                catch (Exception e) { Write("entrance restore FAILED entity=" + active.Entity.Raw + " " + e.GetType().Name); }
            session.Entrances.Clear();
            session.PlayerCuts.Clear();
            Write("entrance DISABLED " + error.GetType().Name);
        }
    }
}
