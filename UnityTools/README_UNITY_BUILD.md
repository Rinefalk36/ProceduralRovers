# Procedural Rovers - Unity `.mu` & C# `.dll` Üretim Kılavuzu

## 1. C# Eklentisini (`ProceduralRovers.dll`) Derleme
1. `GameData/ProceduralRovers/Plugins/Source/ProceduralRovers.csproj` dosyasındaki `<KSPDir>` satırının bilgisayarınızdaki KSP 1.12.5 dizinini gösterdiğinden emin olun.
2. Terminal veya PowerShell üzerinden şu komutu çalıştırın:
   ```powershell
   dotnet build "GameData/ProceduralRovers/Plugins/Source/ProceduralRovers.csproj" -c Release
   ```
3. Derlenen `ProceduralRovers.dll` otomatik olarak `GameData/ProceduralRovers/Plugins/` klasörüne çıkarılır.

---

## 2. Unity 2019.4.18f1 Üzerinde Tek Tıkla `.mu` Modellerini Üretme
1. **Unity 2019.4.18f1** ile boş bir 3D proje açın ve KSP resmi **PartTools** paketini (`PartTools_AssetBundles_2019.4.18f1.unitypackage`) projeye aktarın.
2. `UnityTools/Editor/ProceduralRoversUnityBuilder.cs` dosyasını Unity projenizin `Assets/Editor/` klasörüne kopyalayın.
3. Unity üst menüsünden:
   - **`Tools > Procedural Rovers > 1. Configure KSP Layers (26 & 27)`** seçeneğine tıklayın (Layer 26 `wheelCollidersIgnore` ve Layer 27 `wheelColliders` otomatik tanımlanır).
   - **`Tools > Procedural Rovers > 2. Build All Part Hierarchies (Core, Wheels, Tracks, Payloads)`** seçeneğine tıklayın.
4. Sahnedeki `ProceduralRovers_Workspace` altında tüm 9 parçanın (`RoverCoreProcedural`, `WheelMeshSpring`, `WheelHeavyDuty`, `TrackContinuous`, `DrillRoboticArm`, `MastCamPanoramic`, `RoverMMRTG`, `RoverSolarDeck`, `SampleSpectrometer`) sıfır hatalı hiyerarşisi ve `Assets/ProceduralRovers/Prefabs/` altında prefab'ları oluşacaktır.
5. Her bir kök objenin üzerindeki **PartTools** bileşeninde **"Write"** butonuna basarak `.mu` dosyalarını doğrudan `GameData/ProceduralRovers/Parts/.../Assets/` klasörlerine aktarın.
