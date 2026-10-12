# Cirrus shape reference

`CirrusShapes-P65.png` contains the three grayscale images embedded in page 65 of
Andrew Schneider's *Nubis, Evolved* (SIGGRAPH 2022), supplied with this project at
`NubisEvolved技术复刻/source/NubisEvolved-2022.pdf`.

| Channel | PDF image | Shape | Embedded size |
| --- | --- | --- | --- |
| R | Im4 | cr_streaky | 633 × 632 |
| G | Im2 | cr_wispy | 630 × 634 |
| B | Im3 | cr_round | 627 × 629 |

The embedded images were converted to grayscale and independently resized to
512 × 512 with Pillow LANCZOS, then packed into RGB. Orientation and gray values
are retained; no contrast, gamma, normalization, or brightness enhancement was
applied. This is the presentation's embedded reference imagery, not the original
full-resolution production texture set.

`NubisCirrusAuthoring` decodes the PNG directly as linear channel data and stores
it in the existing RGBAHalf shape subasset with mipmaps, Repeat wrapping and
Trilinear filtering. The PNG's Unity importer settings do not affect this decode.
Seed and Frequency control the procedural Coverage/Type NDF only.

Use `Tools > Nubis Clouds > Restore P65 cirrus shapes` to replace only the shape
channels while retaining the weather field and authored influence. The Cirrus
NDF Editor's regenerate button also restores these shapes.
