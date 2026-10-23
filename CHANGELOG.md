# Changelog

Selene (theme) and Selene Core (add-on) share one version number and are released together.

## Unreleased

- Selene's own theme plays the legacy home's looping background video behind the login, menus and Spotlight's cards (other themes keep the drawn backdrop in their color).
- The View and Menu hint badges use solid icons, as bold as the letter badges.
- Games from no store show Selene's crescent and "Local" as their platform; the first cover's focus ring isn't cut at the left edge.
- No game art flashing when going from Collection to Spotlight; the trailer sits on black (no navy on the left); filter tab text no longer touches the pill's edge.
- Collection: the trailer exactly like the legacy theme (a 16:9 box on the right with its mask, the shades under it); Spotlight's tabs and filters on both screens (one choice, filled LB / RB badges); flat white store logos; Down on the Collection and Store cards stays put.
- Collection scrolls by whole covers: no cover cut in half at the left edge.
- Collection's background and trailer use the legacy theme's exact shades (bottom, left to the middle) and its trailer vignette; LB / RB tabs come with five presets like Spotlight's; last played reads Today / Yesterday / days ago; a crisp store icon instead of the blurry logos.
- Spotlight: the last game's art no longer lingers faintly behind a card (Store, Collection).
- Collection: LB / RB tabs (Playnite's filter presets) centered at the top like Spotlight's, which are centered too; bigger covers starting at the left edge; wide background art fills the screen; the game's art no longer shows through the trailer.
- Selene's own backdrop: deep navy with a beam of light from the top left and a band of fine drifting dust and bokeh (login, menus and Spotlight's cards).
- Spotlight and Collection crossfade into each other (the screen you leave stays up until the other has its picture).
- Moving and confirming sound the same everywhere: Selene's screens play them through Playnite's own player (one volume).
- Switching between Spotlight and Collection: no old game art fading out on Spotlight, and Collection's art arrives sooner.
- Collection: an achievements bar above the covers (Playnite Achievements: unlocked / total and a progress bar, gold trophy when complete), and holding a direction moves as fast as on Spotlight.
- Collection: the game's logo, chips and genres slide in together when you move to another game.
- The two main screens are now Spotlight (the home row) and Collection (every game); Selene settings, Start on Collection, opens Collection after signing in.
- Going from the library back to the home no longer flashes the library game's art.
- Collection: under the logo, playtime, last played, platform (with the store's logo, also on Spotlight) and release year, genres beneath; while a trailer plays the game's art fades away; trailers start about 4 seconds in (like Aniki) and play on the right from a quarter in, melting into black on the left with a vignette at the top and bottom.
- No menu button in the top bar (Start opens the menu).
- Home filters switch with LB / RB instead of the triggers.
- No notification bell in the top bar. Home hints along the bottom: Library and Menu (with their holds) on the left, what the focused thing does on the right.
- New Collection (the library): the game's art behind its big logo, and when you rest on a game its trailer takes over the background (black fade on the left) with the game's song, R3 for the trailer's sound (trailers with black cinema bars are zoomed so the picture reaches the top); one row of big covers close together, no names, no scrollbar, hints along the bottom like the home.
- The View button switches between the home and the library (it opened Playnite's settings); hold View to search the library, hold Start for Selene settings.
- Moving in the menu no longer moves the home row behind it.
- Switching back to Playnite while a game runs (taskbar, Alt+Tab) opens the home on the running game, with Resume and Close Game, instead of Playnite's "is running" screen.
- Quiet loading screen: a starting game's sounds are turned down the moment they start and come back as the game shows (Selene settings, Quiet loading screen).
- Smoother launch: when the game's window appears the loading screen fades to black (the game's own screen switch can't be seen), no background music over it, and the game's sound fades in after the ready sound.
- Down on the Library and Store cards no longer moves focus to nothing.
- No flash of light the first time the menu opens.
- Smoother logo intro: it starts at the first quiet moment after Playnite starts.
- The home has the login's glow and drifting particles behind its cards (and behind games when their art is off), on themes without a wallpaper.
- The home's button hints are off by default, like a console's home (Selene settings, Button hints, turns them back on).
- The menu no longer has More (Playnite's own menu) or Extensions (other add-ons' menus): only Selene, Playnite settings, desktop mode and power.
- Every sound is leveled to sit together (tools/level_sounds.py); the moving tick is no longer louder than the rest.
- On the home the default music plays and a game's own song only while its trailer card is open, like the legacy theme (the music add-on's live settings are now found the way the legacy theme did, and kept that way).
- The menu is a row of solid icons along the bottom (like a console control center): a ring and the name on the focused one, Power opens sleep, restart, shut down and exit; the home stays in view above.
- Update library starts right away in the background; if an update is already running, the menu says what it is doing instead of waiting.
- No more moving sound when Playnite starts, when you sign in or when you press Play (Selene or Playnite moving focus itself sounded like a move).
- Theme step: each theme shows its own wallpaper on its tile, and a strip of its colors sits under the preview.
- Signing in no longer flashes the old home for a moment before the home's intro plays.
- Selene settings (menu, and the add-on's settings page): turn off the logo intro, Who's playing? at start, interface sounds, music control, button hints, glow and particles, freezing games on Guide, close to tray or no-prompt launchers.
- The intro shows only the crescent (no SELENE lettering).
- Play, Details and the heart are readable in light themes (they took the theme's dark text color).
- Light themes dim the home behind Playnite's menus with a dark shade instead of a bright one.
- Power icons in the menu use the theme's color like the rest.
- Background step: with "Game art" on, the preview shows a game's art (without selecting it, so no game song plays); with no games yet it says the wallpaper shows until you add some.
- New sounds: a soft glassy ping and chime when a profile is made, Square Enix menu sounds for picking a profile, pressing Play and the game starting, Final Fantasy VII Remake menu sounds for confirming (A), closing (B), menus opening and when Selene asks "are you sure", a warm swell with a rising harp when you press Play, two soft harp notes when the game is up.
- A Store card next to Library: opens the store apps on your PC (Steam, Epic, Xbox, GOG, Ubisoft, EA, Microsoft Store).
- After Playnite starts or someone signs in, the home starts on the Library card.
- Your own picture as an avatar: frame it in a circle first (move with the stick, zoom with the triggers).
- Fixed moves skipping an item when the same press arrived twice.
- Favorites and Recently played belong to whoever is signed in (the heart, the Favorites tab and the order of the row).
- New sounds made for Selene: a short glassy tick when moving, a bright ping on confirm, a soft high shimmer on back, a two-tone chime on the logo intro.
- First launch: the crescent crown intro, a setup check (install the add-ons Selene works with, set up save backups), then making your profile: name (on-screen keyboard), avatar, theme and background, each theme and background shown live on your real home.
- "Who's playing?" replaces the old login: profiles glow in their own theme's color; add, edit (Y) or remove (X twice); power menu below.
- Extensions menu, Selene: switch profile, edit profile, setup check.
- Every screen follows the color theme: text, focus rings, buttons, glass panels and the shading over game art.
- Your theme's wallpaper shows behind the home when no game is selected, and behind the login and menus.
- New crescent crown logo with a glowing full moon; new icons, default cover and README banner.
- Placeholders instead of blank screens when nothing is set up yet (login picture, home background).
- Groundwork for profiles, 23 color themes with their own wallpapers, 415 avatars and the first-launch setup check.
- The setup check installs the add-ons Selene works with and sets up save backups with Ludusavi.

## 0.9.0 - 2026-10-21

- First build of Selene, rebuilt from the ground up on Playnite's fullscreen template, with its companion add-on
  Selene Core.
