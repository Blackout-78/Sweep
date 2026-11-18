# Sweep

Windows privacy, cleaning, tweaks and game-performance tool. Offline and private: the app never connects to the internet. Installed only from GitHub Releases.

Tabs: Privacy, Cleaner, Tweaks (coming), Games (scan + fixes with undo), Settings.

Built with C# .NET 8 and Avalonia.

## Games tab

- **Scan**: reads Windows settings that cause lag, jitter and tearing, with Safe/Caution fixes and Undo all changes.
- **Test Run**: start the game first, then click Test Run. It measures frame times with the bundled Intel PresentMon (MIT, see THIRD_PARTY_NOTICES.txt), tries one fix at a time and undoes any that do not help. Needs "Run as administrator".
