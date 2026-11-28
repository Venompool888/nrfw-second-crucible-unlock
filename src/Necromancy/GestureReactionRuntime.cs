using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Il2CppQuantum;
using MelonLoader;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        // ---- gesture reactions (T-014; T-012's Warrick roar is the default rule) --------------
        // The table in UserData/NRFWNecromancyReactions.json says which summon x which gesture -> which move. Execution is
        // T-012's playtested path: confirmed frames only, Owner read through the component pointer, NPC through
        // NpcAttachmentComponent, Has/Get<ActionComponent> (never TryGetPointer), move on ActionComponent.ActionLayer.

        private static readonly Dictionary<ulong, long> LastReaction = new();                 // sim thread only; FP raw
        private static readonly Dictionary<(ulong Minion, int Rule), int> ReactionCycle = new(); // sim thread only
        private static readonly HashSet<long> ReactionActionMissing = new();
        private static readonly List<(ulong Minion, long Action, long Start, int Stage)> RoarTraces = new();
        private static readonly NecromancyPolicy.FeatureFaultGuard RoarGuard = new(NecromancyPolicy.DefaultFeatureMaxErrors);
        private static volatile bool _roarEnabled, _reactionsReloadRequested;
        private static bool _profilesNoticeLogged;
        private static volatile ReactionTable _reactions;
        private static string _reactionsPath, _seedGesture, _seedAction;
        private static DateTime _reactionsStamp;
        private static int _gestureLogs;

        private static void RoarError(string stage, Exception e)
        {
            if (RoarGuard.RecordError(out bool justDisabled))
                Write(stage + " ERROR " + e.GetType().Name + ": " + e.Message);
            if (justDisabled)
            {
                _roarEnabled = false;
                Write("roar command DISABLED after repeated errors; rest of plugin unaffected");
            }
        }

        private static void InstallRoarCommand(MelonPreferences_Category category)
        {
            var h = new HarmonyLib.Harmony("NRFW.Necromancy.RoarAoe");
            try
            {
                _seedGesture = category.CreateEntry("roar_gesture", NecromancyPolicy.DefaultRoarGesture,
                    description: "Gesture GUID for the default Warrick rule; only used when NRFWNecromancyReactions.json does not exist yet.").Value;
                _seedAction = category.CreateEntry("roar_action", NecromancyPolicy.DefaultRoarAction,
                    description: "Action GUID for the default Warrick rule; only used when NRFWNecromancyReactions.json does not exist yet.").Value;
                if (!category.CreateEntry("roar_enabled", true,
                    description: "Gesture reactions (T-014): raised minions use moves from UserData/NRFWNecromancyReactions.json when you do a gesture.").Value) return;
                _reactionsPath = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, GestureReactions.FileName);
                LoadReactions(force: true);
                Patch(h, typeof(CommandProcessingSystem), "ExecutePerformGesturePlayerCommand",
                    new[] { typeof(Frame), typeof(int), typeof(PerformGesturePlayerCommand) }, nameof(AfterGestureCommand));
                _roarEnabled = true;
                Write("roar command hook installed (gesture reactions)");
            }
            catch (Exception e)
            {
                h.UnpatchSelf();
                Write("roar command hook FAILED " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void PumpReactionsReload()
        {
            if (!_roarEnabled || !_reactionsReloadRequested) return;
            _reactionsReloadRequested = false;
            LoadReactions(force: false);
        }

        // Writes the default table when the file is missing, then (re)loads it when it changed. A file that cannot be
        // used as a whole falls back to the built-in default rule so T-012's behaviour stays; bad rules are skipped.
        private static void LoadReactions(bool force)
        {
            if (_reactionsPath == null) return;
            long gesture = GestureReactions.SeedGuid(_seedGesture, NecromancyPolicy.DefaultRoarGesture);
            long action = GestureReactions.SeedGuid(_seedAction, NecromancyPolicy.DefaultRoarAction);
            try
            {
                if (!File.Exists(_reactionsPath))
                {
                    if (NecromancyPolicy.RoarAssetGuid(_seedGesture) == null || NecromancyPolicy.RoarAssetGuid(_seedAction) == null)
                        Write("reactions: roar_gesture/roar_action keywords are not supported (runtime asset paths are empty); seeding with GUIDs");
                    File.WriteAllText(_reactionsPath, GestureReactions.DefaultJson(gesture, action));
                    Write("reactions: wrote default table " + GestureReactions.FileName + " (gesture " + gesture + " -> action " + action + ")");
                }
                var stamp = File.GetLastWriteTimeUtc(_reactionsPath);
                if (!force && stamp == _reactionsStamp && _reactions != null) return;
                string text = File.ReadAllText(_reactionsPath);
                // Record the stamp only after a successful read, so a file locked by an editor is retried next session.
                _reactionsStamp = stamp;
                var table = GestureReactions.Parse(text, out string fileError);
                if (table == null)
                {
                    _reactions = GestureReactions.Default(gesture, action);
                    Write("reactions file INVALID (" + fileError + "); using the built-in Warrick rule");
                    return;
                }
                foreach (string err in table.Errors) Write("reactions rule REJECTED " + err);
                _reactions = table;
                _profilesNoticeLogged = false;
                var parts = new List<string>();
                foreach (var r in table.Rules)
                    parts.Add(r.Label + " who=" + r.Who + " gesture=" + r.Gesture + " actions=" + string.Join("/", r.Actions) +
                              " pick=" + r.Pick + " cooldown=" + r.CooldownSeconds);
                Write("reactions loaded " + table.Rules.Length + " rule(s), rejected " + table.Errors.Length + ": " + string.Join("; ", parts));
            }
            catch (Exception e)
            {
                bool had = _reactions != null;
                _reactions ??= GestureReactions.Default(gesture, action);
                Write("reactions load FAILED " + e.GetType().Name + ": " + e.Message + (had ? "; keeping the current table" : "; using the built-in Warrick rule"));
            }
        }

        // T-013 (not merged) gives each minion a profile name; until then profile: rules match nothing.
        private static string MinionProfileName(Frame f, EntityRef minion) => null;

        private static unsafe void AfterGestureCommand(Frame __0, int __1, PerformGesturePlayerCommand __2)
        {
            // React only in the confirmed command stream, not during prediction/replay.
            if (!_enabled || !_roarEnabled || __0 == null || !__0.IsVerified) return;
            try
            {
                EntityRef hero = __0.GetPlayerHero(__1);
                if (!hero.IsValid || hero.Raw != unchecked((ulong)Interlocked.Read(ref _heroRaw))) return;
                var table = _reactions;
                long gestureGuid = __2.GestureData.Id.Value;
                string path = gestureGuid > 0 ? Asset<CollectableGestureData>(__0, gestureGuid)?.Path : null;
                bool match = table != null && table.HasGesture(gestureGuid);
                if (++_gestureLogs <= 60)
                    Write("gesture GUID=" + gestureGuid + " Path=" + (path ?? "<unresolved>") + " matched=" + (match ? "yes" : "no"));
                if (!match) return;
                if (table.UsesProfiles && !_profilesNoticeLogged)
                {
                    _profilesNoticeLogged = true;
                    Write("reactions: profile: rules match nothing until T-013 minion profiles are merged");
                }

                long now = __0.TimeSinceStart.RawValue;
                int owned = 0, commanded = 0;
                var seen = new List<string>();
                var resolved = new Dictionary<long, ActionData>();
                // Candidates: the component iterator plus the minions this plugin recorded from GetMinionFormationSlot,
                // in case the interop iterator yields nothing.
                var candidates = new List<EntityRef>();
                var keys = new HashSet<ulong>();
                int fromIterator = 0;
                foreach (var pair in __0.GetComponentIterator<RaisedMinionComponent>())
                    if (keys.Add(pair.Entity.Raw)) { candidates.Add(pair.Entity); ++fromIterator; }
                foreach (var raw in Minions.Keys)
                    if (keys.Add(raw)) candidates.Add(new EntityRef { Raw = raw });
                seen.Add("iterator=" + fromIterator + " recorded=" + Minions.Count);
                foreach (var minion in candidates)
                {
                    if (!EntityRefExtensions.Has<RaisedMinionComponent>(minion, __0)) { seen.Add("gone:" + minion.Raw); continue; }
                    // 0.15.4 playtest: the iterator's copied component never matched the hero (owned minions=0);
                    // read Owner through the component pointer like MinionDamage does.
                    var rc = EntityRefExtensions.Get<RaisedMinionComponent>(minion, __0);
                    ulong owner = rc == null ? 0 : rc->Owner.Raw;
                    if (owner != hero.Raw) { seen.Add("other-owner:" + owner); continue; }
                    ++owned;
                    var npcData = MinionNpcData(__0, minion);
                    long npc = npcData?.Guid.Value ?? 0;
                    string tier = npcData == null ? "" : npcData.EnemyType.ToString();
                    string profile = MinionProfileName(__0, minion);
                    string who = NecromancyPolicy.NpcName(npc) + "/" + (tier.Length > 0 ? tier : "?");
                    var rule = GestureReactions.Match(table, gestureGuid, npc, profile, tier);
                    if (rule == null) { seen.Add(who + "(no rule)"); continue; }
                    if (LastReaction.TryGetValue(minion.Raw, out long last) && !GestureReactions.CooldownElapsed(last, now, rule.CooldownSeconds))
                    { seen.Add(who + "(cooldown " + rule.Label + ")"); continue; }
                    // 0.15.10 playtest: Frame.TryGetPointer(out T*) needs UIntPtr..ctor(IntPtr), missing at runtime
                    // (MissingMethodException). Has + Get return the pointer directly, as elsewhere in this plugin.
                    if (!EntityRefExtensions.Has<ActionComponent>(minion, __0)) { seen.Add(who + "(no-actions)"); continue; }
                    var actions = EntityRefExtensions.Get<ActionComponent>(minion, __0);
                    if (actions == null) { seen.Add(who + "(null-actions)"); continue; }

                    var cycleKey = (minion.Raw, rule.Index);
                    int cycle = ReactionCycle.TryGetValue(cycleKey, out int c) ? c : 0;
                    long actionGuid = rule.Actions[GestureReactions.PickIndex(rule.Pick, rule.Actions.Length, minion.Raw, now, cycle)];
                    // Advance before resolving: an unresolvable GUID in a cycle list is skipped next time instead of
                    // being picked forever.
                    ReactionCycle[cycleKey] = cycle + 1;
                    if (!resolved.TryGetValue(actionGuid, out var action)) resolved[actionGuid] = action = ResolveReactionAction(__0, actionGuid);
                    if (action == null) { seen.Add(who + "(action " + actionGuid + " unresolved)"); continue; }
                    ++commanded;
                    LastReaction[minion.Raw] = now;
                    try
                    {
                        // 0.15.13 playtest: Execute on layer 0 (ActionLayer.Base, locomotion) returned an id but Warrick never
                        // moved. Attacks run on ActionLayer.Action (1).
                        int layer = ActionComponent.ActionLayer;
                        int result = actions->Execute(__0, action, null, false, default, layer, false, true, default);
                        bool running = actions->IsExecutingAction(__0, action);
                        Write("reaction minion entity=" + minion.Raw + " npc=" + who + " rule=" + rule.Label + " action GUID=" + action.Guid.Value +
                              " layer=" + layer + " -> " + result + " executingNow=" + running);
                        RoarTraces.Add((minion.Raw, action.Guid.Value, now, 0));
                    }
                    catch (Exception e) { Write("reaction Execute FAILED entity=" + minion.Raw + " " + e.GetType().Name + ": " + e.Message); }
                }
                if (commanded == 0)
                    Write("reaction gesture matched but no minion commanded: owned minions=" + owned + " [" + string.Join(", ", seen) + "]");
            }
            catch (Exception e) { RoarError("roar-command", e); }
        }

        // Read-only follow-up: is the commanded move still executing 0.5 s and 1.5 s later? Verified end-of-frame only.
        private static unsafe void TickRoarTraces(Frame f)
        {
            if (RoarTraces.Count == 0) return;
            long now = f.TimeSinceStart.RawValue;
            for (int i = RoarTraces.Count - 1; i >= 0; --i)
            {
                var tr = RoarTraces[i];
                long due = tr.Stage == 0 ? (1L << 15) : (3L << 15);   // 0.5 s, then 1.5 s
                if (now - tr.Start < due) continue;
                var minion = new EntityRef { Raw = tr.Minion };
                string state = "gone";
                try
                {
                    if (EntityRefExtensions.Has<ActionComponent>(minion, f))
                    {
                        var ac = EntityRefExtensions.Get<ActionComponent>(minion, f);
                        var data = Asset<ActionData>(f, tr.Action);
                        state = ac == null || data == null ? "unreadable" :
                            "executing=" + ac->IsExecutingAction(f, data) + " actionLayerBusy=" + ac->IsExecuting(f, ActionComponent.ActionLayer) +
                            " baseLayerBusy=" + ac->IsExecuting(f, ActionComponent.BaseLayer);
                    }
                }
                catch (Exception e) { state = "?" + e.GetType().Name; }
                Write("reaction trace +" + (tr.Stage == 0 ? "0.5" : "1.5") + "s entity=" + tr.Minion + " action=" + tr.Action + " " + state);
                if (tr.Stage == 0) RoarTraces[i] = (tr.Minion, tr.Action, tr.Start, 1); else RoarTraces.RemoveAt(i);
            }
        }

        // Rules carry GUIDs only (runtime asset paths are empty in the release build). A GUID that is not an ActionData
        // is reported once per session with its actual type.
        private static ActionData ResolveReactionAction(Frame f, long guid)
        {
            var action = guid > 0 ? Asset<ActionData>(f, guid) : null;
            if (action == null && ReactionActionMissing.Add(guid))
            {
                string type = "missing";
                try
                {
                    var raw = guid > 0 ? f.Context.ResourceManager.GetAsset(new AssetGuid { Value = guid }) : null;
                    if (raw != null) type = raw.GetIl2CppType().FullName;
                }
                catch (Exception e) { type = "?" + e.GetType().Name; }
                Write("reaction action not resolved: " + guid + " (asset type " + type + ", needs ActionData)");
            }
            return action;
        }
    }
}
