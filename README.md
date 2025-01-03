# [as of JAN 01, 2025. this repo will be changing constantly here and there over time because i tend to change tons of stuff that is either unecessary and necessary]


# GE2Extra with Python Version

### GOD EATER 2 [NPJH50832] Extraction Tool (No Repack)

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
**1.0**
- Release

**1.1**
- Added Offshot Support for extraction (really buggy but some tr2 files are readable)
- Added BLZ4

Jan 03 2025 - updated docs (with a bit of vita)