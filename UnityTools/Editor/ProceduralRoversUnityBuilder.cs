#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using KSPPartTools;

namespace ProceduralRovers.UnityEditorTools
{
    /// <summary>
    /// KSP 1.12.5 Stock-Alike (Porkjet / ReStock Tarzı) Prosedürel Doku, Normal Map ve Yüksek Detaylı 3B Model Üretici.
    /// - 512x512 çözünürlükte KSP stok estetiğine uyumlu Diffuse + Normal Map (.png) dokuları üretir ve KSP/Bumped Specular atar.
    /// - Tüm 9 parçayı .cfg içindeki node_stack_* koordinatlarına milimetrik oturacak şekilde,
    ///   hiçbir iç-içe geçme (clipping) veya havada kalma (floating) olmadan oranlı olarak modeller.
    /// - PartTools.ModelName / FilePath alanlarını doğrudan doldurur ve tek tıkla 9 parçayı (.mu + .png) KSP GameData'ya aktarır.
    /// </summary>
    public static class ProceduralRoversUnityBuilder
    {
        private const int LAYER_DEFAULT = 0;
        private const int LAYER_WHEEL_IGNORE = 26;   // wheelCollidersIgnore (Sadece görünmez CollisionEnhancer için!)
        private const int LAYER_WHEEL_COLLIDER = 27; // wheelColliders (Sadece WheelCollider için!)

        private const string DEFAULT_KSP_GAMEDATA = @"C:\Games\KerbalSpaceProgram\English\GameData";
        private const string PREFAB_OUTPUT_DIR = "Assets/ProceduralRovers/Prefabs";
        private const string MESH_OUTPUT_DIR = "Assets/ProceduralRovers/GeneratedMeshes";
        private const string TEX_OUTPUT_DIR = "Assets/ProceduralRovers/Textures";
        private const string MAT_OUTPUT_DIR = "Assets/ProceduralRovers/Materials";

        private static int _meshCounter = 0;

        // Stok KSP Uyumlu Materyal Havuzu
        private static Material _matStockWhite;     // KSP Porkjet kırık-beyaz/krem panel + perçin + koyu gri çerçeve
        private static Material _matDarkMetal;      // KSP koyu antrasit/çelik yapısal şasi ve süspansiyon metali
        private static Material _matSilverMachined; // İşlenmiş alüminyum / titanyum parlak metal
        private static Material _matGoldFoil;       // Probodobodyne tarzı kırışık altın Kapton MLI termal folyo
        private static Material _matSolarCells;     // OX-STAT / Gigantor tarzı koyu lacivert-mavi güneş hücreleri
        private static Material _matTireRubber;     // Vulkanize kauçuk + kesintisiz çavuş (chevron) sırt deseni
        private static Material _matNitinolMesh;    // Örgülü titanyum/nitinol tel hasır tekerlek dokusu
        private static Material _matSovietOlive;    // Making History tarzı Lunokhod zeytin-yeşili/gri basınçlı gövde
        private static Material _matSciFiArmor;     // Koyu arduvaz-metalik zırh plakası
        private static Material _matOpticsDark;     // Koyu safir kamera merceği camı
        private static Material _matLaserRuby;      // ChemCam yakut kırmızısı lazer merceği
        private static Material _matCyanEmissive;   // Fütüristik cyan sensör / reaktör ışıması

        #region Unity Üst Menü Komutları

        [MenuItem("Tools/Procedural Rovers/1. Configure KSP Layers (26 & 27)", false, 10)]
        public static void ConfigureKspLayers()
        {
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layersProp = tagManager.FindProperty("layers");

            SetLayerName(layersProp, LAYER_WHEEL_IGNORE, "wheelCollidersIgnore");
            SetLayerName(layersProp, LAYER_WHEEL_COLLIDER, "wheelColliders");

            tagManager.ApplyModifiedProperties();
            Debug.Log("[ProceduralRovers] Layer 26 (wheelCollidersIgnore) ve Layer 27 (wheelColliders) ayarlandı.");
        }

        [MenuItem("Tools/Procedural Rovers/2. Build Stock-Alike 3D Parts & Textures", false, 20)]
        public static void BuildAllParts()
        {
            _meshCounter = 0;
            ConfigureKspLayers();
            EnsureDirectories();
            ConfigurePartToolsGameDataDir(DEFAULT_KSP_GAMEDATA);

            // 1. Stok KSP uyumlu 512x512 Diffuse + Normal Map dokularını ve KSP/Bumped Specular materyallerini hazırla
            BuildStockAlikeMaterials();

            GameObject masterRoot = GameObject.Find("ProceduralRovers_Workspace");
            if (masterRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(masterRoot);
            }
            masterRoot = new GameObject("ProceduralRovers_Workspace");

            // 2. Ana Gövde (5 Oranlı & Detaylı Şasi)
            PlaceInWorkspace(BuildRoverCoreProcedural(), masterRoot.transform, new Vector3(0f, 0f, 0f));

            // 3. Tekerlekler ve Tırtıllı Palet
            PlaceInWorkspace(BuildMeshSpringWheelPart(), masterRoot.transform, new Vector3(3.0f, 0f, 0f));
            PlaceInWorkspace(BuildHeavyDutyWheelPart(), masterRoot.transform, new Vector3(4.5f, 0f, 0f));
            PlaceInWorkspace(BuildContinuousTrackPart(), masterRoot.transform, new Vector3(6.5f, 0f, 0f));

            // 4. Modüler Bilim ve Güç Ekipmanları
            PlaceInWorkspace(BuildRoboticDrillPart(), masterRoot.transform, new Vector3(0f, 0f, 3.5f));
            PlaceInWorkspace(BuildMastCamPart(), masterRoot.transform, new Vector3(1.5f, 0f, 3.5f));
            PlaceInWorkspace(BuildMmrtgPart(), masterRoot.transform, new Vector3(3.0f, 0f, 3.5f));
            PlaceInWorkspace(BuildSolarDeckPart(), masterRoot.transform, new Vector3(4.5f, 0f, 3.5f));
            PlaceInWorkspace(BuildSpectrometerPart(), masterRoot.transform, new Vector3(6.5f, 0f, 3.5f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = masterRoot;
            Debug.Log("[ProceduralRovers] Tüm 9 parça KSP Stock-Alike dokular, Normal Map'ler ve oranlı 3B detaylarla oluşturuldu!");
        }

        [MenuItem("Tools/Procedural Rovers/3. ONE-CLICK EXPORT All 9 Models (.mu + Textures) to KSP", false, 30)]
        public static void ExportAllModelsToKsp()
        {
            GameObject masterRoot = GameObject.Find("ProceduralRovers_Workspace");
            if (masterRoot == null)
            {
                BuildAllParts();
                masterRoot = GameObject.Find("ProceduralRovers_Workspace");
            }

            ConfigurePartToolsGameDataDir(DEFAULT_KSP_GAMEDATA);

            int exportedCount = 0;
            foreach (Transform child in masterRoot.transform)
            {
                KSPPartTools.PartTools pt = child.GetComponent<KSPPartTools.PartTools>();
                if (pt == null)
                    continue;

                string relativeDir = pt.FilePath.Trim('/', '\\');
                string fullTargetDir = Path.Combine(DEFAULT_KSP_GAMEDATA, relativeDir.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(fullTargetDir))
                {
                    Directory.CreateDirectory(fullTargetDir);
                }

                // PartWriter.Write hedef dizini '/' ile biten tam yol olarak alır ve hem .mu hem .png dokularını yazar
                string writerPath = fullTargetDir.Replace('\\', '/');
                if (!writerPath.EndsWith("/"))
                    writerPath += "/";

                try
                {
                    // KSPPartTools.PartWriter internal erişim düzeyinde olduğu için Reflection ile doğrudan çağrılır
                    Type partWriterType = typeof(KSPPartTools.PartToolsInspector).Assembly.GetType("KSPPartTools.PartWriter");
                    MethodInfo writeMethod = partWriterType?.GetMethod("Write", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (writeMethod != null)
                    {
                        writeMethod.Invoke(null, new object[] { pt.ModelName, writerPath, child, true, KSPPartTools.PartTools.TextureFormat.PNG });
                    }

                    string expectedMu = Path.Combine(fullTargetDir, pt.ModelName + ".mu");
                    if (File.Exists(expectedMu))
                    {
                        exportedCount++;
                        Debug.Log($"[ProceduralRovers] Yazdırıldı: {expectedMu} ({new FileInfo(expectedMu).Length / 1024} KB)");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ProceduralRovers] '{pt.ModelName}' yazdırılırken hata: {ex}");
                }
            }

            Debug.Log($"[ProceduralRovers] TAMAMLANDI: {exportedCount} / {masterRoot.transform.childCount} parça (.mu + dokular) doğrudan KSP GameData klasörüne aktarıldı!");
        }

        #endregion

        #region Stok KSP Uyumlu Prosedürel Doku & Normal Map Üretimi (512x512)

        private static void BuildStockAlikeMaterials()
        {
            // 1. Stock White Aerospace Panel (Kırık beyaz boyalı alüminyum, koyu panel derzleri, perçinler, sarı-siyah ikaz şeridi)
            CreatePanelTexturePair("PR_StockWhite", new Color(0.86f, 0.87f, 0.89f), new Color(0.74f, 0.76f, 0.79f),
                addHazardStripe: false, out Texture2D whiteDiff, out Texture2D whiteNrm);
            _matStockWhite = CreateBumpedSpecMaterial("PR_Mat_StockWhite", whiteDiff, whiteNrm, new Color(0.32f, 0.33f, 0.35f), 0.42f);

            // 2. Stock Dark Structural Gunmetal (Koyu antrasit şasi/süspansiyon metali + işlenmiş kanallar)
            CreatePanelTexturePair("PR_DarkMetal", new Color(0.24f, 0.26f, 0.29f), new Color(0.17f, 0.18f, 0.21f),
                addHazardStripe: true, out Texture2D darkDiff, out Texture2D darkNrm);
            _matDarkMetal = CreateBumpedSpecMaterial("PR_Mat_DarkMetal", darkDiff, darkNrm, new Color(0.45f, 0.47f, 0.50f), 0.50f);

            // 3. Silver Machined Alloy (Fırçalanmış açık metal / alüminyum / jant ve pistonlar)
            CreatePanelTexturePair("PR_SilverAlloy", new Color(0.66f, 0.69f, 0.73f), new Color(0.54f, 0.57f, 0.61f),
                addHazardStripe: false, out Texture2D silverDiff, out Texture2D silverNrm);
            _matSilverMachined = CreateBumpedSpecMaterial("PR_Mat_SilverAlloy", silverDiff, silverNrm, new Color(0.75f, 0.77f, 0.80f), 0.68f);

            // 4. Gold Kapton MLI Foil (Probodobodyne tarzı kırışık altın termal battaniye)
            CreateGoldFoilTexturePair("PR_GoldFoil", out Texture2D foilDiff, out Texture2D foilNrm);
            _matGoldFoil = CreateBumpedSpecMaterial("PR_Mat_GoldFoil", foilDiff, foilNrm, new Color(0.95f, 0.78f, 0.32f), 0.76f);

            // 5. Stock Solar Cell Array (Koyu lacivert silikon hücre ızgarası + gümüş bara çizgileri)
            CreateSolarCellTexturePair("PR_SolarCells", out Texture2D solarDiff, out Texture2D solarNrm);
            _matSolarCells = CreateBumpedSpecMaterial("PR_Mat_SolarCells", solarDiff, solarNrm, new Color(0.55f, 0.68f, 0.90f), 0.82f);

            // 6. Tire & Continuous Track Tread (Y ekseninde kesintisiz çavuş dişli kauçuk sırt)
            CreateTireTreadTexturePair("PR_TireTread", out Texture2D treadDiff, out Texture2D treadNrm);
            _matTireRubber = CreateBumpedSpecMaterial("PR_Mat_TireTread", treadDiff, treadNrm, new Color(0.16f, 0.16f, 0.18f), 0.22f);

            // 7. Nitinol Woven Wire-Mesh Tire (Çapraz örgülü metalik tel örgü sırt)
            CreateWireMeshTexturePair("PR_NitinolMesh", out Texture2D meshDiff, out Texture2D meshNrm);
            _matNitinolMesh = CreateBumpedSpecMaterial("PR_Mat_NitinolMesh", meshDiff, meshNrm, new Color(0.62f, 0.58f, 0.46f), 0.58f);

            // 8. Soviet Olive-Grey Pressurized Hull (Lunokhod & Palet zırh gövdesi)
            CreatePanelTexturePair("PR_SovietOlive", new Color(0.43f, 0.48f, 0.40f), new Color(0.33f, 0.37f, 0.31f),
                addHazardStripe: false, out Texture2D oliveDiff, out Texture2D oliveNrm);
            _matSovietOlive = CreateBumpedSpecMaterial("PR_Mat_SovietOlive", oliveDiff, oliveNrm, new Color(0.35f, 0.37f, 0.33f), 0.40f);

            // 9. Sci-Fi Armored Slate Hull
            CreatePanelTexturePair("PR_SciFiArmor", new Color(0.21f, 0.26f, 0.33f), new Color(0.14f, 0.18f, 0.24f),
                addHazardStripe: true, out Texture2D scifiDiff, out Texture2D scifiNrm);
            _matSciFiArmor = CreateBumpedSpecMaterial("PR_Mat_SciFiArmor", scifiDiff, scifiNrm, new Color(0.48f, 0.56f, 0.68f), 0.58f);

            // 10. Optik Lensler & Emissive Işıma Materyalleri
            Texture2D opticsTex = CreateSolidGradientTex("PR_OpticsGlass", new Color(0.06f, 0.09f, 0.15f), new Color(0.14f, 0.24f, 0.38f));
            _matOpticsDark = CreateSimpleSpecMaterial("PR_Mat_OpticsGlass", opticsTex, new Color(0.90f, 0.95f, 1.0f), 0.92f);

            Texture2D rubyTex = CreateSolidGradientTex("PR_LaserRuby", new Color(0.45f, 0.06f, 0.08f), new Color(0.88f, 0.18f, 0.16f));
            _matLaserRuby = CreateSimpleSpecMaterial("PR_Mat_LaserRuby", rubyTex, new Color(1.0f, 0.55f, 0.55f), 0.90f);

            Texture2D cyanTex = CreateSolidGradientTex("PR_CyanGlow", new Color(0.10f, 0.65f, 0.85f), new Color(0.35f, 0.92f, 1.0f));
            _matCyanEmissive = CreateEmissiveMaterial("PR_Mat_CyanGlow", cyanTex, new Color(0.20f, 0.85f, 1.0f));
        }

        private static void CreatePanelTexturePair(string baseName, Color primary, Color secondary, bool addHazardStripe, out Texture2D diff, out Texture2D nrm)
        {
            const int size = 512;
            float[,] heightMap = new float[size, size];
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;

                    // Hafif fırçalanmış metal dokusu + AO kenar gölgesi
                    float grain = (Mathf.PerlinNoise(u * 48f, v * 48f) - 0.5f) * 0.035f
                                + (Mathf.PerlinNoise(u * 180f, v * 12f) - 0.5f) * 0.02f;

                    Color c = Color.Lerp(primary, secondary, Mathf.PerlinNoise(u * 4f + 1.3f, v * 4f + 2.7f) * 0.45f);
                    c.r = Mathf.Clamp01(c.r + grain);
                    c.g = Mathf.Clamp01(c.g + grain);
                    c.b = Mathf.Clamp01(c.b + grain);

                    float h = 0.5f + grain * 0.4f;

                    // Ana Panel Derz Çizgileri (Recessed Seams: 4x4 modüler havacılık panelleri)
                    int gx = x % 128;
                    int gy = y % 128;
                    int borderDist = Mathf.Min(Mathf.Min(gx, 127 - gx), Mathf.Min(gy, 127 - gy));

                    if (borderDist < 3)
                    {
                        float seamFactor = borderDist == 0 ? 0.52f : (borderDist == 1 ? 0.72f : 0.88f);
                        c *= seamFactor;
                        h -= (3 - borderDist) * 0.14f;
                    }
                    else if (borderDist == 4)
                    {
                        // Hafif pah parlaması (edge highlight)
                        c = Color.Lerp(c, Color.white, 0.08f);
                        h += 0.04f;
                    }

                    // Köşe Perçinleri (Rivets)
                    int rx = Mathf.Min(gx, 127 - gx);
                    int ry = Mathf.Min(gy, 127 - gy);
                    float rivetDist = Mathf.Sqrt((rx - 10) * (rx - 10) + (ry - 10) * (ry - 10));
                    if (rivetDist < 3.2f)
                    {
                        c *= 0.80f;
                        h += (3.2f - rivetDist) * 0.08f;
                    }

                    // Havalandırma Panjurları (Orta alt bölgede stok KSP tarzı panjur kanalları)
                    if (x > 156 && x < 236 && y > 36 && y < 92 && (y % 8 < 4))
                    {
                        c *= 0.62f;
                        h -= 0.18f;
                    }

                    // Opsiyonel Sarı-Siyah KSP İkaz Şeridi (Alt kenar şeridi)
                    if (addHazardStripe && y >= 8 && y <= 24)
                    {
                        bool yellow = ((x + y) / 14) % 2 == 0;
                        c = yellow ? new Color(0.88f, 0.68f, 0.12f) : new Color(0.15f, 0.15f, 0.16f);
                        h = 0.55f;
                    }

                    c.a = 0.55f; // Specular gloss in alpha
                    pixels[y * size + x] = c;
                    heightMap[x, y] = h;
                }
            }

            diff = SaveTextureAsset(baseName + "_Diff", size, pixels, isNormalMap: false);
            nrm = SaveNormalMapFromHeight(baseName + "_NRM", size, heightMap, strength: 3.2f);
        }

        private static void CreateGoldFoilTexturePair(string baseName, out Texture2D diff, out Texture2D nrm)
        {
            const int size = 512;
            float[,] heightMap = new float[size, size];
            Color[] pixels = new Color[size * size];

            Color goldDark = new Color(0.58f, 0.36f, 0.06f);
            Color goldMid = new Color(0.88f, 0.64f, 0.14f);
            Color goldBright = new Color(1.00f, 0.84f, 0.36f);

            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;

                    // Çok katmanlı kırışık Kapton folyo fasetleri (Voronoi/Perlin hibriti)
                    float n1 = Mathf.Abs(Mathf.PerlinNoise(u * 18f, v * 18f) - 0.5f) * 2f;
                    float n2 = Mathf.Abs(Mathf.PerlinNoise(u * 38f + 5.2f, v * 38f + 3.1f) - 0.5f) * 2f;
                    float n3 = Mathf.PerlinNoise(u * 85f, v * 85f) * 0.25f;
                    float crinkle = Mathf.Clamp01(n1 * 0.55f + n2 * 0.35f + n3);

                    // Kapitone dikiş ızgarası (Quilted MLI blanket seams)
                    int qx = x % 64;
                    int qy = y % 64;
                    if (qx < 2 || qy < 2)
                    {
                        crinkle *= 0.45f;
                    }

                    Color c = crinkle < 0.5f
                        ? Color.Lerp(goldDark, goldMid, crinkle * 2f)
                        : Color.Lerp(goldMid, goldBright, (crinkle - 0.5f) * 2f);

                    c.a = 0.82f;
                    pixels[y * size + x] = c;
                    heightMap[x, y] = crinkle;
                }
            }

            diff = SaveTextureAsset(baseName + "_Diff", size, pixels, isNormalMap: false);
            nrm = SaveNormalMapFromHeight(baseName + "_NRM", size, heightMap, strength: 4.5f);
        }

        private static void CreateSolarCellTexturePair(string baseName, out Texture2D diff, out Texture2D nrm)
        {
            const int size = 512;
            float[,] heightMap = new float[size, size];
            Color[] pixels = new Color[size * size];

            Color cellDeep = new Color(0.07f, 0.14f, 0.32f);
            Color cellMid = new Color(0.11f, 0.23f, 0.48f);
            Color busbarSilver = new Color(0.76f, 0.80f, 0.86f);
            Color groutDark = new Color(0.05f, 0.06f, 0.09f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int cx = x % 64; // 8 sütun hücre
                    int cy = y % 128; // 4 satır uzun silikon hücre

                    Color c;
                    float h = 0.6f;

                    // Hücreler arası çerçeve boşluğu + köşe pahları (kesik köşeli monokristal hücre)
                    bool inGap = (cx < 3 || cx > 61 || cy < 3 || cy > 125) || ((cx + cy) < 9) || ((63 - cx + cy) < 9)
                              || ((cx + 127 - cy) < 9) || ((63 - cx + 127 - cy) < 9);

                    if (inGap)
                    {
                        c = groutDark;
                        h = 0.25f;
                    }
                    else if (cx == 20 || cx == 44)
                    {
                        // Dikey gümüş bara (Busbar) iletkenleri
                        c = busbarSilver;
                        h = 0.72f;
                    }
                    else if (cy % 8 == 0)
                    {
                        // İnce yatay mikro-iletken çizgiler
                        c = Color.Lerp(cellMid, busbarSilver, 0.32f);
                        h = 0.64f;
                    }
                    else
                    {
                        float sheen = ((float)cx / 64f) * 0.25f + ((float)cy / 128f) * 0.20f;
                        c = Color.Lerp(cellDeep, cellMid, sheen);
                        h = 0.60f;
                    }

                    c.a = 0.88f;
                    pixels[y * size + x] = c;
                    heightMap[x, y] = h;
                }
            }

            diff = SaveTextureAsset(baseName + "_Diff", size, pixels, isNormalMap: false);
            nrm = SaveNormalMapFromHeight(baseName + "_NRM", size, heightMap, strength: 3.0f);
        }

        private static void CreateTireTreadTexturePair(string baseName, out Texture2D diff, out Texture2D nrm)
        {
            const int size = 512;
            float[,] heightMap = new float[size, size];
            Color[] pixels = new Color[size * size];

            Color rubberBase = new Color(0.13f, 0.14f, 0.15f);
            Color rubberLug = new Color(0.21f, 0.22f, 0.24f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Y ekseni boyunca kesintisiz (tileable) V-Chevron sırt deseni
                    float centerDist = Mathf.Abs(x - size * 0.5f);
                    int chevronY = (y + (int)(centerDist * 0.35f)) % 32;

                    bool isLug = chevronY < 18 && x > 20 && x < (size - 20) && Mathf.Abs(x - size * 0.5f) > 10;
                    float noise = (Mathf.PerlinNoise(x * 0.2f, y * 0.2f) - 0.5f) * 0.02f;

                    Color c = isLug ? rubberLug : rubberBase;
                    c.r += noise; c.g += noise; c.b += noise;
                    c.a = 0.25f;

                    pixels[y * size + x] = c;
                    heightMap[x, y] = isLug ? 0.85f : 0.25f;
                }
            }

            diff = SaveTextureAsset(baseName + "_Diff", size, pixels, isNormalMap: false);
            nrm = SaveNormalMapFromHeight(baseName + "_NRM", size, heightMap, strength: 5.0f);
        }

        private static void CreateWireMeshTexturePair(string baseName, out Texture2D diff, out Texture2D nrm)
        {
            const int size = 512;
            float[,] heightMap = new float[size, size];
            Color[] pixels = new Color[size * size];

            Color wireBright = new Color(0.74f, 0.70f, 0.56f);
            Color wireShadow = new Color(0.25f, 0.23f, 0.19f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Çapraz elmas tel örgü deseni (Apollo / Curiosity Nitinol Mesh)
                    float d1 = Mathf.Abs(((x + y) % 16) - 8) / 8f;
                    float d2 = Mathf.Abs(((x - y + 512) % 16) - 8) / 8f;
                    float wire = Mathf.Max(1f - d1 * 1.6f, 1f - d2 * 1.6f);
                    wire = Mathf.Clamp01(wire);

                    // Yatay takviye şeritleri
                    if (y % 64 < 6)
                    {
                        wire = 0.92f;
                    }

                    Color c = Color.Lerp(wireShadow, wireBright, wire);
                    c.a = 0.65f;
                    pixels[y * size + x] = c;
                    heightMap[x, y] = wire;
                }
            }

            diff = SaveTextureAsset(baseName + "_Diff", size, pixels, isNormalMap: false);
            nrm = SaveNormalMapFromHeight(baseName + "_NRM", size, heightMap, strength: 4.0f);
        }

        private static Texture2D CreateSolidGradientTex(string baseName, Color dark, Color bright)
        {
            const int size = 128;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - 64f) / 64f;
                    float dy = (y - 64f) / 64f;
                    float r = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    Color c = Color.Lerp(dark, bright, r * r);
                    c.a = 0.9f;
                    pixels[y * size + x] = c;
                }
            }
            return SaveTextureAsset(baseName, size, pixels, isNormalMap: false);
        }

        private static Texture2D SaveTextureAsset(string texName, int size, Color[] pixels, bool isNormalMap)
        {
            string path = $"{TEX_OUTPUT_DIR}/{texName}.png";
            Texture2D tex = new Texture2D(size, size, UnityEngine.TextureFormat.RGBA32, true);
            tex.SetPixels(pixels);
            tex.Apply();

            byte[] pngData = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            File.WriteAllBytes(path, pngData);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.isReadable = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D SaveNormalMapFromHeight(string texName, int size, float[,] h, float strength)
        {
            Color[] nrmPixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                int yPrev = (y - 1 + size) % size;
                int yNext = (y + 1) % size;
                for (int x = 0; x < size; x++)
                {
                    int xPrev = (x - 1 + size) % size;
                    int xNext = (x + 1) % size;

                    float dX = (h[xNext, y] - h[xPrev, y]) * strength;
                    float dY = (h[x, yNext] - h[x, yPrev]) * strength;

                    Vector3 n = new Vector3(-dX, -dY, 1.0f).normalized;
                    nrmPixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1.0f);
                }
            }
            return SaveTextureAsset(texName, size, nrmPixels, isNormalMap: true);
        }

        private static Material CreateBumpedSpecMaterial(string matName, Texture2D mainTex, Texture2D bumpMap, Color specColor, float shininess)
        {
            string path = $"{MAT_OUTPUT_DIR}/{matName}.mat";
            Shader sh = Shader.Find("KSP/Bumped Specular") ?? Shader.Find("KSP/Specular") ?? Shader.Find("Standard");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh) { name = matName };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = sh;
            }

            mat.mainTexture = mainTex;
            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", bumpMap);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", specColor);
            if (mat.HasProperty("_Shininess")) mat.SetFloat("_Shininess", shininess);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateSimpleSpecMaterial(string matName, Texture2D mainTex, Color specColor, float shininess)
        {
            string path = $"{MAT_OUTPUT_DIR}/{matName}.mat";
            Shader sh = Shader.Find("KSP/Specular") ?? Shader.Find("Standard");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh) { name = matName };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = sh;
            }

            mat.mainTexture = mainTex;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", specColor);
            if (mat.HasProperty("_Shininess")) mat.SetFloat("_Shininess", shininess);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateEmissiveMaterial(string matName, Texture2D mainTex, Color emissiveColor)
        {
            string path = $"{MAT_OUTPUT_DIR}/{matName}.mat";
            Shader sh = Shader.Find("KSP/Emissive/Specular") ?? Shader.Find("KSP/Specular") ?? Shader.Find("Standard");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh) { name = matName };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = sh;
            }

            mat.mainTexture = mainTex;
            if (mat.HasProperty("_Emissive")) mat.SetTexture("_Emissive", mainTex);
            if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", emissiveColor);
            if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", Color.white);
            if (mat.HasProperty("_Shininess")) mat.SetFloat("_Shininess", 0.85f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        #endregion

        #region 1. Ana Gövde: RoverCoreProcedural (5 Oranlı & Estetik Şasi)

        private static GameObject BuildRoverCoreProcedural()
        {
            GameObject root = CreatePartRoot("RoverCoreProcedural", "ProceduralRovers/Parts/Core/Assets");

            BuildChassisMicro(root.transform);
            BuildChassisHeavyScience(root.transform);
            BuildChassisLunokhod(root.transform);
            BuildChassisApolloLrv(root.transform);
            BuildChassisSciFi(root.transform);

            SaveAsPrefab(root);
            return root;
        }

        /// <summary>
        /// 1. MICRO (Sojourner — 0.65m W x 0.30m H x 0.90m L)
        /// Node'lar: top(0,+0.15,0), bottom(0,-0.15,0), front(0,0,+0.45), rear(0,0,-0.45),
        /// wheelFL/FR(±0.325,-0.08,+0.34), wheelML/MR(±0.325,-0.08,0), wheelRL/RR(±0.325,-0.08,-0.34).
        /// </summary>
        private static void BuildChassisMicro(Transform parent)
        {
            GameObject group = CreateChassisRoot(parent, "Chassis_Micro", new Vector3(0.65f, 0.30f, 0.90f));

            // Alt Karter Koruma Plakası (Y: -0.150 .. -0.095)
            AddChamferBox(group.transform, "Micro_BellyPan", new Vector3(0f, -0.122f, 0f), Vector3.zero,
                new Vector3(0.54f, 0.055f, 0.82f), 0.015f, _matDarkMetal);

            // Altın Kapton MLI Sıcak Aviyonik Gövde (Y: -0.095 .. +0.115)
            AddChamferBox(group.transform, "Micro_GoldWarmBox", new Vector3(0f, 0.010f, 0f), Vector3.zero,
                new Vector3(0.56f, 0.210f, 0.82f), 0.025f, _matGoldFoil);

            // Üst Alüminyum Güneş Paneli Çerçevesi (Y: +0.115 .. +0.145) & Fotovoltaik Hücre Katmanı (Y: +0.145 .. +0.150)
            AddChamferBox(group.transform, "Micro_SolarDeckFrame", new Vector3(0f, 0.130f, 0f), Vector3.zero,
                new Vector3(0.63f, 0.030f, 0.88f), 0.010f, _matSilverMachined);
            AddChamferBox(group.transform, "Micro_SolarCells", new Vector3(0f, 0.147f, 0f), Vector3.zero,
                new Vector3(0.57f, 0.006f, 0.82f), 0.002f, _matSolarCells);

            // Ön Stereo Kamera & Lazer Mesafe Ölçer Yuvası (Tam ön node Z=+0.45 hizasında)
            AddChamferBox(group.transform, "Micro_FrontVisor", new Vector3(0f, 0.040f, 0.430f), Vector3.zero,
                new Vector3(0.30f, 0.095f, 0.040f), 0.008f, _matDarkMetal);
            AddLensAssembly(group.transform, "Micro_CamL", new Vector3(-0.075f, 0.040f, 0.450f), Vector3.zero, 0.026f, 0.018f, _matOpticsDark);
            AddLensAssembly(group.transform, "Micro_CamR", new Vector3( 0.075f, 0.040f, 0.450f), Vector3.zero, 0.026f, 0.018f, _matOpticsDark);
            AddLensAssembly(group.transform, "Micro_LaserC", new Vector3(0f, 0.040f, 0.450f), Vector3.zero, 0.018f, 0.016f, _matLaserRuby);

            // Arka APXS Braketi ve Sol Arka İnce Telemetri Anteni
            AddChamferBox(group.transform, "Micro_RearMount", new Vector3(0f, 0.00f, -0.430f), Vector3.zero,
                new Vector3(0.28f, 0.14f, 0.040f), 0.008f, _matDarkMetal);
            AddFrustum(group.transform, "Micro_AntennaBase", new Vector3(-0.26f, 0.165f, -0.38f), Vector3.zero, 0.022f, 0.014f, 0.03f, 12, _matDarkMetal);
            AddFrustum(group.transform, "Micro_WhipAntenna", new Vector3(-0.26f, 0.280f, -0.38f), Vector3.zero, 0.007f, 0.004f, 0.20f, 8, _matSilverMachined);

            // Milimetrik Rocker-Bogie (FL=+0.34, ML=0.00, RL=-0.34, Y=-0.08, X=±0.31)
            BuildProportionalRockerBogie(group.transform, sideX: 0.305f, hubY: -0.010f, hubZ: 0.09f,
                frontZ: 0.34f, midZ: 0.00f, rearZ: -0.34f, wheelHubY: -0.080f, diffBarY: 0.154f, tubeRadius: 0.014f);
        }

        /// <summary>
        /// 2. HEAVY SCIENCE (Curiosity / Perseverance — 1.40m W x 0.65m H x 2.20m L)
        /// Node'lar: top(0,+0.325,0), bottom(0,-0.325,0), front(0,0,+1.10), rear(0,0,-1.10),
        /// wheelFL/FR(±0.70,-0.15,+0.85), wheelML/MR(±0.70,-0.15,-0.05), wheelRL/RR(±0.70,-0.15,-0.85),
        /// mastMount(+0.35,+0.325,+0.75), payloadFront(0,+0.10,+1.10), payloadRear(0,+0.16,-1.10).
        /// </summary>
        private static void BuildChassisHeavyScience(Transform parent)
        {
            GameObject group = CreateChassisRoot(parent, "Chassis_HeavySci", new Vector3(1.40f, 0.65f, 2.20f));

            // 1. Alt Karter Koruması (Y: -0.325 .. -0.225)
            AddChamferBox(group.transform, "Heavy_BellySkid", new Vector3(0f, -0.275f, 0.04f), Vector3.zero,
                new Vector3(1.12f, 0.10f, 1.84f), 0.03f, _matDarkMetal);

            // 2. Ana Kırık-Beyaz Aviyonik Gövde (Y: -0.225 .. +0.235)
            AddChamferBox(group.transform, "Heavy_MainWarmBox", new Vector3(0f, 0.005f, 0.04f), Vector3.zero,
                new Vector3(1.26f, 0.46f, 1.96f), 0.06f, _matStockWhite);

            // Yan Girintili Altın Termal Folyo Bölmeleri ve Yapısal Kaburgalar (X: ±0.64m -> X=±0.70 tekerlek kollarına çarpmaz!)
            AddChamferBox(group.transform, "Heavy_GoldBay_L", new Vector3(-0.625f, 0.02f, 0.04f), Vector3.zero,
                new Vector3(0.03f, 0.28f, 1.48f), 0.005f, _matGoldFoil);
            AddChamferBox(group.transform, "Heavy_GoldBay_R", new Vector3( 0.625f, 0.02f, 0.04f), Vector3.zero,
                new Vector3(0.03f, 0.28f, 1.48f), 0.005f, _matGoldFoil);
            for (int i = -2; i <= 2; i++)
            {
                float ribZ = i * 0.36f + 0.04f;
                AddChamferBox(group.transform, $"Heavy_RibL_{i}", new Vector3(-0.635f, 0.02f, ribZ), Vector3.zero,
                    new Vector3(0.035f, 0.34f, 0.05f), 0.006f, _matDarkMetal);
                AddChamferBox(group.transform, $"Heavy_RibR_{i}", new Vector3( 0.635f, 0.02f, ribZ), Vector3.zero,
                    new Vector3(0.035f, 0.34f, 0.05f), 0.006f, _matDarkMetal);
            }

            // 3. Üst Ekipman Güvertesi (Üst yüzeyi tam Y = +0.325m!)
            AddChamferBox(group.transform, "Heavy_UpperDeck", new Vector3(0f, 0.280f, 0.04f), Vector3.zero,
                new Vector3(1.18f, 0.090f, 1.82f), 0.025f, _matStockWhite);
            AddChamferBox(group.transform, "Heavy_DeckTrimRail_L", new Vector3(-0.56f, 0.320f, 0.04f), Vector3.zero,
                new Vector3(0.05f, 0.015f, 1.72f), 0.004f, _matDarkMetal);
            AddChamferBox(group.transform, "Heavy_DeckTrimRail_R", new Vector3( 0.56f, 0.320f, 0.04f), Vector3.zero,
                new Vector3(0.05f, 0.015f, 1.72f), 0.004f, _matDarkMetal);

            // Sağ Ön MastCam Montaj Flanşı (Tam mastMount = +0.35, +0.325, +0.75 noktasında!)
            AddFrustum(group.transform, "Heavy_MastSocket", new Vector3(0.35f, 0.322f, 0.75f), Vector3.zero,
                0.11f, 0.09f, 0.02f, 16, _matDarkMetal);
            AddFrustum(group.transform, "Heavy_MastGoldRing", new Vector3(0.35f, 0.326f, 0.75f), Vector3.zero,
                0.08f, 0.08f, 0.01f, 16, _matGoldFoil);

            // Sol Arka UHF Silindirik Anten & X-Band Yüksek Kazançlı Altın Anten (Merkez payloadTop1/Top2'yi kapatmaz!)
            AddFrustum(group.transform, "Heavy_UHFBase", new Vector3(-0.38f, 0.35f, -0.62f), Vector3.zero, 0.075f, 0.070f, 0.05f, 16, _matSilverMachined);
            AddFrustum(group.transform, "Heavy_UHFCylinder", new Vector3(-0.38f, 0.42f, -0.62f), Vector3.zero, 0.065f, 0.065f, 0.11f, 16, _matDarkMetal);
            AddFrustum(group.transform, "Heavy_HGAPedestal", new Vector3(-0.36f, 0.345f, 0.18f), Vector3.zero, 0.045f, 0.030f, 0.04f, 12, _matDarkMetal);
            AddFrustum(group.transform, "Heavy_HGADish", new Vector3(-0.36f, 0.375f, 0.18f), new Vector3(12f, 0f, 0f), 0.12f, 0.13f, 0.018f, 20, _matGoldFoil);

            // 4. Ön Tampon, HazCam Stereo Barı & Robotik Kol Taret Soketi (Tam payloadFront = 0, +0.10, +1.10 noktasında!)
            AddChamferBox(group.transform, "Heavy_FrontBulkhead", new Vector3(0f, -0.02f, 1.055f), Vector3.zero,
                new Vector3(1.12f, 0.34f, 0.09f), 0.02f, _matDarkMetal);
            AddFrustum(group.transform, "Heavy_FrontPayloadCollar", new Vector3(0f, 0.10f, 1.095f), new Vector3(90f, 0f, 0f),
                0.13f, 0.11f, 0.025f, 16, _matGoldFoil);
            // 4x Ön HazCam Mercekleri
            AddLensAssembly(group.transform, "Heavy_HazCam_LL", new Vector3(-0.42f, -0.06f, 1.10f), Vector3.zero, 0.032f, 0.02f, _matOpticsDark);
            AddLensAssembly(group.transform, "Heavy_HazCam_LR", new Vector3(-0.30f, -0.06f, 1.10f), Vector3.zero, 0.032f, 0.02f, _matOpticsDark);
            AddLensAssembly(group.transform, "Heavy_HazCam_RL", new Vector3( 0.30f, -0.06f, 1.10f), Vector3.zero, 0.032f, 0.02f, _matOpticsDark);
            AddLensAssembly(group.transform, "Heavy_HazCam_RR", new Vector3( 0.42f, -0.06f, 1.10f), Vector3.zero, 0.032f, 0.02f, _matOpticsDark);

            // 5. Arka MMRTG Yuvası & Yan Radyatör Koruma Kanatları (Tam payloadRear = 0, +0.16, -1.10 noktasında!)
            AddChamferBox(group.transform, "Heavy_RearFrame", new Vector3(0f, 0.04f, -1.01f), Vector3.zero,
                new Vector3(1.04f, 0.36f, 0.16f), 0.025f, _matStockWhite);
            AddChamferBox(group.transform, "Heavy_RearShroud_L", new Vector3(-0.38f, 0.14f, -1.04f), new Vector3(12f, 0f, 0f),
                new Vector3(0.06f, 0.34f, 0.16f), 0.01f, _matDarkMetal);
            AddChamferBox(group.transform, "Heavy_RearShroud_R", new Vector3( 0.38f, 0.14f, -1.04f), new Vector3(12f, 0f, 0f),
                new Vector3(0.06f, 0.34f, 0.16f), 0.01f, _matDarkMetal);
            AddFrustum(group.transform, "Heavy_RearPayloadCollar", new Vector3(0f, 0.16f, -1.095f), new Vector3(-90f, 0f, 0f),
                0.15f, 0.13f, 0.025f, 16, _matDarkMetal);

            // 6. Oranlı Titanyum Rocker-Bogie Mekanizması (FL=+0.85, ML=-0.05, RL=-0.85, Y=-0.15, X=±0.685)
            BuildProportionalRockerBogie(group.transform, sideX: 0.685f, hubY: 0.04f, hubZ: 0.18f,
                frontZ: 0.85f, midZ: -0.05f, rearZ: -0.85f, wheelHubY: -0.150f, diffBarY: 0.340f, tubeRadius: 0.028f);
        }

        /// <summary>
        /// 3. TUB HULL (Lunokhod 1/2 — 1.60m W x 0.85m H x 2.10m L)
        /// Node'lar: top(0,+0.425,0), bottom(0,-0.425,0), 8x wheel(±0.80,-0.24, Z=±0.78,±0.26),
        /// mastMount(-0.38,+0.425,+0.68), payloadTop1(0,+0.425,-0.10), payloadFront(0,+0.14,+1.05).
        /// </summary>
        private static void BuildChassisLunokhod(Transform parent)
        {
            GameObject group = CreateChassisRoot(parent, "Chassis_Lunokhod", new Vector3(1.60f, 0.85f, 2.10f));

            // Alt Şasi Omurgası (Y: -0.425 .. -0.180)
            AddChamferBox(group.transform, "Luno_Undercarriage", new Vector3(0f, -0.30f, 0f), Vector3.zero,
                new Vector3(1.24f, 0.25f, 1.76f), 0.03f, _matDarkMetal);

            // İkonik Yukarı Doğru Genişleyen Basınçlı Küvet Gövde (Y: -0.180 .. +0.385, Z uzatmalı eliptik kesit)
            GameObject tubGo = AddFrustum(group.transform, "Luno_ConicalTub", new Vector3(0f, 0.10f, 0f), Vector3.zero,
                radiusBottom: 0.64f, radiusTop: 0.76f, length: 0.56f, segments: 24, _matSovietOlive);
            BakeAdditionalScaleIntoMesh(tubGo, new Vector3(1.0f, 1.0f, 1.24f));

            // Üst Kapak Çemberi & Arka Menteşe (Üst kenar Y = +0.425m!)
            GameObject rimGo = AddFrustum(group.transform, "Luno_TopFlange", new Vector3(0f, 0.402f, 0f), Vector3.zero,
                radiusBottom: 0.76f, radiusTop: 0.78f, length: 0.045f, segments: 24, _matSilverMachined);
            BakeAdditionalScaleIntoMesh(rimGo, new Vector3(1.0f, 1.0f, 1.24f));
            AddChamferBox(group.transform, "Luno_RearHinge", new Vector3(0f, 0.415f, -0.94f), Vector3.zero,
                new Vector3(0.36f, 0.05f, 0.08f), 0.01f, _matDarkMetal);

            // Ön Stereo TV Kameraları (Sol & Sağ Konik Muhafazalar) + Merkez Ekipman Flanşı (payloadFront = 0,+0.14,+1.05)
            AddFrustum(group.transform, "Luno_TeleCam_L", new Vector3(-0.34f, 0.16f, 0.94f), new Vector3(90f, 0f, 0f),
                0.11f, 0.085f, 0.16f, 16, _matSovietOlive);
            AddLensAssembly(group.transform, "Luno_TeleLens_L", new Vector3(-0.34f, 0.16f, 1.02f), Vector3.zero, 0.075f, 0.02f, _matOpticsDark);

            AddFrustum(group.transform, "Luno_TeleCam_R", new Vector3( 0.34f, 0.16f, 0.94f), new Vector3(90f, 0f, 0f),
                0.11f, 0.085f, 0.16f, 16, _matSovietOlive);
            AddLensAssembly(group.transform, "Luno_TeleLens_R", new Vector3( 0.34f, 0.16f, 1.02f), Vector3.zero, 0.075f, 0.02f, _matOpticsDark);

            AddFrustum(group.transform, "Luno_FrontPort", new Vector3(0f, 0.14f, 1.035f), new Vector3(90f, 0f, 0f),
                0.10f, 0.08f, 0.03f, 16, _matGoldFoil);

            // Sol Ön Mast Soketi (-0.38, +0.425, +0.68) + Sağ Ön Konik Helisel Anten (+0.38, +0.52, +0.68)
            AddFrustum(group.transform, "Luno_MastCollar", new Vector3(-0.38f, 0.425f, 0.68f), Vector3.zero,
                0.08f, 0.07f, 0.02f, 16, _matDarkMetal);
            AddFrustum(group.transform, "Luno_HelixCone", new Vector3(0.38f, 0.52f, 0.68f), new Vector3(18f, 0f, 0f),
                0.08f, 0.025f, 0.22f, 14, _matGoldFoil);

            // 8 Tekerlek Yan Burulma Çubuğu (Torsion-Bar) Aks Kolları -> Tam X = ±0.80, Y = -0.24 yuvalarına uzanır!
            float[] wheelZ = { 0.78f, 0.26f, -0.26f, -0.78f };
            for (int i = 0; i < wheelZ.Length; i++)
            {
                AddFrustum(group.transform, $"Luno_Outrigger_L{i}", new Vector3(-0.70f, -0.24f, wheelZ[i]), new Vector3(0f, 0f, 90f),
                    0.055f, 0.045f, 0.20f, 12, _matDarkMetal);
                AddFrustum(group.transform, $"Luno_Outrigger_R{i}", new Vector3( 0.70f, -0.24f, wheelZ[i]), new Vector3(0f, 0f, -90f),
                    0.055f, 0.045f, 0.20f, 12, _matDarkMetal);
            }
        }

        /// <summary>
        /// 4. FOLDABLE FRAME (Apollo LRV — 1.30m W x 0.22m H x 2.30m L)
        /// Node'lar: top(0,+0.11,0), bottom(0,-0.11,0), 4x wheel(±0.65,-0.04,±0.92),
        /// mastMount(0,+0.11,+0.98), payloadTop1(0,+0.11,+0.30), payloadTop2(0,+0.11,-0.55).
        /// </summary>
        private static void BuildChassisApolloLrv(Transform parent)
        {
            GameObject group = CreateChassisRoot(parent, "Chassis_ApolloLRV", new Vector3(1.30f, 0.22f, 2.30f));

            // Alüminyum Uzay-Kafes Taban Şasisi (Y: -0.10 .. +0.02)
            AddChamferBox(group.transform, "LRV_FloorTray", new Vector3(0f, -0.045f, 0f), Vector3.zero,
                new Vector3(0.98f, 0.04f, 1.96f), 0.008f, _matSilverMachined);
            AddChamferBox(group.transform, "LRV_SideRail_L", new Vector3(-0.51f, -0.02f, 0f), Vector3.zero,
                new Vector3(0.05f, 0.09f, 2.22f), 0.010f, _matStockWhite);
            AddChamferBox(group.transform, "LRV_SideRail_R", new Vector3( 0.51f, -0.02f, 0f), Vector3.zero,
                new Vector3(0.05f, 0.09f, 2.22f), 0.010f, _matStockWhite);
            AddChamferBox(group.transform, "LRV_FrontBumper", new Vector3(0f, -0.01f, 1.12f), Vector3.zero,
                new Vector3(1.06f, 0.07f, 0.06f), 0.010f, _matSilverMachined);
            AddChamferBox(group.transform, "LRV_RearBumper", new Vector3(0f, -0.01f, -1.12f), Vector3.zero,
                new Vector3(1.06f, 0.07f, 0.06f), 0.010f, _matSilverMachined);

            // Ön ve Arka Çift-Salıncak (Double-Wishbone) Aks Köprüleri -> Tam (±0.65, -0.04, ±0.92) tekerlek yuvalarına!
            AddChamferBox(group.transform, "LRV_AxleBridge_F", new Vector3(0f, -0.04f, 0.92f), Vector3.zero,
                new Vector3(1.30f, 0.065f, 0.11f), 0.010f, _matDarkMetal);
            AddChamferBox(group.transform, "LRV_AxleBridge_R", new Vector3(0f, -0.04f, -0.92f), Vector3.zero,
                new Vector3(1.30f, 0.065f, 0.11f), 0.010f, _matDarkMetal);

            // Yan Yana 2 Katlanır Astronot Koltuğu (X = ±0.26m, Z = -0.10m) + Orta T-Kol Konsolu
            float[] seatX = { -0.26f, 0.26f };
            for (int i = 0; i < 2; i++)
            {
                string s = i == 0 ? "L" : "R";
                AddChamferBox(group.transform, $"LRV_SeatCushion_{s}", new Vector3(seatX[i], 0.01f, -0.06f), Vector3.zero,
                    new Vector3(0.36f, 0.035f, 0.38f), 0.008f, _matDarkMetal);
                AddChamferBox(group.transform, $"LRV_SeatBack_{s}", new Vector3(seatX[i], 0.19f, -0.26f), new Vector3(-12f, 0f, 0f),
                    new Vector3(0.35f, 0.34f, 0.035f), 0.008f, _matStockWhite);
                AddChamferBox(group.transform, $"LRV_SeatStrap_{s}", new Vector3(seatX[i], 0.19f, -0.24f), new Vector3(-12f, 0f, 0f),
                    new Vector3(0.22f, 0.26f, 0.015f), 0.003f, _matGoldFoil);
            }

            // Orta Navigasyon Konsolu & T-Kumanda Kolu (payloadTop1=0,0.11,0.30 noktasının arkasında Z=0.08)
            AddChamferBox(group.transform, "LRV_ConsolePedestal", new Vector3(0f, 0.08f, 0.08f), new Vector3(22f, 0f, 0f),
                new Vector3(0.12f, 0.18f, 0.16f), 0.01f, _matDarkMetal);

            // Ön Altın Termal Batarya Kutuları (Z = +0.68m) & Arka Bilim Bagaj Rafı (Z = -0.76m)
            AddChamferBox(group.transform, "LRV_FrontBatteryBay", new Vector3(0f, 0.045f, 0.68f), Vector3.zero,
                new Vector3(0.84f, 0.13f, 0.34f), 0.015f, _matGoldFoil);
            AddFrustum(group.transform, "LRV_FrontMastSocket", new Vector3(0f, 0.10f, 0.98f), Vector3.zero,
                0.08f, 0.07f, 0.025f, 16, _matDarkMetal);
            AddChamferBox(group.transform, "LRV_AftToolPallet", new Vector3(0f, 0.04f, -0.76f), Vector3.zero,
                new Vector3(0.86f, 0.12f, 0.28f), 0.012f, _matStockWhite);
        }

        /// <summary>
        /// 5. ARMORED EXPLORER (Sci-Fi — 1.80m W x 0.80m H x 2.60m L)
        /// Node'lar: top(0,+0.40,0), bottom(0,-0.40,0), front(0,0,+1.30), rear(0,0,-1.30),
        /// wheelFL/FR(±0.90,-0.18,+1.00), wheelML/MR(±0.90,-0.18,0), wheelRL/RR(±0.90,-0.18,-1.00).
        /// </summary>
        private static void BuildChassisSciFi(Transform parent)
        {
            GameObject group = CreateChassisRoot(parent, "Chassis_SciFi", new Vector3(1.80f, 0.80f, 2.60f));

            // V-Kesitli Zırhlı Alt Karter (Y: -0.40 .. -0.26)
            AddChamferBox(group.transform, "SciFi_VBelly", new Vector3(0f, -0.33f, 0f), Vector3.zero,
                new Vector3(1.36f, 0.14f, 2.24f), 0.04f, _matDarkMetal);

            // Ana Eğimli Zırh Gövdesi (Y: -0.26 .. +0.40 -> Üst yüzey tam Y=+0.40m!)
            AddChamferBox(group.transform, "SciFi_MainArmorHull", new Vector3(0f, 0.07f, -0.02f), Vector3.zero,
                new Vector3(1.62f, 0.66f, 2.36f), 0.09f, _matSciFiArmor);

            // Ön Eğimli Burun Plakası (Z -> +1.30m) & Işıklı Cyan Kokpit Vizörü
            AddChamferBox(group.transform, "SciFi_FrontNose", new Vector3(0f, 0.02f, 1.20f), new Vector3(16f, 0f, 0f),
                new Vector3(1.42f, 0.44f, 0.20f), 0.04f, _matDarkMetal);
            AddChamferBox(group.transform, "SciFi_CyanVisorStrip", new Vector3(0f, 0.22f, 1.18f), new Vector3(16f, 0f, 0f),
                new Vector3(1.16f, 0.07f, 0.04f), 0.008f, _matCyanEmissive);

            // Yan Zırh Etekleri (X = ±0.82m -> X=±0.90 tekerlek hattına temiz boşluk bırakır)
            AddChamferBox(group.transform, "SciFi_Sponson_L", new Vector3(-0.81f, 0.08f, 0f), Vector3.zero,
                new Vector3(0.06f, 0.34f, 2.20f), 0.015f, _matDarkMetal);
            AddChamferBox(group.transform, "SciFi_Sponson_R", new Vector3( 0.81f, 0.08f, 0f), Vector3.zero,
                new Vector3(0.06f, 0.34f, 2.20f), 0.015f, _matDarkMetal);

            // Üst Yan Koruma Rayları (Merkezdeki mastMount ve payloadTop1/2 tamamen açık!)
            AddChamferBox(group.transform, "SciFi_RoofRail_L", new Vector3(-0.62f, 0.43f, -0.05f), Vector3.zero,
                new Vector3(0.06f, 0.06f, 1.95f), 0.01f, _matSilverMachined);
            AddChamferBox(group.transform, "SciFi_RoofRail_R", new Vector3( 0.62f, 0.43f, -0.05f), Vector3.zero,
                new Vector3(0.06f, 0.06f, 1.95f), 0.01f, _matSilverMachined);

            // Arka Çift Füzyon Soğutma Nozulu (X = ±0.48m -> Merkezdeki payloadRear(0,+0.16,-1.30) yuvası açık!)
            AddFrustum(group.transform, "SciFi_ThrusterShroud_L", new Vector3(-0.48f, 0.08f, -1.24f), new Vector3(-90f, 0f, 0f),
                0.18f, 0.14f, 0.12f, 16, _matDarkMetal);
            AddFrustum(group.transform, "SciFi_ThrusterCore_L", new Vector3(-0.48f, 0.08f, -1.29f), new Vector3(-90f, 0f, 0f),
                0.12f, 0.12f, 0.02f, 16, _matCyanEmissive);

            AddFrustum(group.transform, "SciFi_ThrusterShroud_R", new Vector3( 0.48f, 0.08f, -1.24f), new Vector3(-90f, 0f, 0f),
                0.18f, 0.14f, 0.12f, 16, _matDarkMetal);
            AddFrustum(group.transform, "SciFi_ThrusterCore_R", new Vector3( 0.48f, 0.08f, -1.29f), new Vector3(-90f, 0f, 0f),
                0.12f, 0.12f, 0.02f, 16, _matCyanEmissive);

            // Ağır Rocker-Bogie (FL=+1.00, ML=0.00, RL=-1.00, Y=-0.18, X=±0.885)
            BuildProportionalRockerBogie(group.transform, sideX: 0.885f, hubY: 0.04f, hubZ: 0.18f,
                frontZ: 1.00f, midZ: 0.00f, rearZ: -1.00f, wheelHubY: -0.180f, diffBarY: 0.415f, tubeRadius: 0.036f);
        }

        private static GameObject CreateChassisRoot(Transform parent, string groupName, Vector3 colliderDimensions)
        {
            GameObject group = new GameObject(groupName);
            group.transform.SetParent(parent, false);
            group.layer = LAYER_DEFAULT;

            BoxCollider bc = group.AddComponent<BoxCollider>();
            bc.size = colliderDimensions;
            bc.center = Vector3.zero;

            group.SetActive(true);
            return group;
        }

        /// <summary>
        /// İki nokta arasına tam oturan silindirik titanyum borularla (ConnectTube) Rocker_L/R, Bogie_L/R ve
        /// DifferentialBar mekanizmasını tekerlek node koordinatlarına sıfır hatayla birleştirir.
        /// </summary>
        private static void BuildProportionalRockerBogie(
            Transform chassisParent, float sideX, float hubY, float hubZ,
            float frontZ, float midZ, float rearZ, float wheelHubY, float diffBarY, float tubeRadius)
        {
            float bogiePivotZ = (midZ + rearZ) * 0.5f;
            float bogiePivotY = (hubY + wheelHubY) * 0.5f;

            for (int side = -1; side <= 1; side += 2)
            {
                string s = side < 0 ? "L" : "R";
                float x = side * sideX;

                GameObject rocker = new GameObject($"Rocker_{s}");
                rocker.transform.SetParent(chassisParent, false);
                rocker.transform.localPosition = new Vector3(x, hubY, hubZ);

                // Ana Mafsal Göbeği
                AddFrustum(rocker.transform, $"RockerHub_{s}", Vector3.zero, new Vector3(0f, 0f, 90f),
                    tubeRadius * 2.2f, tubeRadius * 2.2f, tubeRadius * 2.0f, 16, _matGoldFoil);

                // Ön Tekerlek Kolu: (0,0,0)'dan (0, wheelHubY - hubY, frontZ - hubZ) noktasına tam bağlanır!
                Vector3 frontTargetLocal = new Vector3(0f, wheelHubY - hubY, frontZ - hubZ);
                Vector3 frontKneeLocal = new Vector3(0f, (wheelHubY - hubY) * 0.25f, (frontZ - hubZ) * 0.65f);
                AddTubeBetween(rocker.transform, $"RockerFrontUpper_{s}", Vector3.zero, frontKneeLocal, tubeRadius, _matDarkMetal);
                AddTubeBetween(rocker.transform, $"RockerFrontLower_{s}", frontKneeLocal, frontTargetLocal, tubeRadius * 0.9f, _matSilverMachined);
                AddFrustum(rocker.transform, $"RockerFrontJoint_{s}", frontKneeLocal, new Vector3(0f, 0f, 90f),
                    tubeRadius * 1.4f, tubeRadius * 1.4f, tubeRadius * 1.8f, 12, _matDarkMetal);

                // Arka Kol: (0,0,0)'dan Bogie Mafsalına (0, bogiePivotY - hubY, bogiePivotZ - hubZ)
                Vector3 bogiePivotInRocker = new Vector3(0f, bogiePivotY - hubY, bogiePivotZ - hubZ);
                AddTubeBetween(rocker.transform, $"RockerRearArm_{s}", Vector3.zero, bogiePivotInRocker, tubeRadius, _matDarkMetal);

                // Arka Bogie Alt-Grubu
                GameObject bogie = new GameObject($"Bogie_{s}");
                bogie.transform.SetParent(rocker.transform, false);
                bogie.transform.localPosition = bogiePivotInRocker;

                AddFrustum(bogie.transform, $"BogiePivot_{s}", Vector3.zero, new Vector3(0f, 0f, 90f),
                    tubeRadius * 1.8f, tubeRadius * 1.8f, tubeRadius * 1.8f, 14, _matGoldFoil);

                Vector3 midInBogie = new Vector3(0f, wheelHubY - bogiePivotY, midZ - bogiePivotZ);
                Vector3 rearInBogie = new Vector3(0f, wheelHubY - bogiePivotY, rearZ - bogiePivotZ);
                AddTubeBetween(bogie.transform, $"BogieMidLeg_{s}", Vector3.zero, midInBogie, tubeRadius * 0.85f, _matSilverMachined);
                AddTubeBetween(bogie.transform, $"BogieRearLeg_{s}", Vector3.zero, rearInBogie, tubeRadius * 0.85f, _matSilverMachined);
            }

            // Üst Diferansiyel Denge Çubuğu (DifferentialBar)
            GameObject diffBar = new GameObject("DifferentialBar");
            diffBar.transform.SetParent(chassisParent, false);
            diffBar.transform.localPosition = new Vector3(0f, diffBarY, hubZ);

            AddFrustum(diffBar.transform, "DiffCenterBearing", Vector3.zero, Vector3.zero,
                tubeRadius * 2.2f, tubeRadius * 1.8f, tubeRadius * 1.5f, 16, _matGoldFoil);
            AddTubeBetween(diffBar.transform, "DiffCrossRod",
                new Vector3(-sideX * 0.94f, 0f, 0f), new Vector3(sideX * 0.94f, 0f, 0f), tubeRadius * 0.75f, _matDarkMetal);
        }

        #endregion

        #region 2. Tekerlekler: WheelMeshSpring & WheelHeavyDuty (Sıfır Çakışmalı & Detaylı)

        /// <summary>
        /// PR-W10 Nitinol-Mesh Tekerlek (radius = 0.26m, tireWidth = 0.15m, node_stack_mount = +0.18, +0.15, 0)
        /// Dönen tekerlek X: [-0.075, +0.075] aralığındadır; tüm süspansiyon/direksiyon kolları X > +0.095 veya Y > +0.29 bölgesindedir!
        /// </summary>
        private static GameObject BuildMeshSpringWheelPart()
        {
            const float radius = 0.26f;
            const float width = 0.15f;
            const float suspDist = 0.16f;

            GameObject root = CreateWheelCoreHierarchy("WheelMeshSpring", "ProceduralRovers/Parts/Mobility/Assets",
                radius, suspDist, mountBoxPos: new Vector3(0.155f, 0.150f, 0f), mountBoxSize: new Vector3(0.06f, 0.11f, 0.12f),
                out Transform suspTr, out Transform steerTr, out Transform wheelTr, out Transform bustedTr);

            // 1. Süspansiyon Kolu & Helezon Yaylı Amortisör (Tekerlek üstü Y=+0.305m kemerine çıkar -> lastiğe asla değmez!)
            AddTubeBetween(suspTr, "Susp_UpperWishbone", new Vector3(0.145f, 0.165f, 0f), new Vector3(0.00f, 0.305f, 0f), 0.018f, _matDarkMetal);
            AddTubeBetween(suspTr, "Susp_ShockCylinder", new Vector3(0.140f, 0.125f, 0f), new Vector3(0.04f, 0.285f, 0f), 0.022f, _matGoldFoil);

            // 2. Direksiyon Çatalı (Üst rot başı Y=+0.30m -> İç yan kol X=+0.098m üzerinden X=+0.075 aks göbeğine iner!)
            AddFrustum(steerTr, "Steer_KingpinCap", new Vector3(0f, 0.300f, 0f), Vector3.zero, 0.028f, 0.024f, 0.025f, 12, _matGoldFoil);
            AddTubeBetween(steerTr, "Steer_UpperBridge", new Vector3(0f, 0.295f, 0f), new Vector3(0.098f, 0.235f, 0f), 0.016f, _matSilverMachined);
            AddTubeBetween(steerTr, "Steer_SideKnuckle", new Vector3(0.098f, 0.235f, 0f), new Vector3(0.098f, 0.000f, 0f), 0.018f, _matDarkMetal);
            AddFrustum(steerTr, "Steer_SpindleMotor", new Vector3(0.078f, 0f, 0f), new Vector3(0f, 0f, 90f), 0.055f, 0.055f, 0.050f, 16, _matDarkMetal);

            // 3. Dönen Tekerlek (İçi Boş Nitinol Hasır Halka + Altın Göbek + 8 Esnek Yay Tel + 16 İnce Sırt Paleti)
            AddHollowWheelBand(wheelTr, "MeshTire_HollowBand", outerRadius: radius, innerRadius: 0.205f, width: width, segments: 28, _matNitinolMesh);
            AddFrustum(wheelTr, "MeshTire_CenterHub", Vector3.zero, new Vector3(0f, 0f, 90f), 0.070f, 0.070f, width * 0.92f, 18, _matGoldFoil);
            AddFrustum(wheelTr, "MeshTire_OuterCap", new Vector3(-width * 0.47f, 0f, 0f), new Vector3(0f, 0f, 90f), 0.048f, 0.038f, 0.018f, 16, _matDarkMetal);

            for (int i = 0; i < 8; i++)
            {
                float a1 = (i * 45f) * Mathf.Deg2Rad;
                float a2 = (i * 45f + 18f) * Mathf.Deg2Rad;
                Vector3 pInner = new Vector3(0f, Mathf.Cos(a1) * 0.065f, Mathf.Sin(a1) * 0.065f);
                Vector3 pOuter = new Vector3(0f, Mathf.Cos(a2) * 0.210f, Mathf.Sin(a2) * 0.210f);
                AddTubeBetween(wheelTr, $"SpringSpoke_{i}", pInner, pOuter, 0.010f, _matSilverMachined);
            }

            for (int i = 0; i < 16; i++)
            {
                float angDeg = i * 22.5f;
                float rad = angDeg * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(0f, Mathf.Cos(rad) * (radius - 0.002f), Mathf.Sin(rad) * (radius - 0.002f));
                AddChamferBox(wheelTr, $"Cleat_{i}", pos, new Vector3(-angDeg, 0f, 0f),
                    new Vector3(width * 0.98f, 0.010f, 0.018f), 0.002f, _matDarkMetal);
            }

            // 4. Hasarlı Tekerlek
            AddHollowWheelBand(bustedTr, "Busted_Band", outerRadius: radius * 0.84f, innerRadius: 0.16f, width: width * 0.9f, segments: 12, _matDarkMetal);

            SaveAsPrefab(root);
            return root;
        }

        /// <summary>
        /// PR-W50 TitanGrip Ağır Hizmet Tekerlek (radius = 0.42m, tireWidth = 0.26m, node_stack_mount = +0.24, +0.20, 0)
        /// </summary>
        private static GameObject BuildHeavyDutyWheelPart()
        {
            const float radius = 0.42f;
            const float width = 0.26f;
            const float suspDist = 0.24f;

            GameObject root = CreateWheelCoreHierarchy("WheelHeavyDuty", "ProceduralRovers/Parts/Mobility/Assets",
                radius, suspDist, mountBoxPos: new Vector3(0.205f, 0.200f, 0f), mountBoxSize: new Vector3(0.08f, 0.15f, 0.18f),
                out Transform suspTr, out Transform steerTr, out Transform wheelTr, out Transform bustedTr);

            // 1. Çift Hidropnömatik Amortisörlü Üst Salıncak (Y=+0.475m tekerlek üstü rot başına bağlanır!)
            AddTubeBetween(suspTr, "HeavySusp_MainBoom", new Vector3(0.195f, 0.215f, 0f), new Vector3(0.00f, 0.475f, 0f), 0.028f, _matDarkMetal);
            AddTubeBetween(suspTr, "HeavySusp_PistonF", new Vector3(0.190f, 0.160f,  0.055f), new Vector3(0.04f, 0.445f,  0.035f), 0.022f, _matGoldFoil);
            AddTubeBetween(suspTr, "HeavySusp_PistonR", new Vector3(0.190f, 0.160f, -0.055f), new Vector3(0.04f, 0.445f, -0.035f), 0.022f, _matGoldFoil);

            // 2. Direksiyon Kaliperi & Üst Çamurluk Siperi (İç yan kol X=+0.160m -> 0.13m lastik duvarından 3cm açıkta!)
            AddFrustum(steerTr, "HeavySteer_TopPivot", new Vector3(0f, 0.470f, 0f), Vector3.zero, 0.042f, 0.036f, 0.035f, 14, _matGoldFoil);
            AddChamferBox(steerTr, "HeavySteer_MudFender", new Vector3(-0.01f, 0.465f, 0f), Vector3.zero,
                new Vector3(width * 0.92f, 0.020f, 0.34f), 0.005f, _matStockWhite);
            AddTubeBetween(steerTr, "HeavySteer_UpperNeck", new Vector3(0f, 0.465f, 0f), new Vector3(0.160f, 0.340f, 0f), 0.024f, _matSilverMachined);
            AddTubeBetween(steerTr, "HeavySteer_VerticalStrut", new Vector3(0.160f, 0.340f, 0f), new Vector3(0.160f, 0.000f, 0f), 0.028f, _matDarkMetal);
            AddFrustum(steerTr, "HeavySteer_HubDrive", new Vector3(0.125f, 0f, 0f), new Vector3(0f, 0f, 90f), 0.11f, 0.11f, 0.075f, 18, _matDarkMetal);

            // 3. Dönen Tekerlek (Kalın Kauçuk Sırt + Alaşım Jant Çemberi + 6 Jant Kolu + Zırhlı Göbek + 18 Diş)
            AddHollowWheelBand(wheelTr, "HeavyTire_Carcass", outerRadius: 0.412f, innerRadius: 0.255f, width: width, segments: 32, _matTireRubber);
            AddHollowWheelBand(wheelTr, "HeavyRim_OuterRing", outerRadius: 0.260f, innerRadius: 0.215f, width: width * 0.94f, segments: 28, _matSilverMachined);
            AddFrustum(wheelTr, "HeavyRim_CenterDisk", Vector3.zero, new Vector3(0f, 0f, 90f), 0.115f, 0.115f, width * 0.78f, 20, _matDarkMetal);
            AddFrustum(wheelTr, "HeavyRim_GoldHubcap", new Vector3(-width * 0.40f, 0f, 0f), new Vector3(0f, 0f, 90f), 0.085f, 0.065f, 0.030f, 16, _matGoldFoil);

            for (int i = 0; i < 6; i++)
            {
                float rad = (i * 60f) * Mathf.Deg2Rad;
                Vector3 p1 = new Vector3(0f, Mathf.Cos(rad) * 0.10f, Mathf.Sin(rad) * 0.10f);
                Vector3 p2 = new Vector3(0f, Mathf.Cos(rad) * 0.225f, Mathf.Sin(rad) * 0.225f);
                AddTubeBetween(wheelTr, $"HeavySpoke_{i}", p1, p2, 0.026f, _matSilverMachined);
            }

            for (int i = 0; i < 18; i++)
            {
                float angDeg = i * 20f;
                float rad = angDeg * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(0f, Mathf.Cos(rad) * 0.414f, Mathf.Sin(rad) * 0.414f);
                AddChamferBox(wheelTr, $"HeavyLug_{i}", pos, new Vector3(-angDeg, 0f, 0f),
                    new Vector3(width * 0.96f, 0.018f, 0.048f), 0.004f, _matTireRubber);
            }

            AddHollowWheelBand(bustedTr, "Busted_HeavyTire", outerRadius: 0.34f, innerRadius: 0.22f, width: width * 0.85f, segments: 14, _matDarkMetal);

            SaveAsPrefab(root);
            return root;
        }

        private static GameObject CreateWheelCoreHierarchy(
            string partName, string exportPath, float radius, float suspDist, Vector3 mountBoxPos, Vector3 mountBoxSize,
            out Transform suspTransform, out Transform steerTransform, out Transform wheelTransform, out Transform bustedTransform)
        {
            GameObject root = CreatePartRoot(partName, exportPath);

            // 1. Şasiye Bağlanan Montaj Braketi (Layer 0 - node_stack_mount ile tam hizalı)
            GameObject mountGo = AddChamferBox(root.transform, "Mount_Mesh", mountBoxPos, Vector3.zero, mountBoxSize, 0.012f, _matDarkMetal, LAYER_DEFAULT);
            BoxCollider mountCol = mountGo.AddComponent<BoxCollider>();
            mountCol.size = mountBoxSize;

            // 2. Görünmez CollisionEnhancer (Sadece bu obje Layer 26'dadır!)
            GameObject colEnhancer = new GameObject("CollisionEnhancer");
            colEnhancer.transform.SetParent(root.transform, false);
            colEnhancer.layer = LAYER_WHEEL_IGNORE;
            SphereCollider sc = colEnhancer.AddComponent<SphereCollider>();
            sc.radius = radius * 0.55f;

            // 3. wheelCollider (Layer 27 - suspensionTransform ile KARDEŞ!)
            GameObject wcObj = new GameObject("wheelCollider");
            wcObj.transform.SetParent(root.transform, false);
            wcObj.transform.localPosition = new Vector3(0f, suspDist * 0.5f, 0f);
            wcObj.layer = LAYER_WHEEL_COLLIDER;
            WheelCollider wc = wcObj.AddComponent<WheelCollider>();
            wc.radius = radius;
            wc.suspensionDistance = suspDist;
            wc.mass = 0.03f;

            // 4. Görsel Transform Zinciri (Tümü Layer 0 - KSP kameraları tarafından %100 çizilir!)
            GameObject suspGo = new GameObject("suspensionTransform");
            suspGo.transform.SetParent(root.transform, false);
            suspGo.layer = LAYER_DEFAULT;
            suspTransform = suspGo.transform;

            GameObject steerGo = new GameObject("steeringTransform");
            steerGo.transform.SetParent(suspTransform, false);
            steerGo.layer = LAYER_DEFAULT;
            steerTransform = steerGo.transform;

            GameObject wheelGo = new GameObject("wheelTransform");
            wheelGo.transform.SetParent(steerTransform, false);
            wheelGo.layer = LAYER_DEFAULT;
            wheelTransform = wheelGo.transform;

            GameObject bustedGo = new GameObject("bustedWheel");
            bustedGo.transform.SetParent(steerTransform, false);
            bustedGo.layer = LAYER_DEFAULT;
            bustedGo.SetActive(false);
            bustedTransform = bustedGo.transform;

            return root;
        }

        #endregion

        #region 3. Tırtıllı Palet: TrackContinuous (Kesintisiz Oval Palet Şeridi & Sıfır Kopukluk)

        /// <summary>
        /// PR-T90 FoundryTrack (radius = 0.38m, node_stack_mount = +0.28, +0.18, 0)
        /// Kesintisiz stadyum/oval palet şeridi (TrackBelt_Mesh) DriveSprocket, IdlerWheel ve 4 alt RoadWheel
        /// etrafını boşluksuz sarar; UV koordinatları çevre boyunca aktığı için doku kaydırma kusursuz çalışır!
        /// </summary>
        private static GameObject BuildContinuousTrackPart()
        {
            GameObject root = CreatePartRoot("TrackContinuous", "ProceduralRovers/Parts/Mobility/Assets");

            // 1. Şasi Bağlantı Braketi (X: +0.15 .. +0.29, Y: +0.18 -> node_stack_mount=+0.28,+0.18,0 ile tam hizalı!)
            GameObject mountGo = AddChamferBox(root.transform, "Mount_Strut_Mesh", new Vector3(0.215f, 0.180f, 0f), Vector3.zero,
                new Vector3(0.14f, 0.14f, 0.32f), 0.015f, _matDarkMetal, LAYER_DEFAULT);
            mountGo.AddComponent<BoxCollider>().size = new Vector3(0.14f, 0.14f, 0.32f);

            // 2. CollisionEnhancer (Layer 26) & wheelCollider (Layer 27)
            GameObject colEnhancer = new GameObject("CollisionEnhancer");
            colEnhancer.transform.SetParent(root.transform, false);
            colEnhancer.layer = LAYER_WHEEL_IGNORE;
            colEnhancer.AddComponent<SphereCollider>().radius = 0.22f;

            GameObject wcObj = new GameObject("wheelCollider");
            wcObj.transform.SetParent(root.transform, false);
            wcObj.transform.localPosition = new Vector3(0f, 0.11f, 0f);
            wcObj.layer = LAYER_WHEEL_COLLIDER;
            WheelCollider wc = wcObj.AddComponent<WheelCollider>();
            wc.radius = 0.38f;
            wc.suspensionDistance = 0.22f;
            wc.mass = 0.075f;

            // 3. suspensionTransform (Layer 0)
            GameObject suspTr = new GameObject("suspensionTransform");
            suspTr.transform.SetParent(root.transform, false);
            suspTr.layer = LAYER_DEFAULT;

            // Süspansiyon Bağlantı Kolu (Mount ile iç şasiyi birleştirir)
            AddTubeBetween(suspTr.transform, "Track_SuspLinkF", new Vector3(0.16f, 0.18f,  0.11f), new Vector3(0.06f, 0.04f,  0.18f), 0.026f, _matGoldFoil);
            AddTubeBetween(suspTr.transform, "Track_SuspLinkR", new Vector3(0.16f, 0.18f, -0.11f), new Vector3(0.06f, 0.04f, -0.18f), 0.026f, _matGoldFoil);

            // İç Taşıyıcı Şasi Kirişi & Dış Zırh Plakası (TrackFrame_Mesh)
            AddChamferBox(suspTr.transform, "TrackFrame_Mesh", new Vector3(0f, -0.04f, 0f), Vector3.zero,
                new Vector3(0.13f, 0.18f, 0.96f), 0.015f, _matSovietOlive);
            AddChamferBox(suspTr.transform, "Track_OuterArmorSkirt", new Vector3(-0.148f, -0.01f, 0f), Vector3.zero,
                new Vector3(0.022f, 0.17f, 1.02f), 0.005f, _matDarkMetal);
            AddTubeBetween(suspTr.transform, "Track_TensionerPiston", new Vector3(-0.08f, 0.05f, 0.12f), new Vector3(-0.08f, 0.06f, 0.46f), 0.022f, _matSilverMachined);

            // Kesintisiz Oval Palet Şeridi (TrackBelt_Mesh):
            // Arka Sprocket (Z=-0.46, Y=+0.06, R=0.20), Ön Idler (Z=+0.46, Y=+0.06, R=0.20),
            // Alt Taşıyıcı Makaralar (Z=-0.33..+0.33, Y=-0.19, R=0.15) -> Alt yüzey tam Y = -0.375m!
            BuildContinuousBeltLoopMesh(suspTr.transform, "TrackBelt_Mesh",
                width: 0.28f, beltThickness: 0.032f,
                sprocketCenterYZ: new Vector2(0.06f, -0.46f), sprocketR: 0.205f,
                idlerCenterYZ: new Vector2(0.06f, 0.46f), idlerR: 0.205f,
                bottomRunY: -0.345f, bottomFrontZ: 0.34f, bottomRearZ: -0.34f,
                _matTireRubber);

            // Hasarlı Palet (Varsayılan gizli)
            GameObject bustedTrack = AddChamferBox(suspTr.transform, "bustedTrack", new Vector3(0f, -0.15f, 0f), Vector3.zero,
                new Vector3(0.26f, 0.22f, 1.05f), 0.02f, _matDarkMetal);
            bustedTrack.SetActive(false);

            // Arka Tahrik Dişlisi (DriveSprocket: Y=+0.06, Z=-0.46, R=0.20)
            GameObject sprocket = new GameObject("DriveSprocket");
            sprocket.transform.SetParent(suspTr.transform, false);
            sprocket.transform.localPosition = new Vector3(0f, 0.06f, -0.46f);
            AddHollowWheelBand(sprocket.transform, "Sprocket_Rim", 0.195f, 0.135f, 0.24f, 20, _matGoldFoil);
            AddFrustum(sprocket.transform, "Sprocket_Hub", Vector3.zero, new Vector3(0f, 0f, 90f), 0.138f, 0.138f, 0.25f, 16, _matDarkMetal);

            // Ön Gergi Çarkı (IdlerWheel: Y=+0.06, Z=+0.46, R=0.20)
            GameObject idler = new GameObject("IdlerWheel");
            idler.transform.SetParent(suspTr.transform, false);
            idler.transform.localPosition = new Vector3(0f, 0.06f, 0.46f);
            AddHollowWheelBand(idler.transform, "Idler_Rim", 0.195f, 0.135f, 0.23f, 20, _matSilverMachined);
            AddFrustum(idler.transform, "Idler_Hub", Vector3.zero, new Vector3(0f, 0f, 90f), 0.138f, 0.138f, 0.24f, 16, _matDarkMetal);

            // 4 Adet Çift-Flanşlı Alt Taşıyıcı Makara (RoadWheel_01..04: Y=-0.195, R=0.148 -> alt kenarı -0.343m ile palet şeridine tam basar!)
            float[] rollerZ = { 0.33f, 0.11f, -0.11f, -0.33f };
            for (int i = 0; i < rollerZ.Length; i++)
            {
                GameObject rw = new GameObject($"RoadWheel_0{i + 1}");
                rw.transform.SetParent(suspTr.transform, false);
                rw.transform.localPosition = new Vector3(0f, -0.195f, rollerZ[i]);

                AddFrustum(rw.transform, "RW_OuterDisc", new Vector3(-0.085f, 0f, 0f), new Vector3(0f, 0f, 90f), 0.146f, 0.146f, 0.055f, 18, _matSilverMachined);
                AddFrustum(rw.transform, "RW_InnerDisc", new Vector3( 0.085f, 0f, 0f), new Vector3(0f, 0f, 90f), 0.146f, 0.146f, 0.055f, 18, _matSilverMachined);
                AddFrustum(rw.transform, "RW_CenterAxle", Vector3.zero, new Vector3(0f, 0f, 90f), 0.065f, 0.065f, 0.24f, 12, _matGoldFoil);
            }

            SaveAsPrefab(root);
            return root;
        }

        #endregion

        #region 4. Bilim ve Güç Ekipmanları (Payloads — Milimetrik Node Uyumlu)

        /// <summary>
        /// PR-D20 TurretDrill (node_stack_mount = 0, 0, -0.15)
        /// </summary>
        private static GameObject BuildRoboticDrillPart()
        {
            GameObject root = CreatePartRoot("DrillRoboticArm", "ProceduralRovers/Parts/Payload/Assets");

            // 1. Montaj Flanşı (Arka yüzeyi tam Z = -0.15m!)
            GameObject baseGo = AddFrustum(root.transform, "MountBase_Mesh", new Vector3(0f, 0f, -0.115f), new Vector3(90f, 0f, 0f),
                0.135f, 0.120f, 0.070f, 18, _matDarkMetal);
            baseGo.AddComponent<BoxCollider>().size = new Vector3(0.26f, 0.26f, 0.14f);

            // 2. Yatay Taret (ArmBase_Yaw: Z = -0.08m)
            GameObject yawNode = new GameObject("ArmBase_Yaw");
            yawNode.transform.SetParent(root.transform, false);
            yawNode.transform.localPosition = new Vector3(0f, 0f, -0.06f);
            AddFrustum(yawNode.transform, "TurretRing_Mesh", Vector3.zero, new Vector3(90f, 0f, 0f), 0.115f, 0.105f, 0.045f, 16, _matGoldFoil);
            AddChamferBox(yawNode.transform, "ShoulderClevis_L", new Vector3(-0.065f, 0f, 0.045f), Vector3.zero, new Vector3(0.025f, 0.13f, 0.08f), 0.005f, _matDarkMetal);
            AddChamferBox(yawNode.transform, "ShoulderClevis_R", new Vector3( 0.065f, 0f, 0.045f), Vector3.zero, new Vector3(0.025f, 0.13f, 0.08f), 0.005f, _matDarkMetal);

            // 3. Omuz Kolu (ArmShoulder_Pitch)
            GameObject pitchNode = new GameObject("ArmShoulder_Pitch");
            pitchNode.transform.SetParent(yawNode.transform, false);
            pitchNode.transform.localPosition = new Vector3(0f, 0f, 0.06f);

            AddFrustum(pitchNode.transform, "ShoulderPin", Vector3.zero, new Vector3(0f, 0f, 90f), 0.045f, 0.045f, 0.16f, 14, _matSilverMachined);
            Vector3 elbowPos = new Vector3(0f, 0.12f, 0.34f);
            AddTubeBetween(pitchNode.transform, "UpperBoom_L", new Vector3(-0.04f, 0f, 0f), elbowPos + new Vector3(-0.04f, 0f, 0f), 0.022f, _matStockWhite);
            AddTubeBetween(pitchNode.transform, "UpperBoom_R", new Vector3( 0.04f, 0f, 0f), elbowPos + new Vector3( 0.04f, 0f, 0f), 0.022f, _matStockWhite);
            AddTubeBetween(pitchNode.transform, "HydraulicActuator", new Vector3(0f, -0.055f, 0.02f), new Vector3(0f, 0.055f, 0.24f), 0.020f, _matGoldFoil);

            // 4. Ön Kol & Sondaj Kızağı (ArmForearm_Hinge)
            GameObject forearmHinge = new GameObject("ArmForearm_Hinge");
            forearmHinge.transform.SetParent(pitchNode.transform, false);
            forearmHinge.transform.localPosition = elbowPos;

            AddFrustum(forearmHinge.transform, "ElbowJoint", Vector3.zero, new Vector3(0f, 0f, 90f), 0.042f, 0.042f, 0.13f, 14, _matGoldFoil);
            AddChamferBox(forearmHinge.transform, "DrillMotorHousing", new Vector3(0f, 0f, 0.15f), Vector3.zero,
                new Vector3(0.14f, 0.14f, 0.24f), 0.020f, _matDarkMetal);
            // Çift Zemin Sabitleme Kızak Çubukları
            AddTubeBetween(forearmHinge.transform, "GuideRod_L", new Vector3(-0.06f, -0.04f, 0.06f), new Vector3(-0.06f, -0.04f, 0.50f), 0.010f, _matSilverMachined);
            AddTubeBetween(forearmHinge.transform, "GuideRod_R", new Vector3( 0.06f, -0.04f, 0.06f), new Vector3( 0.06f, -0.04f, 0.50f), 0.010f, _matSilverMachined);

            // Dönen Helezonik Matkap Ucu (DrillBit_Mesh)
            GameObject bitRoot = new GameObject("DrillBit_Mesh");
            bitRoot.transform.SetParent(forearmHinge.transform, false);
            bitRoot.transform.localPosition = new Vector3(0f, 0f, 0.27f);
            AddFrustum(bitRoot.transform, "BitChuck", new Vector3(0f, 0f, 0.02f), new Vector3(90f, 0f, 0f), 0.042f, 0.030f, 0.04f, 14, _matSilverMachined);
            AddFrustum(bitRoot.transform, "BitShaft", new Vector3(0f, 0f, 0.16f), new Vector3(90f, 0f, 0f), 0.024f, 0.008f, 0.26f, 12, _matGoldFoil);
            for (int f = 0; f < 5; f++)
            {
                float zOff = 0.06f + f * 0.042f;
                float rFlute = Mathf.Lerp(0.034f, 0.018f, f / 5f);
                AddFrustum(bitRoot.transform, $"AugerFlute_{f}", new Vector3(0f, 0f, zOff), new Vector3(78f, f * 36f, 0f), rFlute, rFlute * 0.8f, 0.012f, 12, _matDarkMetal);
            }

            GameObject impactTr = new GameObject("ImpactTransform");
            impactTr.transform.SetParent(forearmHinge.transform, false);
            impactTr.transform.localPosition = new Vector3(0f, 0f, 0.60f);

            SaveAsPrefab(root);
            return root;
        }

        /// <summary>
        /// PR-M10 SuperCam Panoramik MastCam (node_stack_mount = 0, -0.25, 0)
        /// </summary>
        private static GameObject BuildMastCamPart()
        {
            GameObject root = CreatePartRoot("MastCamPanoramic", "ProceduralRovers/Parts/Payload/Assets");

            // Alt Montaj Flanşı (Alt yüzey tam Y = -0.25m!) + Kademeli Direk (Y: -0.25 .. +0.30)
            AddFrustum(root.transform, "MastBaseFlange", new Vector3(0f, -0.235f, 0f), Vector3.zero, 0.095f, 0.075f, 0.030f, 16, _matDarkMetal);
            GameObject poleGo = AddFrustum(root.transform, "MastPole_Lower", new Vector3(0f, -0.08f, 0f), Vector3.zero, 0.056f, 0.048f, 0.28f, 16, _matStockWhite);
            poleGo.AddComponent<BoxCollider>().size = new Vector3(0.14f, 0.54f, 0.14f);
            AddFrustum(root.transform, "MastPole_Upper", new Vector3(0f, 0.17f, 0f), Vector3.zero, 0.046f, 0.042f, 0.24f, 16, _matStockWhite);
            AddFrustum(root.transform, "MastCollarRing", new Vector3(0f, 0.055f, 0f), Vector3.zero, 0.058f, 0.058f, 0.025f, 16, _matGoldFoil);

            // REMS Rüzgar Sensörü Kolları (Direğin dış yüzeyinden başlar, arkadan taşmaz!)
            AddTubeBetween(root.transform, "WindSensor_1", new Vector3(0.045f, 0.12f, 0f), new Vector3(0.16f, 0.12f, 0.04f), 0.011f, _matDarkMetal);
            AddTubeBetween(root.transform, "WindSensor_2", new Vector3(0f, 0.08f, 0.045f), new Vector3(0.03f, 0.08f, 0.15f), 0.011f, _matDarkMetal);

            // 360° Kule Boynu (MastHead_Yaw: Y = +0.305m)
            GameObject yawNode = new GameObject("MastHead_Yaw");
            yawNode.transform.SetParent(root.transform, false);
            yawNode.transform.localPosition = new Vector3(0f, 0.305f, 0f);
            AddFrustum(yawNode.transform, "AzimuthServo", new Vector3(0f, 0.02f, 0f), Vector3.zero, 0.052f, 0.052f, 0.045f, 16, _matDarkMetal);

            // Dikey Eğim Kamera Kafası (MastOptics_Pitch: Y = +0.08m -> Curiosity/Perseverance SuperCam kutusu)
            GameObject pitchNode = new GameObject("MastOptics_Pitch");
            pitchNode.transform.SetParent(yawNode.transform, false);
            pitchNode.transform.localPosition = new Vector3(0f, 0.095f, 0f);

            // Ana Beyaz Optik Muhafaza + Üst ChemCam Kulesi
            AddChamferBox(pitchNode.transform, "CameraHead_MainBox", new Vector3(0f, -0.02f, 0f), Vector3.zero,
                new Vector3(0.28f, 0.11f, 0.16f), 0.016f, _matStockWhite);
            AddChamferBox(pitchNode.transform, "CameraHead_SunHood", new Vector3(0f, 0.032f, 0.082f), Vector3.zero,
                new Vector3(0.27f, 0.014f, 0.03f), 0.003f, _matDarkMetal);
            AddChamferBox(pitchNode.transform, "ChemCam_UpperHousing", new Vector3(0.015f, 0.075f, 0.005f), Vector3.zero,
                new Vector3(0.15f, 0.095f, 0.15f), 0.015f, _matStockWhite);

            // Gömülü Optik Mercekler (ChemCam Büyük Yakut Lazer + Sol Geniş Açı + Sağ Telefoto + 2x NavCam)
            AddLensAssembly(pitchNode.transform, "ChemCam_MainLaser", new Vector3(0.015f, 0.075f, 0.080f), Vector3.zero, 0.038f, 0.022f, _matLaserRuby);
            AddLensAssembly(pitchNode.transform, "MastCam_TelephotoR", new Vector3( 0.075f, -0.020f, 0.080f), Vector3.zero, 0.030f, 0.018f, _matOpticsDark);
            AddLensAssembly(pitchNode.transform, "MastCam_WideAngleL", new Vector3(-0.055f, -0.020f, 0.080f), Vector3.zero, 0.022f, 0.018f, _matOpticsDark);
            AddLensAssembly(pitchNode.transform, "NavCam_L", new Vector3(-0.105f, -0.025f, 0.080f), Vector3.zero, 0.014f, 0.014f, _matOpticsDark);
            AddLensAssembly(pitchNode.transform, "NavCam_R", new Vector3( 0.115f, -0.025f, 0.080f), Vector3.zero, 0.014f, 0.014f, _matOpticsDark);

            SaveAsPrefab(root);
            return root;
        }

        /// <summary>
        /// PR-G100 MMRTG Nükleer Güç Ünitesi (node_stack_mount = 0, 0, -0.22)
        /// </summary>
        private static GameObject BuildMmrtgPart()
        {
            GameObject root = CreatePartRoot("RoverMMRTG", "ProceduralRovers/Parts/Payload/Assets");

            // Arka Montaj Adaptörü (Arka yüzey tam Z = -0.22m!)
            AddFrustum(root.transform, "MMRTG_BaseRing", new Vector3(0f, 0f, -0.200f), new Vector3(90f, 0f, 0f),
                0.155f, 0.135f, 0.040f, 18, _matDarkMetal);

            // Ana Beyaz Pu-238 Muhafaza Silindiri (Z: -0.18 .. +0.28)
            GameObject coreGo = AddFrustum(root.transform, "MMRTG_CoreCylinder", new Vector3(0f, 0f, 0.05f), new Vector3(90f, 0f, 0f),
                0.125f, 0.120f, 0.46f, 20, _matStockWhite);
            coreGo.AddComponent<BoxCollider>().size = new Vector3(0.46f, 0.46f, 0.52f);

            // Altın Kapton Uç Kubbe ve Soğutma Halkaları
            AddFrustum(root.transform, "MMRTG_EndCap", new Vector3(0f, 0f, 0.295f), new Vector3(90f, 0f, 0f),
                0.118f, 0.075f, 0.035f, 18, _matGoldFoil);
            AddFrustum(root.transform, "MMRTG_MidCollar", new Vector3(0f, 0f, 0.05f), new Vector3(90f, 0f, 0f),
                0.132f, 0.132f, 0.030f, 18, _matDarkMetal);

            // 8 Radyal Radyatör Kanatçığı (Her biri ince, pahlı ve dış kenarında koyu termal şeritli)
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f;
                Quaternion rot = Quaternion.Euler(0f, 0f, ang);
                Vector3 finCenter = rot * new Vector3(0.175f, 0f, 0.05f);
                Vector3 tipCenter = rot * new Vector3(0.238f, 0f, 0.05f);

                AddChamferBox(root.transform, $"MMRTG_Fin_{i}", finCenter, new Vector3(0f, 0f, ang),
                    new Vector3(0.13f, 0.014f, 0.42f), 0.003f, _matStockWhite);
                AddChamferBox(root.transform, $"MMRTG_FinTip_{i}", tipCenter, new Vector3(0f, 0f, ang),
                    new Vector3(0.015f, 0.018f, 0.43f), 0.003f, _matDarkMetal);
            }

            SaveAsPrefab(root);
            return root;
        }

        /// <summary>
        /// PR-S40 Lunokhod-Clamshell Güverte Güneş Paneli (node_stack_mount = 0, -0.04, 0)
        /// </summary>
        private static GameObject BuildSolarDeckPart()
        {
            GameObject root = CreatePartRoot("RoverSolarDeck", "ProceduralRovers/Parts/Payload/Assets");

            // Alt Menteşe Kirişi (Tam Y = -0.04m hizasında)
            AddChamferBox(root.transform, "SolarMountPedestal", new Vector3(0f, -0.025f, 0f), Vector3.zero,
                new Vector3(0.36f, 0.030f, 0.36f), 0.006f, _matDarkMetal);

            GameObject pivot = new GameObject("solarPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = Vector3.zero;

            // Pahlı Alüminyum Ana Güverte Çerçevesi + 2 Parçalı Fotovoltaik Hücre Dizisi + Altın Arka Kaplama
            GameObject frameGo = AddChamferBox(pivot.transform, "SolarFrame_Mesh", Vector3.zero, Vector3.zero,
                new Vector3(1.10f, 0.032f, 1.28f), 0.012f, _matSilverMachined);
            frameGo.AddComponent<BoxCollider>().size = new Vector3(1.10f, 0.04f, 1.28f);

            AddChamferBox(pivot.transform, "SolarArray_Left", new Vector3(-0.265f, 0.017f, 0f), Vector3.zero,
                new Vector3(0.49f, 0.006f, 1.18f), 0.002f, _matSolarCells);
            AddChamferBox(pivot.transform, "SolarArray_Right", new Vector3( 0.265f, 0.017f, 0f), Vector3.zero,
                new Vector3(0.49f, 0.006f, 1.18f), 0.002f, _matSolarCells);
            AddChamferBox(pivot.transform, "SolarCenterSpine", new Vector3(0f, 0.017f, 0f), Vector3.zero,
                new Vector3(0.03f, 0.008f, 1.22f), 0.002f, _matDarkMetal);
            AddChamferBox(pivot.transform, "SolarGoldBack", new Vector3(0f, -0.017f, 0f), Vector3.zero,
                new Vector3(1.02f, 0.005f, 1.20f), 0.002f, _matGoldFoil);

            // suncatcher (+Z ekseni tam gökyüzüne bakar!)
            GameObject suncatcher = new GameObject("suncatcher");
            suncatcher.transform.SetParent(pivot.transform, false);
            suncatcher.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            suncatcher.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            SaveAsPrefab(root);
            return root;
        }

        /// <summary>
        /// PR-X30 APXS-Vault Spektrometre ve Numune Haznesi (node_stack_mount = 0, -0.10, 0)
        /// </summary>
        private static GameObject BuildSpectrometerPart()
        {
            GameObject root = CreatePartRoot("SampleSpectrometer", "ProceduralRovers/Parts/Payload/Assets");

            // Hermetik Numune Karusel Gövdesi (Alt yüzey Y = -0.10m, Üst yüzey Y = +0.09m)
            AddFrustum(root.transform, "VaultBaseFlange", new Vector3(0f, -0.088f, 0f), Vector3.zero, 0.195f, 0.185f, 0.024f, 20, _matDarkMetal);
            GameObject vaultGo = AddFrustum(root.transform, "VaultMainBody", new Vector3(0f, 0.00f, 0f), Vector3.zero, 0.185f, 0.178f, 0.155f, 20, _matStockWhite);
            vaultGo.AddComponent<BoxCollider>().size = new Vector3(0.38f, 0.21f, 0.38f);
            AddFrustum(root.transform, "VaultTopLid", new Vector3(0f, 0.085f, 0f), Vector3.zero, 0.178f, 0.165f, 0.018f, 20, _matDarkMetal);

            // 8 Adet Altın Karot Numune Tüpü Kapağı
            for (int i = 0; i < 8; i++)
            {
                float rad = (i * 45f) * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(Mathf.Cos(rad) * 0.115f, 0.098f, Mathf.Sin(rad) * 0.115f);
                AddFrustum(root.transform, $"SampleTubeCap_{i}", pos, Vector3.zero, 0.022f, 0.019f, 0.016f, 12, _matGoldFoil);
            }

            // APXS Sensör Kolu (SensorArm_Yaw -> SensorHead_Pitch)
            GameObject yawNode = new GameObject("SensorArm_Yaw");
            yawNode.transform.SetParent(root.transform, false);
            yawNode.transform.localPosition = new Vector3(0f, 0.095f, 0f);
            AddFrustum(yawNode.transform, "SensorTurretHub", new Vector3(0f, 0.018f, 0f), Vector3.zero, 0.055f, 0.048f, 0.036f, 16, _matGoldFoil);

            GameObject pitchNode = new GameObject("SensorHead_Pitch");
            pitchNode.transform.SetParent(yawNode.transform, false);
            pitchNode.transform.localPosition = new Vector3(0f, 0.035f, 0f);

            AddTubeBetween(pitchNode.transform, "APXS_Boom", Vector3.zero, new Vector3(0f, 0.03f, 0.13f), 0.022f, _matSilverMachined);
            AddFrustum(pitchNode.transform, "APXS_DetectorCan", new Vector3(0f, 0.03f, 0.19f), new Vector3(90f, 0f, 0f),
                0.062f, 0.058f, 0.11f, 18, _matGoldFoil);
            AddLensAssembly(pitchNode.transform, "APXS_XRayEmitter", new Vector3(0f, 0.03f, 0.245f), Vector3.zero, 0.044f, 0.020f, _matCyanEmissive);

            SaveAsPrefab(root);
            return root;
        }

        #endregion

        #region Hassas Prosedürel Geometri Üreticileri (ChamferBox, Frustum, HollowWheelBand, ContinuousBelt, TubeBetween)

        /// <summary>
        /// Keskin 90° küpler yerine tüm kenarları 45° pahlı (beveled/chamfered), normalleri %100 dışa bakan
        /// ve kutu-projeksiyon (box-projected) temiz UV'lere sahip havacılık gövde kutusu üretir.
        /// </summary>
        private static GameObject AddChamferBox(
            Transform parent, string name, Vector3 localPos, Vector3 localEuler,
            Vector3 fullSize, float chamfer, Material mat, int layer = LAYER_DEFAULT)
        {
            float hx = Mathf.Max(0.002f, fullSize.x * 0.5f);
            float hy = Mathf.Max(0.002f, fullSize.y * 0.5f);
            float hz = Mathf.Max(0.002f, fullSize.z * 0.5f);
            float c = Mathf.Min(chamfer, Mathf.Min(hx, Mathf.Min(hy, hz)) * 0.45f);

            float ix = hx - c, iy = hy - c, iz = hz - c;

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            // 6 Ana Yüzey + 12 Pah Kenarı + 8 Köşe Üçgeni için tepe noktası ızgarası (3D Super-Cube projeksiyonu)
            // 4x4 halka kesit ile pürüzsüz pahlı prizma (yüzey normalleri %100 dışa dönük)
            float[] yLevels = { -hy, -iy, iy, hy };
            float[] yExpand = { 0f, 1f, 1f, 0f };

            Vector2[] ringDirs =
            {
                new Vector2(-ix,  hz), new Vector2( ix,  hz), // Front (+Z)
                new Vector2( hx,  iz), new Vector2( hx, -iz), // Right (+X)
                new Vector2( ix, -hz), new Vector2(-ix, -hz), // Back (-Z)
                new Vector2(-hx, -iz), new Vector2(-hx,  iz)  // Left (-X)
            };
            Vector2[] ringInset =
            {
                new Vector2(-ix,  iz), new Vector2( ix,  iz),
                new Vector2( ix,  iz), new Vector2( ix, -iz),
                new Vector2( ix, -iz), new Vector2(-ix, -iz),
                new Vector2(-ix, -iz), new Vector2(-ix,  iz)
            };

            // Yan ve Pah Halkaları
            int ringCount = ringDirs.Length;
            for (int yl = 0; yl < 3; yl++)
            {
                float y0 = yLevels[yl], y1 = yLevels[yl + 1];
                float e0 = yExpand[yl], e1 = yExpand[yl + 1];

                for (int r = 0; r < ringCount; r++)
                {
                    int rn = (r + 1) % ringCount;
                    Vector2 p00 = Vector2.Lerp(ringInset[r], ringDirs[r], e0);
                    Vector2 p10 = Vector2.Lerp(ringInset[rn], ringDirs[rn], e0);
                    Vector2 p11 = Vector2.Lerp(ringInset[rn], ringDirs[rn], e1);
                    Vector2 p01 = Vector2.Lerp(ringInset[r], ringDirs[r], e1);

                    // Dışa bakan yüzey normali: (p10 - p00) x (p11 - p00) -> Dışarı doğru (+Z / +X / -Z / -X)!
                    AddQuadFacet(verts, norms, uvs, tris,
                        new Vector3(p00.x, y0, p00.y),
                        new Vector3(p10.x, y0, p10.y),
                        new Vector3(p11.x, y1, p11.y),
                        new Vector3(p01.x, y1, p01.y));
                }
            }

            // Üst Kapak (+Y) ve Alt Kapak (-Y)
            AddQuadFacet(verts, norms, uvs, tris,
                new Vector3(-ix, hy, -iz), new Vector3(-ix, hy,  iz), new Vector3( ix, hy,  iz), new Vector3( ix, hy, -iz));
            AddQuadFacet(verts, norms, uvs, tris,
                new Vector3(-ix, -hy,  iz), new Vector3(-ix, -hy, -iz), new Vector3( ix, -hy, -iz), new Vector3( ix, -hy,  iz));

            return FinalizeGeneratedMeshObject(parent, name, localPos, Quaternion.Euler(localEuler), verts, norms, uvs, tris, mat, layer);
        }

        /// <summary>
        /// Tam uzunluğu 'length' (metre) olan konik/silindirik gövde (radiusBottom == radiusTop ise tam silindir).
        /// Merkez noktası tam ortadadır [-length/2 .. +length/2].
        /// </summary>
        private static GameObject AddFrustum(
            Transform parent, string name, Vector3 localPos, Vector3 localEuler,
            float radiusBottom, float radiusTop, float length, int segments, Material mat, int layer = LAYER_DEFAULT)
        {
            float halfL = length * 0.5f;
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            float slopeY = (radiusBottom - radiusTop) / Mathf.Max(1e-4f, length);

            // Yan Yüzey
            for (int i = 0; i <= segments; i++)
            {
                float u = (float)i / segments;
                float ang = u * Mathf.PI * 2f;
                float cos = Mathf.Cos(ang);
                float sin = Mathf.Sin(ang);

                Vector3 n = new Vector3(cos, slopeY, sin).normalized;
                verts.Add(new Vector3(cos * radiusBottom, -halfL, sin * radiusBottom));
                norms.Add(n);
                uvs.Add(new Vector2(u * 2f, 0f));

                verts.Add(new Vector3(cos * radiusTop, halfL, sin * radiusTop));
                norms.Add(n);
                uvs.Add(new Vector2(u * 2f, 1f));
            }

            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                // Dışa bakan normal: (b+1 - b+0) x (b+3 - b+0) = (0, L, 0) x (-sin, L, cos) -> (+cos, 0, +sin) DIŞARI!
                tris.Add(b + 0); tris.Add(b + 1); tris.Add(b + 3);
                tris.Add(b + 0); tris.Add(b + 3); tris.Add(b + 2);
            }

            // Üst (+Y) ve Alt (-Y) Daire Kapakları
            if (radiusTop > 1e-4f)
            {
                int cIdx = verts.Count;
                verts.Add(new Vector3(0f, halfL, 0f)); norms.Add(Vector3.up); uvs.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= segments; i++)
                {
                    float ang = ((float)i / segments) * Mathf.PI * 2f;
                    float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);
                    verts.Add(new Vector3(cos * radiusTop, halfL, sin * radiusTop));
                    norms.Add(Vector3.up);
                    uvs.Add(new Vector2(cos * 0.45f + 0.5f, sin * 0.45f + 0.5f));
                }
                for (int i = 0; i < segments; i++)
                {
                    tris.Add(cIdx); tris.Add(cIdx + i + 2); tris.Add(cIdx + i + 1);
                }
            }

            if (radiusBottom > 1e-4f)
            {
                int cIdx = verts.Count;
                verts.Add(new Vector3(0f, -halfL, 0f)); norms.Add(Vector3.down); uvs.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= segments; i++)
                {
                    float ang = ((float)i / segments) * Mathf.PI * 2f;
                    float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);
                    verts.Add(new Vector3(cos * radiusBottom, -halfL, sin * radiusBottom));
                    norms.Add(Vector3.down);
                    uvs.Add(new Vector2(cos * 0.45f + 0.5f, sin * 0.45f + 0.5f));
                }
                for (int i = 0; i < segments; i++)
                {
                    tris.Add(cIdx); tris.Add(cIdx + i + 1); tris.Add(cIdx + i + 2);
                }
            }

            return FinalizeGeneratedMeshObject(parent, name, localPos, Quaternion.Euler(localEuler), verts, norms, uvs, tris, mat, layer);
        }

        /// <summary>
        /// +X aksı (Sol-Sağ tekerlek dönme ekseni) etrafında içi boş silindirik lastik/jant çemberi (Torus/Hollow Cylinder) üretir.
        /// Transform.localRotation = (0,0,0) kalır!
        /// </summary>
        private static GameObject AddHollowWheelBand(
            Transform parent, string name, float outerRadius, float innerRadius, float width, int segments, Material mat)
        {
            float hx = width * 0.5f;
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            for (int i = 0; i < segments; i++)
            {
                float t0 = (float)i / segments;
                float t1 = (float)(i + 1) / segments;
                float a0 = t0 * Mathf.PI * 2f;
                float a1 = t1 * Mathf.PI * 2f;

                float y0 = Mathf.Cos(a0), z0 = Mathf.Sin(a0);
                float y1 = Mathf.Cos(a1), z1 = Mathf.Sin(a1);

                Vector3 oL0 = new Vector3(-hx, y0 * outerRadius, z0 * outerRadius);
                Vector3 oR0 = new Vector3( hx, y0 * outerRadius, z0 * outerRadius);
                Vector3 oR1 = new Vector3( hx, y1 * outerRadius, z1 * outerRadius);
                Vector3 oL1 = new Vector3(-hx, y1 * outerRadius, z1 * outerRadius);

                Vector3 iL0 = new Vector3(-hx, y0 * innerRadius, z0 * innerRadius);
                Vector3 iR0 = new Vector3( hx, y0 * innerRadius, z0 * innerRadius);
                Vector3 iR1 = new Vector3( hx, y1 * innerRadius, z1 * innerRadius);
                Vector3 iL1 = new Vector3(-hx, y1 * innerRadius, z1 * innerRadius);

                // Dış Sırt (Outer Tread - V ekseni çevre boyunca uzanır)
                AddQuadWithCustomUV(verts, norms, uvs, tris, oL0, oL1, oR1, oR0,
                    new Vector2(0f, t0 * 4f), new Vector2(0f, t1 * 4f), new Vector2(1f, t1 * 4f), new Vector2(1f, t0 * 4f));

                // İç Çember (Inner Rim)
                AddQuadWithCustomUV(verts, norms, uvs, tris, iR0, iR1, iL1, iL0,
                    new Vector2(0f, t0 * 2f), new Vector2(0f, t1 * 2f), new Vector2(1f, t1 * 2f), new Vector2(1f, t0 * 2f));

                // Sol Yan Duvar (-X) & Sağ Yan Duvar (+X)
                AddQuadFacet(verts, norms, uvs, tris, iL0, iL1, oL1, oL0);
                AddQuadFacet(verts, norms, uvs, tris, oR0, oR1, iR1, iR0);
            }

            return FinalizeGeneratedMeshObject(parent, name, Vector3.zero, Quaternion.identity, verts, norms, uvs, tris, mat, LAYER_DEFAULT);
        }

        /// <summary>
        /// Arka Sprocket, Ön Idler ve Alt Makaraların etrafını kesintisiz saran Stadyum/Trapez Palet Şeridi üretir.
        /// UV Y koordinatı şerit boyunca kesintisiz ilerlediği için ProceduralContinuousTrack doku kaydırması gerçek palet gibi akar!
        /// </summary>
        private static GameObject BuildContinuousBeltLoopMesh(
            Transform parent, string name, float width, float beltThickness,
            Vector2 sprocketCenterYZ, float sprocketR, Vector2 idlerCenterYZ, float idlerR,
            float bottomRunY, float bottomFrontZ, float bottomRearZ, Material mat)
        {
            // Y-Z düzleminde palet iç profil noktalarını saat yönünde oluştur
            List<Vector2> innerPts = new List<Vector2>();

            // 1. Ön Idler yayı (Üstten alta, +Z etrafında)
            const int arcSegs = 12;
            for (int i = 0; i <= arcSegs; i++)
            {
                float ang = Mathf.Lerp(Mathf.PI * 0.5f, -Mathf.PI * 0.35f, (float)i / arcSegs);
                innerPts.Add(idlerCenterYZ + new Vector2(Mathf.Sin(ang) * idlerR, Mathf.Cos(ang) * idlerR));
            }

            // 2. Alt Düz Hat (Önden arkaya)
            innerPts.Add(new Vector2(bottomRunY + beltThickness, bottomFrontZ));
            innerPts.Add(new Vector2(bottomRunY + beltThickness, bottomRearZ));

            // 3. Arka Sprocket yayı (Alttan üste, -Z etrafında)
            for (int i = 0; i <= arcSegs; i++)
            {
                float ang = Mathf.Lerp(-Mathf.PI * 0.65f, Mathf.PI * 0.5f, (float)i / arcSegs);
                innerPts.Add(sprocketCenterYZ + new Vector2(Mathf.Sin(ang) * sprocketR, -Mathf.Cos(ang) * sprocketR));
            }

            int n = innerPts.Count;
            Vector2[] outerPts = new Vector2[n];
            float[] cumDist = new float[n + 1];
            float totalLen = 0f;

            for (int i = 0; i < n; i++)
            {
                Vector2 pPrev = innerPts[(i - 1 + n) % n];
                Vector2 pCurr = innerPts[i];
                Vector2 pNext = innerPts[(i + 1) % n];

                Vector2 tangent = (pNext - pPrev).normalized;
                // Y-Z düzleminde dışa bakan normal: (tangent.y=dZ, -tangent.x=-dY) kontrolü
                // Üst hatta pNext.y(Z) artıyor (+Z), dış yön +Y olmalı -> (tangent.y, -tangent.x) = (+1, 0) -> Doğru!
                Vector2 outwardYZ = new Vector2(tangent.y, -tangent.x).normalized;
                outerPts[i] = pCurr + outwardYZ * beltThickness;

                cumDist[i] = totalLen;
                totalLen += Vector2.Distance(pCurr, pNext);
            }
            cumDist[n] = totalLen;

            float hx = width * 0.5f;
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            float vRepeat = Mathf.Max(1f, Mathf.Round(totalLen * 2.5f)); // Tam sayı tekrar ile dikişsiz (seamless) UV

            for (int i = 0; i < n; i++)
            {
                int inxt = (i + 1) % n;
                float v0 = (cumDist[i] / totalLen) * vRepeat;
                float v1 = (cumDist[i + 1] / totalLen) * vRepeat;

                Vector3 oL0 = new Vector3(-hx, outerPts[i].x, outerPts[i].y);
                Vector3 oR0 = new Vector3( hx, outerPts[i].x, outerPts[i].y);
                Vector3 oR1 = new Vector3( hx, outerPts[inxt].x, outerPts[inxt].y);
                Vector3 oL1 = new Vector3(-hx, outerPts[inxt].x, outerPts[inxt].y);

                Vector3 iL0 = new Vector3(-hx, innerPts[i].x, innerPts[i].y);
                Vector3 iR0 = new Vector3( hx, innerPts[i].x, innerPts[i].y);
                Vector3 iR1 = new Vector3( hx, innerPts[inxt].x, innerPts[inxt].y);
                Vector3 iL1 = new Vector3(-hx, innerPts[inxt].x, innerPts[inxt].y);

                // Dış Palet Sırtı
                AddQuadWithCustomUV(verts, norms, uvs, tris, oL0, oL1, oR1, oR0,
                    new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(1f, v1), new Vector2(1f, v0));
                // İç Palet Yüzeyi
                AddQuadWithCustomUV(verts, norms, uvs, tris, iR0, iR1, iL1, iL0,
                    new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(1f, v1), new Vector2(1f, v0));
                // Sol & Sağ Kenarlar
                AddQuadWithCustomUV(verts, norms, uvs, tris, iL0, iL1, oL1, oL0,
                    new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(0.15f, v1), new Vector2(0.15f, v0));
                AddQuadWithCustomUV(verts, norms, uvs, tris, oR0, oR1, iR1, iR0,
                    new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(0.15f, v1), new Vector2(0.15f, v0));
            }

            return FinalizeGeneratedMeshObject(parent, name, Vector3.zero, Quaternion.identity, verts, norms, uvs, tris, mat, LAYER_DEFAULT);
        }

        /// <summary>
        /// pStart ve pEnd noktaları arasına milimetrik oturan silindirik bağlantı borusu üretir.
        /// </summary>
        private static GameObject AddTubeBetween(Transform parent, string name, Vector3 pStart, Vector3 pEnd, float radius, Material mat)
        {
            Vector3 delta = pEnd - pStart;
            float len = delta.magnitude;
            if (len < 1e-4f)
                return null;

            Vector3 mid = (pStart + pEnd) * 0.5f;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            return AddFrustum(parent, name, mid, rot.eulerAngles, radius, radius, len, 12, mat, LAYER_DEFAULT);
        }

        /// <summary>
        /// +Z yönüne bakan metal çerçeveli + gömülü optik camlı kamera/lazer lens modülü üretir.
        /// </summary>
        private static void AddLensAssembly(Transform parent, string name, Vector3 pos, Vector3 euler, float outerRadius, float depth, Material glassMat)
        {
            Quaternion rot = Quaternion.Euler(euler) * Quaternion.Euler(90f, 0f, 0f);
            AddFrustum(parent, name + "_Bezel", pos, rot.eulerAngles, outerRadius, outerRadius * 0.92f, depth, 16, _matDarkMetal);
            Vector3 glassOffset = Quaternion.Euler(euler) * new Vector3(0f, 0f, depth * 0.35f);
            AddFrustum(parent, name + "_Glass", pos + glassOffset, rot.eulerAngles, outerRadius * 0.76f, outerRadius * 0.76f, depth * 0.35f, 16, glassMat);
        }

        private static void AddQuadFacet(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            Vector3 n = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            int b = verts.Count;
            verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);

            // Düzlemsel Kutu-Projeksiyon UV (Oranlı ölçekli doku haritalaması)
            Vector3 absN = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = verts[b + i];
                if (absN.y >= absN.x && absN.y >= absN.z)
                    uvs.Add(new Vector2(p.x * 0.8f + 0.5f, p.z * 0.8f + 0.5f));
                else if (absN.x >= absN.y && absN.x >= absN.z)
                    uvs.Add(new Vector2(p.z * 0.8f + 0.5f, p.y * 0.8f + 0.5f));
                else
                    uvs.Add(new Vector2(p.x * 0.8f + 0.5f, p.y * 0.8f + 0.5f));
            }

            tris.Add(b + 0); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 3);
        }

        private static void AddQuadWithCustomUV(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
        {
            Vector3 n = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            int b = verts.Count;
            verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            uvs.Add(uv0); uvs.Add(uv1); uvs.Add(uv2); uvs.Add(uv3);

            tris.Add(b + 0); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 3);
        }

        private static GameObject FinalizeGeneratedMeshObject(
            Transform parent, string name, Vector3 localPos, Quaternion bakeRot,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris, Material mat, int layer)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = layer;

            if (bakeRot != Quaternion.identity)
            {
                for (int i = 0; i < verts.Count; i++)
                {
                    verts[i] = bakeRot * verts[i];
                    norms[i] = (bakeRot * norms[i]).normalized;
                }
            }

            string uniqueMeshName = $"PRMesh_{++_meshCounter}_{MakeSafeFileName(name)}";
            Mesh mesh = new Mesh { name = uniqueMeshName };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            SaveMeshAsset(mesh, uniqueMeshName);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            return go;
        }

        private static void BakeAdditionalScaleIntoMesh(GameObject go, Vector3 scale)
        {
            MeshFilter mf = go != null ? go.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return;

            Mesh m = mf.sharedMesh;
            Vector3[] v = m.vertices;
            Vector3[] n = m.normals;
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = Vector3.Scale(v[i], scale);
                n[i] = new Vector3(n[i].x / scale.x, n[i].y / scale.y, n[i].z / scale.z).normalized;
            }
            m.vertices = v;
            m.normals = n;
            m.RecalculateBounds();
            m.RecalculateTangents();
            EditorUtility.SetDirty(m);
        }

        #endregion

        #region PartTools ve Asset Yardımcıları

        private static GameObject CreatePartRoot(string modelName, string kspGameDataSubfolder)
        {
            GameObject root = new GameObject(modelName);
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            root.layer = LAYER_DEFAULT;

            KSPPartTools.PartTools pt = root.AddComponent<KSPPartTools.PartTools>();
            pt.ModelName = modelName;
            pt.FilePath = kspGameDataSubfolder;
            pt.WriteTextures = true;
            pt.TexFormat = KSPPartTools.PartTools.TextureFormat.PNG;
            EditorUtility.SetDirty(pt);

            return root;
        }

        private static void ConfigurePartToolsGameDataDir(string gameDataPath)
        {
            if (!Directory.Exists(gameDataPath))
                return;

            EditorPrefs.SetString("KSPGameData", gameDataPath);
            EditorPrefs.SetString("PartTools_GameData", gameDataPath);
        }

        private static void SaveMeshAsset(Mesh mesh, string meshName)
        {
            string safeName = MakeSafeFileName(meshName);
            string path = $"{MESH_OUTPUT_DIR}/{safeName}.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(mesh, path);
        }

        private static void SaveAsPrefab(GameObject root)
        {
            string path = $"{PREFAB_OUTPUT_DIR}/{root.name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }

        private static void PlaceInWorkspace(GameObject go, Transform workspaceRoot, Vector3 offset)
        {
            go.transform.SetParent(workspaceRoot, false);
            go.transform.localPosition = offset;
        }

        private static void SetLayerName(SerializedProperty layersProp, int index, string layerName)
        {
            SerializedProperty sp = layersProp.GetArrayElementAtIndex(index);
            if (sp != null)
            {
                sp.stringValue = layerName;
            }
        }

        private static void EnsureDirectories()
        {
            string[] dirs = { PREFAB_OUTPUT_DIR, MESH_OUTPUT_DIR, TEX_OUTPUT_DIR, MAT_OUTPUT_DIR };
            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        #endregion
    }
}
#endif
