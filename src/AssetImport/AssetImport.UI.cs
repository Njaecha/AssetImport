using System.IO;
using System.Linq;
using UnityEngine;
using KKAPI;
using KKAPI.Maker;
using Sirenix.Serialization.Utilities;
using Main = AssetImport.AssetImport;
// ReSharper disable UseObjectOrCollectionInitializer

namespace AssetImport
{
    /// <summary>
    /// Everything IMGUI
    /// </summary>
    internal class AssetUI : MonoBehaviour
    {
        // UI
        // main
        internal static bool UIActive;
        internal static Rect MainWindowRect = new Rect(500, 40, 240, 140);
        internal static string FilePath = "";
        internal static int ScaleSelection = 4;
        internal static readonly float[] Scales = { 10f, 5f, 2f, 1.5f, 1.0f, 0.5f, 0.1f, 0.01f, 0.001f, 0.0001f };
        internal static bool ImportBones;
        internal static bool PerRendererMaterials;
        internal static bool DoFbxTranslation;
        
        // preload
        internal static bool PreloadUI = false;
        internal static Rect PreloadWindowRect = new Rect(Screen.width / 2f - 250, Screen.height / 2f - 350, 500, 600);
        internal static Vector2 ScrollPosition = Vector2.zero;
        internal static bool ArmatureMode;
        private Texture2D _buttonBase;
        private Texture2D _buttonHover;
        internal Texture2D ButtonPressed;
        internal static string CommonPathText;
        internal static bool Replacer;
        internal static string ReplaceString;
        internal static string ReplaceWith = "";

        private const KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags SingleFileFlags = KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags.OFN_FILEMUSTEXIST | KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags.OFN_LONGNAMES | KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags.OFN_EXPLORER;


        void Awake()
        {
            // setup textures
            const float w = 84f / 255f;
            _buttonBase = ColorTexture(new Color(w, w, w));
            _buttonHover = ColorTexture(new Color(w + 0.2f, w + 0.2f, w + 0.2f));
            ButtonPressed = ColorTexture(new Color(w + 0.3f, w + +0.3f, w + 0.3f));
        }

        void Update()
        {
            if (Main.hotkey.Value.IsDown() && (KoikatuAPI.GetCurrentGameMode() == GameMode.Maker || KoikatuAPI.GetCurrentGameMode() == GameMode.Studio))
            {
                UIActive = !UIActive;
                return;
            }
            if (KoikatuAPI.GetCurrentGameMode() != GameMode.Studio && KoikatuAPI.GetCurrentGameMode() != GameMode.Maker)
                UIActive = false;
    
        }

        void OnGUI()
        {
            if (!(KoikatuAPI.GetCurrentGameMode() == GameMode.Maker || KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)) return;
            if (UIActive)
            {
                MainWindowRect = GUI.Window(33361, MainWindowRect, WindowFunction, "Asset Import v" + Main.Version);
                KKAPI.Utilities.IMGUIUtils.EatInputInRect(MainWindowRect);
            }
            if (PreloadUI)
            {
                PreloadWindowRect = GUI.Window(
                    33362,
                    PreloadWindowRect,
                    PreLoadWindowFunction,
                    Main.currentLoadProcess.Import.SourceFileName,
                    KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.window
                );
                KKAPI.Utilities.IMGUIUtils.EatInputInRect(PreloadWindowRect);
            }
        }
        private static Texture2D ColorTexture(Color color)
        {
            Texture2D txt = new Texture2D(1, 1);
            txt.SetPixel(1, 1, color);
            txt.Apply();
            return txt;
        }


        private void WindowFunction(int windowID)
        {
            var y = 0;
            FilePath = GUI.TextField(new Rect(10, y += 20, 195, 20), FilePath);
            FilePath = FilePath.Replace("\\", "/");
            FilePath = FilePath.Replace("\"", "");
            if (GUI.Button(new Rect(205, y, 25, 20), "..."))
            {
                string dir = (FilePath == "") ? Main.defaultDir.Value : FilePath.Replace(FilePath.Substring(FilePath.LastIndexOf("/")), "");
                string[] file = KKAPI.Utilities.OpenFileDialog.ShowDialog("Open 3D file", dir,
                        "3D files (*.fbx; *.dae; *.gltf;  *.blend; *.3ds; *.ase; *.obj; *.ifc; *.xgl; *.ply; *.dxf; *.lwo; *.lws; *.lxo; *.stl; *.x; *.ac; *.ms3d; *.smd) " +
                        "|*.fbx; *.dae; *.gltf; *.blend; *.3ds; *.ase; *.obj; *.ifc; *.xgl; *.ply; *.dxf; *.lwo; *.lws; *.lxo; *.stl; *.x; *.ac; *.ms3d; *.smd | All files (*.*)|*.*",
                        "obj", SingleFileFlags);
                if (file != null)
                {
                    FilePath = file[0];
                }
            }

            if (GUI.Button(new Rect(10, y += 25, 220, 25), ImportBones ? "☑ Import Armature" : "☐ Import Armature"))
            {
                ImportBones = !ImportBones;
            }

            if (GUI.Button(new Rect(10, y += 25, 220, 25), PerRendererMaterials ? "☑ Material per Renderer" : "☐ Material per Renderer"))
            {
                PerRendererMaterials = !PerRendererMaterials;
            }
            if (Path.GetExtension(FilePath).ToLower() == ".fbx")
            {
                if (GUI.Button(new Rect(10, y += 25, 220, 25), DoFbxTranslation ? "☑ Ignore Root translation" : "☐ Ignore Root Translation"))
                {
                    DoFbxTranslation = !DoFbxTranslation;
                }
            }

            GUI.Label(new Rect(10, y+=30, 160, 25), $"Scaling-factor: {Scales[ScaleSelection]}");
            if (ScaleSelection == 0) GUI.enabled = false;
            if (GUI.Button(new Rect(190, y, 20, 20), "+"))
            {
                ScaleSelection--;
            }
            //GUI.enabled = true;
            if (ScaleSelection == 9) GUI.enabled = false;
            if (GUI.Button(new Rect(210, y, 20, 20), "-"))
            {
                ScaleSelection++;
            }
            GUI.enabled = true;
            if (FilePath.IsNullOrEmpty()) GUI.enabled = false;
            if (GUI.Button(new Rect(10, y+=25, 220, 30), "Import"))
            {
                if (KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)
                    Main.asc.Import(FilePath, new Vector3(Scales[ScaleSelection], Scales[ScaleSelection], Scales[ScaleSelection]), ImportBones, PerRendererMaterials, DoFbxTranslation);
                else if (KoikatuAPI.GetCurrentGameMode() == GameMode.Maker)
                {
                    int slot = AccessoriesApi.SelectedMakerAccSlot;
                    if (slot != -1)
                    {
                        MakerAPI.GetCharacterControl()
                        .gameObject.GetComponentInChildren<AssetCharaController>()?
                        .Import(
                            slot,
                            MakerAPI.GetCharacterControl().nowCoordinate.accessory.parts[slot].type,
                            MakerAPI.GetCharacterControl().nowCoordinate.accessory.parts[slot].parentKey,
                            FilePath,
                            new Vector3(Scales[ScaleSelection], Scales[ScaleSelection], Scales[ScaleSelection]),
                            ImportBones,
                            PerRendererMaterials,
                            DoFbxTranslation);
                    }
                    else Main.Logger.LogMessage("Please select an accessory which you want to replace!");
                }
            }
            GUI.enabled = true;

            MainWindowRect.height = y + 40;

            GUI.DragWindow();
        }

        private void PreLoadWindowFunction(int windowID)
        {
            if (!PreloadUI) return;
            if (!(Main.currentLoadProcess.Import.HasBones || Main.currentLoadProcess.Import.HasTextures))
            {
                if (KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)
                    Main.asc?.FinishLoadProcess(Main.currentLoadProcess);
                else if (KoikatuAPI.GetCurrentGameMode() == GameMode.Maker)
                    MakerAPI.GetCharacterControl().gameObject.GetComponentInChildren<AssetCharaController>()?.FinishLoadProcess(Main.currentLoadProcess);
                return;
            }

            if (!(Main.currentLoadProcess.Import.HasBones && Main.currentLoadProcess.Import.HasTextures)) GUI.enabled = false;
            if (GUI.Button(new Rect(10, 20, 480, 25), ArmatureMode ? "Switch to Textures" : "Switch to Armature"))
            {
                ArmatureMode = !ArmatureMode;
            }
            GUI.enabled = true;

            if (ArmatureMode)
            {
                // custom button styles
                GUIStyle normal = new GUIStyle(KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.button);
                normal.normal.background = _buttonBase;
                normal.hover.background = _buttonHover;
                normal.active.background = ButtonPressed;
                normal.alignment = TextAnchor.UpperLeft;

                GUIStyle dynamicRoot = new GUIStyle(normal);
                dynamicRoot.normal.textColor = Color.green;
                dynamicRoot.hover.textColor = Color.green;

                GUIStyle dynamic = new GUIStyle(normal);
                dynamic.normal.textColor = new Color(0.7f, 1, 0);
                dynamic.hover.textColor = new Color(0.7f, 1, 0);

                GUIStyle arrow = new GUIStyle(normal);
                arrow.alignment = TextAnchor.MiddleCenter;
                arrow.hover.textColor = new Color(1, 1, 0);

                Rect scrollContentRect = new Rect(0, 0, 10, 30);
                Main.currentLoadProcess.Import.BoneNodes.Where(node => node.UIActive).ForEach(_ => scrollContentRect.height += 22);

                ScrollPosition = GUI.BeginScrollView(new Rect(0, 50, 490, 500), ScrollPosition, scrollContentRect);

                var y = 0;
                foreach (BoneNode node in Main.currentLoadProcess.Import.BoneNodes)
                {
                    if (!node.UIActive) continue;
                    int x = 10 + node.Depth * 20;
                    scrollContentRect.height += 22;
                    int xx = x + 200;
                    if (xx > scrollContentRect.width) scrollContentRect.width = xx + 10;
                    if (GUI.Button(new Rect(x + 25, y, 1000, 22), node.GameObject.name, node.IsDynamicRoot ? dynamicRoot : node.IsDynamic ? dynamic : normal))
                    {
                        node.SetDynamic(!node.IsDynamic);
                    }
                    if (node.Children.Count > 0)
                        if (GUI.Button(new Rect(x, y, 25, 22), node.NodeOpen ? "▼" : "►", arrow))
                        {
                            node.SetNodeOpen(!node.NodeOpen);
                        }
                    y += 22;
                }
                GUI.EndScrollView();
            }
            else
            {
                // custom styles 
                GUIStyle goodPath = new GUIStyle(KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.textField);
                goodPath.normal.textColor = Color.green;
                goodPath.hover.textColor = Color.green;
                goodPath.focused.textColor = Color.green;

                GUIStyle badPath = new GUIStyle(goodPath);
                badPath.normal.textColor = Color.red;
                badPath.hover.textColor = Color.red;
                badPath.focused.textColor = Color.red;

                GUIStyle label = new GUIStyle(KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.label);
                label.alignment = TextAnchor.MiddleCenter;

                GUIStyle box = new GUIStyle(KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.box);
                box.alignment = TextAnchor.MiddleCenter;
                
                if (GUI.Button(new Rect(10, 50, 480, 25), Replacer ? "◀ Replace in all paths ▶" : "◀ Path shared by all textures: ▶", KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.button))
                {
                    Replacer = !Replacer;
                }
                if (!Replacer)
                {
                    CommonPathText = GUI.TextField(new Rect(10, 75, 335, 25), CommonPathText, Directory.Exists(CommonPathText) ? goodPath : badPath);

                    if (!Directory.Exists(CommonPathText)) GUI.enabled = false;
                    if (GUI.Button(new Rect(350, 75, 480 - 350, 25), "Apply to all"))
                    {
                        Main.currentLoadProcess.Import.CommonPath = CommonPathText;
                    }
                    GUI.enabled = true;
                }
                else
                {
                    if (ReplaceString == null) ReplaceString = CommonPathText;
                    ReplaceString = GUI.TextField(new Rect(10, 75, 215, 25), ReplaceString, KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.textField);
                    if (GUI.Button(new Rect(230, 75, 40, 25), "with")) 
                    { 
                        foreach (Material mat in Main.currentLoadProcess.Import.MaterialTextures.Keys)
                        {
                            foreach (TexturePath tPath in Main.currentLoadProcess.Import.MaterialTextures[mat])
                            {
                                tPath.Path = tPath.Path.Replace(ReplaceString, ReplaceWith);
                            }
                        }
                    }
                    ReplaceWith = GUI.TextField(new Rect(275, 75, 215, 25), ReplaceWith, KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.textField);
                }

                Rect scrollContentRect = new Rect(0, 0, 470, 0);
                foreach (Material mat in Main.currentLoadProcess.Import.MaterialTextures.Keys)
                {
                    scrollContentRect.height += 30;
                    Main.currentLoadProcess.Import.MaterialTextures[mat].Where(tPath => tPath.Type == Assimp.TextureType.Diffuse || tPath.Type == Assimp.TextureType.Normals).ForEach(_ => scrollContentRect.height += 30);
                }

                ScrollPosition = GUI.BeginScrollView(new Rect(0, 110, 490, 440), ScrollPosition, scrollContentRect);

                var y = 0;
                foreach (Material mat in Main.currentLoadProcess.Import.MaterialTextures.Keys)
                {
                    GUI.Box(new Rect(10, y, 460, 25), mat.name);

                    foreach (TexturePath tPath in Main.currentLoadProcess.Import.MaterialTextures[mat].Where(tPath => tPath.Type == Assimp.TextureType.Diffuse || tPath.Type == Assimp.TextureType.Normals))
                    {
                        y += 30;
                        if (!tPath.PathOkay())
                        {
                            GUI.enabled = false;
                            if (tPath.Use) tPath.Use = false;
                        }
                        if (GUI.Button(new Rect(10, y, 25, 25), tPath.Use ? "☑" : "☐"))
                        {
                            tPath.Use = !tPath.Use;
                        }
                        GUI.enabled = true;
                        GUI.Box(new Rect(35, y, 100, 25), "");
                        GUI.Label(new Rect(35, y, 100, 25), tPath.Type.ToString(), label);
                        tPath.Path = GUI.TextField(
                            new Rect(135, y, 470 - 160, 25),
                            tPath.Path,
                            tPath.PathOkay() ? goodPath : badPath
                        );
                        if (!GUI.Button(new Rect(470 - 25, y, 25, 25), "...")) continue;
                        tPath.Path = tPath.Path.Replace("\\", "/");
                        string[] file = KKAPI.Utilities.OpenFileDialog.ShowDialog("Select Texture", Main.currentLoadProcess.Import.SourceIdentifier,
                            "Image files (*.png; *.jpg) |*.png; *.jpg | All files (*.*)|*.*", "png", SingleFileFlags);
                        if (file != null)
                        {
                            tPath.Path = file[0];
                        }
                    }

                    y += 30;
                }

                GUI.EndScrollView();
            }

            GUI.enabled = true;

            if (!GUI.Button(new Rect(10, PreloadWindowRect.height - 45, PreloadWindowRect.width - 20, 40),
                    "Finish")) return;
            if (KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)
                Main.asc.FinishLoadProcess(Main.currentLoadProcess);
            else if (KoikatuAPI.GetCurrentGameMode() == GameMode.Maker)
                MakerAPI.GetCharacterControl().gameObject.GetComponentInChildren<AssetCharaController>()?.FinishLoadProcess(Main.currentLoadProcess);
        }
    }
}
