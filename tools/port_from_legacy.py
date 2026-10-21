"""One-time port of the legacy add-on code (DuoPOC\\extras\\playnite\\aniki-ps5\\Ps5*.cs, loaded by a PowerShell
extension via Add-Type) into Selene Core: clean names, settings-based paths, no per-game rules.
Re-runnable while the legacy code is still the one being fixed day to day."""
import os, re

SRC = r'C:\Users\you\DuoPOC\extras\playnite\aniki-ps5'
DST = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'SeleneCore', 'src')
os.makedirs(DST, exist_ok=True)

FILES = {  # legacy file -> Selene file
    'Ps5RowNav.cs': 'HomeNav.cs',
    'Ps5GameMedia.cs': 'GameMedia.cs',
    'Ps5LocalGames.cs': 'LocalGames.cs',
    'Ps5CloseToTray.cs': 'CloseToTray.cs',
    'Ps5GamePause.cs': 'GamePause.cs',
    'Ps5LoginScene.cs': 'LoginScene.cs',
}
CLASSES = {
    'Ps5RowNav': 'HomeNav', 'Ps5GameMedia': 'GameMedia', 'Ps5LocalGames': 'LocalGames', 'Ps5CloseToTray': 'CloseToTray',
    'Ps5GamePause': 'GamePause', 'Ps5Smooth': 'Smoothness', 'Ps5LoginScene': 'LoginScene',
}
PATHS = [  # exact literal -> Selene expression
    (r'@"D:\PlayniteMedia\Hero"', 'SelenePaths.Media("Hero")'),
    (r'@"D:\PlayniteMedia\Square"', 'SelenePaths.Media("Square")'),
    (r'@"D:\PlayniteMedia\PreviewClips"', 'SelenePaths.Media("PreviewClips")'),
    (r'@"D:\PlayniteMedia\Screenshots"', 'SelenePaths.Media("Screenshots")'),
    (r'@"D:\PlayniteMedia\hub-background.mp4"', 'SelenePaths.ThemeFile("Media", "home-background.mp4")'),
    (r'@"D:\PlayniteMedia\GameStarting.wav"', 'SelenePaths.ThemeAudio("GameStarting.wav")'),
    (r'@"D:\PlayniteMedia\GameStarted.wav"', 'SelenePaths.ThemeAudio("GameReady.wav")'),
    (r'@"D:\PlayniteMedia\sounds\ps5-boot.wav"', 'SelenePaths.ThemeAudio("Boot.wav")'),
    (r'@"D:\PlayniteMedia\sounds\aniki-login.wav"', 'SelenePaths.ThemeAudio("LoginPress.wav")'),
    (r'@"C:\Users\you\DuoPOC\extras\tvmode\ConsoleHelper.exe"', 'SelenePaths.Helper'),
    (r'@"C:\Program Files\Apollo\config\logs"', 'SelenePaths.StreamLogs'),
    (r'@"C:\Program Files (x86)\Steam\userdata"', 'SelenePaths.SteamUserdata'),
]

for src_name, dst_name in FILES.items():
    t = open(os.path.join(SRC, src_name), encoding='utf-8-sig').read()
    t = t.replace('namespace ClaudeTools', 'namespace Selene')
    for old, new in CLASSES.items():
        t = re.sub(r'\b' + old + r'\b', new, t)
    for old, new in PATHS:
        t = t.replace(old, new)
    # paths that must not be literal in const contexts
    t = re.sub(r'(private|public|internal) (?:static )?const string (\w+) = (SelenePaths\.[^;]+);',
               r'\1 static string \2 { get { return \3; } }', t)  # fields first
    t = re.sub(r'\bconst string (\w+) = SelenePaths\.', r'string \1 = SelenePaths.', t)  # then local constants
    if dst_name == 'HomeNav.cs':
        t = t.replace(' Ps5Test.Start();', '')
        # the theme folder comes from Selene's own helper now
        t = re.sub(r'        // folder of the fullscreen theme in use.*?\n        public static void Log\(string s\)',
                   '        public static string ThemeDir { get { return SelenePaths.ThemeDir; } }\n\n        public static void Log(string s)',
                   t, flags=re.S)
        t = t.replace('"ps5nav.log"', '"selene.log"')
    if dst_name == 'LocalGames.cs':
        # per-game rules go: automatic rules replace them (plan phase 5)
        t = t.replace('                FixKnownEntries(db, games);\n', '')
        t = t.replace('                UseElevatedTasks(db, games);\n', '')
        t = '\n'.join(l for l in t.split('\n')
                      if not (l.strip().startswith('{ "Genshin Impact"') or l.strip().startswith('{ "Honkai')))
        t = re.sub(r'private static readonly string\[\] Roots = \{[^}]*\};',
                   'private static string[] Roots { get { return SelenePaths.GameFolders; } }', t)
    if dst_name == 'LoginScene.cs':
        t = t.replace('PlaySound(ThemeAudio("Connexion.mp3"), 0.9);', 'PlaySound(SelenePaths.ThemeAudio("LoginSelect.wav"), 0.9);')
        t = t.replace('return System.IO.Path.Combine(HomeNav.ThemeDir, "audio", file);', 'return SelenePaths.ThemeAudio(file);')
    left = re.findall(r'@"[A-Z]:\\[^"]*"', t)
    left = [x for x in left if 'Windows\\System32' not in x]
    open(os.path.join(DST, dst_name), 'w', encoding='utf-8').write(t)
    print(f'{src_name} -> {dst_name}' + (f'  STILL HARD-CODED: {left}' if left else ''))
