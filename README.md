# My Silly Script Collection for GOD EATER 2 (PSP and Vita)
# A bit buggy but what we get is what we get after all




### Sources:

~ BLZ2 + BLZ4 Codes by HaoJun: https://github.com/haoJun0823/GECV-OLD/

~ Extraction Codes Originally by SkyBladeCloud: https://gbatemp.net/members/skybladecloud.264289/

## Required Files for these Scripts (inside within the same directory of all the programs):
- package.rdp
- data.rdp
- system.res (for base game)
- patch.rdp (for dlc) [rename `patch.edat` to this]
- system_update.res (for dlc) [rename `system_update.edat` to this]

## Extraction Methods:
~ Base Game: (uses `package.rdp, data.rdp, system.res`)

~ DLC: (uses `package.rdp, data.rdp, patch.rdp, system_update.res`)
~ For DLC CSharp_Ver1: if you want to extract the game data related without the data.rdp. create a a file matching with the name and leave it empty

#### Base game files are required for extraction since the dlc uses a few important things in them.

#### Changelog
- Jan 05 2025 `->` added `RES_ARCHIVE` (old tool, simple extraction. just debugger stuff, written in C# Visual Studio 2017. yes that old and i only picked it up again on my old USB drive)
- Feb 08 2025 `->` added some random python scripts
- Feb 15 2025 `->` added some more python code 
- Feb 23 2025 `->` added+updated PRES_PROT (supports PSP/Vita **EXCEPT ENGLISH VERSIONS due to RES archive differences**). And moved archived stuff outside here so people can see it :)
- Feb 24 2025 `->` added Basic_Stuff with the sources too, this is my testing stages before PRES_PROT becomes public. Also added `RES_STRUCTURE` documentation in text. this is old as well lmao