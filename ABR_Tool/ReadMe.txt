Arcade Stage Texture Tool
A texture archive extractor/importer for Arcade Stage 4/5/6/7/8/0 and Extreme Stage
Ver 1.0
By PockyWitch

How to:
Click the Load button, load your file. Export textures, import new ones. Save.
That's all there is to it. Make sure to examine the exported textures, so you know which DDS settings to use, or else it won't import it.

Changelog:
- Merged the old ABRTool and PacTool into this texture tool to make updating both easier. They're now using the same codebase.
- Added HQ image filtering to the texture previews.
- Added progress indicator to taskbar icon while loading, saving and importing/exporting textures and archives.
- Updated the checkerboard bg texture

NOTES NOTES NOTES:
Also, the DDS Surface Type Box is not as helpful as I would like, so I would recommend using nvidias old WTV tool to open the dds files, and look for the type in there because it's way more accurate.
(And you need to reimport the correct texture type)
If a texture won't import because of the file size, please check:
- That you are using the right DDS Surface Type
- That you are using mipmaps (and the correct amount) if the original one had some.
- That you are using alpha channel if the original texture had one. (Only really an issue with DXT1 files.)

Have fun.