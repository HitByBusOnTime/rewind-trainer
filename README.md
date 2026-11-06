# Rewind Trainer

An unofficial, offline rhythm game practice build with two extra mods. It is a modified version of the open-source osu!(lazer) client.

**This project is not made by, affiliated with or endorsed by ppy Pty Ltd or osu!.** "osu!" and "ppy" are trademarks of their owners, and are only used here to say what this software is based on. Please don't report problems with this build to the osu! team.

## The mods

Both are in the Difficulty Reduction column of the mod menu. Only one can be on at a time.

- **Rewind on Miss (RW):** a miss rewinds to half a second before it.
- **Rewind on Not SS (RS):** a 100, a 50, a miss or anything else that would cost an SS rewinds to half a second before it.

A rewind plays backwards, pauses briefly so you can place your cursor, then resumes with a flash. It never lands inside a slider or spinner, and the next object always gets some warning, so a rewind can go back further than half a second. In a non-stop stream it uses the biggest gap it can find, which can be short. You can't fail with these mods on.

The rewind distance, animation length and pause can be changed in each mod's settings.

## Install (Windows 64-bit)

1. Download `RewindTrainer-win-x64.zip` from Releases and unzip it anywhere.
2. Run `RewindTrainer.exe`. Windows may warn you because the program isn't signed.
3. If you have osu!(lazer) on this computer, your settings, key bindings, skins, beatmaps and scores are copied across the first time you open it. Close osu! first. To add more later, drag `.osz` beatmap files and `.osk` skin files onto the window.

Your data is kept in `%APPDATA%\rewind-trainer`. The first-time copy only reads your osu! folder and never changes it, your login isn't copied, and nothing leaves your computer. Beatmaps and skins are linked rather than duplicated, so they take almost no extra disk space. After that the two are separate: changes in one don't appear in the other.

## What this build leaves out

- **No online features.** It has no login, leaderboards, multiplayer, beatmap downloads, error reporting or updates, and it doesn't connect to the osu! servers. Scores are only saved on your computer. Parts of the interface still show these features, but they don't work.
- **No new versions automatically.** Download new versions from Releases yourself.
- **Different fonts.** Everything uses Inter, because the original Torus and Venera fonts need a commercial licence to distribute. The packaged build has them removed.

Some interface text and game resources still mention osu!.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet publish osu.Desktop -c Release -r win-x64 --self-contained true -o build
dotnet run --project Tools/StripCommercialFonts -- build/osu.Game.Resources.dll
```

The second command is required if you share the build, because it removes the commercially licensed fonts. The mods are in `osu.Game.Rulesets.Osu/Mods/OsuModRewind.cs`.

## Licences

This software is free and must stay non-commercial, because it includes the game resources below.

| Part | Licence | Where |
|---|---|---|
| Code, based on [ppy/osu](https://github.com/ppy/osu) and [ppy/osu-framework](https://github.com/ppy/osu-framework) | MIT, Copyright (c) ppy Pty Ltd | `LICENCE` |
| Game resources from [ppy/osu-resources](https://github.com/ppy/osu-resources), modified by removing fonts | CC BY-NC 4.0, Copyright (c) ppy Pty Ltd | `licences/osu-resources-CC-BY-NC-4.0.md` |
| Inter font | SIL Open Font License 1.1, Copyright 2016-2019 The Inter Project Authors | `licences/Inter-OFL.txt` |
| Noto font | Apache License 2.0 | `licences/Noto-LICENSE.txt` |

These changes were made to the original client: the two mods, online features turned off, a new name and logo, a changed intro and a font swap. The commit history has the details.

The software and resources come with no warranty, as described in their licences. Other libraries it uses are under their own licences, listed by their packages.
