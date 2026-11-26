using System;
using UnityEngine;

namespace ProceduralRovers
{
    /// <summary>
    /// Modüler Bilim, Güç ve Yardımcı Ekipman (Payload) Kontrol Modülü.
    /// 1) Rover şasisinin boyutuna uyum sağlaması için ekipmanları prosedürel ölçekler (payloadScale) ve mount node'unu kaydırır.
    /// 2) MMRTG (ModuleGenerator), Güneş Paneli (ModuleDeployableSolarPanel) ve Matkap (ModuleResourceHarvester)
    ///    çıktılarını ölçeğe göre gerçek zamanlı ayarlar.
    /// 3) MastCam, Spektrometre ve Robotik Matkapta Pan/Tilt (Yaw/Pitch) kule yönlendirme ve
    ///    Unity .anim dosyasına ihtiyaç duymayan C# tabanlı matkap ucu dönüş / kol açılma animasyonu sağlar.
    /// </summary>
    public class ProceduralRoverPayload : PartModule, IPartMassModifier, IPartCostModifier
    {
        #region KSPFields (PAW Kontrolleri)

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Ekipman Ölçeği"),
         UI_FloatRange(minValue = 0.5f, maxValue = 2.0f, stepIncrement = 0.05f, affectSymCounterparts = UI_Scene.Editor, scene = UI_Scene.Editor)]
        public float payloadScale = 1.0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Kule Yatay Açı (Yaw)"),
         UI_FloatRange(minValue = -180f, maxValue = 180f, stepIncrement = 2.5f, affectSymCounterparts = UI_Scene.All)]
        public float turretYaw = 0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Kule Dikey Açı (Pitch)"),
         UI_FloatRange(minValue = -45f, maxValue = 65f, stepIncrement = 2.5f, affectSymCounterparts = UI_Scene.All)]
        public float turretPitch = 0f;

        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = true, guiName = "Otonom Çevre Tarama"),
         UI_Toggle(disabledText = "Manuel", enabledText = "Aktif Tarama", affectSymCounterparts = UI_Scene.Flight)]
        public bool autoPanScan = false;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Mekanizma Durumu"),
         UI_Toggle(disabledText = "Katlı / Kapalı", enabledText = "Açık / Aktif", affectSymCounterparts = UI_Scene.All)]
        public bool isMechanismDeployed = false;

        // CFG'den gelen yapılandırma parametreleri
        [KSPField] public string mountNodeId = "mount";
        [KSPField] public bool hasTurretControl = false;
        [KSPField] public bool hasDeployableMechanism = false;
        [KSPField] public string yawTransformName = "Turret_Yaw";
        [KSPField] public string pitchTransformName = "Turret_Pitch";
        [KSPField] public string deployHingeTransformName = "";
        [KSPField] public string spinningBitTransformName = "DrillBit_Mesh";
        [KSPField] public float deployedAngleDeg = -35f;
        [KSPField] public float retractedAngleDeg = 45f;

        [KSPField] public float baseGeneratorOutput = 0f;   // MMRTG için 1.0x ölçekte EC/sn
        [KSPField] public float baseSolarChargeRate = 0f;   // Güneş paneli için 1.0x ölçekte EC/sn
        [KSPField] public float baseDrillEfficiency = 0f;   // Matkap için 1.0x ölçekte baz verimlilik

        // PAW Bilgi Satırı
        [KSPField(guiActiveEditor = true, guiActive = true, guiName = "Ekipman Durumu")]
        public string payloadTelemetry = "Hazır (1.00x)";

        #endregion

        #region Dahili Değişkenler

        private Vector3 _baseMountNodePos = Vector3.zero;
        private bool _hasBaseMountNode = false;

        private Transform _yawTransform;
        private Transform _pitchTransform;
        private Transform _deployHingeTransform;
        private Transform _spinningBitTransform;
        private ModuleResourceHarvester _harvester;

        private float _currentAnimatedYaw = 0f;
        private float _currentAnimatedPitch = 0f;
        private float _currentDeployAngle = 0f;
        private float _scanTimer = 0f;

        private float _previousScale = 1.0f;
        private bool _isInitialized = false;

        private float _massDelta = 0f;
        private float _costDelta = 0f;

        #endregion

        #region Yaşam Döngüsü (OnStart, Update)

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _harvester = part.FindModuleImplementing<ModuleResourceHarvester>();

            CacheBaseMountNode();
            DiscoverTurretTransforms();
            ConfigureTurretUIVisibility();

            _previousScale = payloadScale;
            _currentAnimatedYaw = turretYaw;
            _currentAnimatedPitch = turretPitch;
            _currentDeployAngle = isMechanismDeployed ? deployedAngleDeg : retractedAngleDeg;
            _isInitialized = true;

            ApplyPayloadScaling(repositionMountNode: false);
            ApplyTurretRotation(_currentAnimatedYaw, _currentAnimatedPitch);
        }

        public void Update()
        {
            if (!_isInitialized)
                return;

            // 1. Editor'de Ölçek Değişimi Kontrolü
            if (HighLogic.LoadedSceneIsEditor && Mathf.Abs(payloadScale - _previousScale) > 1e-4f)
            {
                ApplyPayloadScaling(repositionMountNode: true);
                _previousScale = payloadScale;
            }

            // 2. Kule (Pan/Tilt) Yönlendirme ve Otonom Tarama Animasyonu
            if (hasTurretControl)
            {
                UpdateTurretOrientation(Time.deltaTime);
            }

            // 3. C# Tabanlı Katlanır Kol / Kapak & Matkap Ucu Dönüş Animasyonu
            if (hasDeployableMechanism)
            {
                UpdateMechanismAnimation(Time.deltaTime);
            }
        }

        #endregion

        #region Kurulum ve Transform Keşfi

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

        private void DiscoverTurretTransforms()
        {
            Transform modelRoot = part.transform.Find("model");
            if (modelRoot == null)
                return;

            if (hasTurretControl)
            {
                _yawTransform = FindDeepChild(modelRoot, yawTransformName);
                _pitchTransform = FindDeepChild(modelRoot, pitchTransformName);
            }

            if (hasDeployableMechanism)
            {
                if (!string.IsNullOrEmpty(deployHingeTransformName))
                    _deployHingeTransform = FindDeepChild(modelRoot, deployHingeTransformName);
                if (!string.IsNullOrEmpty(spinningBitTransformName))
                    _spinningBitTransform = FindDeepChild(modelRoot, spinningBitTransformName);
            }
        }

        private void ConfigureTurretUIVisibility()
        {
            Fields[nameof(turretYaw)].guiActive = hasTurretControl;
            Fields[nameof(turretYaw)].guiActiveEditor = hasTurretControl;
            Fields[nameof(turretPitch)].guiActive = hasTurretControl;
            Fields[nameof(turretPitch)].guiActiveEditor = hasTurretControl;
            Fields[nameof(autoPanScan)].guiActive = hasTurretControl;

            Fields[nameof(isMechanismDeployed)].guiActive = hasDeployableMechanism;
            Fields[nameof(isMechanismDeployed)].guiActiveEditor = hasDeployableMechanism;
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

        #region Prosedürel Ölçekleme ve Alt-Modül (RTG, Solar, Drill) Senkronizasyonu

        public void ApplyPayloadScaling(bool repositionMountNode)
        {
            payloadScale = Mathf.Clamp(payloadScale, 0.5f, 2.0f);

            // 1. Görsel Model Ölçekleme
            Transform modelTransform = part.transform.Find("model");
            if (modelTransform != null)
            {
                modelTransform.localScale = Vector3.one * payloadScale;
            }

            // 2. Mount Node Pozisyonunu Kaydır
            UpdateMountNodePosition(repositionMountNode);

            // 3. Kütle ve Maliyet Hesaplaması
            float prefabMass = (part.partInfo != null && part.partInfo.partPrefab != null)
                ? part.partInfo.partPrefab.mass
                : part.mass;
            float prefabCost = (part.partInfo != null)
                ? part.partInfo.cost
                : 500f;

            float massFactor = Mathf.Pow(payloadScale, 2.3f);
            float costFactor = Mathf.Pow(payloadScale, 1.9f);

            _massDelta = (prefabMass * massFactor) - prefabMass;
            _costDelta = (prefabCost * costFactor) - prefabCost;

            // 4. Alt-Modül Çıktılarını Ölçekle (RTG, Güneş Paneli, Matkap)
            string perfInfo = $"{payloadScale:F2}x Ölçek";

            if (baseGeneratorOutput > 0f)
            {
                ModuleGenerator gen = part.FindModuleImplementing<ModuleGenerator>();
                if (gen != null && gen.resHandler != null && gen.resHandler.outputResources != null)
                {
                    double scaledRate = Math.Round(baseGeneratorOutput * Mathf.Pow(payloadScale, 2.3f), 2);
                    for (int i = 0; i < gen.resHandler.outputResources.Count; i++)
                    {
                        if (gen.resHandler.outputResources[i].name == "ElectricCharge")
                        {
                            gen.resHandler.outputResources[i].rate = scaledRate;
                            perfInfo = $"{scaledRate:F2} EC/sn (RTG)";
                        }
                    }
                }
            }

            if (baseSolarChargeRate > 0f)
            {
                ModuleDeployableSolarPanel solar = part.FindModuleImplementing<ModuleDeployableSolarPanel>();
                if (solar != null)
                {
                    float scaledSolar = baseSolarChargeRate * (payloadScale * payloadScale);
                    solar.chargeRate = scaledSolar;
                    perfInfo = $"{scaledSolar:F2} EC/sn (Solar)";
                }
            }

            if (baseDrillEfficiency > 0f && _harvester != null)
            {
                float scaledEff = baseDrillEfficiency * (payloadScale * payloadScale);
                _harvester.Efficiency = scaledEff;
                perfInfo = $"Sondaj Verimi: {(scaledEff * 100f):F0}%";
            }

            payloadTelemetry = perfInfo;

            if (repositionMountNode && HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
            {
                if (EditorLogic.RootPart != null && part.localRoot == EditorLogic.RootPart)
                {
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }
            }
        }

        private void UpdateMountNodePosition(bool repositionMountNode)
        {
            if (!_hasBaseMountNode)
                return;

            AttachNode mountNode = part.FindAttachNode(mountNodeId);
            if (mountNode == null)
                return;

            Vector3 newLocalPos = _baseMountNodePos * payloadScale;
            Vector3 oldLocalPos = mountNode.position;
            Vector3 localDelta = newLocalPos - oldLocalPos;

            mountNode.position = newLocalPos;

            if (!repositionMountNode || mountNode.attachedPart == null || localDelta.sqrMagnitude < 1e-7f)
                return;

            Vector3 worldDelta = part.transform.TransformVector(localDelta);

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

        #endregion

        #region Pan / Tilt Kule ve Mekanizma Animasyonları

        private void UpdateTurretOrientation(float dt)
        {
            float targetYaw = turretYaw;
            float targetPitch = turretPitch;

            if (HighLogic.LoadedSceneIsFlight && autoPanScan)
            {
                _scanTimer += dt;
                targetYaw = Mathf.Sin(_scanTimer * 0.45f) * 75f;
                targetPitch = Mathf.Sin(_scanTimer * 0.90f) * 18f - 8f;
            }

            _currentAnimatedYaw = Mathf.Lerp(_currentAnimatedYaw, targetYaw, dt * 5.0f);
            _currentAnimatedPitch = Mathf.Lerp(_currentAnimatedPitch, targetPitch, dt * 5.0f);

            ApplyTurretRotation(_currentAnimatedYaw, _currentAnimatedPitch);
        }

        private void ApplyTurretRotation(float yawDeg, float pitchDeg)
        {
            if (_yawTransform != null)
            {
                _yawTransform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            }

            if (_pitchTransform != null)
            {
                _pitchTransform.localRotation = Quaternion.Euler(pitchDeg, 0f, 0f);
            }
        }

        private void UpdateMechanismAnimation(float dt)
        {
            bool shouldBeOut = isMechanismDeployed || (_harvester != null && _harvester.IsActivated);
            float targetAngle = shouldBeOut ? deployedAngleDeg : retractedAngleDeg;

            _currentDeployAngle = Mathf.Lerp(_currentDeployAngle, targetAngle, dt * 4.0f);

            if (_deployHingeTransform != null)
            {
                _deployHingeTransform.localRotation = Quaternion.Euler(_currentDeployAngle, 0f, 0f);
            }

            // Matkap aktif veya mekanizma açıkken uç (DrillBit_Mesh) kendi ekseninde döner
            if (_spinningBitTransform != null && shouldBeOut && HighLogic.LoadedSceneIsFlight)
            {
                float rpmSpeed = (_harvester != null && _harvester.IsActivated) ? 540f : 120f;
                _spinningBitTransform.Rotate(Vector3.forward, rpmSpeed * dt, Space.Self);
            }
        }

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => _massDelta;
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) => _costDelta;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        #endregion
    }
}
