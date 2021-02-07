ABRTool
A .ABR and .EFO texture (And .kmrs) extractor/importer for Arcade Stage 4/5/6/7/8/0 and Extreme Stage
Ver 0.3 ALPHA
By PockyWitch

How to:
Click the Load button, load your file. Export textures, import new ones. Save.
That's all there is to it. Make sure to examine the exported textures, so you know which DDS settings to use, or else it won't import it.

Changelog:
- Added "Mip Map Amount" to each entry, so you can see whether or not you need to export with them in Photoshop. (And how many)
- Importing/Exporting ABR and DDS files now store their last used folders, so you can easily work with two seperate folders without having to navigate to them each time.
- Show the currently loaded archive name in the title bar, so you don't lose track of which file you are editing
- Reworked the design of the app
- Made the surface type more readable
- Improved error messages when loading incompatible textures. It will now show you a comparison between old and new files in regards to Type and Mipmaps.

NOTES NOTES NOTES:
This is an alpha version. It will be buggy and unstable.
It might crash or freeze when loading archive after archive. Try to wait a few seconds before loading a new .abr/.efo file, so it can clear the old one from memory first.

Also, the DDS Surface Type Box is not as helpful as I would like, so I would recommend using nvidias old WTV tool to open the dds files, and look for the type in there because it's way more accurate.
(And you need to reimport the correct texture type)
If a texture won't import because of the file size, please check:
- That you are using the right DDS Surface Type
- That you are using mipmaps (and the correct amount) if the original one had some.
- That you are using alpha channel if the original texture had one. (Only really an issue with DXT1 files.)

Have fun.