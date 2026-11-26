using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRovers
{
    /// <summary>
    /// Kerbal Foundries mantığında çalışan Tırtıllı Palet (Continuous Track) Kontrol Modülü.
    /// Stok KSP ModuleWheelBase / ModuleWheelMotor / ModuleWheelMotorSteering ile tam uyumlu çalışır;
    /// palet şeridi UV doku kaydırma (Diffuse + Normal Map), çoklu taşıyıcı makara (Road Wheels),
    /// tahrik dişlisi (Drive Sprocket) ve gergi çarkı (Idler) senkron dönüşlerini ve
    /// düşük hızda yerinde tank dönüşü (Pivot/Spot Turn) fiziğini yönetir.
    /// </summary>
    public class ProceduralContinuousTrack : PartModule
    {
        #region KSPFields (Konfigürasyon ve PAW Kontrolleri)

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Tank Dönüş Desteği (Pivot)"),
         UI_Toggle(disabledText = "Kapalı", enabledText = "Aktif", affectSymCounterparts = UI_Scene.All)]
        public bool enablePivotSteer = true;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Pivot Dönüş Gücü"),
         UI_FloatRange(minValue = 0.2f, maxValue = 3.0f, stepIncrement = 0.1f, affectSymCounterparts = UI_Scene.All)]
        public float pivotTorqueMultiplier = 1.2f;

        // Unity Model Hiyerarşisi Transform İsimleri (CFG'den özelleştirilebilir)
        [KSPField] public string trackBeltMeshName = "TrackBelt_Mesh";
        [KSPField] public string driveSprocketName = "DriveSprocket";
        [KSPField] public string idlerWheelName = "IdlerWheel";
        [KSPField] public string roadWheelPrefix = "RoadWheel_";

        // Geometrik ve Görsel Kalibrasyon Parametreleri
        [KSPField] public float rollerRadius = 0.16f;
        [KSPField] public float sprocketRadius = 0.22f;
        [KSPField] public float idlerRadius = 0.20f;
        [KSPField] public float uvScrollRate = 0.85f; // 1 m/s hızda saniyede kayacak UV birimi
        [KSPField] public string uvScrollAxis = "Y";  // "X" veya "Y"
        [KSPField] public float maxPivotSpeed = 4.5f; // m/s (Bu hızın altında yerinde tank dönüşü desteklenir)
        [KSPField] public float basePivotForce = 6.5f; // kN eşdeğeri diferansiyel itki

        // PAW Canlı Telemetri
        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Palet Hızı")]
        public string trackSpeedDisplay = "0.0 m/s";

        #endregion

        #region Dahili Durum ve Unity Referansları

        private ModuleWheelBase _wheelBase;
        private ProceduralRoverWheel _procWheel;

        private Material _trackMaterialInstance;
        private Vector2 _currentUvOffset = Vector2.zero;
        private bool _scrollAlongX = false;

        private Transform _sprocketTransform;
        private Transform _idlerTransform;
        private readonly List<Transform> _roadWheelTransforms = new List<Transform>();

        // Simetrik paletlerde sağ ve sol paletin ileri vektör yönünü ayırt etmek için
        private float _currentLinearVelocity = 0f;

        #endregion

        #region Yaşam Döngüsü (OnStart, Update, FixedUpdate, OnDestroy)

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _wheelBase = part.FindModuleImplementing<ModuleWheelBase>();
            _procWheel = part.FindModuleImplementing<ProceduralRoverWheel>();
            _scrollAlongX = string.Equals(uvScrollAxis, "X", StringComparison.OrdinalIgnoreCase);

            DiscoverTrackTransforms();
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null)
                return;

            AnimateTrackBeltAndRollers(Time.deltaTime);
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded)
                return;

            CalculateLinearTrackVelocity();
            ApplyDifferentialPivotSteer();
        }

        public void OnDestroy()
        {
            // Bellek sızıntısını (Material leak) önlemek için kopyalanmış materyali temizle
            if (_trackMaterialInstance != null)
            {
                Destroy(_trackMaterialInstance);
                _trackMaterialInstance = null;
            }
        }

        #endregion

        #region Unity Hiyerarşi Keşfi (Belt, Sprocket, Idler, RoadWheels)

        private void DiscoverTrackTransforms()
        {
            _roadWheelTransforms.Clear();

            Transform modelRoot = part.transform.Find("model");
            if (modelRoot == null)
                return;

            // 1. Palet Şeridi (Track Belt) Mesh ve Materyalini Bul
            Transform beltTr = FindDeepChild(modelRoot, trackBeltMeshName);
            if (beltTr != null)
            {
                Renderer beltRenderer = beltTr.GetComponent<Renderer>();
                if (beltRenderer != null)
                {
                    // sharedMaterial yerine material kullanarak yalnızca bu palet örneğine ait UV kaydırması yapıyoruz
                    _trackMaterialInstance = beltRenderer.material;
                }
            }

            // 2. Tahrik Dişlisi (Drive Sprocket) ve Gergi Çarkı (Idler Wheel)
            _sprocketTransform = FindDeepChild(modelRoot, driveSprocketName);
            _idlerTransform = FindDeepChild(modelRoot, idlerWheelName);

            // 3. Çoklu Taşıyıcı Makaraları (RoadWheel_01, RoadWheel_02...) Tara
            CollectTransformsByPrefix(modelRoot, roadWheelPrefix, _roadWheelTransforms);
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

        private static void CollectTransformsByPrefix(Transform parent, string prefix, List<Transform> results)
        {
            if (parent == null || string.IsNullOrEmpty(prefix))
                return;

            foreach (Transform child in parent)
            {
                if (child.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(child);
                }
                CollectTransformsByPrefix(child, prefix, results);
            }
        }

        #endregion

        #region Palet Hızı Hesaplama, UV Kaydırma ve Makara Animasyonu

        /// <summary>
        /// Paletin dünya uzayındaki gerçek doğrusal hızını ve yerinde dönüş (yaw) katkısını hesaplar.
        /// </summary>
        private void CalculateLinearTrackVelocity()
        {
            if (part.rb == null)
            {
                _currentLinearVelocity = 0f;
                return;
            }

            // Parçanın kendi ileri ekseni (part.transform.forward) boyunca hız bileşeni
            Vector3 partWorldVel = part.rb.GetPointVelocity(part.transform.position);
            float forwardSpeed = Vector3.Dot(partWorldVel, part.transform.forward);

            // Eğer araç durduğu yerde gaz veya yön tuşuna basıyorsa (patinaj / tank dönüşü),
            // görsel palet tepkisini komut girdisiyle harmanla
            float throttleInput = vessel.ctrlState.wheelThrottle;
            float steerInput = vessel.ctrlState.wheelSteer;

            // Paletin aracın sağında mı solunda mı olduğunu tespit et (Diferansiyel dönüş yönü için)
            float sideSign = GetTrackSideSign();
            float commandedVirtualSpeed = (throttleInput - (steerInput * sideSign * 0.85f)) * 2.5f;

            if (Mathf.Abs(forwardSpeed) < 0.35f && Mathf.Abs(commandedVirtualSpeed) > 0.1f)
            {
                _currentLinearVelocity = Mathf.Lerp(forwardSpeed, commandedVirtualSpeed, 0.65f);
            }
            else
            {
                _currentLinearVelocity = forwardSpeed;
            }

            trackSpeedDisplay = $"{_currentLinearVelocity:F1} m/s";
        }

        private void AnimateTrackBeltAndRollers(float dt)
        {
            if (Mathf.Abs(_currentLinearVelocity) < 1e-3f)
                return;

            float scaleFactor = (_procWheel != null && _procWheel.wheelScale > 0.1f) ? _procWheel.wheelScale : 1.0f;

            // 1. UV Doku Kaydırma (_MainTex ve _BumpMap)
            if (_trackMaterialInstance != null)
            {
                float uvDelta = (_currentLinearVelocity / scaleFactor) * uvScrollRate * dt;
                if (_scrollAlongX)
                    _currentUvOffset.x = Mathf.Repeat(_currentUvOffset.x + uvDelta, 1.0f);
                else
                    _currentUvOffset.y = Mathf.Repeat(_currentUvOffset.y + uvDelta, 1.0f);

                _trackMaterialInstance.SetTextureOffset("_MainTex", _currentUvOffset);
                if (_trackMaterialInstance.HasProperty("_BumpMap"))
                {
                    _trackMaterialInstance.SetTextureOffset("_BumpMap", _currentUvOffset);
                }
            }

            // 2. Tahrik Dişlisi (Drive Sprocket) Dönüşü (Local +X Aksı)
            if (_sprocketTransform != null && sprocketRadius > 1e-3f)
            {
                float effSprocketR = sprocketRadius * scaleFactor;
                float degDelta = (_currentLinearVelocity / effSprocketR) * Mathf.Rad2Deg * dt;
                _sprocketTransform.Rotate(Vector3.right, degDelta, Space.Self);
            }

            // 3. Ön Gergi Çarkı (Idler Wheel) Dönüşü
            if (_idlerTransform != null && idlerRadius > 1e-3f)
            {
                float effIdlerR = idlerRadius * scaleFactor;
                float degDelta = (_currentLinearVelocity / effIdlerR) * Mathf.Rad2Deg * dt;
                _idlerTransform.Rotate(Vector3.right, degDelta, Space.Self);
            }

            // 4. Alt Taşıyıcı Makaralar (Road Wheels) Dönüşü
            if (_roadWheelTransforms.Count > 0 && rollerRadius > 1e-3f)
            {
                float effRollerR = rollerRadius * scaleFactor;
                float degDelta = (_currentLinearVelocity / effRollerR) * Mathf.Rad2Deg * dt;
                for (int i = 0; i < _roadWheelTransforms.Count; i++)
                {
                    if (_roadWheelTransforms[i] != null)
                    {
                        _roadWheelTransforms[i].Rotate(Vector3.right, degDelta, Space.Self);
                    }
                }
            }
        }

        #endregion

        #region Diferansiyel Tank Dönüşü (Spot / Pivot Turn Fiziği)

        /// <summary>
        /// KSP'de araç dururken (wheelThrottle == 0) sadece A/D (wheelSteer) tuşlarına basıldığında
        /// stok ModuleWheelMotorSteering tek başına paletleri ters yönde döndürmez.
        /// Bu metot düşük hızlarda sağ ve sol paletlere ters yönlü diferansiyel kuvvet uygulayarak
        /// gerçekçi yerinde tank dönüşü (Pivot Turn) sağlar.
        /// </summary>
        private void ApplyDifferentialPivotSteer()
        {
            if (!enablePivotSteer || part.rb == null || vessel == null)
                return;

            // Yalnızca palet zemine temas halindeyse (veya çok yakınsa) çalışsın
            if (_wheelBase != null && !_wheelBase.isGrounded)
                return;

            float steerInput = vessel.ctrlState.wheelSteer;
            if (Mathf.Abs(steerInput) < 0.05f)
                return;

            // Yalnızca düşük hızlarda (< maxPivotSpeed) devreye girsin; yüksek hızda normal diferansiyel sürüş yeterlidir
            if (vessel.srfSpeed > maxPivotSpeed)
                return;

            // Elektrik tüketimi kontrolü
            double ecRequired = 0.08 * Time.fixedDeltaTime * Mathf.Abs(steerInput) * pivotTorqueMultiplier;
            double ecDrawn = part.RequestResource("ElectricCharge", ecRequired);
            if (ecDrawn < ecRequired * 0.25)
                return;

            float sideSign = GetTrackSideSign();
            if (Mathf.Abs(sideSign) < 0.1f)
                return;

            // Hız yükseldikçe pivot desteğini yumuşakça azalt
            float speedAttenuation = 1.0f - Mathf.Clamp01((float)vessel.srfSpeed / maxPivotSpeed);
            float scaleFactor = (_procWheel != null) ? Mathf.Pow(_procWheel.wheelScale, 1.5f) : 1.0f;

            // KSP'de wheelSteer: Sol (A) = +1, Sağ (D) = -1 (veya kontrol noktasına göre tersi).
            // Sol palet (-X sideSign < 0) ve Sağ palet (+X sideSign > 0) ters yönlü kuvvet üretir:
            float forceMagnitude = -steerInput * sideSign * basePivotForce * pivotTorqueMultiplier * scaleFactor * speedAttenuation;
            Vector3 worldForce = part.transform.forward * forceMagnitude;

            part.rb.AddForceAtPosition(worldForce, part.transform.position, ForceMode.Force);
        }

        /// <summary>
        /// Bu paletin aracın referans kontrol noktasına (Root/ReferenceTransform) göre
        /// sol (-1) tarafında mı yoksa sağ (+1) tarafında mı olduğunu döner.
        /// </summary>
        private float GetTrackSideSign()
        {
            Transform refTr = vessel?.ReferenceTransform;
            if (refTr == null)
                return 0f;

            Vector3 localPosInVessel = refTr.InverseTransformPoint(part.transform.position);
            if (Mathf.Abs(localPosInVessel.x) < 0.05f)
                return 0f;

            return Mathf.Sign(localPosInVessel.x);
        }

        #endregion
    }
}
