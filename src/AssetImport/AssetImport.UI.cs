using System.Collections.Generic;
using System.IO;
using UnityEngine;
using KKAPI;
using KKAPI.Maker;
using Main = AssetImport.AssetImport;

namespace AssetImport
{
    /// <summary>
    /// Everything IMGUI
    /// </summary>
    class AssetUI : MonoBehaviour
    {
        // UI
        // main
        internal static bool uiActive = false;
        internal static Rect mainWindowRect = new Rect(500, 40, 240, 140);
        internal static string filePath = "";
        internal static int scaleSelection = 4;
        internal static float[] scales = { 10f, 5f, 2f, 1.5f, 1.0f, 0.5f, 0.1f, 0.01f, 0.001f, 0.0001f };
        internal static bool importBones = false;
        internal static bool perRendererMaterials = false;
        internal static bool doFbxTranslation = false;
        internal static Dictionary<int, Import> objectKeysStudio = new Dictionary<int, Import>();
        // maker specific
        internal static int accType = 0;
        internal static int parentNode = 0;

        // preload
        internal static bool preloadUI = false;
        internal static Rect preloadWinodwRect = new Rect(Screen.width / 2 - 250, Screen.height / 2 - 350, 500, 600);
        internal static Vector2 scrollPosition = Vector2.zero;
        internal static bool armatureMode = false;
        internal Texture2D buttonBase, buttonHover, buttonPressed;
        internal static string commonPathText;
        internal static bool replacer = false;
        internal static string replaceString = null;
        internal static string replaceWith = "";

        internal static KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags SingleFileFlags =
                KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags.OFN_FILEMUSTEXIST |
                KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags.OFN_LONGNAMES |
                KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags.OFN_EXPLORER;


        void Awake()
        {
            // setup textures
            float w = 84f / 255f;
            buttonBase = colorTexture(new Color(w, w, w));
            buttonHover = colorTexture(new Color(w + 0.2f, w + 0.2f, w + 0.2f));
            buttonPressed = colorTexture(new Color(w + 0.3f, w + +0.3f, w + 0.3f));
        }

        void Update()
        {
            if (Main.hotkey.Value.IsDown() && (KKAPI.KoikatuAPI.GetCurrentGameMode() == GameMode.Maker || KKAPI.KoikatuAPI.GetCurrentGameMode() == GameMode.Studio))
            {
                uiActive = !uiActive;
                return;
            }
            if (KoikatuAPI.GetCurrentGameMode() != GameMode.Studio && KoikatuAPI.GetCurrentGameMode() != GameMode.Maker)
                uiActive = false;
    
        }

        void OnGUI()
        {
            if (!(KKAPI.KoikatuAPI.GetCurrentGameMode() == GameMode.Maker || KKAPI.KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)) return;
            if (uiActive)
            {
                mainWindowRect = GUI.Window(33361, mainWindowRect, WindowFunction, "Asset Import v" + Main.Version);
                KKAPI.Utilities.IMGUIUtils.EatInputInRect(mainWindowRect);
            }
            if (preloadUI)
            {
                preloadWinodwRect = GUI.Window(
                    33362,
                    preloadWinodwRect,
                    preLoadWindowFunction,
                    Main.currentLoadProcess.import.sourceFileName,
                    KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.window
                );
                KKAPI.Utilities.IMGUIUtils.EatInputInRect(preloadWinodwRect);
            }
        }
        private Texture2D colorTexture(Color color)
        {
            Texture2D txt = new Texture2D(1, 1);
            txt.SetPixel(1, 1, color);
            txt.Apply();
            return txt;
        }


        private void WindowFunction(int WindowID)
        {
            int y = 0;
            filePath = GUI.TextField(new Rect(10, y += 20, 195, 20), filePath);
            filePath = filePath.Replace("\\", "/");
            filePath = filePath.Replace("\"", "");
            if (GUI.Button(new Rect(205, y, 25, 20), "..."))
            {
                string dir = (filePath == "") ? Main.defaultDir.Value : filePath.Replace(filePath.Substring(filePath.LastIndexOf("/")), "");
                string[] file = KKAPI.Utilities.OpenFileDialog.ShowDialog("Open 3D file", dir,
                        "3D files (*.fbx; *.dae; *.gltf;  *.blend; *.3ds; *.ase; *.obj; *.ifc; *.xgl; *.ply; *.dxf; *.lwo; *.lws; *.lxo; *.stl; *.x; *.ac; *.ms3d; *.smd) " +
                        "|*.fbx; *.dae; *.gltf; *.blend; *.3ds; *.ase; *.obj; *.ifc; *.xgl; *.ply; *.dxf; *.lwo; *.lws; *.lxo; *.stl; *.x; *.ac; *.ms3d; *.smd | All files (*.*)|*.*",
                        "obj", SingleFileFlags);
                if (file != null)
                {
                    filePath = file[0];
                }
            }

            if (GUI.Button(new Rect(10, y += 25, 220, 25), importBones ? "☑ Import Armature" : "☐ Import Armature"))
            {
                importBones = !importBones;
            }

            if (GUI.Button(new Rect(10, y += 25, 220, 25), perRendererMaterials ? "☑ Material per Renderer" : "☐ Material per Renderer"))
            {
                perRendererMaterials = !perRendererMaterials;
            }
            if (Path.GetExtension(filePath).ToLower() == ".fbx")
            {
                if (GUI.Button(new Rect(10, y += 25, 220, 25), doFbxTranslation ? "☑ Ignore Root translation" : "☐ Ignore Root Translation"))
                {
                    doFbxTranslation = !doFbxTranslation;
                }
            }

            GUI.Label(new Rect(10, y+=30, 160, 25), $"Scaling-factor: {scales[scaleSelection]}");
            if (scaleSelection == 0) GUI.enabled = false;
            if (GUI.Button(new Rect(190, y, 20, 20), "+"))
            {
                scaleSelection--;
            }
            //GUI.enabled = true;
            if (scaleSelection == 9) GUI.enabled = false;
            if (GUI.Button(new Rect(210, y, 20, 20), "-"))
            {
                scaleSelection++;
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(10, y+=25, 220, 30), "Import"))
            {
                if (KKAPI.KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)
                    AssetImport.asc.Import(filePath, new Vector3(scales[scaleSelection], scales[scaleSelection], scales[scaleSelection]), importBones, perRendererMaterials, doFbxTranslation);
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
                            filePath,
                            new Vector3(scales[scaleSelection], scales[scaleSelection], scales[scaleSelection]),
                            importBones,
                            perRendererMaterials,
                            doFbxTranslation);
                    }
                    else AssetImport.Logger.LogMessage("Please select an accessory which you want to replace!");
                }
            }

            mainWindowRect.height = y += 40;

            GUI.DragWindow();
        }

        private void preLoadWindowFunction(int WindowID)
        {
            if (!preloadUI) return;
            if (!(Main.currentLoadProcess.import.hasBones || Main.currentLoadProcess.import.hasTextures))
            {
                if (KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)
                    Main.asc?.FinishLoadProcess(Main.currentLoadProcess);
                else if (KoikatuAPI.GetCurrentGameMode() == GameMode.Maker)
                    MakerAPI.GetCharacterControl().gameObject.GetComponentInChildren<AssetCharaController>()?.FinishLoadProcess(Main.currentLoadProcess);
                return;
            }

            if (!(Main.currentLoadProcess.import.hasBones && Main.currentLoadProcess.import.hasTextures)) GUI.enabled = false;
            if (GUI.Button(new Rect(10, 20, 480, 25), armatureMode ? "Switch to Textures" : "Switch to Armature"))
            {
                armatureMode = !armatureMode;
            }
            GUI.enabled = true;

            if (armatureMode)
            {
                // custom button styles
                GUIStyle normal = new GUIStyle(KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.button);
                normal.normal.background = buttonBase;
                normal.hover.background = buttonHover;
                normal.active.background = buttonPressed;
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

                Rect scrollConentRect = new Rect(0, 0, 10, 30);
                foreach (BoneNode node in Main.currentLoadProcess.import.boneNodes)
                {
                    if (!node.uiActive) continue;
                    scrollConentRect.height += 22;
                }

                scrollPosition = GUI.BeginScrollView(new Rect(0, 50, 490, 500), scrollPosition, scrollConentRect);

                int y = 0;
                for (int i = 0; i < Main.currentLoadProcess.import.boneNodes.Count; i++)
                {
                    BoneNode node = Main.currentLoadProcess.import.boneNodes[i];
                    if (!node.uiActive) continue;
                    int x = 10 + node.depth * 20;
                    scrollConentRect.height += 22;
                    int xx = x + 200;
                    if (xx > scrollConentRect.width) scrollConentRect.width = xx + 10;
                    if (GUI.Button(new Rect(x + 25, y, 1000, 22), node.gameObject.name, node.isDynamicRoot ? dynamicRoot : node.isDynamic ? dynamic : normal))
                    {
                        node.setDynamic(!node.isDynamic);
                    }
                    if (node.children.Count > 0)
                        if (GUI.Button(new Rect(x, y, 25, 22), node.nodeOpen ? "▼" : "►", arrow))
                        {
                            node.setNodeOpen(!node.nodeOpen);
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
                
                if (GUI.Button(new Rect(10, 50, 480, 25), replacer ? "◀ Replace in all paths ▶" : "◀ Path shared by all textures: ▶", KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.button))
                {
                    replacer = !replacer;
                }
                if (!replacer)
                {
                    commonPathText = GUI.TextField(new Rect(10, 75, 335, 25), commonPathText, Directory.Exists(commonPathText) ? goodPath : badPath);

                    if (!Directory.Exists(commonPathText)) GUI.enabled = false;
                    if (GUI.Button(new Rect(350, 75, 480 - 350, 25), "Apply to all"))
                    {
                        Main.currentLoadProcess.import.commonPath = commonPathText;
                    }
                    GUI.enabled = true;
                }
                else
                {
                    if (replaceString == null) replaceString = commonPathText;
                    replaceString = GUI.TextField(new Rect(10, 75, 215, 25), replaceString, KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.textField);
                    if (GUI.Button(new Rect(230, 75, 40, 25), "with")) 
                    { 
                        foreach (Material mat in Main.currentLoadProcess.import.materialTextures.Keys)
                        {
                            foreach (TexturePath tPath in Main.currentLoadProcess.import.materialTextures[mat])
                            {
                                tPath.path = tPath.path.Replace(replaceString, replaceWith);
                            }
                        }
                    }
                    replaceWith = GUI.TextField(new Rect(275, 75, 215, 25), replaceWith, KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.textField);
                }

                Rect scrollConentRect = new Rect(0, 0, 470, 0);
                foreach (Material mat in Main.currentLoadProcess.import.materialTextures.Keys)
                {
                    scrollConentRect.height += 30;
                    foreach (TexturePath tPath in Main.currentLoadProcess.import.materialTextures[mat])
                    {
                        if (!(tPath.type == Assimp.TextureType.Diffuse || tPath.type == Assimp.TextureType.Normals)) continue;
                        scrollConentRect.height += 30;
                    }
                }

                scrollPosition = GUI.BeginScrollView(new Rect(0, 110, 490, 440), scrollPosition, scrollConentRect);

                int y = 0;
                foreach (Material mat in Main.currentLoadProcess.import.materialTextures.Keys)
                {
                    GUI.Box(new Rect(10, y, 460, 25), mat.name);

                    foreach (TexturePath tPath in Main.currentLoadProcess.import.materialTextures[mat])
                    {
                        // currently only supports diffuse and normals
                        if (!(tPath.type == Assimp.TextureType.Diffuse || tPath.type == Assimp.TextureType.Normals)) continue;
                        y += 30;
                        if (!tPath.pathOkay())
                        {
                            GUI.enabled = false;
                            if (tPath.use) tPath.use = false;
                        }
                        if (GUI.Button(new Rect(10, y, 25, 25), tPath.use ? "☑" : "☐"))
                        {
                            tPath.use = !tPath.use;
                        }
                        GUI.enabled = true;
                        GUI.Box(new Rect(35, y, 100, 25), "");
                        GUI.Label(new Rect(35, y, 100, 25), tPath.type.ToString(), label);
                        tPath.path = GUI.TextField(
                            new Rect(135, y, 470 - 160, 25),
                            tPath.path,
                            tPath.pathOkay() ? goodPath : badPath
                        );
                        if (GUI.Button(new Rect(470-25, y, 25, 25), "..."))
                        {
                            tPath.path = tPath.path.Replace("\\", "/");
                            string[] file = KKAPI.Utilities.OpenFileDialog.ShowDialog("Select Texture", Main.currentLoadProcess.import.sourceIdentifier,
                                    "Image files (*.png; *.jpg) |*.png; *.jpg | All files (*.*)|*.*", "png", SingleFileFlags);
                            if (file != null)
                            {
                                tPath.path = file[0];
                            }
                        }
                    }

                    y += 30;
                }

                GUI.EndScrollView();
            }

            GUI.enabled = true;

            if (GUI.Button(new Rect(10, preloadWinodwRect.height - 45, preloadWinodwRect.width - 20, 40), "Finish"))
            {
                if (KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)
                    Main.asc.FinishLoadProcess(Main.currentLoadProcess);
                else if (KoikatuAPI.GetCurrentGameMode() == GameMode.Maker)
                    MakerAPI.GetCharacterControl().gameObject.GetComponentInChildren<AssetCharaController>()?.FinishLoadProcess(Main.currentLoadProcess);
            }
        }
    }
}
