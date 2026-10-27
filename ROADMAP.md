# How Selene is developed

Selene is a one-person project. Suggestions are welcome and many of them shape it, but it can't do everything everyone asks:
a project that says yes to every request never ships, and gets harder to keep stable with each addition. So work happens in
releases with a short, fixed list.

## Releases come first

- **Each release has a short list**, three to five things. The list is written down before the work starts (see "Next release" below).
- **A new request goes on a later list**, not into the release in progress. It is read, answered and labelled, then it waits.
- **Bugs come before features.** Something that is broken is fixed first; it costs users more than a missing feature does.
- **A release ships when its list is done**, not when the ideas run out. Then the next list is written.

## What "done" means

An item is done when:

1. it works in a fresh Playnite with the packaged theme and add-on (`tools/fresh-test.ps1`), not only on the author's own setup;
2. it plays by the same rules as the rest of Selene: a switch to turn it off, motion and sound feedback, theme colors from the palette;
3. it has a line in `CHANGELOG.md` under "Unreleased", written for the people using Selene.

A release is done when every item on its list is done, the theme and add-on build with `tools\package.ps1`, and nothing known to be
broken is left on the list. It is then published as GitHub release `v<version>` (see [Building](docs/guide/building.md), "Releasing").

## How a request is decided

Each request is asked four things:

1. **Is it a bug?** Broken things come first.
2. **Does it fit Selene?** Selene is a console-style front end for Playnite. Things that make that better fit; things that turn it into
   something else don't.
3. **Is it worth its size?** A day of work for one person's wish is a poor trade; an hour for something several people hit is a good one.
4. **Will it be maintained?** Every feature is code that has to keep working. Each one gets a switch, and each one is a cost.

The answer is one of:

- **Planned**: it is on the next release's list.
- **Later**: a good idea that isn't on a list yet. It stays open and is looked at when the next list is written.
- **Not planned**: a fair request that won't be built, with the reason given. The issue is closed with the **not planned** label.
  This is a normal answer, not a rejection of the person. Pull requests for these are welcome if they meet "done" above.

## This release (1.3)

Done and waiting to ship:

- News card and News screen with your own feeds (#14)
- Metadata source per game (Steam or IGDB, or an exact ID) for the screenshots of older and emulated games
- Language choice at first setup and in settings, with starter translations
- B on the loading screen cancels the launch and closes the game
- Keyboard: Tab and V work like Start and View, with holds
- Background sizing (Fill, Fit, Stretch) and Focus on Guide (a switch for other overlays)
- Smarter cover crop and logos cut to their lettering

## Next release (1.4)

Not chosen yet. Three to five items go here, taken from the open [issues](https://github.com/mattxslv/Selene/issues), bugs first.

## Not planned

Nothing yet. Requests that are closed as not planned are listed under the
[`not planned`](https://github.com/mattxslv/Selene/issues?q=label%3A%22not+planned%22) label.
