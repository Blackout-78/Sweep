# Sweep v0.2.0 "Test Run"

**Added**
- Games tab "Test Run": start the game, click Test Run, keep playing. Sweep measures frame times, finds stutter, tries one fix at a time and keeps only what clearly helps; anything that does not help is undone
- Reports the present mode (windowed or fullscreen) and the busiest apps during the test
- Raises the game's priority to High as a measured, undoable fix
- Bundles Intel PresentMon 2.5.1 (MIT license) for frame timing; see THIRD_PARTY_NOTICES.txt

**Notes**
- Test Run needs administrator rights: right-click Sweep.exe and choose Run as administrator
- GPU preference and fullscreen optimizations need a game restart, so they stay as Fix buttons on the scan
- Privacy, Cleaner and Tweaks tabs are still placeholders
