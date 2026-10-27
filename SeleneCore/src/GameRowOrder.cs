using System;
using System.Collections.Generic;
using System.Linq;

namespace Selene
{
    public static class GameRowOrder
    {
        private class Key
        {
            public object Game;
            public int Pinned, Preset;
            public DateTime? LastPlayed;
            public string Name;
        }

        // Read expensive reflection/profile values once per game, outside the sort comparer.
        public static List<object> Sort(IEnumerable<object> games, IList<string> pinned,
            Func<object, string> id, Func<object, DateTime?> lastPlayed, Func<object, string> name, Func<object, int> preset)
        {
            var pinOrder = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < pinned.Count; i++) if (!pinOrder.ContainsKey(pinned[i])) pinOrder.Add(pinned[i], i);
            var keys = games.Select(g =>
            {
                int pin;
                if (!pinOrder.TryGetValue(id(g), out pin)) pin = int.MaxValue;
                return new Key { Game = g, Pinned = pin, Preset = preset != null ? preset(g) : 0,
                    LastPlayed = lastPlayed(g), Name = name(g) };
            }).ToList();
            keys.Sort((a, b) =>
            {
                if (a.Preset != b.Preset) return a.Preset.CompareTo(b.Preset);
                if (a.Pinned != b.Pinned) return a.Pinned.CompareTo(b.Pinned);
                int byDate = Nullable.Compare(b.LastPlayed, a.LastPlayed);
                return byDate != 0 ? byDate : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return keys.Select(g => g.Game).ToList();
        }
    }
}
