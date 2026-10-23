using System;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace Selene
{
    // Collection's LB / RB tabs are Playnite's filter presets marked for fullscreen. With none of their own, Selene adds
    // the same five as Spotlight's tabs (a user who has made their own keeps those).
    public static class CollectionPresets
    {
        public static void Ensure(IPlayniteAPI api)
        {
            try
            {
                var presets = api.Database.FilterPresets;
                if (presets.Any(p => p.ShowInFullscreeQuickSelection)) return;
                Add(api, "All games", new FilterPresetSettings(), SortOrder.Name, SortOrderDirection.Ascending);
                Add(api, "Installed", new FilterPresetSettings { IsInstalled = true }, SortOrder.Name, SortOrderDirection.Ascending);
                Add(api, "Recently played", new FilterPresetSettings(), SortOrder.LastActivity, SortOrderDirection.Descending);
                Add(api, "Favorites", new FilterPresetSettings { Favorite = true }, SortOrder.Name, SortOrderDirection.Ascending);
                Add(api, "Not played yet", new FilterPresetSettings { PlayTime = new EnumFilterItemProperties((int)PlaytimeCategory.NotPlayed) }, SortOrder.Name, SortOrderDirection.Ascending);
                HomeNav.Log("collection presets: Selene's five added");
            }
            catch (Exception ex) { HomeNav.Log("collection presets: " + ex.Message); }
        }

        private static void Add(IPlayniteAPI api, string name, FilterPresetSettings settings, SortOrder order, SortOrderDirection direction)
        {
            var existing = api.Database.FilterPresets.FirstOrDefault(p => p.Name == name);
            if (existing != null) { existing.ShowInFullscreeQuickSelection = true; api.Database.FilterPresets.Update(existing); return; }
            api.Database.FilterPresets.Add(new FilterPreset
            {
                Name = name, Settings = settings, SortingOrder = order, SortingOrderDirection = direction, ShowInFullscreeQuickSelection = true
            });
        }
    }
}
