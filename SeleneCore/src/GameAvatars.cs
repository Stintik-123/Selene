using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace Selene
{
    public class AvatarChoice
    {
        public Guid GameId { get; set; }
        public string GameName { get; set; }
        public string Image { get; set; }
        // share of the spare height above the square crop: 0.5 centred, lower for covers (faces sit near the top)
        public double FocusY { get; set; }
    }

    // Avatars from the user's own games: round crops of art Playnite (or Selene) already has. Nothing is shipped.
    public static class GameAvatars
    {
        public static List<AvatarChoice> FromLibrary(IPlayniteAPI api, int max)
        {
            return api.Database.Games
                .OrderByDescending(g => g.LastActivity ?? DateTime.MinValue)
                .ThenByDescending(g => g.Playtime)
                .Select(g => Choice(api, g))
                .Where(c => c != null)
                .Take(max)
                .ToList();
        }

        private static AvatarChoice Choice(IPlayniteAPI api, Game game)
        {
            // Selene's square tile art first (made to be cropped), then the cover, then the background
            var square = Path.Combine(SelenePaths.Media("Square"), game.Id + ".jpg");
            if (File.Exists(square)) return new AvatarChoice { GameId = game.Id, GameName = game.Name, Image = square, FocusY = 0.5 };
            var cover = FullPath(api, game.CoverImage);
            if (cover != null) return new AvatarChoice { GameId = game.Id, GameName = game.Name, Image = cover, FocusY = 0.25 };
            var background = FullPath(api, game.BackgroundImage);
            if (background != null) return new AvatarChoice { GameId = game.Id, GameName = game.Name, Image = background, FocusY = 0.45 };
            return null;
        }

        private static string FullPath(IPlayniteAPI api, string databaseFile)
        {
            if (string.IsNullOrEmpty(databaseFile)) return null;
            var path = api.Database.GetFullFilePath(databaseFile);
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
        }

        // saves a square crop as the profile's own picture, so it stays even if the game's art changes later
        public static string SaveFor(Guid profileId, AvatarChoice choice, string dataFolder)
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri(choice.Image);
            source.EndInit();

            int side = Math.Min(source.PixelWidth, source.PixelHeight);
            int x = (source.PixelWidth - side) / 2;
            int y = (int)Math.Round((source.PixelHeight - side) * Math.Max(0, Math.Min(1, choice.FocusY)));
            var square = new CroppedBitmap(source, new Int32Rect(x, y, side, side));
            var scaled = new TransformedBitmap(square, new System.Windows.Media.ScaleTransform(512.0 / side, 512.0 / side));

            var folder = Path.Combine(dataFolder, "Avatars");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, profileId + ".png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(scaled));
            using (var stream = File.Create(file)) encoder.Save(stream);
            return file;
        }
    }
}
