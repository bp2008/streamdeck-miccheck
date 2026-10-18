# Elgato Marketplace listing

Text and files to paste or upload in Maker Console (maker.elgato.com → Create product → Stream Deck plugin).

## Files
- **Product file:** `dist\com.bp2008.miccheck.streamDeckPlugin` (from `build.ps1 -Pack`)
- **Thumbnail:** `dist\marketplace\thumbnail.png` (1920 × 960)
- **Gallery:** `dist\marketplace\gallery-1.png`, `gallery-2.png`, `gallery-3.png` (1920 × 960)
- **App icon:** `dist\marketplace\plugin@2x.png` (512 × 512) or `icon-288.png` (288 × 288), whichever size the form asks for

## Name
Mic Check

## Description
Can people actually hear you? Mic Check puts a live microphone level meter on a Stream Deck key, so one glance tells you whether your mic is working before someone has to say "you're on mute".

It follows the mic your call is really using. When Microsoft Teams, Discord, Zoom, or any other app starts recording, the key shows that app's name, a scrolling level meter, and the name of the mic it picked, so you'll notice if it's using the wrong one.

It also catches the problems that leave you silent without any warning:
- NO SIGNAL: the app is recording pure silence, usually from a headset mute switch, an unplugged mic, or a Windows privacy block
- MUTED: the mic is muted in Windows
- Low input level: the Windows input level is turned down below 20%
- Different mic: your call app is recording from a different mic than the one you chose

When nothing is recording, press the key for a quick mic test. Settings let you follow any mic automatically, stick to the Windows default communications mic, or watch one specific mic, and optionally keep the meter live all the time.

Works with Microsoft Teams, Discord, Zoom, Slack, OBS, web browsers, and any other Windows app that uses a microphone. Windows 10 and 11.

Privacy: everything runs on your PC. Mic Check never records, stores or sends audio, and makes no internet connections.

Open source (MIT License), © 2026 bp2008: https://github.com/bp2008/streamdeck-miccheck

## Tags
microphone, mic, audio, meter, level, mute, Teams, Discord, Zoom, meetings, calls, productivity

## Price
Free

## Additional links
- Source code and support: https://github.com/bp2008/streamdeck-miccheck
- Report a problem: https://github.com/bp2008/streamdeck-miccheck/issues

## Release notes (1.0.0)
First release: live mic meter that follows your call app's mic, with warnings for no signal, Windows mute, low input level and the wrong mic, plus press-to-test and an always-listen option.
