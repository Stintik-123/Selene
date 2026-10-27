using System.Windows;
using System.Windows.Media;

namespace Selene
{
    public static class BackgroundFit
    {
        public static Stretch Current
        {
            get { return Features.Value("backgroundfit") == "fit" ? Stretch.Uniform : Features.Value("backgroundfit") == "stretch" ? Stretch.Fill : Stretch.UniformToFill; }
        }
        public static void Apply()
        {
            if (Application.Current != null) Application.Current.Resources["SeleneBackgroundStretch"] = Current;
        }
    }
}
