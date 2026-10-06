using System;
using System.Threading;

namespace CrucibleUnlock
{
    /// <summary>Configure second-trial boss traces before entering a run; keep the value fixed throughout the run. No game APIs or disk writes.</summary>
    public static class BossTraceDropApi
    {
        public const int DefaultAmount = 300;
        public const int MaximumAmount = 1000;
        private static int _amount = DefaultAmount;

        public static int Amount => Volatile.Read(ref _amount);

        /// <summary>Call before a run. 0 disables traces; valid range is 0 through MaximumAmount. Invalid values leave Amount unchanged.</summary>
        public static void SetAmount(int amount)
        {
            if (amount < 0 || amount > MaximumAmount)
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Boss trace amount must be between 0 and " + MaximumAmount + ".");
            Volatile.Write(ref _amount, amount);
        }

        public static void ResetToDefault() => SetAmount(DefaultAmount);
    }
}
