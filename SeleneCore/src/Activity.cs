using System;

namespace Selene
{
    // What Selene has running in the background, for a red dot on the menu's Selene settings and on the settings group that owns
    // the work: songs (Sound & music), trailers and art (Spotlight), the achievements check (Games).
    public static class Background
    {
        public static bool Songs { get { try { return SongSetup.Busy; } catch { return false; } } }
        public static bool Art { get { try { return TrailerFetch.Busy; } catch { return false; } } }
        public static bool Achievements
        {
            get
            {
                try { int n, all; double part; Guid? on; return AchievementsData.Checking(out n, out all, out part, out on); }
                catch { return false; }
            }
        }

        public static bool Any { get { return Songs || Art || Achievements; } }

        public static bool ForGroup(string group)
        {
            if (group == "Sound & music") return Songs;
            if (group == "Spotlight") return Art;
            if (group == "Games") return Achievements;
            return false;
        }
    }
}
