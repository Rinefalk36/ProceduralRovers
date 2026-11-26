using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRovers
{
    /// <summary>
    /// Şasi arketiplerinin statik, dinamik ve anatomik bağlantı noktası (AttachNode) karakteristiklerini tanımlayan veri yapısı.
    /// </summary>
    [Serializable]
    public class RoverChassisProfile
    {
        public string id;
        public string displayName;
        public string transformName;

        // Fiziksel ve yapısal çarpanlar
        public float baseMass;
        public float baseCost;
        public float baseElectricCharge;
        public float baseReactionTorque;
        public int sasServiceLevel;
        public float crashTolerance;
        public double maxTemp;
        public double skinMaxTemp;

        // Bu şaside Rocker-Bogie mekanizmasının aktif olup olmadığı
        public bool supportsRockerBogie;

        // Şasiye özgü varsayılan (1x ölçekteki) boyutlar (Metre: X=Genişlik, Y=Yükseklik, Z=Uzunluk)
        public Vector3 referenceDimensions;

        // Bu şaside aktif olacak bağlantı noktası (AttachNode) ID listesi
        public HashSet<string> activeNodes;

        // Şasiye özgü 1.0x ölçekteki anatomik AttachNode koordinat haritası.
        // Şasi tipi değiştiğinde tekerlekler, MastCam ve güç modülleri o şasinin gerçek yuvalarına oturur.
        public Dictionary<string, Vector3> chassisNodePositions;
    }

    /// <summary>
    /// KSP 1.12.5 Prosedürel Keşif Aracı (Procedural Rover) Ana Gövde Modülü.
    /// Prosedürel boyutlandırma, şasi tipi geçişi, şasiye özel anatomik AttachNode haritalaması,
    /// yüzey bağlantı (srfAttach) adaptasyonu ve alt-sistem (EC, SAS, Tork, Termal) yönetimini üstlenir.
    /// </summary>
    public class ProceduralRoverCore : PartModule, IPartMassModifier, IPartCostModifier, IPartSizeModifier
    {
        #region KSPFields (PAW - Part Action Window Kontrolleri)

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Şasi Tipi"),
         UI_ChooseOption(affectSymCounterparts = UI_Scene.Editor, scene = UI_Scene.Editor, suppressEditorShipModified = false)]
        public int chassisIndex = 1; // Varsayılan: Heavy Science (Curiosity/Perseverance)

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Uzunluk Ölçeği (Z)", guiFormat = "F2"),
         UI_FloatRange(minValue = 0.6f, maxValue = 3.5f, stepIncrement = 0.05f, affectSymCounterparts = UI_Scene.Editor, scene = UI_Scene.Editor)]
        public float lengthScale = 1.0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Genişlik Ölçeği (X)", guiFormat = "F2"),
         UI_FloatRange(minValue = 0.6f, maxValue = 3.0f, stepIncrement = 0.05f, affectSymCounterparts = UI_Scene.Editor, scene = UI_Scene.Editor)]
        public float widthScale = 1.0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Yükseklik Ölçeği (Y)", guiFormat = "F2"),
         UI_FloatRange(minValue = 0.5f, maxValue = 2.5f, stepIncrement = 0.05f, affectSymCounterparts = UI_Scene.Editor, scene = UI_Scene.Editor)]
        public float heightScale = 1.0f;

        // PAW Bilgi Alanları (Salt-Okunur Telemetri)
        [KSPField(guiActiveEditor = true, guiActive = true, guiName = "Aktif Şasi")]
        public string activeChassisDisplay = "Heavy Science";

        [KSPField(guiActiveEditor = true, guiActive = false, guiName = "Gövde Ebatları")]
        public string dimensionsDisplay = "1.40m x 0.65m x 2.20m";

        #endregion

        #region Dahili Durum Değişkenleri

        private List<RoverChassisProfile> _profiles;
        private readonly Dictionary<string, Transform> _chassisTransforms = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Vector3> _prefabFallbackNodePositions = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, int> _baseNodeSizes = new Dictionary<string, int>();

        // Yüzey bağlantılı (srfAttach) çocuk parçaların orantısal kaydırılması için önceki gerçek ebat kaydı
        private Vector3 _previousEffectiveDimensions = new Vector3(1.40f, 0.65f, 2.20f);
        private Vector3 _previousScale = Vector3.one;
        private int _previousChassisIndex = -1;
        private bool _isInitialized = false;

        // Kütle ve maliyet modifikatör farkları (Prefab değerine göre delta)
        private float _massDelta = 0f;
        private float _costDelta = 0f;
        private Vector3 _currentSize = Vector3.one;

        #endregion

        #region Yaşam Döngüsü (Lifecycle: OnAwake, OnStart, Update)

        public override void OnAwake()
        {
            base.OnAwake();
            InitializeChassisProfiles();
            CacheAndInitializeChassisTransforms();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (_profiles == null || _profiles.Count == 0)
                InitializeChassisProfiles();

            CacheAndInitializeChassisTransforms();
            CachePrefabNodePositions();
            SetupEditorUI();

            chassisIndex = Mathf.Clamp(chassisIndex, 0, _profiles.Count - 1);
            _previousScale = new Vector3(widthScale, heightScale, lengthScale);
            _previousChassisIndex = chassisIndex;
            _previousEffectiveDimensions = Vector3.Scale(_profiles[chassisIndex].referenceDimensions, _previousScale);
            _isInitialized = true;

            // İlk yüklemede (Editor veya Flight) parçaları koparmadan geometrik ve fiziksel durumu uygula
            ApplyRoverConfiguration(repositionAttachedParts: false, rebuildDragCubes: HighLogic.LoadedSceneIsFlight);
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor || !_isInitialized)
                return;

            Vector3 currentScale = new Vector3(widthScale, heightScale, lengthScale);
            bool scaleChanged = (currentScale - _previousScale).sqrMagnitude > 1e-6f;
            bool chassisChanged = chassisIndex != _previousChassisIndex;

            if (scaleChanged || chassisChanged)
            {
                ApplyRoverConfiguration(repositionAttachedParts: true, rebuildDragCubes: false);
                _previousScale = currentScale;
                _previousChassisIndex = chassisIndex;
            }
        }

        public RoverChassisProfile GetActiveProfile()
        {
            if (_profiles == null || _profiles.Count == 0)
                InitializeChassisProfiles();

            int idx = Mathf.Clamp(chassisIndex, 0, _profiles.Count - 1);
            return _profiles[idx];
        }

        #endregion

        #region Şasi Profilleri ve Anatomik Node Haritaları (ChassisNodeOffsets)

        private void InitializeChassisProfiles()
        {
            _profiles = new List<RoverChassisProfile>
            {
                // -------------------------------------------------------------------------
                // 1. MICRO (Sojourner / Pathfinder) - Kompakt 6 Tekerlekli Mikro Şasi
                //    Ebatlar: 0.65m (G) x 0.30m (Y) x 0.90m (U)
                // -------------------------------------------------------------------------
                new RoverChassisProfile
                {
                    id = "micro",
                    displayName = "Micro (Sojourner)",
                    transformName = "Chassis_Micro",
                    baseMass = 0.045f,
                    baseCost = 450f,
                    baseElectricCharge = 150f,
                    baseReactionTorque = 0.25f,
                    sasServiceLevel = 1,
                    crashTolerance = 14f,
                    maxTemp = 1200,
                    skinMaxTemp = 1400,
                    supportsRockerBogie = true,
                    referenceDimensions = new Vector3(0.65f, 0.30f, 0.90f),
                    activeNodes = new HashSet<string>
                    {
                        "top", "bottom", "front", "rear",
                        "wheelFL", "wheelFR", "wheelML", "wheelMR", "wheelRL", "wheelRR",
                        "payloadTop1", "payloadFront"
                    },
                    chassisNodePositions = new Dictionary<string, Vector3>
                    {
                        { "top",          new Vector3( 0.000f,  0.150f,  0.000f) },
                        { "bottom",       new Vector3( 0.000f, -0.150f,  0.000f) },
                        { "front",        new Vector3( 0.000f,  0.000f,  0.450f) },
                        { "rear",         new Vector3( 0.000f,  0.000f, -0.450f) },
                        { "wheelFL",      new Vector3(-0.325f, -0.080f,  0.340f) },
                        { "wheelFR",      new Vector3( 0.325f, -0.080f,  0.340f) },
                        { "wheelML",      new Vector3(-0.325f, -0.080f,  0.000f) },
                        { "wheelMR",      new Vector3( 0.325f, -0.080f,  0.000f) },
                        { "wheelRL",      new Vector3(-0.325f, -0.080f, -0.340f) },
                        { "wheelRR",      new Vector3( 0.325f, -0.080f, -0.340f) },
                        { "payloadTop1",  new Vector3( 0.000f,  0.150f,  0.050f) },
                        { "payloadFront", new Vector3( 0.000f,  0.040f,  0.450f) }
                    }
                },

                // -------------------------------------------------------------------------
                // 2. HEAVY SCIENCE (Curiosity / Perseverance) - 6 Tekerlekli Rocker-Bogie
                //    Ebatlar: 1.40m (G) x 0.65m (Y) x 2.20m (U)
                // -------------------------------------------------------------------------
                new RoverChassisProfile
                {
                    id = "heavy_sci",
                    displayName = "Heavy Science (Curiosity)",
                    transformName = "Chassis_HeavySci",
                    baseMass = 0.28f,
                    baseCost = 2400f,
                    baseElectricCharge = 850f,
                    baseReactionTorque = 1.5f,
                    sasServiceLevel = 3,
                    crashTolerance = 22f,
                    maxTemp = 1800,
                    skinMaxTemp = 2000,
                    supportsRockerBogie = true,
                    referenceDimensions = new Vector3(1.40f, 0.65f, 2.20f),
                    activeNodes = new HashSet<string>
                    {
                        "top", "bottom", "front", "rear",
                        "wheelFL", "wheelFR", "wheelML", "wheelMR", "wheelRL", "wheelRR",
                        "payloadTop1", "payloadTop2", "payloadFront", "payloadRear", "mastMount"
                    },
                    chassisNodePositions = new Dictionary<string, Vector3>
                    {
                        { "top",          new Vector3( 0.000f,  0.325f,  0.000f) },
                        { "bottom",       new Vector3( 0.000f, -0.325f,  0.000f) },
                        { "front",        new Vector3( 0.000f,  0.000f,  1.100f) },
                        { "rear",         new Vector3( 0.000f,  0.000f, -1.100f) },
                        { "wheelFL",      new Vector3(-0.700f, -0.150f,  0.850f) },
                        { "wheelFR",      new Vector3( 0.700f, -0.150f,  0.850f) },
                        { "wheelML",      new Vector3(-0.700f, -0.150f, -0.050f) },
                        { "wheelMR",      new Vector3( 0.700f, -0.150f, -0.050f) },
                        { "wheelRL",      new Vector3(-0.700f, -0.150f, -0.850f) },
                        { "wheelRR",      new Vector3( 0.700f, -0.150f, -0.850f) },
                        { "mastMount",    new Vector3( 0.350f,  0.325f,  0.750f) },
                        { "payloadTop1",  new Vector3( 0.000f,  0.325f,  0.400f) },
                        { "payloadTop2",  new Vector3( 0.000f,  0.325f, -0.450f) },
                        { "payloadFront", new Vector3( 0.000f,  0.100f,  1.100f) },
                        { "payloadRear",  new Vector3( 0.000f,  0.160f, -1.100f) }
                    }
                },

                // -------------------------------------------------------------------------
                // 3. TUB HULL (Lunokhod 1/2) - 8 Tekerlekli Küvet Gövde & Üst Güneş Kapağı
                //    Ebatlar: 1.60m (G) x 0.85m (Y) x 2.10m (U)
                // -------------------------------------------------------------------------
                new RoverChassisProfile
                {
                    id = "lunokhod",
                    displayName = "Tub Hull (Lunokhod)",
                    transformName = "Chassis_Lunokhod",
                    baseMass = 0.35f,
                    baseCost = 1950f,
                    baseElectricCharge = 1200f,
                    baseReactionTorque = 1.2f,
                    sasServiceLevel = 2,
                    crashTolerance = 25f,
                    maxTemp = 2200,
                    skinMaxTemp = 2400,
                    supportsRockerBogie = false,
                    referenceDimensions = new Vector3(1.60f, 0.85f, 2.10f),
                    activeNodes = new HashSet<string>
                    {
                        "top", "bottom", "front", "rear",
                        "wheelFL", "wheelFR", "wheelML", "wheelMR", "wheelML2", "wheelMR2", "wheelRL", "wheelRR",
                        "payloadTop1", "payloadFront", "mastMount"
                    },
                    chassisNodePositions = new Dictionary<string, Vector3>
                    {
                        { "top",          new Vector3( 0.000f,  0.425f,  0.000f) },
                        { "bottom",       new Vector3( 0.000f, -0.425f,  0.000f) },
                        { "front",        new Vector3( 0.000f,  0.000f,  1.050f) },
                        { "rear",         new Vector3( 0.000f,  0.000f, -1.050f) },
                        { "wheelFL",      new Vector3(-0.800f, -0.240f,  0.780f) },
                        { "wheelFR",      new Vector3( 0.800f, -0.240f,  0.780f) },
                        { "wheelML",      new Vector3(-0.800f, -0.240f,  0.260f) },
                        { "wheelMR",      new Vector3( 0.800f, -0.240f,  0.260f) },
                        { "wheelML2",     new Vector3(-0.800f, -0.240f, -0.260f) },
                        { "wheelMR2",     new Vector3( 0.800f, -0.240f, -0.260f) },
                        { "wheelRL",      new Vector3(-0.800f, -0.240f, -0.780f) },
                        { "wheelRR",      new Vector3( 0.800f, -0.240f, -0.780f) },
                        { "mastMount",    new Vector3(-0.380f,  0.425f,  0.680f) },
                        { "payloadTop1",  new Vector3( 0.000f,  0.425f, -0.100f) },
                        { "payloadFront", new Vector3( 0.000f,  0.140f,  1.050f) }
                    }
                },

                // -------------------------------------------------------------------------
                // 4. FOLDABLE FRAME (Apollo LRV) - 4 Tekerlekli Ultra-Hafif Açık İskelet
                //    Ebatlar: 1.30m (G) x 0.22m (Y) x 2.30m (U)
                // -------------------------------------------------------------------------
                new RoverChassisProfile
                {
                    id = "apollo_lrv",
                    displayName = "Foldable Frame (Apollo LRV)",
                    transformName = "Chassis_ApolloLRV",
                    baseMass = 0.095f,
                    baseCost = 1100f,
                    baseElectricCharge = 350f,
                    baseReactionTorque = 0.4f,
                    sasServiceLevel = 1,
                    crashTolerance = 16f,
                    maxTemp = 1400,
                    skinMaxTemp = 1500,
                    supportsRockerBogie = false,
                    referenceDimensions = new Vector3(1.30f, 0.22f, 2.30f),
                    activeNodes = new HashSet<string>
                    {
                        "top", "bottom", "front", "rear",
                        "wheelFL", "wheelFR", "wheelRL", "wheelRR",
                        "payloadTop1", "payloadTop2", "payloadFront", "payloadRear", "mastMount"
                    },
                    chassisNodePositions = new Dictionary<string, Vector3>
                    {
                        { "top",          new Vector3( 0.000f,  0.110f,  0.000f) },
                        { "bottom",       new Vector3( 0.000f, -0.110f,  0.000f) },
                        { "front",        new Vector3( 0.000f,  0.000f,  1.150f) },
                        { "rear",         new Vector3( 0.000f,  0.000f, -1.150f) },
                        { "wheelFL",      new Vector3(-0.650f, -0.040f,  0.920f) },
                        { "wheelFR",      new Vector3( 0.650f, -0.040f,  0.920f) },
                        { "wheelRL",      new Vector3(-0.650f, -0.040f, -0.920f) },
                        { "wheelRR",      new Vector3( 0.650f, -0.040f, -0.920f) },
                        { "mastMount",    new Vector3( 0.000f,  0.110f,  0.980f) },
                        { "payloadTop1",  new Vector3( 0.000f,  0.110f,  0.300f) },
                        { "payloadTop2",  new Vector3( 0.000f,  0.110f, -0.550f) },
                        { "payloadFront", new Vector3( 0.000f,  0.020f,  1.150f) },
                        { "payloadRear",  new Vector3( 0.000f,  0.020f, -1.150f) }
                    }
                },

                // -------------------------------------------------------------------------
                // 5. ARMORED EXPLORER (Sci-Fi) - Ağır Zırhlı 6 Tekerlekli / Paletli Şasi
                //    Ebatlar: 1.80m (G) x 0.80m (Y) x 2.60m (U)
                // -------------------------------------------------------------------------
                new RoverChassisProfile
                {
                    id = "scifi_armored",
                    displayName = "Armored Explorer (Sci-Fi)",
                    transformName = "Chassis_SciFi",
                    baseMass = 0.55f,
                    baseCost = 4200f,
                    baseElectricCharge = 2500f,
                    baseReactionTorque = 4.0f,
                    sasServiceLevel = 3,
                    crashTolerance = 45f,
                    maxTemp = 2600,
                    skinMaxTemp = 2900,
                    supportsRockerBogie = true,
                    referenceDimensions = new Vector3(1.80f, 0.80f, 2.60f),
                    activeNodes = new HashSet<string>
                    {
                        "top", "bottom", "front", "rear",
                        "wheelFL", "wheelFR", "wheelML", "wheelMR", "wheelRL", "wheelRR",
                        "payloadTop1", "payloadTop2", "payloadFront", "payloadRear", "mastMount"
                    },
                    chassisNodePositions = new Dictionary<string, Vector3>
                    {
                        { "top",          new Vector3( 0.000f,  0.400f,  0.000f) },
                        { "bottom",       new Vector3( 0.000f, -0.400f,  0.000f) },
                        { "front",        new Vector3( 0.000f,  0.000f,  1.300f) },
                        { "rear",         new Vector3( 0.000f,  0.000f, -1.300f) },
                        { "wheelFL",      new Vector3(-0.900f, -0.180f,  1.000f) },
                        { "wheelFR",      new Vector3( 0.900f, -0.180f,  1.000f) },
                        { "wheelML",      new Vector3(-0.900f, -0.180f,  0.000f) },
                        { "wheelMR",      new Vector3( 0.900f, -0.180f,  0.000f) },
                        { "wheelRL",      new Vector3(-0.900f, -0.180f, -1.000f) },
                        { "wheelRR",      new Vector3( 0.900f, -0.180f, -1.000f) },
                        { "mastMount",    new Vector3( 0.000f,  0.400f,  0.880f) },
                        { "payloadTop1",  new Vector3( 0.000f,  0.400f,  0.300f) },
                        { "payloadTop2",  new Vector3( 0.000f,  0.400f, -0.600f) },
                        { "payloadFront", new Vector3( 0.000f,  0.120f,  1.300f) },
                        { "payloadRear",  new Vector3( 0.000f,  0.160f, -1.300f) }
                    }
                }
            };
        }

        private void SetupEditorUI()
        {
            BaseField chassisField = Fields[nameof(chassisIndex)];
            if (chassisField?.uiControlEditor is UI_ChooseOption chooseOption)
            {
                string[] options = new string[_profiles.Count];
                for (int i = 0; i < _profiles.Count; i++)
                {
                    options[i] = _profiles[i].displayName;
                }
                chooseOption.options = options;
            }
        }

        private void CachePrefabNodePositions()
        {
            _prefabFallbackNodePositions.Clear();
            _baseNodeSizes.Clear();

            List<AttachNode> sourceNodes = (part.partInfo != null && part.partInfo.partPrefab != null)
                ? part.partInfo.partPrefab.attachNodes
                : part.attachNodes;

            foreach (AttachNode srcNode in sourceNodes)
            {
                if (srcNode == null || string.IsNullOrEmpty(srcNode.id))
                    continue;

                _prefabFallbackNodePositions[srcNode.id] = srcNode.originalPosition;
                _baseNodeSizes[srcNode.id] = srcNode.size;
            }

            foreach (AttachNode node in part.attachNodes)
            {
                if (node != null && !_prefabFallbackNodePositions.ContainsKey(node.id))
                {
                    _prefabFallbackNodePositions[node.id] = node.originalPosition;
                    _baseNodeSizes[node.id] = node.size;
                }
            }
        }

        #endregion

        #region Ana Konfigürasyon, Mesh Ölçekleme ve Alt-Sistem Güncellemesi

        private void ApplyRoverConfiguration(bool repositionAttachedParts, bool rebuildDragCubes)
        {
            chassisIndex = Mathf.Clamp(chassisIndex, 0, _profiles.Count - 1);
            RoverChassisProfile profile = _profiles[chassisIndex];

            Vector3 scaleVec = new Vector3(widthScale, heightScale, lengthScale);
            Vector3 newEffectiveDimensions = Vector3.Scale(profile.referenceDimensions, scaleVec);

            // 1. Görsel Mesh ve Collider Hiyerarşisini Güncelle
            UpdateChassisMeshes(profile, scaleVec);

            // 2. Şasiye Özel Anatomik Bağlantı Noktalarını (AttachNode) ve Yüzey Parçalarını Güncelle
            UpdateNodePositions(profile, scaleVec, repositionAttachedParts);
            if (repositionAttachedParts)
            {
                RepositionSurfaceAttachedChildren(newEffectiveDimensions);
            }
            _previousEffectiveDimensions = newEffectiveDimensions;

            // 3. Kütle, Maliyet, Kaynaklar (EC), Tork, SAS ve Termal Değerleri Güncelle
            UpdateSubsystemsAndStats(profile, scaleVec);

            // 4. Rocker-Bogie ve Foldable LRV Modülleri Varsa Şasi Değişimini Bildir
            ProceduralRockerBogie rockerBogie = part.FindModuleImplementing<ProceduralRockerBogie>();
            if (rockerBogie != null)
            {
                rockerBogie.OnChassisOrScaleUpdated(profile, scaleVec);
            }

            ProceduralFoldableLRV foldableLrv = part.FindModuleImplementing<ProceduralFoldableLRV>();
            if (foldableLrv != null)
            {
                foldableLrv.OnChassisChanged(chassisIndex);
                if (foldableLrv.isFoldedCrate)
                {
                    foldableLrv.ApplyFoldedCrateState(true);
                }
            }

            // 5. Uçuş sahnesinde aerodinamik ve termal hesaplama için DragCube yenile
            if (rebuildDragCubes && part.DragCubes != null)
            {
                part.DragCubes.ForceUpdate(true, true, false);
            }

            // 6. Editor'de YALNIZCA kullanıcı bir ayar değiştirdiğinde (repositionAttachedParts == true)
            // ve parça gemiye yerleşmiş durumdayken gemi modifikasyon event'ini tetikle.
            // (OnStart esnasında tetiklemek EditorLogic FSM'ini ve Undo/Redo durumunu bozar!)
            if (repositionAttachedParts && HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
            {
                if (EditorLogic.RootPart != null && part.localRoot == EditorLogic.RootPart)
                {
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }
            }
        }

        /// <summary>
        /// KSP'de .mu modelleri "part/model/ProceduralRovers/.../RoverCoreProcedural(Clone)/Chassis_*"
        /// hiyerarşisiyle yüklendiği için doğrudan modelRoot.Find() yerine derin arama (FindDeepChild) ile
        /// tüm şasi Transform referanslarını önbelleğe alır ve seçili şasiyi aktif edip diğerlerini gizler.
        /// </summary>
        private void CacheAndInitializeChassisTransforms()
        {
            if (part == null || part.transform == null || _profiles == null)
                return;

            _chassisTransforms.Clear();
            foreach (RoverChassisProfile p in _profiles)
            {
                Transform tr = FindDeepChild(part.transform, p.transformName);
                if (tr != null)
                {
                    _chassisTransforms[p.id] = tr;
                }
            }

            int activeIdx = Mathf.Clamp(chassisIndex, 0, _profiles.Count - 1);
            UpdateChassisMeshes(_profiles[activeIdx], new Vector3(widthScale, heightScale, lengthScale));
        }

        private void UpdateChassisMeshes(RoverChassisProfile activeProfile, Vector3 scaleVec)
        {
            if (_chassisTransforms.Count == 0)
            {
                foreach (RoverChassisProfile p in _profiles)
                {
                    Transform tr = FindDeepChild(part.transform, p.transformName);
                    if (tr != null)
                        _chassisTransforms[p.id] = tr;
                }
            }

            bool foundSpecificMesh = false;
            foreach (RoverChassisProfile p in _profiles)
            {
                if (_chassisTransforms.TryGetValue(p.id, out Transform chassisTr) && chassisTr != null)
                {
                    bool isTarget = (p.id == activeProfile.id);
                    chassisTr.gameObject.SetActive(isTarget);
                    if (isTarget)
                    {
                        chassisTr.localScale = scaleVec;
                        foundSpecificMesh = true;
                    }
                }
            }

            if (!foundSpecificMesh)
            {
                Transform fallbackHull = FindDeepChild(part.transform, "CoreHull");
                if (fallbackHull != null)
                {
                    fallbackHull.localScale = scaleVec;
                }
            }
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

        #region Şasiye Özel Dinamik Node Kaydırma (Anatomical Node Shifting)

        public void UpdateNodePositions(RoverChassisProfile profile, Vector3 scaleVec, bool repositionAttachedParts)
        {
            if (part.attachNodes == null)
                return;

            foreach (AttachNode node in part.attachNodes)
            {
                if (node == null || string.IsNullOrEmpty(node.id))
                    continue;

                // Önce aktif şasinin kendi anatomik node haritasına bak; yoksa prefab fallback değerini kullan
                if (profile.chassisNodePositions == null || !profile.chassisNodePositions.TryGetValue(node.id, out Vector3 basePos))
                {
                    if (!_prefabFallbackNodePositions.TryGetValue(node.id, out basePos))
                        basePos = node.originalPosition;
                }

                bool isNodeActiveForChassis = profile.activeNodes.Contains(node.id);
                Vector3 newLocalPos = Vector3.Scale(basePos, scaleVec);

                if (!isNodeActiveForChassis && node.attachedPart == null)
                {
                    node.position = Vector3.zero;
                    node.size = 0;
                    continue;
                }

                if (_baseNodeSizes.TryGetValue(node.id, out int baseSize))
                {
                    float avgScale = (scaleVec.x + scaleVec.z) * 0.5f;
                    node.size = Mathf.Clamp(Mathf.RoundToInt(baseSize * avgScale), 0, 3);
                }

                Vector3 oldLocalPos = node.position;
                Vector3 localDelta = newLocalPos - oldLocalPos;

                node.position = newLocalPos;

                if (repositionAttachedParts && node.attachedPart != null && localDelta.sqrMagnitude > 1e-7f)
                {
                    TranslateNodeAttachedPart(node.attachedPart, localDelta);
                }
            }
        }

        private void TranslateNodeAttachedPart(Part attachedPart, Vector3 localDelta)
        {
            Vector3 worldDelta = part.transform.TransformVector(localDelta);

            if (attachedPart.parent == this.part)
            {
                attachedPart.transform.position += worldDelta;
                attachedPart.attPos0 += localDelta;
            }
            else if (attachedPart == this.part.parent)
            {
                part.transform.position -= worldDelta;

                if (part.parent != null)
                {
                    Vector3 parentSpaceDelta = part.parent.transform.InverseTransformVector(-worldDelta);
                    part.attPos0 += parentSpaceDelta;
                }

                foreach (Part child in part.children)
                {
                    if (child != null)
                    {
                        child.transform.position += worldDelta;
                    }
                }
            }
        }

        /// <summary>
        /// Yüzeye yapışık (srfAttach) çocuk parçaları sadece slider ölçeğine göre değil,
        /// şasi tipi değiştiğinde değişen gerçek gövde ebatları oranına (newEffectiveDimensions / _previousEffectiveDimensions) göre kaydırır.
        /// Böylece Heavy Science şasisinden Micro veya Apollo LRV şasisine geçildiğinde yüzeydeki güneş panelleri de yeni gövde kabuğuna tam yapışır!
        /// </summary>
        private void RepositionSurfaceAttachedChildren(Vector3 newEffectiveDimensions)
        {
            if (part.children == null || part.children.Count == 0)
                return;

            Vector3 dimRatio = new Vector3(
                _previousEffectiveDimensions.x > 1e-4f ? newEffectiveDimensions.x / _previousEffectiveDimensions.x : 1f,
                _previousEffectiveDimensions.y > 1e-4f ? newEffectiveDimensions.y / _previousEffectiveDimensions.y : 1f,
                _previousEffectiveDimensions.z > 1e-4f ? newEffectiveDimensions.z / _previousEffectiveDimensions.z : 1f
            );

            if ((dimRatio - Vector3.one).sqrMagnitude < 1e-7f)
                return;

            foreach (Part child in part.children)
            {
                if (child == null || child.attachMode != AttachModes.SRF_ATTACH)
                    continue;

                Vector3 childLocalPos = part.transform.InverseTransformPoint(child.transform.position);
                Vector3 scaledLocalPos = Vector3.Scale(childLocalPos, dimRatio);
                Vector3 worldOffset = part.transform.TransformPoint(scaledLocalPos) - child.transform.position;

                child.transform.position += worldOffset;
                child.attPos0 = Vector3.Scale(child.attPos0, dimRatio);
            }
        }

        #endregion

        #region Alt-Sistemler (Batarya, Reaction Wheel, Probe SAS, Termal) ve IPart*Modifier

        private void UpdateSubsystemsAndStats(RoverChassisProfile profile, Vector3 scaleVec)
        {
            float rawVolumeFactor = scaleVec.x * scaleVec.y * scaleVec.z;
            float structuralFactor = Mathf.Pow(rawVolumeFactor, 0.85f);

            float prefabMass = (part.partInfo != null && part.partInfo.partPrefab != null)
                ? part.partInfo.partPrefab.mass
                : part.mass;
            float prefabCost = (part.partInfo != null)
                ? part.partInfo.cost
                : 1500f;

            float targetMass = profile.baseMass * structuralFactor;
            ProceduralFoldableLRV foldableLrv = part.FindModuleImplementing<ProceduralFoldableLRV>();
            if (foldableLrv != null && foldableLrv.isFoldedCrate)
            {
                // Katlanmış taşıma sandığı (Crate) modundayken tek bir Kerbal'ın sırt çantasında
                // taşıyabilmesi için kompakt paket kütlesi (35 kg) uygulanır.
                targetMass = 0.035f;
            }
            float targetCost = profile.baseCost * structuralFactor;

            _massDelta = targetMass - prefabMass;
            _costDelta = targetCost - prefabCost;

            _currentSize = Vector3.Scale(profile.referenceDimensions, scaleVec);
            activeChassisDisplay = profile.displayName;
            dimensionsDisplay = $"{_currentSize.x:F2}m (G) x {_currentSize.y:F2}m (Y) x {_currentSize.z:F2}m (U)";

            // Root GameObject üzerinde fiziksel çarpıştırıcı varsa ve kutu modunda değilsek aktif şasi boyutuna eşitle
            BoxCollider rootCol = part.gameObject.GetComponent<BoxCollider>();
            if (rootCol != null && (foldableLrv == null || !foldableLrv.isFoldedCrate))
            {
                rootCol.size = _currentSize;
                rootCol.center = Vector3.zero;
            }

            part.crashTolerance = profile.crashTolerance;
            part.maxTemp = profile.maxTemp;
            part.skinMaxTemp = profile.skinMaxTemp;

            PartResource ecResource = part.Resources["ElectricCharge"];
            if (ecResource != null)
            {
                double fillRatio = ecResource.maxAmount > 0 ? (ecResource.amount / ecResource.maxAmount) : 1.0;
                double newMaxEC = Math.Round(profile.baseElectricCharge * rawVolumeFactor, 1);

                ecResource.maxAmount = newMaxEC;
                if (HighLogic.LoadedSceneIsEditor)
                {
                    ecResource.amount = Math.Round(newMaxEC * fillRatio, 1);
                }
                else
                {
                    ecResource.amount = Math.Min(ecResource.amount, ecResource.maxAmount);
                }
            }

            ModuleReactionWheel rw = part.FindModuleImplementing<ModuleReactionWheel>();
            if (rw != null)
            {
                float scaledTorque = profile.baseReactionTorque * Mathf.Pow(rawVolumeFactor, 0.65f);
                rw.PitchTorque = scaledTorque;
                rw.YawTorque = scaledTorque;
                rw.RollTorque = scaledTorque * 0.75f;
            }

            ModuleSAS sas = part.FindModuleImplementing<ModuleSAS>();
            if (sas != null)
            {
                sas.SASServiceLevel = profile.sasServiceLevel;
            }
        }

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => _massDelta;
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) => _costDelta;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        public Vector3 GetModuleSize(Vector3 defaultSize, ModifierStagingSituation sit) => _currentSize - defaultSize;
        public ModifierChangeWhen GetModuleSizeChangeWhen() => ModifierChangeWhen.FIXED;

        #endregion
    }
}
