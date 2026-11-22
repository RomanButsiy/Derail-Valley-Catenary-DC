# Derail Valley Catenary DC

A DC electrification mod for **Derail Valley**, based on Knsgf’s original work.
It adds overhead wires and powered side rails for compatible electric locomotives.

The expanded network includes **HB–CS–CMS–SM**, **HB–GF towards the mines**, and
**GF station with its connections towards SM and FF**, **CP approaches**, and
**IMW–MF**, including MF passenger, industrial and military I/O tracks. City West includes the passenger yard, yard C and the
A2/A3 service tracks, with neutral sections towards CP and SM. The FRC route
connects the Farm, the CP triangle and OR. At FRC, B1L has a signed wire gap
at the water tower, and the third through track has continuous 1500 V overhead.

[**Download from Releases**](https://github.com/RomanButsiy/Derail-Valley-Catenary-DC/releases)

## Installation

Use Unity Mod Manager 0.27.3 or later. Close the game and move the original
`Catenary-DC` folder and any previous copy of the vehicle bridge outside `Mods`.
Install **Derail-Valley-Catenary-DC-0.3.0.zip** from the release assets.
The core alone provides the network.

## Electric locomotives

Install **Derail-Valley-Catenary-DC-2WE3-Bridge-0.3.0.zip** from the same
release for either locomotive. Both use Custom Car Loader and its prerequisites.

- **2WE3** — choose one model archive from Knsgf's v2.8.0. Use
  `2WE3CCL.zip` (2.3.9) for sandbox or `2WE3CCL-licence.zip` (2.4.0)
  with DV Custom Licenses for career. [2WE3 v2.8.0](https://github.com/Knsgf/Derail_Valley_electric_concept/releases/tag/v2.8.0)
- **WE-4U** — install both `EU07` and `EU07Electrics` from InDev-0.3.1,
  DV Custom Licenses, and the player package `CatenaryForCCL.zip`.
  Its own electrics drive the locomotive. The bridge supplies overhead contact
  even when no 2WE3 models are installed.
  [WE-4U InDev-0.3.1](https://github.com/Comrad501/Dv-WE-4U/releases/tag/InDev-0.3.1) · [Custom Car Loader Catenary Support](https://github.com/Knsgf/CCL-OCS/releases/tag/v1.1.0)

[Інструкція встановлення українською](docs/INSTALL_RELEASE_UK.md)

## Compatibility

- Designed for the **standard Derail Valley map**.
- **Double-Tracked Valley is not supported.**
- Do not run the original author’s Catenary-DC alongside this mod and its bridge.
- VR and multiplayer have not been tested.

## Credits

Original electrification system, models and 2WE3 implementation by
[**Knsgf**](https://github.com/Knsgf/Derail_Valley_electric_concept).
Network expansion and this fork maintained by **RomanButsiy**.

See [NOTICE](NOTICE.md), [CC0](LICENSE_CC0.txt) and [MIT](LICENSE_MIT.txt).
