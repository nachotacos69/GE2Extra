# [as of JAN 01, 2025. this repo will be changing constantly here and there over time because i tend to do tons of stuff that is either unecessary and necessary, and archiving stuff that i made in the past]


# GE2Extra with Python Version

### GOD EATER 2 [NPJH50832] Extraction Tool (No Repack)
(older version are stored in `archives`)
~ Supports: 1.30 - 1.40 Versions (atleast on my testings with bugs/errors along the way).

~ This tool is mostly represents extracting many files as possible within the given input, though there's some errors like a 'Stream' error but it'll do for now atleast. And there will be No Repack support due to complexity.

~ I made this for fun and feel free to modify the code for your own desires.


### Sources:

~ BLZ2 + BLZ4 Codes by HaoJun: https://github.com/haoJun0823/GECV-OLD/

~ Extraction Codes Originally by SkyBladeCloud (original codes in `src`): https://gbatemp.net/members/skybladecloud.264289/

## Required Files (same directory as the executable):
- package.rdp
- data.rdp
- system.res (for base game)
- patch.rdp (for dlc) [rename `patch.edat` to this]
- system_update.res (for dlc) [rename `system_update.edat` to this]

## Extraction Methods:
~ Base Game: (uses `package.rdp, data.rdp, system.res`)

~ DLC: (uses `package.rdp, data.rdp, patch.rdp, system_update.res`)
~ For DLC CSharpVersion: if you want to extract the game data related without the data.rdp. create a a file matching with the name and leave it empty

#### Base game files are required for extraction since the dlc uses a few important things in them.

#### Changelog
- Jan 05 2025 `->` added `RES_ARCHIVE` (old tool, simple extraction. just debugger stuff, written in C# Visual Studio 2017. yes that old and i only picked it up again on my old USB drive)
- Feb 08 2025 `->` added some random python scripts
- Feb 15 2025 `->` added some more python code 
- Feb 22 2025 `-` added+updated PRES_PROT (supports PSP/Vita **EXCEPT ENGLISH VERSIONS**) 