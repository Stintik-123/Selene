using System;

namespace Selene
{
    // The controller's battery as a card (the "Controller battery" switch): once when a wireless controller is first seen after
    // Selene starts (its level), and once as it runs low (again at empty), from HomeNav's XInput poll ("none" | "wired" |
    // "0".."3"). XInput only: Xbox-type controllers, others through a wrapper like DS4Windows.
    public static class PadBattery
    {
        private static bool told;           // the start-up card shown this session
        private static int lowNoted = 9;    // the level the low card was shown for (9: none since the battery was up)

        public static void Show(string value)
        {
            try
            {
                int level;
                bool wireless = int.TryParse(value, out level);
                if (!wireless || !Features.On("padbattery")) { if (!wireless) lowNoted = 9; return; }
                if (!told)
                {
                    told = true;
                    ProgressToast.ShowNote("Controller", Words(level), level <= 1 ? 0xe19c : 0xe1a4);
                    if (level <= 1) lowNoted = level;   // (the start-up card said it already)
                    return;
                }
                // the card: once as the battery gets low, once more should it reach empty; not again until it has charged up
                if (level <= 1 && level < lowNoted) { lowNoted = level; ProgressToast.ShowNote("Controller", Words(level), 0xe19c); }
                else if (level >= 2) lowNoted = 9;
            }
            catch (Exception ex) { HomeNav.Log("battery: " + ex.Message); }
        }

        private static string Words(int level)
        {
            return level >= 3 ? "Battery full" : level == 2 ? "Battery medium" : level == 1 ? "Battery low" : "Battery empty";
        }
    }
}
