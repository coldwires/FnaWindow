# Font attribution

The four atlases in this folder (`fixedsys_12`, `sserife_11`, `sserife_11_bold`, `sserife_13_bold`)
are baked from the **Liberation** fonts:

- `fixedsys_12` - Liberation Mono, forced to an 8px advance so it fits the 8x15 editor cell
- `sserife_11`, `sserife_11_bold`, `sserife_13_bold` - Liberation Sans, regular and bold

Copyright notice, verbatim from the Liberation project:

> Digitized data copyright (c) 2010 Google Corporation with Reserved Font Arimo, Tinos and Cousine.
> Copyright (c) 2012 Red Hat, Inc. with Reserved Font Name Liberation.

This Font Software is licensed under the **SIL Open Font License, Version 1.1**.
Full text: https://openfontlicense.org  -  Project: https://github.com/liberationfonts/liberation-fonts

Two conditions this repository has to keep meeting:

1. The OFL requires its text to travel with the software, which is why `OFL.txt` sits beside these
   atlases. Keep it there, and keep it in anything that redistributes them.
2. "Liberation" is a Reserved Font Name, so nothing derived from it may be named Liberation. The
   atlases above are named for their role, not for the typeface, which satisfies this.

## Why these and not the Windows fonts

These atlases are only a **fallback**. On Windows an app normally overrides
`WindowGame.UseSystemFonts` and the engine rasterises the machine's own MS Sans Serif, Courier and
friends at startup - the genuine Windows 3.1 faces this look imitates. Nothing is redistributed that
way, because the font belongs to the user who already has it.

The fallback exists for the cases that path cannot cover: a machine missing one of those faces, and
any non-Windows platform.

Earlier versions of these atlases were baked from **Courier New and Tahoma**, which are
Microsoft/Monotype typefaces and were never ours to redistribute - this repository is public and MIT
licensed, which tells everyone downstream that they may redistribute it too. Do not bake a
proprietary face into an atlas that ships here. If you need a different fallback, use a font whose
licence permits redistribution, and record it in this file.
