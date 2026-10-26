Selene in your language
=======================

Drop a text file here named after Playnite's language setting: de_DE.txt, fr_FR.txt, pt_BR.txt... or just de.txt,
fr.txt to cover every variant. (A theme update replaces this folder: to keep yours, put it in Selene Core's data folder
instead, Playnite's ExtensionsData\cd424c29-97f4-4dd0-aa66-4d8e01cc8724\Localization, which is looked at first.) One phrase per line, the English on the left, yours on the right, separated by " = ":

    # German
    Who's playing? = Wer spielt?
    Find a game = Spiel suchen
    Type a game's name = Name des Spiels eingeben

Lines starting with # are notes. Phrases you leave out stay English. Numbers and names (play times, dates, game
titles) are never translated.

To get the full list of English phrases: in Selene settings > Advanced, turn on "Collect words to translate", then
walk through the screens. Every phrase shown is written to "Selene phrases.txt" on your Desktop, once each, with " = "
ready after it. Fill in the right side, save it here under your language's name (id_ID.txt, fr.txt...), restart
Playnite, and turn the collecting off again. Selene's log (%TEMP%\selene.log, or Advanced > Copy log) says which file it
looked for and which it loaded.

The switch Advanced > "Selene in Playnite's language" turns this on and off. Playnite's own words (its settings, its
dialogs) follow Playnite's language on their own.
