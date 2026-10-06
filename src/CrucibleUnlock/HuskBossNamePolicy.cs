using System;
using System.Collections.Generic;

namespace CrucibleUnlock
{
    internal static class HuskBossNamePolicy
    {
        // Actual bloatedWarrickBossFight payload uses the ordinary director, Build 22928553.
        internal const int ZoneGuid = -1626030785;
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["变异者沃里克"] = "堕落外壳",
            ["Warrick the Torn"] = "Wallowing Husk",
            ["Warrick, o Infecto"] = "Casca Chafurdeira",
            ["Warrick l'Infecté"] = "Carcasse bourbeuse",
            ["Warrick der Entrissene"] = "Suhlende Hülle",
            ["Warrick il lacerato"] = "Guscio autocommiserante",
            ["異形化したウォリック"] = "ワロウイング・ハスク",
            ["토른 워릭"] = "구르는 껍질",
            ["Warrick Rozdarty"] = "Pogrążona Łuska",
            ["Разделенный Варрик"] = "Тяжелый панцирь",
            ["Warrick el Guiñaposo"] = "Cascarón revolcándose",
            ["裂變者沃里克"] = "泥濘外殼"
        };

        internal static string Resolve(int zone, bool hasBoss, string original) =>
            zone == ZoneGuid && hasBoss && original != null && Names.TryGetValue(original, out var name)
                ? name : original;
    }
}
