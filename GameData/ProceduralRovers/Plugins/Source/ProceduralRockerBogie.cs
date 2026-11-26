using System;
using UnityEngine;
using ModuleWheels;

namespace ProceduralRovers
{
    /// <summary>
    /// NASA/JPL Mars Keşif Araçları (Sojourner, Spirit/Opportunity, Curiosity, Perseverance) tarzı
    /// 6 Tekerlekli Rocker-Bogie Diferansiyel Süspansiyon ve Kinematik Dengeleyici Modülü.
    ///
    /// 1) Görsel Kinematik (IK): wheelFL/ML/RL (Sol) ve wheelFR/MR/RR (Sağ) yuvalarına bağlı tekerleklerin
    ///    anlık süspansiyon konumlarını okuyarak Rocker_L, Rocker_R, Bogie_L, Bogie_R ve DifferentialBar
    ///    mekanik kollarını gerçek zamanlı döndürür.
    /// 2) Fiziksel Diferansiyel Yük Paylaşımı: Bir tekerlek tümseğe çıktığında aynı taraftaki diğer tekerleklerin
    ///    zeminden kesilmesini önleyen kaldıraç kuvveti (Bogie Load Sharing) ve gövdeyi iki yanın ortalama
    ///    eğiminde tutan diferansiyel burulma dengesi uygular.
    /// </summary>
    public class ProceduralRockerBogie : PartModule
    {
        #region KSPFields (PAW Kontrolleri ve Telemetri)

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Rocker-Bogie Diferansiyel"),
         UI_Toggle(disabledText = "Kilitli", enabledText = "Aktif", affectSymCounterparts = UI_Scene.All)]
        public bool enableRockerBogie = true;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Yük Paylaşım Gücü"),
         UI_FloatRange(minValue = 0.2f, maxValue = 2.5f, stepIncrement = 0.1f, affectSymCounterparts = UI_Scene.All)]
        public float bogieLinkStiffness = 1.0f;

        [KSPField(guiActiveEditor = false, guiActive = true, guiName = "Rocker Eğimi (Sol / Sağ)")]
        public string rockerAngleTelemetry = "0.0° / 0.0°";

        // Unity Transform İsimleri (Aktif şasi altında veya model kökünde aranır)
        [KSPField] public string rockerLeftName = "Rocker_L";
        [KSPField] public string rockerRightName = "Rocker_R";
        [KSPField] public string bogieLeftName = "Bogie_L";
        [KSPField] public string bogieRightName = "Bogie_R";
        [KSPField] public string differentialBarName = "DifferentialBar";

        [KSPField] public float maxRockerPitchDeg = 28.0f;
        [KSPField] public float maxBogiePitchDeg = 35.0f;

        #endregion

        #region Bağlı Tekerlek Veri Yapısı

        private class WheelSlotLink
        {
            public string nodeId;
            public Part wheelPart;
            public ModuleWheelBase wheelBase;
            public ModuleWheelSuspension wheelSuspension;
            public Transform suspensionTransform;
            public Vector3 restLocalPos;
        }

        private readonly WheelSlotLink _fl = new WheelSlotLink { nodeId = "wheelFL" };
        private readonly WheelSlotLink _fr = new WheelSlotLink { nodeId = "wheelFR" };
        private readonly WheelSlotLink _ml = new WheelSlotLink { nodeId = "wheelML" };
        private readonly WheelSlotLink _mr = new WheelSlotLink { nodeId = "wheelMR" };
        private readonly WheelSlotLink _rl = new WheelSlotLink { nodeId = "wheelRL" };
        private readonly WheelSlotLink _rr = new WheelSlotLink { nodeId = "wheelRR" };

        private ProceduralRoverCore _roverCore;
        private bool _chassisSupportsRockerBogie = true;

        private Transform _rockerL;
        private Transform _rockerR;
        private Transform _bogieL;
        private Transform _bogieR;
        private Transform _diffBar;

        private float _currentRockerAngleL = 0f;
        private float _currentRockerAngleR = 0f;
        private float _currentBogieAngleL = 0f;
        private float _currentBogieAngleR = 0f;

        #endregion

        #region Yaşam Döngüsü (OnStart, OnDestroy, Update, FixedUpdate)

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _roverCore = part.FindModuleImplementing<ProceduralRoverCore>();
            if (_roverCore != null)
            {
                RoverChassisProfile profile = _roverCore.GetActiveProfile();
                _chassisSupportsRockerBogie = profile != null && profile.supportsRockerBogie;
            }

            DiscoverKinematicTransforms();
            RefreshAttachedWheelLinks();

            GameEvents.onVesselWasModified.Add(OnVesselModified);
            GameEvents.onEditorShipModified.Add(OnEditorShipModified);
        }

        public void OnDestroy()
        {
            GameEvents.onVesselWasModified.Remove(OnVesselModified);
            GameEvents.onEditorShipModified.Remove(OnEditorShipModified);
        }

        public void OnChassisOrScaleUpdated(RoverChassisProfile activeProfile, Vector3 scaleVec)
        {
            _chassisSupportsRockerBogie = activeProfile != null && activeProfile.supportsRockerBogie;
            Fields[nameof(enableRockerBogie)].guiActive = _chassisSupportsRockerBogie;
            Fields[nameof(enableRockerBogie)].guiActiveEditor = _chassisSupportsRockerBogie;
            Fields[nameof(bogieLinkStiffness)].guiActive = _chassisSupportsRockerBogie;
            Fields[nameof(bogieLinkStiffness)].guiActiveEditor = _chassisSupportsRockerBogie;
            Fields[nameof(rockerAngleTelemetry)].guiActive = _chassisSupportsRockerBogie;

            DiscoverKinematicTransforms();
            RefreshAttachedWheelLinks();
        }

        private void OnVesselModified(Vessel v)
        {
            if (v == vessel)
            {
                RefreshAttachedWheelLinks();
            }
        }

        private void OnEditorShipModified(ShipConstruct ship)
        {
            RefreshAttachedWheelLinks();
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || !_chassisSupportsRockerBogie || !enableRockerBogie)
                return;

            UpdateRockerBogieKinematics(Time.deltaTime);
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded)
                return;

            if (!_chassisSupportsRockerBogie || !enableRockerBogie)
                return;

            ApplyDifferentialSuspensionBalance();
        }

        #endregion

        #region Tekerlek ve Transform Keşfi

        private void DiscoverKinematicTransforms()
        {
            Transform searchRoot = part.transform.Find("model");
            if (searchRoot == null)
                return;

            // Aktif şasi objesi varsa önce onun altında ara
            if (_roverCore != null)
            {
                RoverChassisProfile profile = _roverCore.GetActiveProfile();
                if (profile != null)
                {
                    Transform chassisTr = searchRoot.Find(profile.transformName);
                    if (chassisTr != null)
                    {
                        searchRoot = chassisTr;
                    }
                }
            }

            _rockerL = FindDeepChild(searchRoot, rockerLeftName);
            _rockerR = FindDeepChild(searchRoot, rockerRightName);
            _bogieL = FindDeepChild(searchRoot, bogieLeftName);
            _bogieR = FindDeepChild(searchRoot, bogieRightName);
            _diffBar = FindDeepChild(searchRoot, differentialBarName);
        }

        public void RefreshAttachedWheelLinks()
        {
            BindWheelSlot(_fl);
            BindWheelSlot(_fr);
            BindWheelSlot(_ml);
            BindWheelSlot(_mr);
            BindWheelSlot(_rl);
            BindWheelSlot(_rr);
        }

        private void BindWheelSlot(WheelSlotLink slot)
        {
            slot.wheelPart = null;
            slot.wheelBase = null;
            slot.wheelSuspension = null;
            slot.suspensionTransform = null;

            AttachNode node = part.FindAttachNode(slot.nodeId);
            if (node == null || node.attachedPart == null)
                return;

            Part wPart = node.attachedPart;
            slot.wheelPart = wPart;
            slot.wheelBase = wPart.FindModuleImplementing<ModuleWheelBase>();
            slot.wheelSuspension = wPart.FindModuleImplementing<ModuleWheelSuspension>();
            slot.restLocalPos = node.position;

            if (slot.wheelSuspension != null && !string.IsNullOrEmpty(slot.wheelSuspension.suspensionTransformName))
            {
                Transform modelTr = wPart.transform.Find("model") ?? wPart.transform;
                slot.suspensionTransform = FindDeepChild(modelTr, slot.wheelSuspension.suspensionTransformName);
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

        #region Görsel Rocker-Bogie ve Diferansiyel Bar Kinematik Hesabı

        private void UpdateRockerBogieKinematics(float dt)
        {
            // Sol Taraf (FL, ML, RL) Açıları
            ComputeSideAngles(_fl, _ml, _rl, out float targetRockerL, out float targetBogieL);

            // Sağ Taraf (FR, MR, RR) Açıları
            ComputeSideAngles(_fr, _mr, _rr, out float targetRockerR, out float targetBogieR);

            float lerpSpeed = Mathf.Clamp01(dt * 10.0f);
            _currentRockerAngleL = Mathf.Lerp(_currentRockerAngleL, targetRockerL, lerpSpeed);
            _currentRockerAngleR = Mathf.Lerp(_currentRockerAngleR, targetRockerR, lerpSpeed);
            _currentBogieAngleL = Mathf.Lerp(_currentBogieAngleL, targetBogieL, lerpSpeed);
            _currentBogieAngleR = Mathf.Lerp(_currentBogieAngleR, targetBogieR, lerpSpeed);

            // 1. Sol ve Sağ Rocker Kollarını Döndür (Yerel X Ekseni - Pitch)
            if (_rockerL != null)
                _rockerL.localRotation = Quaternion.Euler(_currentRockerAngleL, 0f, 0f);

            if (_rockerR != null)
                _rockerR.localRotation = Quaternion.Euler(_currentRockerAngleR, 0f, 0f);

            // 2. Sol ve Sağ Arka Bogie Salıncaklarını Döndür
            if (_bogieL != null)
                _bogieL.localRotation = Quaternion.Euler(_currentBogieAngleL - _currentRockerAngleL, 0f, 0f);

            if (_bogieR != null)
                _bogieR.localRotation = Quaternion.Euler(_currentBogieAngleR - _currentRockerAngleR, 0f, 0f);

            // 3. Üst Diferansiyel Denge Çubuğu (Sol ve Sağ Rocker farkına göre Y/Z ekseninde mafsallanır)
            if (_diffBar != null)
            {
                float diffAngle = (_currentRockerAngleL - _currentRockerAngleR) * 0.5f;
                _diffBar.localRotation = Quaternion.Euler(0f, diffAngle, 0f);
            }

            rockerAngleTelemetry = $"Sol: {_currentRockerAngleL:+0.0;-0.0;0.0}° | Sağ: {_currentRockerAngleR:+0.0;-0.0;0.0}°";
        }

        private void ComputeSideAngles(WheelSlotLink front, WheelSlotLink mid, WheelSlotLink rear, out float rockerDeg, out float bogieDeg)
        {
            rockerDeg = 0f;
            bogieDeg = 0f;

            Vector3 frontPos = GetWheelHubLocalPosition(front);
            Vector3 midPos = GetWheelHubLocalPosition(mid);
            Vector3 rearPos = GetWheelHubLocalPosition(rear);

            // Arka Bogie Açısı (Orta ve Arka tekerlek arasındaki eğim)
            float dzBogie = midPos.z - rearPos.z;
            float dyBogie = midPos.y - rearPos.y;
            if (Mathf.Abs(dzBogie) > 0.05f)
            {
                bogieDeg = Mathf.Clamp(-Mathf.Atan2(dyBogie, dzBogie) * Mathf.Rad2Deg, -maxBogiePitchDeg, maxBogiePitchDeg);
            }

            // Ana Rocker Açısı (Ön tekerlek ile Arka Bogie mafsal merkezi arasındaki eğim)
            Vector3 bogiePivotPos = (midPos + rearPos) * 0.5f;
            float dzRocker = frontPos.z - bogiePivotPos.z;
            float dyRocker = frontPos.y - bogiePivotPos.y;
            if (Mathf.Abs(dzRocker) > 0.05f)
            {
                rockerDeg = Mathf.Clamp(-Mathf.Atan2(dyRocker, dzRocker) * Mathf.Rad2Deg, -maxRockerPitchDeg, maxRockerPitchDeg);
            }
        }

        /// <summary>
        /// Tekerleğin anlık süspansiyon sıkışmasını içeren gövde-yerel (chassis-local) konumunu döner.
        /// </summary>
        private Vector3 GetWheelHubLocalPosition(WheelSlotLink slot)
        {
            if (slot.wheelPart == null)
                return slot.restLocalPos;

            Transform targetTr = slot.suspensionTransform != null ? slot.suspensionTransform : slot.wheelPart.transform;
            return part.transform.InverseTransformPoint(targetTr.position);
        }

        #endregion

        #region Fiziksel Diferansiyel Yük Paylaşımı (Bogie Load Equalization)

        /// <summary>
        /// Bir taraftaki ön tekerlek bir kayaya tırmanırken yükseldiğinde, mekanik Rocker-Bogie kaldıracı
        /// aynı taraftaki orta ve arka tekerlekleri zemine doğru bastırarak 6 tekerleğin de çekişini korur.
        /// Ayrıca sol ve sağ Rocker açılarının ortalamasını alarak ana gövdenin sarsıntısını yarıya indirir.
        /// </summary>
        private void ApplyDifferentialSuspensionBalance()
        {
            if (part.rb == null)
                return;

            // Sol ve Sağ Üçlü Tekerlek Gruplarında Yük Paylaşımı
            BalanceSideTriad(_fl, _ml, _rl);
            BalanceSideTriad(_fr, _mr, _rr);

            // Diferansiyel Burulma Çubuğu (Torque Tube) Gövde Pitch/Roll Sönümleme
            int groundedCount = CountGroundedWheels();
            if (groundedCount >= 4)
            {
                float diffDiscrepancyRad = (_currentRockerAngleL - _currentRockerAngleR) * Mathf.Deg2Rad;
                if (Mathf.Abs(diffDiscrepancyRad) > 0.01f)
                {
                    // Sol ve sağ salıncaklar farklı eğimdeyken gövdenin devrilmesini önleyen dengeleyici tork
                    float stabilizingTorque = -diffDiscrepancyRad * part.rb.mass * 4.5f * bogieLinkStiffness;
                    part.rb.AddRelativeTorque(new Vector3(0f, 0f, stabilizingTorque), ForceMode.Force);
                }
            }
        }

        private void BalanceSideTriad(WheelSlotLink front, WheelSlotLink mid, WheelSlotLink rear)
        {
            if (front.wheelPart == null || rear.wheelPart == null)
                return;

            // Ön, orta ve arka tekerleklerin dinlenme konumlarına göre anlık dikey (Y) yer değiştirmeleri
            float defFront = GetWheelVerticalDeflection(front);
            float defMid = GetWheelVerticalDeflection(mid);
            float defRear = GetWheelVerticalDeflection(rear);

            // Eğer ön tekerlek yukarı sıkışmışsa (defFront > 0) ve orta/arka tekerlek aşağıda kalmışsa,
            // Rocker kaldıracı orta/arka tekerleğin süspansiyon hedef pozisyonunu (targetPosition) dengeler
            float avgDeflection = (defFront + defMid + defRear) / 3.0f;

            ApplyBogieTargetBias(front, avgDeflection - defFront);
            ApplyBogieTargetBias(mid, avgDeflection - defMid);
            ApplyBogieTargetBias(rear, avgDeflection - defRear);
        }

        private float GetWheelVerticalDeflection(WheelSlotLink slot)
        {
            if (slot.wheelPart == null)
                return 0f;

            Vector3 currentLocal = GetWheelHubLocalPosition(slot);
            return currentLocal.y - slot.restLocalPos.y;
        }

        private void ApplyBogieTargetBias(WheelSlotLink slot, float deflectionDelta)
        {
            if (slot.wheelSuspension == null)
                return;

            // KSP'de targetPosition (0.0 .. 1.0) süspansiyon yayın dinlenme noktasını belirler.
            // Kaldıraç farkına göre 0.45 baz değer etrafında dinamik olarak [0.25 .. 0.75] aralığında esnetilir.
            float bias = Mathf.Clamp(deflectionDelta * 1.8f * bogieLinkStiffness, -0.22f, 0.22f);
            slot.wheelSuspension.targetPosition = Mathf.Clamp(0.48f + bias, 0.25f, 0.75f);
        }

        private int CountGroundedWheels()
        {
            int c = 0;
            if (_fl.wheelBase != null && _fl.wheelBase.isGrounded) c++;
            if (_fr.wheelBase != null && _fr.wheelBase.isGrounded) c++;
            if (_ml.wheelBase != null && _ml.wheelBase.isGrounded) c++;
            if (_mr.wheelBase != null && _mr.wheelBase.isGrounded) c++;
            if (_rl.wheelBase != null && _rl.wheelBase.isGrounded) c++;
            if (_rr.wheelBase != null && _rr.wheelBase.isGrounded) c++;
            return c;
        }

        #endregion
    }
}
