using System;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        // Atomically clears every per-session collection and flag when a new simulation tree is created.
        private static void ResetSessionState()
        {
            Found.Clear();
            Added.Clear();
            RaisedDamageOwners.Clear();
            MinionLastDamage.Clear();
            TeleportMinions.Clear();
            HeldStuck.Clear();
            HeldHeight.Clear();
            LastReaction.Clear();
            ReactionCycle.Clear();
            ReactionActionMissing.Clear();
            MinionBirth.Clear();
            Expired.Clear();
            TeamChecked.Clear();
            RoarTraces.Clear();
            lock (NoNavLogged) NoNavLogged.Clear();
            lock (LastTeleportLog) LastTeleportLog.Clear();
            FollowStartRestore.Clear();
            Minions.Clear();
            lock (VisualsLock)
            {
                Attached.Clear();
                AddedEffects.Clear();
                TintAttempts.Clear();
                TintSuccesses.Clear();
                _tintInFlight = IntPtr.Zero;
            }
            BossStorages.Clear();
            _bossSession = null;
            _bossReadWithoutSession = 0;
            _enableChecks = 0;
            _heroFrames = 0;
            _simFrame = null;
            _damageProtectionLogs = 0;
            _lastRosterRaw = 0;
            _lastRoster = "";
            _reactionsReloadRequested = false;
        }
    }
}
