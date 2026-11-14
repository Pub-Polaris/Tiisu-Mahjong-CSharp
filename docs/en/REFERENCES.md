# Technical References

This document records external technical research done during development and content explicitly **not used**, so source questions can be answered.

## Alice in Cradle

- Scope: spatial-audio architecture ideas (in-engine middleware).
- Not used: no code, middleware, assets, or derivatives from that game.
- This project's audio path is independently implemented and has no dependency on that middleware.

## Star Mahjong 2002 (明星三缺一 2002)

- Scope: generic envelope/duration/density feature analysis of reference audio with FFmpeg.
- Not used: no original audio embedded, sampled, transcribed, or redistributed; no original code or art copied.
- This project's SFX are synthesized independently by `tools/Tiisu.AudioGen`; the result sounds different.

## Principles

- Borrow only **functional features not protected by copyright** (rules, duration, density, envelope shape).
- Do not copy protected expression (code, audio, art, UI, characters, logos).
- This project is not affiliated with or endorsed by the original authors.
- Product names and trademarks belong to their owners; this project's name and promotion do not use those game names.

> This document explains technical provenance and is not legal advice. Re-check for the target region and EULA before release.

## Voice callouts

- Voice files are not shipped with the installer; at runtime Windows SAPI generates them locally under `%LOCALAPPDATA%\TiisuMahjong\voices`.
- No third-party recordings are used or embedded; output is subject to the local Windows license.

## Fonts

- Uses a variable-font subset of WenYuan Rounded v1.110, renamed to `Tiisu Rounded SC` per the OFL reserved-name requirement.
- Glyphs are not modified; only subsetting and family renaming. License at `Assets/fonts/LICENSE-WenYuan.md`.
