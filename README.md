# Procedural Rovers (KSP 1.12.5)

**Procedural Rovers** is a modular, procedural rover part pack and C# plugin for **Kerbal Space Program 1.12.5** featuring real-time chassis switching, dynamic attach-node shifting, stock-alike procedural textures, Rocker-Bogie differential suspension, continuous tracks, and an EVA-deployable foldable Apollo LRV crate.

---

## Features

- **Procedural Core (`PR-C1`):**
  - Real-time **Length, Width, and Height** scaling in VAB/SPH with automatic `AttachNode` and surface-attached (`srfAttach`) child repositioning.
  - **5 Chassis Archetypes** with distinct anatomy, mass, battery capacity, reaction torque, SAS level, and thermal limits:
    1. *Micro (Sojourner)*
    2. *Heavy Science (Curiosity / Perseverance)*
    3. *Tub Hull (Lunokhod)*
    4. *Foldable Frame (Apollo LRV)*
    5. *Armored Explorer (Sci-Fi)*
- **Foldable Apollo LRV Crate & EVA Deployment:**
  - Pack the entire **Foldable Frame (Apollo LRV)** rover and all attached wheels/instruments into a compact **25L / 35 kg** cargo crate in VAB/SPH.
  - Store it inside a Kerbal's personal inventory or cargo bay, then deploy and unfold it on the surface with an **Engineer Kerbal**.
- **6-Wheel Rocker-Bogie Kinematics:**
  - Real-time visual `Rocker_L/R`, `Bogie_L/R`, and `DifferentialBar` linkages plus physical bogie load equalization.
- **Mobility (`Ground`):**
  - `PR-W10 "Nitinol-Mesh"` Spring/Mesh Wheel
  - `PR-W50 "TitanGrip"` Heavy-Duty Alloy/Rubber Wheel
  - `PR-T90 "FoundryTrack"` Continuous Track with UV belt scrolling, synchronized road wheels, and low-speed differential pivot steering
  - 5 surface traction presets (*Standard*, *Low Gravity*, *High Slope Regolith*, *Ice Surface*, *Heavy Load*)
- **Modular Science & Power Payloads:**
  - `PR-D20 "TurretDrill"` Robotic Arm Drill & Core Sampler
  - `PR-M10 "SuperCam"` Panoramic MastCam (360° Pan/Tilt + Auto Scan + KerbNet)
  - `PR-G100` MMRTG Nuclear Generator
  - `PR-S40` Clamshell Solar Deck
  - `PR-X30 "APXS-Vault"` Spectrometer & Sample Container

---

## Installation

1. Download or clone this repository.
2. Copy the **`GameData/ProceduralRovers`** folder into your `<Kerbal Space Program>/GameData/` directory.
3. *(Recommended)* Install **ModuleManager** for Community Tech Tree, Breaking Ground, and compatibility patches.

---

## Building from Source

- Target framework: `.NET Framework 4.7.2`
- Update `<KSPDir>` in `GameData/ProceduralRovers/Plugins/Source/ProceduralRovers.csproj` to point to your KSP 1.12.5 installation, then run:
  ```powershell
  dotnet build "GameData/ProceduralRovers/Plugins/Source/ProceduralRovers.csproj" -c Release
  ```
- Unity 2019.4.x model/texture generator scripts are included under `UnityTools/Editor/`.

---

## Acknowledgments
- Developed with AI assistance from **Google Gemini**.

## License

Licensed under the [GPL3 License](LICENSE).
