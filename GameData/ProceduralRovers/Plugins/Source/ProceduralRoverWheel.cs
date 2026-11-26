using System;
using UnityEngine;
using ModuleWheels;

namespace ProceduralRovers
{
    /// <summary>
    /// Gezegen/Yüzey tipine göre tekerlek ve paletlerin sürtünme (Friction) ve süspansiyon davranışını optimize eden profiller.
    /// KSP 1.12.x'te düşük yerçekimli uydularda (Minmus, Gilly, Mun) yaşanan kayma/zıplama bug'larını önler.
    /// </summary>
    public enum SurfaceTractionPreset
    {
        Standard = 0,       // Kerbin, Laythe (1.0x Friction, Dengeli Amortisör)
        LowGravity = 1,     // Mun, Minmus, Ike, Dres (1.65x Friction, Yumuşak Yay / Yüksek Sönümleme)
        HighSlopeRegolith = 2, // Duna, Moho (1.35x Friction, Orta-Sert Süspansiyon)
        IceSurface = 3,     // Vall, Eeloo, Kutup Buzulları (2.10x Çivili/Palet Tutuşu, Kayma Koruması)
        HeavyLoad = 4       // Eve veya Ağır Maden Rover'ları (1.25x Friction, 1.8x Sert Yay)
    }

    /// <summary>
    /// KSP 1.12.5 Prosedürel Tekerlek ve Süspansiyon Kontrol Modülü.
    /// Stok ModuleWheelBase, ModuleWheelSuspension, ModuleWheelMotor ve ModuleWheelBrakes modüllerini
    /// prosedürel ölçeklemeye (wheelScale) ve gezegen çekim/zemin profillerine göre gerçek zamanlı senkronize eder.
    /// </summary>
    public class ProceduralRoverWheel : PartModule, IPartMassModifier, IPartCostModifier
    {
        #region KSPFields (PAW Kontrolleri)

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Tekerlek Ölçeği"),
         UI_FloatRange(minValue = 0.5f, maxValue = 2.5f, stepIncrement = 0.05f, affectSymCounterparts = UI_Scene.Editor, scene = UI_Scene.Editor)]
        public float wheelScale = 1.0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Zemin Tutuş Modu"),
         UI_ChooseOption(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]
        public int tractionPresetIndex = 0;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Yay Sertliği (Çarpan)"),
         UI_FloatRange(minValue = 0.4f, maxValue = 3.0f, stepIncrement = 0.1f, affectSymCounterparts = UI_Scene.All)]
        public float springMultiplier = 1.0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Amortisör Sönümü"),
         UI_FloatRange(minValue = 0.4f, maxValue = 3.0f, stepIncrement = 0.1f, affectSymCounterparts = UI_Scene.All)]
        public float damperMultiplier = 1.0f;

        // CFG'den okunan 1.0x ölçekteki referans değerler
        [KSPField] public float baseRadius = 0.35f;
        [KSPField] public float baseSuspensionDistance = 0.20f;
        [KSPField] public float baseSpringRatio = 12.0f;
        [KSPField] public float baseDamperRatio = 1.5f;
        [KSPField] public float baseDriveOutput = 2.2f;
        [KSPField] public float baseBrakeTorque = 15.0f;
        [KSPField] public float baseFrictionMultiplier = 1.0f;
        [KSPField] public string mountNodeId = "mount";

        // PAW Bilgi Satırları
        [KSPField(guiActiveEditor = true, guiActive = true, guiName = "Aktif Profil")]
        public string activeProfileDisplay = "Standart (Kerbin/Laythe)";

        [KSPField(guiActiveEditor = true, guiActive = true, guiName = "Efektif Çap / Tork")]
        public string wheelStatsDisplay = "0.70m | 2.2 kNm";

        #endregion

        #region Dahili Referanslar ve Önbellek

        private ModuleWheelBase _wheelBase;
        private ModuleWheelSuspension _wheelSuspension;
        private ModuleWheelMotor _wheelMotor;
        private ModuleWheelBrakes _wheelBrakes;

        private Vector3 _baseMountNodePos = Vector3.zero;
        private bool _hasBaseMountNode = false;

        private float _previousWheelScale = 1.0f;
        private int _previousTractionIndex = -1;
        private float _previousSpringMult = 1.0f;
        private float _previousDamperMult = 1.0f;
        private bool _isInitialized = false;

        private float _massDelta = 0f;
        private float _costDelta = 0f;

        private static readonly string[] TractionPresetNames =
        {
            "Standart (Kerbin/Laythe)",
            "Düşük Yerçekimi (Mun/Minmus)",
            "Kayalık/Regolit (Duna/Moho)",
            "Buzul Zemin (Vall/Eeloo)",
            "Ağır Yük / Yüksek G (Eve)"
        };

        #endregion

        #region Yaşam Döngüsü (OnAwake, OnStart, Update)

        public override void OnAwake()
        {
            base.OnAwake();
            EnsureVisualMeshesAreRenderable();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            EnsureVisualMeshesAreRenderable();
            LocateStockWheelModules();
            CacheBaseMountNode();
            SetupPAWOptions();

            _previousWheelScale = wheelScale;
            _previousTractionIndex = tractionPresetIndex;
            _previousSpringMult = springMultiplier;
            _previousDamperMult = damperMultiplier;
            _isInitialized = true;

            // Sahne başlangıcında ölçeği ve fizik parametrelerini uygula (parçaları kaydırmadan)
            ApplyWheelConfiguration(repositionMountNode: false);
        }

        public void Update()
        {
            if (!_isInitialized)
                return;

            bool scaleChanged = HighLogic.LoadedSceneIsEditor && Mathf.Abs(wheelScale - _previousWheelScale) > 1e-4f;
            bool tuningChanged = tractionPresetIndex != _previousTractionIndex
                                 || Mathf.Abs(springMultiplier - _previousSpringMult) > 1e-4f
                                 || Mathf.Abs(damperMultiplier - _previousDamperMult) > 1e-4f;

            if (scaleChanged || tuningChanged)
            {
                ApplyWheelConfiguration(repositionMountNode: scaleChanged);

                _previousWheelScale = wheelScale;
                _previousTractionIndex = tractionPresetIndex;
                _previousSpringMult = springMultiplier;
                _previousDamperMult = damperMultiplier;
            }
        }

        #endregion

        #region Stok Modül Keşfi ve Kurulum

        /// <summary>
        /// KSP'de Layer 26 (wheelCollidersIgnore) ana kameralar tarafından çizilmez (culling).
        /// Sadece görünmez fizik çarpıştırıcıları (CollisionEnhancer) Layer 26'da,
        /// WheelCollider ise Layer 27'de kalmalıdır. Görsel MeshRenderer objelerini Layer 0 (Default)
        /// katmanına çekerek tekerlek ve paletlerin hem VAB ikonunda hem uçuşta görünmesini garanti eder.
        /// </summary>
        private void EnsureVisualMeshesAreRenderable()
        {
            if (part == null || part.transform == null)
                return;

            MeshRenderer[] renderers = part.transform.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                if (mr != null && mr.gameObject.layer == 26)
                {
                    mr.gameObject.layer = 0;
                }
            }
        }

        private void LocateStockWheelModules()
        {
            _wheelBase = part.FindModuleImplementing<ModuleWheelBase>();
            _wheelSuspension = part.FindModuleImplementing<ModuleWheelSuspension>();
            _wheelMotor = part.FindModuleImplementing<ModuleWheelMotor>();
            _wheelBrakes = part.FindModuleImplementing<ModuleWheelBrakes>();
        }

        private void CacheBaseMountNode()
        {
            Part prefab = part.partInfo?.partPrefab;
            AttachNode sourceNode = prefab != null ? prefab.FindAttachNode(mountNodeId) : part.FindAttachNode(mountNodeId);
            if (sourceNode != null)
            {
                _baseMountNodePos = sourceNode.originalPosition;
                _hasBaseMountNode = true;
            }
        }

        private void SetupPAWOptions()
        {
            BaseField presetField = Fields[nameof(tractionPresetIndex)];
            if (presetField != null)
            {
                if (presetField.uiControlEditor is UI_ChooseOption editorChoose)
                    editorChoose.options = TractionPresetNames;

                if (presetField.uiControlFlight is UI_ChooseOption flightChoose)
                    flightChoose.options = TractionPresetNames;
            }
        }

        #endregion

        #region Tekerlek Ölçekleme, Fizik Senkronizasyonu ve Node Kaydırma

        public void ApplyWheelConfiguration(bool repositionMountNode)
        {
            wheelScale = Mathf.Clamp(wheelScale, 0.5f, 2.5f);
            tractionPresetIndex = Mathf.Clamp(tractionPresetIndex, 0, TractionPresetNames.Length - 1);

            // 1. Görsel Model Ölçekleme
            Transform modelTransform = part.transform.Find("model");
            if (modelTransform != null)
            {
                modelTransform.localScale = Vector3.one * wheelScale;
            }

            // 2. Bağlantı Noktasını (Mount AttachNode) Ölçekle ve Rover Şasisine Göre Hizala
            UpdateMountNodePosition(repositionMountNode);

            // 3. Yüzey Tutuş Profiline Göre Çarpanları Al
            GetPresetMultipliers((SurfaceTractionPreset)tractionPresetIndex,
                out float presetFrictionMult,
                out float presetSpringMult,
                out float presetDamperMult);

            // 4. Stok KSP Wheel Modüllerini Güncelle
            float effectiveRadius = baseRadius * wheelScale;
            float effectiveSuspDist = baseSuspensionDistance * wheelScale;

            // Motor torku ve fren gücü tekerlek hacmi/kaldıraç kolu ile (scale^2.2) ölçeklenir
            float torqueScaleFactor = Mathf.Pow(wheelScale, 2.2f);
            float effectiveDriveOutput = baseDriveOutput * torqueScaleFactor;
            float effectiveBrakeTorque = baseBrakeTorque * torqueScaleFactor;

            float finalFriction = baseFrictionMultiplier * presetFrictionMult;
            float finalSpring = baseSpringRatio * presetSpringMult * springMultiplier * Mathf.Pow(wheelScale, 1.2f);
            float finalDamper = baseDamperRatio * presetDamperMult * damperMultiplier * Mathf.Pow(wheelScale, 1.2f);

            if (_wheelBase != null)
            {
                _wheelBase.radius = effectiveRadius;
                _wheelBase.frictionMultiplier = finalFriction;
            }

            if (_wheelSuspension != null)
            {
                _wheelSuspension.suspensionDistance = effectiveSuspDist;
                _wheelSuspension.springRatio = finalSpring;
                _wheelSuspension.damperRatio = finalDamper;
            }

            if (_wheelMotor != null)
            {
                _wheelMotor.driveOutput = effectiveDriveOutput;
            }

            if (_wheelBrakes != null)
            {
                _wheelBrakes.maxBrakeTorque = effectiveBrakeTorque;
            }

            // 5. Kütle ve Maliyet Modifikatörlerini Güncelle
            UpdateMassAndCost();

            // 6. PAW Telemetri Metinlerini Güncelle
            activeProfileDisplay = TractionPresetNames[tractionPresetIndex];
            wheelStatsDisplay = $"{(effectiveRadius * 2f):F2}m Çap | {effectiveDriveOutput:F1} kNm";

            if (repositionMountNode && HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
            {
                if (EditorLogic.RootPart != null && part.localRoot == EditorLogic.RootPart)
                {
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }
            }
        }

        private void GetPresetMultipliers(SurfaceTractionPreset preset, out float frictionMult, out float springMult, out float damperMult)
        {
            switch (preset)
            {
                case SurfaceTractionPreset.LowGravity:
                    // Mun/Minmus'ta zıplamayı sönümlemek için yumuşak yay, yüksek amortisör ve yüksek tutuş
                    frictionMult = 1.65f;
                    springMult = 0.70f;
                    damperMult = 1.60f;
                    break;
                case SurfaceTractionPreset.HighSlopeRegolith:
                    frictionMult = 1.35f;
                    springMult = 1.15f;
                    damperMult = 1.25f;
                    break;
                case SurfaceTractionPreset.IceSurface:
                    frictionMult = 2.10f;
                    springMult = 0.90f;
                    damperMult = 1.40f;
                    break;
                case SurfaceTractionPreset.HeavyLoad:
                    frictionMult = 1.25f;
                    springMult = 1.80f;
                    damperMult = 1.50f;
                    break;
                case SurfaceTractionPreset.Standard:
                default:
                    frictionMult = 1.00f;
                    springMult = 1.00f;
                    damperMult = 1.00f;
                    break;
            }
        }

        /// <summary>
        /// Tekerlek ölçeği VAB/SPH'de değiştiğinde mount node'unu günceller ve tekerleği rover gövdesine bitişik tutar.
        /// </summary>
        private void UpdateMountNodePosition(bool repositionMountNode)
        {
            if (!_hasBaseMountNode)
                return;

            AttachNode mountNode = part.FindAttachNode(mountNodeId);
            if (mountNode == null)
                return;

            Vector3 newLocalPos = _baseMountNodePos * wheelScale;
            Vector3 oldLocalPos = mountNode.position;
            Vector3 localDelta = newLocalPos - oldLocalPos;

            mountNode.position = newLocalPos;

            if (!repositionMountNode || mountNode.attachedPart == null || localDelta.sqrMagnitude < 1e-7f)
                return;

            Vector3 worldDelta = part.transform.TransformVector(localDelta);

            // Tekerlek genellikle Rover gövdesinin ÇOCUĞUDUR (mountNode.attachedPart == part.parent)
            if (mountNode.attachedPart == part.parent)
            {
                part.transform.position -= worldDelta;
                if (part.parent != null)
                {
                    part.attPos0 += part.parent.transform.InverseTransformVector(-worldDelta);
                }
            }
            else if (mountNode.attachedPart.parent == part)
            {
                mountNode.attachedPart.transform.position += worldDelta;
                mountNode.attachedPart.attPos0 += localDelta;
            }
        }

        private void UpdateMassAndCost()
        {
            float prefabMass = (part.partInfo != null && part.partInfo.partPrefab != null)
                ? part.partInfo.partPrefab.mass
                : part.mass;
            float prefabCost = (part.partInfo != null)
                ? part.partInfo.cost
                : 400f;

            // Kütle ölçeğin 2.4 üssüyle, maliyet ise 2.0 üssüyle büyür
            float massFactor = Mathf.Pow(wheelScale, 2.4f);
            float costFactor = Mathf.Pow(wheelScale, 2.0f);

            _massDelta = (prefabMass * massFactor) - prefabMass;
            _costDelta = (prefabCost * costFactor) - prefabCost;
        }

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => _massDelta;
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) => _costDelta;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        #endregion
    }
}
