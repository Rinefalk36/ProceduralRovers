using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ProceduralRovers
{
    /// <summary>
    /// Katlanmış LRV paketi içindeki her bir alt parçanın (tekerlek, MastCam, güneş paneli, matkap vb.)
    /// konum, rotasyon, bağlantı noktası ve prosedürel ölçek bilgilerini tutan veri yapısı.
    /// </summary>
    [Serializable]
    public class PackedChildPartEntry
    {
        public string partName;
        public int parentIndex; // 0 = RoverCore (this.part), 1..N = diğer alt parçalar
        public int attachMode;  // 0 = STACK, 1 = SRF_ATTACH
        public string parentNodeId;
        public string childNodeId;
        public Vector3 localPos;
        public Quaternion localRot;
        public Vector3 attPos0;
        public Quaternion attRot0;

        // Prosedürel tekerlek / ekipman özel parametreleri
        public float customScale = 1.0f;
        public int tractionPreset = 1;
        public float springMult = 1.0f;
        public float damperMult = 1.0f;
        public float turretYaw = 0f;
        public float turretPitch = 0f;
        public bool mechanismDeployed = false;
    }

    /// <summary>
    /// Apollo LRV (Foldable Frame - chassisIndex == 3) için Özel Katlanabilir Kutu / Çanta (Crate) Modülü.
    /// - VAB/SPH'de yalnızca "Foldable Frame (Apollo LRV)" seçiliyken sağ tık menüsünde (PAW) aktif olur.
    /// - Rover üzerine takılan tüm tekerlek, kamera, güneş paneli ve bilim modüllerini tek tuşla
    ///   kompakt bir taşıma sandığına (Crate) katlar ve şemayı (Blueprint) kaydeder.
    /// - Katlanmış sandık (25L, 35kg) doğrudan bir Kerbal'ın kişisel envanterine (veya kargo slotuna) konulabilir.
    /// - Ay/Gezegen yüzeyinde Mühendis (Engineer) bir Kerbal sandığı envanterinden yere koyup sağ tıklayarak
    ///   "LRV'yi Aç ve Yüzeye Kur" tuşuna bastığında sandık kapakları açılır ve tüm tekerlek/modüller
    ///   orijinal yerlerine KSP'nin yerel OnAttachFlight sistemiyle monte edilerek sürüşe hazır hale gelir.
    /// </summary>
    public class ProceduralFoldableLRV : PartModule
    {
        private const int APOLLO_LRV_CHASSIS_INDEX = 3;

        #region KSPFields (Durum ve PAW Kontrolleri)

        [KSPField(isPersistant = true)]
        public bool isFoldedCrate = false;

        [KSPField(isPersistant = true)]
        public string packedBlueprint = "";

        [KSPField(isPersistant = true)]
        public float savedLengthScale = 1.0f;

        [KSPField(isPersistant = true)]
        public float savedWidthScale = 1.0f;

        [KSPField(isPersistant = true)]
        public float savedHeightScale = 1.0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "LRV Paket Durumu")]
        public string crateStatusDisplay = "Açık Şasi";

        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = true, guiName = "Mühendis Şartı"),
         UI_Toggle(disabledText = "Kapalı (Herkes)", enabledText = "Aktif (Mühendis)")]
        public bool requireEngineerOnEva = true;

        #endregion

        #region Dahili Değişkenler ve 3B Sandık (Crate) Referansları

        private ProceduralRoverCore _roverCore;
        private BoxCollider _rootCrateCollider;
        private GameObject _crateVisualRoot;
        private Transform _flapFront;
        private Transform _flapRear;
        private Transform _flapLeft;
        private Transform _flapRight;

        private static Material _sharedCrateOliveMat;
        private static Material _sharedCrateGoldMat;
        private static Material _sharedCrateDarkMat;
        private static Material _sharedCrateSilverMat;

        private bool _isUnfoldingInFlight = false;
        private float _unfoldTimer = 0f;
        private const float UNFOLD_DURATION = 1.6f;

        private static string BlueprintFilePath =>
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "ProceduralRovers", "PluginData", "FoldedLRV_Blueprint.txt");

        #endregion

        #region Yaşam Döngüsü (OnStart, OnPartCreatedFomInventory, Update)

        public override void OnAwake()
        {
            base.OnAwake();
            EnsureRootPhysicsCollider();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _roverCore = part.FindModuleImplementing<ProceduralRoverCore>();
            EnsureRootPhysicsCollider();
            EnsureCrateVisualHierarchy();

            // Eğer parça uçuşta bir Kerbal'ın envanterinden yere yeni çıkarılmışsa (ve tek parçaysa)
            // ve kayıtlı bir LRV şeması varsa otomatik olarak Katlanmış Kutu (Crate) modunda başlat!
            if (HighLogic.LoadedSceneIsFlight && part.children.Count == 0)
            {
                if (isFoldedCrate || (_roverCore != null && _roverCore.chassisIndex == APOLLO_LRV_CHASSIS_INDEX))
                {
                    if (string.IsNullOrEmpty(packedBlueprint) && File.Exists(BlueprintFilePath))
                    {
                        packedBlueprint = File.ReadAllText(BlueprintFilePath).Trim();
                    }
                    if (!string.IsNullOrEmpty(packedBlueprint))
                    {
                        isFoldedCrate = true;
                    }
                }
            }

            ApplyFoldedCrateState(isFoldedCrate);
            RefreshPAWVisibility();
        }

        public override void OnPartCreatedFomInventory(ModuleInventoryPart moduleInventoryPart)
        {
            base.OnPartCreatedFomInventory(moduleInventoryPart);

            // Kerbal envanterinden çıkarıldığında kayıtlı LRV şemasını yükle ve Kutu moduna geç
            if (string.IsNullOrEmpty(packedBlueprint) && File.Exists(BlueprintFilePath))
            {
                packedBlueprint = File.ReadAllText(BlueprintFilePath).Trim();
            }

            if (_roverCore != null)
            {
                _roverCore.chassisIndex = APOLLO_LRV_CHASSIS_INDEX;
            }

            isFoldedCrate = true;
            EnsureCrateVisualHierarchy();
            ApplyFoldedCrateState(true);
            RefreshPAWVisibility();
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsFlight && _isUnfoldingInFlight)
            {
                UpdateFlightUnfoldAnimation(Time.deltaTime);
            }
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || part == null || part.rb == null)
                return;

            // Katlanmış kutu modundayken EVA'da yere bırakıldığında zeminin içinden geçmesini (tunneling)
            // kesin olarak önlemek için ContinuousDynamic çarpışma algılamasını ve root BoxCollider'ı aktif tut.
            if (isFoldedCrate)
            {
                EnsureRootPhysicsCollider();
                if (_rootCrateCollider != null)
                {
                    _rootCrateCollider.enabled = true;
                    _rootCrateCollider.isTrigger = false;
                }
                if (part.rb != null)
                {
                    part.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                }
            }
        }

        private void EnsureRootPhysicsCollider()
        {
            if (part == null || part.gameObject == null)
                return;

            if (_rootCrateCollider == null)
            {
                _rootCrateCollider = part.gameObject.GetComponent<BoxCollider>();
                if (_rootCrateCollider == null)
                {
                    _rootCrateCollider = part.gameObject.AddComponent<BoxCollider>();
                }
            }

            _rootCrateCollider.isTrigger = false;
            _rootCrateCollider.enabled = true;
            if (isFoldedCrate)
            {
                _rootCrateCollider.size = new Vector3(0.62f, 0.38f, 0.72f);
                _rootCrateCollider.center = Vector3.zero;
            }
        }

        public void OnChassisChanged(int newChassisIndex)
        {
            // Eğer oyuncu VAB'da kutu modundayken başka şasiye geçerse kutu modundan çık
            if (newChassisIndex != APOLLO_LRV_CHASSIS_INDEX && isFoldedCrate && HighLogic.LoadedSceneIsEditor)
            {
                isFoldedCrate = false;
                ApplyFoldedCrateState(false);
            }

            RefreshPAWVisibility();
        }

        #endregion

        #region PAW (Sağ Tık Menüsü) Tuşları ve Görünürlük Yönetimi

        [KSPEvent(guiActiveEditor = true, guiActive = false, guiName = "📦 LRV'yi Kutu Olarak Katla (Envanter Paketi)")]
        public void PackRoverIntoCrateEvent()
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            if (_roverCore != null && _roverCore.chassisIndex != APOLLO_LRV_CHASSIS_INDEX)
            {
                ScreenMessages.PostScreenMessage("⚠️ Kutu olarak katlama özelliği yalnızca 'Foldable Frame (Apollo LRV)' şasisinde kullanılabilir!", 4f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // 1. Mevcut ölçekleri ve bağlı tüm alt parçaları (Tekerlekler, MastCam, Güneş Paneli vb.) şemaya kaydet
            if (_roverCore != null)
            {
                savedLengthScale = _roverCore.lengthScale;
                savedWidthScale = _roverCore.widthScale;
                savedHeightScale = _roverCore.heightScale;
            }

            List<PackedChildPartEntry> entries = CaptureAttachedSubtree();
            packedBlueprint = SerializeBlueprint(entries, savedWidthScale, savedHeightScale, savedLengthScale);
            SaveBlueprintToDisk(packedBlueprint);

            // 2. Bağlı alt parçaları VAB sahnesinden temizle (böylece tek parça haline gelip Kerbal envanterine sürüklenebilir)
            RemoveChildPartsInEditor();

            // 3. Görseli Katlanmış Apollo LRV Sandığına (Crate) çevir
            isFoldedCrate = true;
            ApplyFoldedCrateState(true);
            RefreshPAWVisibility();

            ScreenMessages.PostScreenMessage(
                $"📦 Apollo LRV ve bağlı {entries.Count} ekipman taşıma sandığına katlandı!\n" +
                "Artık bu kutuyu bir Kerbal'ın envanter slotuna sürükleyebilir veya 'Envantere Otomatik Koy' tuşuna basabilirsiniz.",
                6f, ScreenMessageStyle.UPPER_CENTER);

            if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
            {
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
        }

        [KSPEvent(guiActiveEditor = true, guiActive = false, guiName = "🛠️ LRV Kutusunu Geri Aç (Düzenle)")]
        public void UnpackRoverInEditorEvent()
        {
            if (!HighLogic.LoadedSceneIsEditor || !isFoldedCrate)
                return;

            if (string.IsNullOrEmpty(packedBlueprint) && File.Exists(BlueprintFilePath))
            {
                packedBlueprint = File.ReadAllText(BlueprintFilePath).Trim();
            }

            List<PackedChildPartEntry> entries = DeserializeBlueprint(packedBlueprint, out float wScale, out float hScale, out float lScale);

            isFoldedCrate = false;
            if (_roverCore != null)
            {
                _roverCore.chassisIndex = APOLLO_LRV_CHASSIS_INDEX;
                _roverCore.widthScale = wScale;
                _roverCore.heightScale = hScale;
                _roverCore.lengthScale = lScale;
            }

            ApplyFoldedCrateState(false);

            // Bağlı alt parçaları VAB/SPH içerisinde orijinal yuvalarına geri oluştur
            int restoredCount = RespawnChildrenInEditor(entries);
            RefreshPAWVisibility();

            ScreenMessages.PostScreenMessage($"🛠️ Apollo LRV kutudan çıkarıldı ({restoredCount} ekipman geri yüklendi).", 4f, ScreenMessageStyle.UPPER_CENTER);

            if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
            {
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
        }

        [KSPEvent(guiActiveEditor = true, guiActive = false, guiName = "🎒 Gemideki Kerbal / Kargo Envanterine Koy")]
        public void StoreInShipInventoryEvent()
        {
            if (!HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null || EditorLogic.fetch.ship == null)
                return;

            if (!isFoldedCrate)
            {
                PackRoverIntoCrateEvent();
            }

            // Gemideki (kendimiz hariç) tüm ModuleInventoryPart bileşenlerini tara
            ModuleInventoryPart targetInv = null;
            int emptySlot = -1;

            foreach (Part p in EditorLogic.fetch.ship.parts)
            {
                if (p == null || p == this.part)
                    continue;

                List<ModuleInventoryPart> invModules = p.FindModulesImplementing<ModuleInventoryPart>();
                for (int i = 0; i < invModules.Count; i++)
                {
                    ModuleInventoryPart inv = invModules[i];
                    if (inv != null && inv.TotalEmptySlots() > 0)
                    {
                        int slot = inv.FirstEmptySlot();
                        if (slot >= 0)
                        {
                            targetInv = inv;
                            emptySlot = slot;
                            break;
                        }
                    }
                }
                if (targetInv != null)
                    break;
            }

            if (targetInv == null || emptySlot < 0)
            {
                ScreenMessages.PostScreenMessage(
                    "ℹ️ Gemide başka bir Kapsül / Kargo Envanteri bulunamadı.\n" +
                    "LRV şemanız kaydedildi! Bir kapsül/roket ekledikten sonra bu kutuyu (veya sol listeden PR-C1'i) doğrudan Kerbal envanter slotuna sürükleyebilirsiniz.",
                    6f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Kutuyu hedef envanter slotuna yerleştir
            targetInv.StoreCargoPartAtSlot(this.part, emptySlot);
            ScreenMessages.PostScreenMessage($"🎒 Katlanmış Apollo LRV, '{targetInv.part.partInfo.title}' envanterinin {emptySlot + 1}. slotuna yerleştirildi!", 5f, ScreenMessageStyle.UPPER_CENTER);

            // Eğer bu parça geminin kök parçası (RootPart) değilse sahneden sil
            if (EditorLogic.RootPart != this.part)
            {
                if (part.parent != null)
                {
                    AttachNode pNode = part.parent.FindAttachNodeByPart(part);
                    if (pNode != null) pNode.attachedPart = null;
                    part.parent.children.Remove(part);
                }
                EditorLogic.fetch.ship.Remove(part);
                Destroy(part.gameObject);
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
        }

        [KSPEvent(guiActive = true, guiActiveUnfocused = true, externalToEVAOnly = false, unfocusedRange = 6.0f,
                  guiName = "🛠️ LRV'yi Aç ve Yüzeye Kur (Mühendis)")]
        public void UnfoldRoverOnSurfaceEvent()
        {
            if (!HighLogic.LoadedSceneIsFlight || !isFoldedCrate || _isUnfoldingInFlight)
                return;

            // Mühendis (Engineer) Kerbal kontrolü
            if (requireEngineerOnEva && !IsEngineerKerbalNearby(out string kerbalMessage))
            {
                ScreenMessages.PostScreenMessage(kerbalMessage, 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (string.IsNullOrEmpty(packedBlueprint) && File.Exists(BlueprintFilePath))
            {
                packedBlueprint = File.ReadAllText(BlueprintFilePath).Trim();
            }

            // Eğer oyuncu hiç paketleme yapmadan doğrudan PR-C1'i envantere koymuşsa varsayılan 4 tekerlekli Apollo LRV şemasını kullan!
            if (string.IsNullOrEmpty(packedBlueprint))
            {
                packedBlueprint = GetDefaultApolloLrvBlueprint();
            }

            // 1. Tekerleklerin yerin altına girmemesi için sandığı yüzey normali boyunca 0.50m yukarı kaldır ve düzleştir
            LiftAndLevelCrateAboveTerrain(0.50f);

            // 2. Kutu kapaklarının açılma animasyonunu başlat
            _isUnfoldingInFlight = true;
            _unfoldTimer = 0f;
            ScreenMessages.PostScreenMessage("🔧 Mühendis LRV taşıma sandığı kilitlerini açıyor... Şasi ve tekerlekler kuruluyor...", 3.5f, ScreenMessageStyle.UPPER_CENTER);
        }

        [KSPEvent(guiActive = true, guiActiveUnfocused = true, externalToEVAOnly = false, unfocusedRange = 6.0f,
                  guiName = "📦 LRV'yi Tekrar Kutuya Katla (Mühendis)")]
        public void FoldRoverOnSurfaceEvent()
        {
            if (!HighLogic.LoadedSceneIsFlight || isFoldedCrate || _isUnfoldingInFlight)
                return;

            if (_roverCore == null || _roverCore.chassisIndex != APOLLO_LRV_CHASSIS_INDEX)
                return;

            if (requireEngineerOnEva && !IsEngineerKerbalNearby(out string kerbalMessage))
            {
                ScreenMessages.PostScreenMessage(kerbalMessage, 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Mevcut bağlı parçaları şemaya kaydet
            savedLengthScale = _roverCore.lengthScale;
            savedWidthScale = _roverCore.widthScale;
            savedHeightScale = _roverCore.heightScale;

            List<PackedChildPartEntry> entries = CaptureAttachedSubtree();
            if (entries.Count > 0)
            {
                packedBlueprint = SerializeBlueprint(entries, savedWidthScale, savedHeightScale, savedLengthScale);
                SaveBlueprintToDisk(packedBlueprint);
            }

            // Alt parçaları uçuş sahnesinden kaldır ve tek parçalı Kutu (Crate) haline getir
            List<Part> childrenToRemove = new List<Part>(part.children);
            for (int i = 0; i < childrenToRemove.Count; i++)
            {
                DestroySubtreeInFlight(childrenToRemove[i]);
            }
            part.children.Clear();

            foreach (AttachNode node in part.attachNodes)
            {
                if (node != null && node.attachedPart != part.parent)
                {
                    node.attachedPart = null;
                }
            }

            isFoldedCrate = true;
            ApplyFoldedCrateState(true);
            RefreshPAWVisibility();

            if (vessel != null)
            {
                GameEvents.onVesselWasModified.Fire(vessel);
            }

            ScreenMessages.PostScreenMessage("📦 Apollo LRV tekrar taşıma kutusuna katlandı! ('I' tuşu ile EVA İnşa Modunu açıp Kerbal envanterine alabilirsiniz)", 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        public void RefreshPAWVisibility()
        {
            bool isApolloChassis = (_roverCore == null) || (_roverCore.chassisIndex == APOLLO_LRV_CHASSIS_INDEX);

            Fields[nameof(crateStatusDisplay)].guiActiveEditor = isApolloChassis;
            Fields[nameof(crateStatusDisplay)].guiActive = isApolloChassis || isFoldedCrate;
            Fields[nameof(requireEngineerOnEva)].guiActive = isApolloChassis || isFoldedCrate;

            Events[nameof(PackRoverIntoCrateEvent)].guiActiveEditor = isApolloChassis && !isFoldedCrate;
            Events[nameof(UnpackRoverInEditorEvent)].guiActiveEditor = isApolloChassis && isFoldedCrate;
            Events[nameof(StoreInShipInventoryEvent)].guiActiveEditor = isApolloChassis;

            Events[nameof(UnfoldRoverOnSurfaceEvent)].guiActive = isFoldedCrate;
            Events[nameof(UnfoldRoverOnSurfaceEvent)].guiActiveUnfocused = isFoldedCrate;

            Events[nameof(FoldRoverOnSurfaceEvent)].guiActive = isApolloChassis && !isFoldedCrate;
            Events[nameof(FoldRoverOnSurfaceEvent)].guiActiveUnfocused = isApolloChassis && !isFoldedCrate;
        }

        #endregion

        #region 3B Taşıma Sandığı (Crate) Görsel Modeli ve Durum Geçişi

        private void EnsureCrateVisualHierarchy()
        {
            if (_crateVisualRoot != null)
                return;

            Transform modelRoot = part.transform.Find("model") ?? part.transform;
            Transform existing = modelRoot.Find("FoldedLRV_CrateRoot");
            if (existing != null)
            {
                _crateVisualRoot = existing.gameObject;
                _flapFront = existing.Find("CrateFlap_Front");
                _flapRear = existing.Find("CrateFlap_Rear");
                _flapLeft = existing.Find("CrateFlap_Left");
                _flapRight = existing.Find("CrateFlap_Right");
                return;
            }

            // C# üzerinden detaylı 3B Apollo LRV Taşıma Sandığı (0.62m x 0.38m x 0.74m) oluştur
            _crateVisualRoot = new GameObject("FoldedLRV_CrateRoot");
            _crateVisualRoot.transform.SetParent(modelRoot, false);
            _crateVisualRoot.transform.localPosition = Vector3.zero;
            _crateVisualRoot.transform.localRotation = Quaternion.identity;

            EnsureCrateMaterials();

            // Ana Sandık Tabanı ve İç Gövde (keepCollider: true -> Yere konulduğunda zemine sağlam basması için BoxCollider korunur!)
            CreateRuntimePrimitive(_crateVisualRoot.transform, "Crate_BaseBox", PrimitiveType.Cube,
                new Vector3(0f, -0.02f, 0f), Vector3.zero, new Vector3(0.58f, 0.32f, 0.68f), _sharedCrateOliveMat, keepCollider: true);
            CreateRuntimePrimitive(_crateVisualRoot.transform, "Crate_GoldTopFoil", PrimitiveType.Cube,
                new Vector3(0f, 0.145f, 0f), Vector3.zero, new Vector3(0.52f, 0.025f, 0.60f), _sharedCrateGoldMat);

            // Köşe Koruma Tamponları ve Taşıma Kulpları
            CreateRuntimePrimitive(_crateVisualRoot.transform, "Crate_FrameBandF", PrimitiveType.Cube,
                new Vector3(0f, -0.01f, 0.29f), Vector3.zero, new Vector3(0.61f, 0.35f, 0.045f), _sharedCrateDarkMat);
            CreateRuntimePrimitive(_crateVisualRoot.transform, "Crate_FrameBandR", PrimitiveType.Cube,
                new Vector3(0f, -0.01f, -0.29f), Vector3.zero, new Vector3(0.61f, 0.35f, 0.045f), _sharedCrateDarkMat);
            CreateRuntimePrimitive(_crateVisualRoot.transform, "Crate_HandleF", PrimitiveType.Cylinder,
                new Vector3(0f, 0.01f, 0.355f), new Vector3(0f, 0f, 90f), new Vector3(0.032f, 0.13f, 0.032f), _sharedCrateSilverMat);
            CreateRuntimePrimitive(_crateVisualRoot.transform, "Crate_HandleR", PrimitiveType.Cylinder,
                new Vector3(0f, 0.01f, -0.355f), new Vector3(0f, 0f, 90f), new Vector3(0.032f, 0.13f, 0.032f), _sharedCrateSilverMat);

            // Açılır Yan/Ön/Arka Sandık Kapakları (Unfold Animasyonunda Dışa Açılır)
            _flapFront = CreateFlapHinge(_crateVisualRoot.transform, "CrateFlap_Front", new Vector3(0f, -0.16f, 0.335f),
                new Vector3(0f, 0.15f, 0f), new Vector3(0.54f, 0.30f, 0.025f), _sharedCrateGoldMat);
            _flapRear = CreateFlapHinge(_crateVisualRoot.transform, "CrateFlap_Rear", new Vector3(0f, -0.16f, -0.335f),
                new Vector3(0f, 0.15f, 0f), new Vector3(0.54f, 0.30f, 0.025f), _sharedCrateGoldMat);
            _flapLeft = CreateFlapHinge(_crateVisualRoot.transform, "CrateFlap_Left", new Vector3(-0.295f, -0.16f, 0f),
                new Vector3(0f, 0.15f, 0f), new Vector3(0.025f, 0.30f, 0.56f), _sharedCrateOliveMat);
            _flapRight = CreateFlapHinge(_crateVisualRoot.transform, "CrateFlap_Right", new Vector3(0.295f, -0.16f, 0f),
                new Vector3(0f, 0.15f, 0f), new Vector3(0.025f, 0.30f, 0.56f), _sharedCrateOliveMat);

            _crateVisualRoot.SetActive(false);
        }

        private static void EnsureCrateMaterials()
        {
            if (_sharedCrateOliveMat != null)
                return;

            Shader sh = Shader.Find("KSP/Specular") ?? Shader.Find("KSP/Diffuse") ?? Shader.Find("Standard");
            _sharedCrateOliveMat = BuildStockPanelRuntimeMaterial(sh, new Color(0.42f, 0.47f, 0.39f), hazardStripe: false, shininess: 0.38f);
            _sharedCrateDarkMat = BuildStockPanelRuntimeMaterial(sh, new Color(0.22f, 0.24f, 0.27f), hazardStripe: true, shininess: 0.48f);
            _sharedCrateSilverMat = BuildStockPanelRuntimeMaterial(sh, new Color(0.68f, 0.71f, 0.75f), hazardStripe: false, shininess: 0.68f);
            _sharedCrateGoldMat = BuildGoldFoilRuntimeMaterial(sh);
        }

        private static Material BuildStockPanelRuntimeMaterial(Shader sh, Color baseCol, bool hazardStripe, float shininess)
        {
            const int sz = 128;
            Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, true);
            Color[] px = new Color[sz * sz];
            for (int y = 0; y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    float n = (Mathf.PerlinNoise(x * 0.18f, y * 0.18f) - 0.5f) * 0.04f;
                    Color c = new Color(Mathf.Clamp01(baseCol.r + n), Mathf.Clamp01(baseCol.g + n), Mathf.Clamp01(baseCol.b + n), 0.55f);
                    int bDist = Mathf.Min(Mathf.Min(x % 64, 63 - (x % 64)), Mathf.Min(y % 64, 63 - (y % 64)));
                    if (bDist < 2) c *= 0.62f;
                    if (hazardStripe && y >= 4 && y <= 12)
                    {
                        c = (((x + y) / 8) % 2 == 0) ? new Color(0.88f, 0.68f, 0.12f, 0.6f) : new Color(0.14f, 0.14f, 0.15f, 0.6f);
                    }
                    px[y * sz + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();

            Material mat = new Material(sh) { mainTexture = tex };
            if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", new Color(0.45f, 0.45f, 0.48f));
            if (mat.HasProperty("_Shininess")) mat.SetFloat("_Shininess", shininess);
            return mat;
        }

        private static Material BuildGoldFoilRuntimeMaterial(Shader sh)
        {
            const int sz = 128;
            Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, true);
            Color[] px = new Color[sz * sz];
            Color gDark = new Color(0.58f, 0.36f, 0.06f, 0.8f);
            Color gBright = new Color(0.98f, 0.80f, 0.30f, 0.85f);
            for (int y = 0; y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    float crinkle = Mathf.Abs(Mathf.PerlinNoise(x * 0.14f, y * 0.14f) - 0.5f) * 2f;
                    if (x % 32 < 2 || y % 32 < 2) crinkle *= 0.5f;
                    px[y * sz + x] = Color.Lerp(gDark, gBright, crinkle);
                }
            }
            tex.SetPixels(px);
            tex.Apply();

            Material mat = new Material(sh) { mainTexture = tex };
            if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", new Color(0.95f, 0.78f, 0.32f));
            if (mat.HasProperty("_Shininess")) mat.SetFloat("_Shininess", 0.75f);
            return mat;
        }

        private static Transform CreateFlapHinge(Transform parent, string name, Vector3 hingePos, Vector3 panelOffset, Vector3 panelScale, Material mat)
        {
            GameObject hinge = new GameObject(name);
            hinge.transform.SetParent(parent, false);
            hinge.transform.localPosition = hingePos;
            hinge.transform.localRotation = Quaternion.identity;

            CreateRuntimePrimitive(hinge.transform, name + "_Panel", PrimitiveType.Cube, panelOffset, Vector3.zero, panelScale, mat);
            return hinge.transform;
        }

        private static GameObject CreateRuntimePrimitive(Transform parent, string name, PrimitiveType type, Vector3 localPos, Vector3 localEuler, Vector3 localScale, Material mat, bool keepCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider)
            {
                Collider col = go.GetComponent<Collider>();
                if (col != null)
                {
                    DestroyImmediate(col);
                }
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            go.layer = 0;

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null)
            {
                mr.sharedMaterial = mat;
            }
            return go;
        }

        public void ApplyFoldedCrateState(bool folded)
        {
            EnsureRootPhysicsCollider();
            EnsureCrateVisualHierarchy();

            if (_rootCrateCollider != null)
            {
                _rootCrateCollider.enabled = true;
                _rootCrateCollider.isTrigger = false;
                _rootCrateCollider.size = folded
                    ? new Vector3(0.62f, 0.38f, 0.72f)
                    : new Vector3(1.30f, 0.22f, 2.30f);
                _rootCrateCollider.center = Vector3.zero;
                part.collider = _rootCrateCollider;
            }

            if (_crateVisualRoot != null)
            {
                _crateVisualRoot.SetActive(folded);
                if (folded)
                {
                    if (_flapFront != null) _flapFront.localRotation = Quaternion.identity;
                    if (_flapRear != null) _flapRear.localRotation = Quaternion.identity;
                    if (_flapLeft != null) _flapLeft.localRotation = Quaternion.identity;
                    if (_flapRight != null) _flapRight.localRotation = Quaternion.identity;
                }
            }

            // KRİTİK FİZİK KURALI:
            // KSP EVA İnşa Modunda (I tuşu) parçanın yere sağlam basması için prefab'dan gelen
            // Chassis_ApolloLRV objesinin BoxCollider bileşeni ASLA devre dışı bırakılmamalıdır!
            // Bunun yerine katlanmış modda sadece görsel MeshRenderer'ları gizleyip BoxCollider boyutunu sandık boyutuna çekiyoruz.
            Transform modelRoot = part.transform.Find("model") ?? part.transform;
            Transform apolloTr = FindDeepChild(modelRoot, "Chassis_ApolloLRV");

            if (folded)
            {
                string[] otherChassis = { "Chassis_Micro", "Chassis_HeavySci", "Chassis_Lunokhod", "Chassis_SciFi" };
                for (int i = 0; i < otherChassis.Length; i++)
                {
                    Transform tr = FindDeepChild(modelRoot, otherChassis[i]);
                    if (tr != null)
                        tr.gameObject.SetActive(false);
                }

                if (apolloTr != null)
                {
                    apolloTr.gameObject.SetActive(true);
                    apolloTr.localScale = Vector3.one;

                    // Görsel şasi mesh'lerini gizle ama fiziksel BoxCollider'ı sandık boyutunda aktif tut!
                    MeshRenderer[] apolloRenderers = apolloTr.GetComponentsInChildren<MeshRenderer>(true);
                    for (int i = 0; i < apolloRenderers.Length; i++)
                    {
                        if (apolloRenderers[i] != null)
                            apolloRenderers[i].enabled = false;
                    }

                    BoxCollider bc = apolloTr.GetComponent<BoxCollider>();
                    if (bc != null)
                    {
                        bc.enabled = true;
                        bc.size = new Vector3(0.62f, 0.38f, 0.72f);
                        bc.center = Vector3.zero;
                    }
                }

                // Tekerlek/ekipman bağlantı noktalarını kutu içindeyken gizle (sadece top ve bottom aktif kalsın)
                foreach (AttachNode node in part.attachNodes)
                {
                    if (node == null) continue;
                    if (node.id != "top" && node.id != "bottom" && node.attachedPart == null)
                    {
                        node.position = Vector3.zero;
                        node.size = 0;
                    }
                }

                int count = CountBlueprintEntries(packedBlueprint);
                crateStatusDisplay = count > 0 ? $"Katlanmış Kutu ({count} Ekipman)" : "Katlanmış LRV Kutusu";
            }
            else
            {
                if (apolloTr != null)
                {
                    MeshRenderer[] apolloRenderers = apolloTr.GetComponentsInChildren<MeshRenderer>(true);
                    for (int i = 0; i < apolloRenderers.Length; i++)
                    {
                        if (apolloRenderers[i] != null)
                            apolloRenderers[i].enabled = true;
                    }

                    BoxCollider bc = apolloTr.GetComponent<BoxCollider>();
                    if (bc != null)
                    {
                        bc.enabled = true;
                        bc.size = new Vector3(1.30f, 0.22f, 2.30f);
                        bc.center = Vector3.zero;
                    }
                }

                crateStatusDisplay = "Açık Şasi (Sürüşe Hazır)";
                if (_roverCore != null)
                {
                    _roverCore.OnStart(HighLogic.LoadedSceneIsFlight ? StartState.Landed : StartState.Editor);
                }
            }
        }

        #endregion

        #region Uçuş Sahnesi (Ay Yüzeyi) Kutu Açılma Animasyonu ve Parça Kurulumu (OnAttachFlight)

        private void UpdateFlightUnfoldAnimation(float dt)
        {
            _unfoldTimer += dt;
            float t = Mathf.Clamp01(_unfoldTimer / UNFOLD_DURATION);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            // Sandık kapaklarını dışarı doğru 105 derece aç
            float flapAngle = smoothT * 105f;
            if (_flapFront != null) _flapFront.localRotation = Quaternion.Euler(flapAngle, 0f, 0f);
            if (_flapRear != null) _flapRear.localRotation = Quaternion.Euler(-flapAngle, 0f, 0f);
            if (_flapLeft != null) _flapLeft.localRotation = Quaternion.Euler(0f, 0f, flapAngle);
            if (_flapRight != null) _flapRight.localRotation = Quaternion.Euler(0f, 0f, -flapAngle);

            if (t >= 1.0f)
            {
                _isUnfoldingInFlight = false;
                CompleteFlightRoverDeployment();
            }
        }

        private void CompleteFlightRoverDeployment()
        {
            List<PackedChildPartEntry> entries = DeserializeBlueprint(packedBlueprint, out float wScale, out float hScale, out float lScale);

            isFoldedCrate = false;
            if (_roverCore != null)
            {
                _roverCore.chassisIndex = APOLLO_LRV_CHASSIS_INDEX;
                _roverCore.widthScale = wScale;
                _roverCore.heightScale = hScale;
                _roverCore.lengthScale = lScale;
            }

            ApplyFoldedCrateState(false);

            int spawnedCount = SpawnAndAttachChildrenInFlight(entries);
            RefreshPAWVisibility();

            // Bataryayı tam dolu başlat
            PartResource ec = part.Resources["ElectricCharge"];
            if (ec != null)
            {
                ec.amount = ec.maxAmount;
            }

            if (vessel != null)
            {
                GameEvents.onVesselWasModified.Fire(vessel);
            }

            ScreenMessages.PostScreenMessage($"✅ Apollo LRV başarıyla kuruldu! ({spawnedCount} tekerlek/modül monte edildi - Sürüşe hazır)", 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>
        /// KSP 1.12.5'in yerel EVA İnşa Sistemi (ProtoPartSnapshot.CreatePart + Part.OnAttachFlight)
        /// mimarisini kullanarak paketlenmiş tüm tekerlek ve ekipmanları uçuş esnasında şasiye monte eder.
        /// </summary>
        private int SpawnAndAttachChildrenInFlight(List<PackedChildPartEntry> entries)
        {
            if (entries == null || entries.Count == 0 || vessel == null)
                return 0;

            List<Part> spawnedPartsByIndex = new List<Part> { this.part };
            int successCount = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                PackedChildPartEntry entry = entries[i];
                AvailablePart ap = PartLoader.getPartInfoByName(entry.partName);
                if (ap == null)
                {
                    Debug.LogWarning($"[ProceduralRovers] Paketlenmiş parça bulunamadı: {entry.partName}");
                    spawnedPartsByIndex.Add(null);
                    continue;
                }

                Part targetParent = (entry.parentIndex >= 0 && entry.parentIndex < spawnedPartsByIndex.Count && spawnedPartsByIndex[entry.parentIndex] != null)
                    ? spawnedPartsByIndex[entry.parentIndex]
                    : this.part;

                try
                {
                    uint flightId = ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState);
                    ConfigNode partNode = ProtoVessel.CreatePartNode(entry.partName, flightId, null);
                    ProtoPartSnapshot snapshot = new ProtoPartSnapshot(partNode, vessel.protoVessel, HighLogic.CurrentGame);

                    Part newPart = snapshot.CreatePart();
                    if (newPart == null)
                    {
                        spawnedPartsByIndex.Add(null);
                        continue;
                    }

                    // Dünya pozisyonu ve rotasyonunu ana rover gövdesine göre ayarla
                    Vector3 worldPos = this.part.transform.TransformPoint(entry.localPos);
                    Quaternion worldRot = this.part.transform.rotation * entry.localRot;

                    newPart.transform.position = worldPos;
                    newPart.transform.rotation = worldRot;

                    newPart.isAttached = true;
                    newPart.attachMode = (AttachModes)entry.attachMode;
                    newPart.attPos0 = targetParent.transform.InverseTransformPoint(worldPos);
                    newPart.attRotation0 = Quaternion.Inverse(targetParent.transform.rotation) * worldRot;

                    // AttachNode bağlantısını kur
                    if (!string.IsNullOrEmpty(entry.parentNodeId))
                    {
                        AttachNode pNode = targetParent.FindAttachNode(entry.parentNodeId);
                        if (pNode != null) pNode.attachedPart = newPart;
                    }
                    if (!string.IsNullOrEmpty(entry.childNodeId))
                    {
                        AttachNode cNode = newPart.FindAttachNode(entry.childNodeId);
                        if (cNode != null) cNode.attachedPart = targetParent;
                    }

                    // KSP 1.12.5 yerel uçuş montaj metodunu çağır (PartJoint, Vessel.Parts ve modülleri başlatır)
                    newPart.OnAttachFlight(targetParent);

                    // Özel prosedürel ölçekleri uygula
                    ApplyCustomSavedModuleValues(newPart, entry);

                    spawnedPartsByIndex.Add(newPart);
                    successCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ProceduralRovers] Uçuşta parça kurulum hatası ({entry.partName}): {ex}");
                    spawnedPartsByIndex.Add(null);
                }
            }

            return successCount;
        }

        private void LiftAndLevelCrateAboveTerrain(float liftHeightMeters)
        {
            if (vessel == null || vessel.mainBody == null)
                return;

            Vector3 upAxis = (vessel.transform.position - vessel.mainBody.transform.position).normalized;
            Vector3 forwardProjected = Vector3.ProjectOnPlane(part.transform.forward, upAxis).normalized;
            if (forwardProjected.sqrMagnitude < 1e-4f)
            {
                forwardProjected = Vector3.ProjectOnPlane(part.transform.up, upAxis).normalized;
            }

            Quaternion levelRotation = Quaternion.LookRotation(forwardProjected, upAxis);
            Vector3 liftedPos = vessel.transform.position + upAxis * liftHeightMeters;

            vessel.SetPosition(liftedPos, true);
            vessel.SetRotation(levelRotation, true);
        }

        private bool IsEngineerKerbalNearby(out string failMessage)
        {
            failMessage = "";
            Vessel activeV = FlightGlobals.ActiveVessel;

            // Aktif gemi bir EVA Kerbal ise
            if (activeV != null && activeV.isEVA)
            {
                List<ProtoCrewMember> crew = activeV.GetVesselCrew();
                if (crew != null && crew.Count > 0)
                {
                    ProtoCrewMember pcm = crew[0];
                    if (pcm.experienceTrait != null && string.Equals(pcm.experienceTrait.TypeName, "Engineer", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    failMessage = $"⚠️ {pcm.name} bir Mühendis (Engineer) değil ({pcm.experienceTrait?.Title})!\nApollo LRV kurulumu için Mühendis Kerbal gereklidir.";
                    return false;
                }
            }

            // Veya kutunun 12 metre yakınında EVA'da bir Mühendis Kerbal var mı kontrol et
            if (FlightGlobals.VesselsLoaded != null)
            {
                foreach (Vessel v in FlightGlobals.VesselsLoaded)
                {
                    if (v != null && v.isEVA && Vector3.Distance(v.transform.position, part.transform.position) <= 12.0f)
                    {
                        List<ProtoCrewMember> crew = v.GetVesselCrew();
                        if (crew != null && crew.Count > 0 && crew[0].experienceTrait != null &&
                            string.Equals(crew[0].experienceTrait.TypeName, "Engineer", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            failMessage = "⚠️ Yakında (12m içinde) EVA'da bir Mühendis (Engineer) Kerbal bulunamadı!\n(Test için sağ tık menüsünden 'Mühendis Şartı' seçeneğini kapatabilirsiniz)";
            return false;
        }

        private void DestroySubtreeInFlight(Part p)
        {
            if (p == null)
                return;

            List<Part> subChildren = new List<Part>(p.children);
            for (int i = 0; i < subChildren.Count; i++)
            {
                DestroySubtreeInFlight(subChildren[i]);
            }

            p.decouple(0f);
            if (p.vessel != null && p.vessel != this.vessel)
            {
                p.vessel.Die();
            }
            else
            {
                p.Die();
            }
        }

        #endregion

        #region VAB / SPH Alt Ağaç Yakalama, Silme ve Geri Yükleme

        private List<PackedChildPartEntry> CaptureAttachedSubtree()
        {
            List<PackedChildPartEntry> result = new List<PackedChildPartEntry>();
            List<Part> indexedParts = new List<Part> { this.part };

            Queue<Part> queue = new Queue<Part>();
            queue.Enqueue(this.part);

            while (queue.Count > 0)
            {
                Part currentParent = queue.Dequeue();
                int parentIdx = indexedParts.IndexOf(currentParent);

                if (currentParent.children == null)
                    continue;

                foreach (Part child in currentParent.children)
                {
                    if (child == null || child.partInfo == null)
                        continue;

                    indexedParts.Add(child);
                    queue.Enqueue(child);

                    string pNodeId = "";
                    string cNodeId = "";

                    AttachNode pNode = currentParent.FindAttachNodeByPart(child);
                    if (pNode != null) pNodeId = pNode.id;

                    AttachNode cNode = child.FindAttachNodeByPart(currentParent);
                    if (cNode != null) cNodeId = cNode.id;

                    PackedChildPartEntry entry = new PackedChildPartEntry
                    {
                        partName = child.partInfo.name,
                        parentIndex = parentIdx,
                        attachMode = (int)child.attachMode,
                        parentNodeId = pNodeId,
                        childNodeId = cNodeId,
                        localPos = this.part.transform.InverseTransformPoint(child.transform.position),
                        localRot = Quaternion.Inverse(this.part.transform.rotation) * child.transform.rotation,
                        attPos0 = child.attPos0,
                        attRot0 = child.attRotation0
                    };

                    ProceduralRoverWheel wheelMod = child.FindModuleImplementing<ProceduralRoverWheel>();
                    if (wheelMod != null)
                    {
                        entry.customScale = wheelMod.wheelScale;
                        entry.tractionPreset = wheelMod.tractionPresetIndex;
                        entry.springMult = wheelMod.springMultiplier;
                        entry.damperMult = wheelMod.damperMultiplier;
                    }

                    ProceduralRoverPayload payloadMod = child.FindModuleImplementing<ProceduralRoverPayload>();
                    if (payloadMod != null)
                    {
                        entry.customScale = payloadMod.payloadScale;
                        entry.turretYaw = payloadMod.turretYaw;
                        entry.turretPitch = payloadMod.turretPitch;
                        entry.mechanismDeployed = payloadMod.isMechanismDeployed;
                    }

                    result.Add(entry);
                }
            }

            return result;
        }

        private void RemoveChildPartsInEditor()
        {
            if (part.children == null || part.children.Count == 0)
                return;

            List<Part> allDescendants = new List<Part>();
            CollectDescendantsRecursive(this.part, allDescendants);

            // Simetri bağlantılarını temizle ve tersten (yapraklardan köke) sil
            for (int i = allDescendants.Count - 1; i >= 0; i--)
            {
                Part child = allDescendants[i];
                if (child == null)
                    continue;

                if (child.symmetryCounterparts != null)
                {
                    child.symmetryCounterparts.Clear();
                }

                if (child.parent != null)
                {
                    AttachNode pNode = child.parent.FindAttachNodeByPart(child);
                    if (pNode != null) pNode.attachedPart = null;
                    child.parent.children.Remove(child);
                }

                if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
                {
                    EditorLogic.fetch.ship.Remove(child);
                }

                DestroyImmediate(child.gameObject);
            }

            part.children.Clear();
            foreach (AttachNode node in part.attachNodes)
            {
                if (node != null && node.attachedPart != part.parent)
                {
                    node.attachedPart = null;
                }
            }
        }

        private static void CollectDescendantsRecursive(Part parentPart, List<Part> list)
        {
            if (parentPart == null || parentPart.children == null)
                return;

            foreach (Part c in parentPart.children)
            {
                if (c != null)
                {
                    list.Add(c);
                    CollectDescendantsRecursive(c, list);
                }
            }
        }

        private int RespawnChildrenInEditor(List<PackedChildPartEntry> entries)
        {
            if (entries == null || entries.Count == 0 || EditorLogic.fetch == null || EditorLogic.fetch.ship == null)
                return 0;

            List<Part> spawnedByIndex = new List<Part> { this.part };
            int count = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                PackedChildPartEntry entry = entries[i];
                AvailablePart ap = PartLoader.getPartInfoByName(entry.partName);
                if (ap == null || ap.partPrefab == null)
                {
                    spawnedByIndex.Add(null);
                    continue;
                }

                Part targetParent = (entry.parentIndex >= 0 && entry.parentIndex < spawnedByIndex.Count && spawnedByIndex[entry.parentIndex] != null)
                    ? spawnedByIndex[entry.parentIndex]
                    : this.part;

                Part newPart = Instantiate(ap.partPrefab);
                newPart.gameObject.SetActive(true);
                newPart.name = ap.name;
                newPart.partInfo = ap;
                newPart.craftID = ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame != null ? HighLogic.CurrentGame.flightState : null);

                Vector3 worldPos = this.part.transform.TransformPoint(entry.localPos);
                Quaternion worldRot = this.part.transform.rotation * entry.localRot;

                newPart.transform.SetParent(targetParent.transform, true);
                newPart.transform.position = worldPos;
                newPart.transform.rotation = worldRot;

                newPart.attachMode = (AttachModes)entry.attachMode;
                newPart.attPos0 = entry.attPos0;
                newPart.attRotation0 = entry.attRot0;
                newPart.isAttached = true;

                if (!string.IsNullOrEmpty(entry.parentNodeId))
                {
                    AttachNode pNode = targetParent.FindAttachNode(entry.parentNodeId);
                    if (pNode != null) pNode.attachedPart = newPart;
                }
                if (!string.IsNullOrEmpty(entry.childNodeId))
                {
                    AttachNode cNode = newPart.FindAttachNode(entry.childNodeId);
                    if (cNode != null) cNode.attachedPart = targetParent;
                }

                targetParent.addChild(newPart);
                EditorLogic.fetch.ship.Add(newPart);
                newPart.onAttach(targetParent, true);

                ApplyCustomSavedModuleValues(newPart, entry);

                spawnedByIndex.Add(newPart);
                count++;
            }

            return count;
        }

        private static void ApplyCustomSavedModuleValues(Part targetPart, PackedChildPartEntry entry)
        {
            if (targetPart == null)
                return;

            ProceduralRoverWheel wheelMod = targetPart.FindModuleImplementing<ProceduralRoverWheel>();
            if (wheelMod != null)
            {
                wheelMod.wheelScale = entry.customScale;
                wheelMod.tractionPresetIndex = entry.tractionPreset;
                wheelMod.springMultiplier = entry.springMult;
                wheelMod.damperMultiplier = entry.damperMult;
                wheelMod.ApplyWheelConfiguration(repositionMountNode: false);
            }

            ProceduralRoverPayload payloadMod = targetPart.FindModuleImplementing<ProceduralRoverPayload>();
            if (payloadMod != null)
            {
                payloadMod.payloadScale = entry.customScale;
                payloadMod.turretYaw = entry.turretYaw;
                payloadMod.turretPitch = entry.turretPitch;
                payloadMod.isMechanismDeployed = entry.mechanismDeployed;
                payloadMod.ApplyPayloadScaling(repositionMountNode: false);
            }
        }

        #endregion

        #region Blueprint Serileştirme (String & Disk Yedeği)

        private static string SerializeBlueprint(List<PackedChildPartEntry> entries, float wScale, float hScale, float lScale)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(inv, "CORE|{0:F3},{1:F3},{2:F3}", wScale, hScale, lScale);

            for (int i = 0; i < entries.Count; i++)
            {
                PackedChildPartEntry e = entries[i];
                sb.Append(';');
                sb.AppendFormat(inv,
                    "{0}|{1}|{2}|{3}|{4}|{5:F4},{6:F4},{7:F4}|{8:F4},{9:F4},{10:F4},{11:F4}|{12:F4},{13:F4},{14:F4}|{15:F4},{16:F4},{17:F4},{18:F4}|{19:F3}|{20}|{21:F2}|{22:F2}|{23:F1}|{24:F1}|{25}",
                    e.partName,
                    e.parentIndex,
                    e.attachMode,
                    e.parentNodeId ?? "",
                    e.childNodeId ?? "",
                    e.localPos.x, e.localPos.y, e.localPos.z,
                    e.localRot.x, e.localRot.y, e.localRot.z, e.localRot.w,
                    e.attPos0.x, e.attPos0.y, e.attPos0.z,
                    e.attRot0.x, e.attRot0.y, e.attRot0.z, e.attRot0.w,
                    e.customScale,
                    e.tractionPreset,
                    e.springMult,
                    e.damperMult,
                    e.turretYaw,
                    e.turretPitch,
                    e.mechanismDeployed ? 1 : 0);
            }

            return sb.ToString();
        }

        private static List<PackedChildPartEntry> DeserializeBlueprint(string data, out float wScale, out float hScale, out float lScale)
        {
            wScale = 1.0f;
            hScale = 1.0f;
            lScale = 1.0f;
            List<PackedChildPartEntry> list = new List<PackedChildPartEntry>();

            if (string.IsNullOrEmpty(data))
                return list;

            CultureInfo inv = CultureInfo.InvariantCulture;
            string[] records = data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < records.Length; i++)
            {
                string[] f = records[i].Split('|');
                if (f.Length >= 2 && f[0] == "CORE")
                {
                    Vector3 s = ParseVec3(f[1], inv);
                    wScale = s.x > 0.1f ? s.x : 1.0f;
                    hScale = s.y > 0.1f ? s.y : 1.0f;
                    lScale = s.z > 0.1f ? s.z : 1.0f;
                    continue;
                }

                if (f.Length < 10)
                    continue;

                PackedChildPartEntry e = new PackedChildPartEntry
                {
                    partName = f[0],
                    parentIndex = int.Parse(f[1], inv),
                    attachMode = int.Parse(f[2], inv),
                    parentNodeId = f[3],
                    childNodeId = f[4],
                    localPos = ParseVec3(f[5], inv),
                    localRot = ParseQuat(f[6], inv),
                    attPos0 = ParseVec3(f[7], inv),
                    attRot0 = ParseQuat(f[8], inv),
                    customScale = float.Parse(f[9], inv)
                };

                if (f.Length >= 16)
                {
                    e.tractionPreset = int.Parse(f[10], inv);
                    e.springMult = float.Parse(f[11], inv);
                    e.damperMult = float.Parse(f[12], inv);
                    e.turretYaw = float.Parse(f[13], inv);
                    e.turretPitch = float.Parse(f[14], inv);
                    e.mechanismDeployed = f[15] == "1";
                }

                list.Add(e);
            }

            return list;
        }

        /// <summary>
        /// Eğer oyuncu VAB'da hiç paketleme yapmadan doğrudan PR-C1'i Kerbal envanterine sürükleyip Ay'a götürmüşse
        /// 4 adet PR-W10 Hasır Tekerlek, 1 adet MastCam ve 1 adet Güneş Paneli içeren hazır Apollo LRV şeması üretir.
        /// </summary>
        private static string GetDefaultApolloLrvBlueprint()
        {
            List<PackedChildPartEntry> def = new List<PackedChildPartEntry>
            {
                new PackedChildPartEntry
                {
                    partName = "pr.wheel.meshSpring", parentIndex = 0, attachMode = 0,
                    parentNodeId = "wheelFL", childNodeId = "mount",
                    localPos = new Vector3(-0.83f, -0.19f, 0.92f), localRot = Quaternion.identity,
                    attPos0 = new Vector3(-0.83f, -0.19f, 0.92f), attRot0 = Quaternion.identity,
                    customScale = 1.0f, tractionPreset = 1, springMult = 1.0f, damperMult = 1.0f
                },
                new PackedChildPartEntry
                {
                    partName = "pr.wheel.meshSpring", parentIndex = 0, attachMode = 0,
                    parentNodeId = "wheelFR", childNodeId = "mount",
                    localPos = new Vector3(0.83f, -0.19f, 0.92f), localRot = Quaternion.Euler(0f, 180f, 0f),
                    attPos0 = new Vector3(0.83f, -0.19f, 0.92f), attRot0 = Quaternion.Euler(0f, 180f, 0f),
                    customScale = 1.0f, tractionPreset = 1, springMult = 1.0f, damperMult = 1.0f
                },
                new PackedChildPartEntry
                {
                    partName = "pr.wheel.meshSpring", parentIndex = 0, attachMode = 0,
                    parentNodeId = "wheelRL", childNodeId = "mount",
                    localPos = new Vector3(-0.83f, -0.19f, -0.92f), localRot = Quaternion.identity,
                    attPos0 = new Vector3(-0.83f, -0.19f, -0.92f), attRot0 = Quaternion.identity,
                    customScale = 1.0f, tractionPreset = 1, springMult = 1.0f, damperMult = 1.0f
                },
                new PackedChildPartEntry
                {
                    partName = "pr.wheel.meshSpring", parentIndex = 0, attachMode = 0,
                    parentNodeId = "wheelRR", childNodeId = "mount",
                    localPos = new Vector3(0.83f, -0.19f, -0.92f), localRot = Quaternion.Euler(0f, 180f, 0f),
                    attPos0 = new Vector3(0.83f, -0.19f, -0.92f), attRot0 = Quaternion.Euler(0f, 180f, 0f),
                    customScale = 1.0f, tractionPreset = 1, springMult = 1.0f, damperMult = 1.0f
                },
                new PackedChildPartEntry
                {
                    partName = "pr.payload.mastCam", parentIndex = 0, attachMode = 0,
                    parentNodeId = "mastMount", childNodeId = "mount",
                    localPos = new Vector3(0.00f, 0.36f, 0.98f), localRot = Quaternion.identity,
                    attPos0 = new Vector3(0.00f, 0.36f, 0.98f), attRot0 = Quaternion.identity,
                    customScale = 1.0f
                }
            };

            return SerializeBlueprint(def, 1.0f, 1.0f, 1.0f);
        }

        private static void SaveBlueprintToDisk(string blueprint)
        {
            try
            {
                string dir = Path.GetDirectoryName(BlueprintFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(BlueprintFilePath, blueprint);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProceduralRovers] LRV şeması diske kaydedilemedi: {ex.Message}");
            }
        }

        private static int CountBlueprintEntries(string data)
        {
            if (string.IsNullOrEmpty(data))
                return 0;

            string[] parts = data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            return Mathf.Max(0, parts.Length - 1);
        }

        private static Vector3 ParseVec3(string s, CultureInfo inv)
        {
            string[] c = s.Split(',');
            if (c.Length < 3) return Vector3.zero;
            return new Vector3(float.Parse(c[0], inv), float.Parse(c[1], inv), float.Parse(c[2], inv));
        }

        private static Quaternion ParseQuat(string s, CultureInfo inv)
        {
            string[] c = s.Split(',');
            if (c.Length < 4) return Quaternion.identity;
            return new Quaternion(float.Parse(c[0], inv), float.Parse(c[1], inv), float.Parse(c[2], inv), float.Parse(c[3], inv));
        }

        private static Transform FindDeepChild(Transform parent, string targetName)
        {
            if (parent == null || string.IsNullOrEmpty(targetName))
                return null;

            foreach (Transform child in parent)
            {
                if (string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase))
                    return child;

                Transform found = FindDeepChild(child, targetName);
                if (found != null)
                    return found;
            }
            return null;
        }

        #endregion
    }
}
