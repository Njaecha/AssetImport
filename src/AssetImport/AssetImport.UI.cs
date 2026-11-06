using System;
using System.IO;
using System.Linq;
using UnityEngine;
using KKAPI;
using KKAPI.Maker;
using Main = AssetImport.AssetImport;
// ReSharper disable UseObjectOrCollectionInitializer

namespace AssetImport
{
    /// <summary>
    /// Everything IMGUI
    /// </summary>
    internal partial class AssetUI : MonoBehaviour
    {
        // UI
        // main
        internal static bool UIActive;
        internal static Rect MainWindowRect = new Rect(500, 40, 260, 140);
        internal static string FilePath = "";
        internal static int ScaleSelection = 4;
        internal static readonly float[] Scales = { 10f, 5f, 2f, 1.5f, 1.0f, 0.5f, 0.1f, 0.01f, 0.001f, 0.0001f };
        internal static bool ImportBones = true;
        internal static bool PerRendererMaterials;
        internal static bool LoadBlendshapes = true;
        internal static bool AutoSelectPmxPhysics = true;
        internal static bool EnablePhysicsAfterImport = true;
        internal static bool UsePmxPhysicsPreset = true;
        internal static bool PmxHairCollisionAssist;
        internal static PhysicsEnvironmentSettings NewPhysicsEnvironment = PhysicsEnvironmentSettings.NewImport();
        internal static CubeLut ImportColorLut;
        private bool _convertKkColor;
        private string _colorLutPath = "";
        private LocalizedText _colorLutError = "";

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
        private Import _armatureImport;
        private string _boneSearch = "";
        private bool _selectedRootsOnly;
        private LocalizedText _physicsSelectionStatus = "";
        private LocalizedText _mmddStatus = "";
        private LocalizedText _physicsActionStatus = "";
        private Vector2 _mainScroll = Vector2.zero;
        private float _mainContentHeight = 140;

        private const KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags SingleFileFlags = KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags.OFN_FILEMUSTEXIST | KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags.OFN_LONGNAMES | KKAPI.Utilities.OpenFileDialog.OpenSaveFileDialgueFlags.OFN_EXPLORER;


        void Awake()
        {
            _colorLutPath = Main.colorLutPath.Value;
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
            var skin = GUI.skin;
            var color = GUI.color;
            var contentColor = GUI.contentColor;
            var backgroundColor = GUI.backgroundColor;
            var wasEnabled = GUI.enabled;
            try
            {
                GUI.skin = InterfaceSkin();
                GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
                GUI.enabled = true;
                if (UIActive)
                {
                    MainWindowRect = GUI.Window(33361, MainWindowRect, WindowFunction, "Asset Import v" + Main.Version);
                    KKAPI.Utilities.IMGUIUtils.EatInputInRect(MainWindowRect);
                    DrawChainPhysicsWindow();
                }
                if (PreloadUI)
                {
                    PreloadWindowRect = GUI.Window(
                        33362,
                        PreloadWindowRect,
                        PreLoadWindowFunction,
                        Main.currentLoadProcess.Import.SourceFileName,
                        GUI.skin.window
                    );
                    KKAPI.Utilities.IMGUIUtils.EatInputInRect(PreloadWindowRect);
                }
            }
            finally
            {
                GUI.skin = skin;
                GUI.color = color;
                GUI.contentColor = contentColor;
                GUI.backgroundColor = backgroundColor;
                GUI.enabled = wasEnabled;
            }
        }

        // Draw the indicator ourselves: Unity's dynamic font atlas can lose
        // U+2610/U+2611 when another plugin rebuilds it (especially on KKS).
        private static bool CheckButton(Rect rect, bool selected, string label)
        {
            var style = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
            style.padding = new RectOffset(25, 4, 2, 2);
            bool clicked = GUI.Button(rect, label, style);
            if (Event.current.type == EventType.Repaint)
            {
                Color color = GUI.color;
                try
                {
                    float x = rect.x + 7, y = rect.y + (rect.height - 12) / 2;
                    GUI.color = new Color(1, 1, 1, GUI.enabled ? 1 : .45f);
                    GUI.DrawTexture(new Rect(x, y, 12, 1), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(x, y + 11, 12, 1), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(x, y, 1, 12), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(x + 11, y, 1, 12), Texture2D.whiteTexture);
                    if (selected) GUI.DrawTexture(new Rect(x + 3, y + 3, 6, 6), Texture2D.whiteTexture);
                }
                finally { GUI.color = color; }
            }
            return clicked;
        }
        private static Texture2D ColorTexture(Color color)
        {
            Texture2D txt = new Texture2D(1, 1);
            txt.SetPixel(0, 0, color);
            txt.Apply();
            return txt;
        }


        private void WindowFunction(int windowID)
        {
            float viewHeight = Math.Min(_mainContentHeight, Math.Max(180, Screen.height - 100));
            _mainScroll = GUI.BeginScrollView(new Rect(0, 20, 260, viewHeight), _mainScroll,
                new Rect(0, 20, 240, _mainContentHeight));
            var y = 20;
            if (CheckButton(new Rect(10, y, 220, 25), AssetDebug.Enabled, L.Text("Debug")))
            {
                Main.debugMode.Value = !AssetDebug.Enabled;
                _diagnosticActionStatus = "";
            }
            y += 10;
            if (AssetDebug.Enabled)
            {
                GUI.Label(new Rect(10, y += 18, 220, 20), L.Format("Build: {0}", NativeContactDiagnostics.ShortBuild));
                if (GUI.Button(new Rect(10, y += 22, 220, 25), L.Text("Open diagnostics folder")))
                    _diagnosticActionStatus = OpenContactDiagnosticsFolder();
                y += 8;
                if (!string.IsNullOrEmpty(NativeContactDiagnostics.LastError))
                {
                    var diagnosticStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                    float diagnosticHeight = diagnosticStyle.CalcHeight(new GUIContent(NativeContactDiagnostics.LastError), 220);
                    GUI.Label(new Rect(10, y += 20, 220, diagnosticHeight), NativeContactDiagnostics.LastError, diagnosticStyle);
                    y += (int)diagnosticHeight;
                }
            }
            FilePath = GUI.TextField(new Rect(10, y += 20, 195, 20), FilePath);
            FilePath = FilePath.Replace("\\", "/");
            FilePath = FilePath.Replace("\"", "");
            if (GUI.Button(new Rect(205, y, 25, 20), "..."))
            {
                string dir = (FilePath == "") ? Main.defaultDir.Value : FilePath.Replace(FilePath.Substring(FilePath.LastIndexOf("/")), "");
                string[] file = KKAPI.Utilities.OpenFileDialog.ShowDialog(L.Text("Open 3D file"), dir,
                        L.Text("3D files") + " (*.pmx; *.fbx; *.dae; *.gltf; *.3ds; *.ase; *.obj; *.ifc; *.xgl; *.ply; *.dxf; *.lwo; *.lws; *.lxo; *.stl; *.x; *.ac; *.ms3d; *.smd) " +
                        "|*.pmx; *.fbx; *.dae; *.gltf; *.3ds; *.ase; *.obj; *.ifc; *.xgl; *.ply; *.dxf; *.lwo; *.lws; *.lxo; *.stl; *.x; *.ac; *.ms3d; *.smd |" + L.Text("All files") + " (*.*)|*.*",
                        "obj", SingleFileFlags);
                if (file != null)
                {
                    FilePath = file[0];
                }
            }

            if (FilePath.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase) &&
                GUI.Button(new Rect(10, y += 25, 220, 25), L.Text("PMX / VMD preset")))
            {
                ImportBones = true;
                LoadBlendshapes = true;
                PerRendererMaterials = false;
                ScaleSelection = 4;
                AutoSelectPmxPhysics = true;
                EnablePhysicsAfterImport = true;
                UsePmxPhysicsPreset = true;
                NewPhysicsEnvironment = PhysicsEnvironmentSettings.NewImport();
            }
            if (CheckButton(new Rect(10, y += 25, 220, 25), ImportBones, L.Text("Import Armature")))
            {
                ImportBones = !ImportBones;
            }
            if (CheckButton(new Rect(10, y += 25, 220, 25), LoadBlendshapes, L.Text("Import Blendshapes")))
            {
                LoadBlendshapes = !LoadBlendshapes;
            }

            if (CheckButton(new Rect(10, y += 25, 220, 25), PerRendererMaterials, L.Text("Material per Renderer")))
            {
                PerRendererMaterials = !PerRendererMaterials;
            }
            if (CheckButton(new Rect(10, y += 25, 220, 25), _convertKkColor, L.Text("Convert diffuse to KK color")))
                _convertKkColor = !_convertKkColor;
            if (_convertKkColor)
            {
                _colorLutPath = GUI.TextField(new Rect(10, y += 25, 195, 22), _colorLutPath);
                if (GUI.Button(new Rect(205, y, 25, 22), "..."))
                {
                    string[] files = KKAPI.Utilities.OpenFileDialog.ShowDialog(L.Text("Select PS to KK color LUT"), Main.defaultDir.Value,
                        L.Text("3D LUT") + " (*.cube)|*.cube", "cube", SingleFileFlags);
                    if (files != null && files.Length > 0) _colorLutPath = files[0];
                }
                var lutHelpStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                string lutHelp = L.Text("Select PS-to-KK .cube. Already converted textures: leave OFF.");
                float lutHelpHeight = lutHelpStyle.CalcHeight(new GUIContent(lutHelp), 220);
                GUI.Label(new Rect(10, y += 25, 220, lutHelpHeight), lutHelp, lutHelpStyle);
                y += (int)lutHelpHeight - 20;
            }
            if (FilePath.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase))
            {
                GUI.enabled = ImportBones;
                if (CheckButton(new Rect(10, y += 25, 220, 25), AutoSelectPmxPhysics, L.Text("Auto PMX physics chains")))
                    AutoSelectPmxPhysics = !AutoSelectPmxPhysics;
                if (CheckButton(new Rect(10, y += 25, 220, 25), EnablePhysicsAfterImport, L.Text("Enable physics after import")))
                    EnablePhysicsAfterImport = !EnablePhysicsAfterImport;
                if (CheckButton(new Rect(10, y += 25, 220, 25), UsePmxPhysicsPreset, L.Text("PMX physics preset")))
                    UsePmxPhysicsPreset = !UsePmxPhysicsPreset;
                if (CheckButton(new Rect(10, y += 25, 220, 25), PmxHairCollisionAssist, L.Text("Hair body collision assist")))
                    PmxHairCollisionAssist = !PmxHairCollisionAssist;
                if (CheckButton(new Rect(10, y += 25, 220, 25), NewPhysicsEnvironment.Floor, L.Text("Ground collision")))
                    NewPhysicsEnvironment.Floor = !NewPhysicsEnvironment.Floor;
                if (CheckButton(new Rect(10, y += 25, 220, 25), NewPhysicsEnvironment.DownwardForce > 0f, L.Text("Downward force")))
                    NewPhysicsEnvironment.DownwardForce = NewPhysicsEnvironment.DownwardForce > 0f ? 0f : .001f;
                if (GUI.Button(new Rect(10, y += 25, 220, 25), L.Format("Physics: {0}", PhysicsModeName(NewPhysicsEnvironment.DefaultMode))))
                    NewPhysicsEnvironment.NaturalDrape = !NewPhysicsEnvironment.NaturalDrape;
                if (CheckButton(new Rect(10, y += 25, 220, 25), NewPhysicsEnvironment.Settle, L.Text("Settle on load / reset")))
                    NewPhysicsEnvironment.Settle = !NewPhysicsEnvironment.Settle;
                GUI.enabled = true;
            }


            GUI.Label(new Rect(10, y+=30, 160, 25), L.Format("Scaling-factor: {0}", Scales[ScaleSelection]));
            if (ScaleSelection == 0) GUI.enabled = false;
            if (GUI.Button(new Rect(190, y, 20, 20), "+"))
            {
                ScaleSelection--;
            }
            GUI.enabled = true;
            if (ScaleSelection == 9) GUI.enabled = false;
            if (GUI.Button(new Rect(210, y, 20, 20), "-"))
            {
                ScaleSelection++;
            }
            GUI.enabled = true;
            if (string.IsNullOrEmpty(FilePath)) GUI.enabled = false;
            if (GUI.Button(new Rect(10, y+=25, 220, 30), L.Text("Import")))
            {
                ImportColorLut = null;
                _colorLutError = "";
                try
                {
                    if (_convertKkColor && !PerRendererMaterials)
                    {
                        using (var reader = new StreamReader(_colorLutPath)) ImportColorLut = CubeLut.Read(reader);
                        Main.colorLutPath.Value = _colorLutPath;
                    }
                }
                catch (Exception error) { _colorLutError = L.Message("Color LUT: {0}", error.Message); }
                if (_colorLutError.Length == 0)
                {
                    _mmddStatus = "";
                    _physicsActionStatus = "";
                    if (KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)
                        Main.asc.Import(FilePath, new Vector3(Scales[ScaleSelection], Scales[ScaleSelection], Scales[ScaleSelection]), ImportBones, PerRendererMaterials, LoadBlendshapes);
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
                                LoadBlendshapes);
                        }
                        else Main.Logger.LogMessage(L.Text("Please select an accessory which you want to replace!"));
                    }
                }
            }
            GUI.enabled = true;
            if (_colorLutError.Length > 0)
            {
                var errorStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                float height = errorStyle.CalcHeight(new GUIContent(_colorLutError), 220);
                GUI.Label(new Rect(10, y += 32, 220, height), _colorLutError, errorStyle);
                y += (int)height;
            }

            if (!string.IsNullOrEmpty(ImportTextureData.Status))
            {
                var textureStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                string status = ImportTextureData.PendingImports > 1
                    ? L.Format("{0} imports pending. {1}", ImportTextureData.PendingImports, ImportTextureData.Status)
                    : (string)ImportTextureData.Status;
                float height = textureStyle.CalcHeight(new GUIContent(status), 220);
                GUI.Label(new Rect(10, y += 32, 220, height), status, textureStyle);
                y += (int)height;
            }

            var mode = KoikatuAPI.GetCurrentGameMode();
            AssetCharaController character = mode == GameMode.Maker
                ? MakerAPI.GetCharacterControl()?.gameObject.GetComponentInChildren<AssetCharaController>() : null;
            LocalizedText physicsStatus = mode == GameMode.Studio ? Main.asc?.GetSelectedPhysicsStatus()
                : character?.GetSelectedAccessoryPhysicsStatus();
            var physicsStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            string displayPhysicsStatus = physicsStatus ?? L.Text("Select an imported model to control its physics.");
            float physicsHeight = physicsStyle.CalcHeight(new GUIContent(displayPhysicsStatus), 220);
            GUI.Label(new Rect(10, y += 35, 220, physicsHeight), displayPhysicsStatus, physicsStyle);
            y += (int)physicsHeight;
            GUI.enabled = !PreloadUI && (mode == GameMode.Studio ? Main.asc != null : character != null);
            if (GUI.Button(new Rect(10, y += 5, 107, 25), L.Text("Physics ON")))
                _physicsActionStatus = mode == GameMode.Studio ? Main.asc.SetSelectedPhysicsEnabled(true)
                    : character.SetSelectedAccessoryPhysicsEnabled(true);
            if (GUI.Button(new Rect(122, y, 108, 25), L.Text("Physics OFF")))
                _physicsActionStatus = mode == GameMode.Studio ? Main.asc.SetSelectedPhysicsEnabled(false)
                    : character.SetSelectedAccessoryPhysicsEnabled(false);
            if (GUI.Button(new Rect(10, y += 30, 220, 25), L.Text("Reset physics motion")))
                _physicsActionStatus = mode == GameMode.Studio ? Main.asc.ResetSelectedPhysics()
                    : character.ResetSelectedAccessoryPhysics();
            GUI.enabled = true;
            y += 25;
            Asset environmentAsset = null;
            var environment = mode == GameMode.Studio ? Main.asc?.GetSelectedPhysicsEnvironment(out environmentAsset)
                : character?.GetSelectedPhysicsEnvironment(out environmentAsset);
            DrawPhysicsEnvironment(ref y, environment, environmentAsset);
            if (AssetDebug.Enabled)
            {
                NativeContactDiagnostics.Select(environment, _environment == environment && _environmentDraft != null
                    ? (bool?)_environmentDraft.NativeBreastContacts : null, physicsStatus == null ? "No selection" : physicsStatus.English);
                if (GUI.Button(new Rect(10, y += 5, 220, 25), L.Text("Save contact diagnostics")))
                    _diagnosticActionStatus = SaveContactDiagnostics(environment, physicsStatus == null ? "No selection" : physicsStatus.English);
                y += 25;
                if (!string.IsNullOrEmpty(_diagnosticActionStatus))
                {
                    float height = physicsStyle.CalcHeight(new GUIContent(_diagnosticActionStatus), 220);
                    GUI.Label(new Rect(10, y += 5, 220, height), _diagnosticActionStatus, physicsStyle);
                    y += (int)height;
                }
            }
            if (!string.IsNullOrEmpty(_physicsActionStatus))
            {
                string actionFeedback = L.Format("Last action: {0}", _physicsActionStatus);
                float height = physicsStyle.CalcHeight(new GUIContent(actionFeedback), 220);
                GUI.Label(new Rect(10, y += 5, 220, height), actionFeedback, physicsStyle);
                y += (int)height;
            }

            if (KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)
            {
                GUI.enabled = !PreloadUI && Main.asc != null;
                if (GUI.Button(new Rect(10, y += 35, 220, 25), L.Text("Repair selected PMX Auto")))
                    _mmddStatus = Main.asc.RepairSelectedPmxAuto();
                GUI.enabled = true;
                if (!string.IsNullOrEmpty(_mmddStatus))
                {
                    var statusStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                    float height = statusStyle.CalcHeight(new GUIContent(_mmddStatus), 220);
                    GUI.Label(new Rect(10, y += 30, 220, height), _mmddStatus, statusStyle);
                    y += (int)height;
                }
            }

            GUI.EndScrollView();
            _mainContentHeight = y + 20;
            MainWindowRect.height = Math.Min(_mainContentHeight, Math.Max(180, Screen.height - 100)) + 40;
            GUI.DragWindow(new Rect(0, 0, 260, 20));
        }

        private void PreLoadWindowFunction(int windowID)
        {
            if (!PreloadUI) return;
            if (!(Main.currentLoadProcess.Import.HasBones || Main.currentLoadProcess.Import.HasTextures))
            {
                if (KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)
                {
                    Main.asc?.FinishLoadProcess(Main.currentLoadProcess);
                    _mmddStatus = Main.currentLoadProcess.Import.LastMmddExportStatus;
                }
                else if (KoikatuAPI.GetCurrentGameMode() == GameMode.Maker)
                    MakerAPI.GetCharacterControl().gameObject.GetComponentInChildren<AssetCharaController>()?.FinishLoadProcess(Main.currentLoadProcess);
                return;
            }

            if (!(Main.currentLoadProcess.Import.HasBones && Main.currentLoadProcess.Import.HasTextures)) GUI.enabled = false;
            if (GUI.Button(new Rect(10, 20, 480, 25), ArmatureMode ? L.Text("Switch to Textures") : L.Text("Switch to Armature")))
            {
                ArmatureMode = !ArmatureMode;
            }
            GUI.enabled = true;

            if (ArmatureMode)
            {
                Import import = Main.currentLoadProcess.Import;
                if (_armatureImport != import)
                {
                    _armatureImport = import;
                    _boneSearch = "";
                    _selectedRootsOnly = false;
                    _physicsSelectionStatus = import.InitialPhysicsSelectionStatus;
                    ScrollPosition = Vector2.zero;
                }
                // custom button styles
                GUIStyle normal = new GUIStyle(GUI.skin.button);
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

                GUI.Label(new Rect(10, 48, 480, 40), L.Text("All bones are already imported. Click a name to select a PHYSICS ROOT.\nGreen = physics root; yellow-green = children. Do not select the whole skeleton."));

                float controlsY = 92;
                if (import.IsPmx)
                {
                    if (GUI.Button(new Rect(10, controlsY, 235, 25), L.Text("All PMX physics chains")))
                    {
                        _physicsSelectionStatus = import.SelectPmxPhysicsRoots();
                        ScrollPosition = Vector2.zero;
                    }
                    if (GUI.Button(new Rect(250, controlsY, 240, 25), L.Text("Add hair roots (incl. ponytails)")))
                    {
                        _physicsSelectionStatus = import.SelectPmxHairRoots();
                        ScrollPosition = Vector2.zero;
                    }
                    controlsY += 29;
                    if (GUI.Button(new Rect(10, controlsY, 200, 25), L.Text("Clear physics roots")))
                    {
                        import.ClearPhysicsRoots();
                        _physicsSelectionStatus = L.Message("Physics roots cleared. The full skeleton is still imported.");
                    }
                    if (CheckButton(new Rect(215, controlsY, 275, 25), import.PhysicsEnabledAfterImport, L.Text("Enable physics after import")))
                        import.PhysicsEnabledAfterImport = !import.PhysicsEnabledAfterImport;
                    controlsY += 29;
                    if (GUI.Button(new Rect(10, controlsY, 480, 25), L.Format("Physics settings: {0}", import.PhysicsProfile == 1 ? L.Text("PMX preset (preview)") : L.Text("Legacy defaults"))))
                        import.PhysicsProfile = import.PhysicsProfile == 1 ? 0 : 1;
                    controlsY += 29;
                    if (CheckButton(new Rect(10, controlsY, 480, 25), import.PmxHairCollisionAssist, L.Text("Hair body collision assist (override PMX groups)")))
                        import.PmxHairCollisionAssist = !import.PmxHairCollisionAssist;
                    controlsY += 29;

                }
                GUI.Label(new Rect(10, controlsY, 50, 25), L.Text("Search:"));
                string search = GUI.TextField(new Rect(60, controlsY, 235, 25), _boneSearch);
                if (search != _boneSearch) { _boneSearch = search; ScrollPosition = Vector2.zero; }
                if (CheckButton(new Rect(300, controlsY, 190, 25), _selectedRootsOnly, L.Text("Selected roots only")))
                {
                    _selectedRootsOnly = !_selectedRootsOnly;
                    ScrollPosition = Vector2.zero;
                }
                controlsY += 29;
                if (GUI.Button(new Rect(10, controlsY, 110, 25), L.Text("Expand all")))
                    foreach (var node in import.BoneNodes.OrderBy(n => n.Depth)) node.SetNodeOpen(true);
                if (GUI.Button(new Rect(125, controlsY, 110, 25), L.Text("Collapse all")))
                    foreach (var node in import.BoneNodes.OrderByDescending(n => n.Depth)) node.SetNodeOpen(false);
                GUI.Label(new Rect(245, controlsY, 245, 25), L.Format("Selected physics roots: {0}", import.BoneNodes.Count(n => n.IsDynamicRoot)));
                controlsY += 29;
                if (!string.IsNullOrEmpty(_physicsSelectionStatus))
                {
                    var statusStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                    float height = statusStyle.CalcHeight(new GUIContent(_physicsSelectionStatus), 480);
                    GUI.Label(new Rect(10, controlsY, 480, height), _physicsSelectionStatus, statusStyle);
                    controlsY += height + 4;
                }

                bool filtering = _selectedRootsOnly || !string.IsNullOrEmpty(_boneSearch);
                var visibleNodes = import.BoneNodes.Where(node =>
                    (filtering || node.UIActive) && (!_selectedRootsOnly || node.IsDynamicRoot) &&
                    (string.IsNullOrEmpty(_boneSearch) || node.GameObject.name.IndexOf(_boneSearch, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
                float contentWidth = visibleNodes.Length == 0 ? 470 : Math.Max(470, visibleNodes.Max(n => 65 + n.Depth * 20 + n.GameObject.name.Length * 8));
                Rect scrollContentRect = new Rect(0, 0, contentWidth, 22 * visibleNodes.Length + 10);
                ScrollPosition = GUI.BeginScrollView(new Rect(0, controlsY, 490, Math.Max(80, PreloadWindowRect.height - controlsY - 55)), ScrollPosition, scrollContentRect);

                var y = 0;
                foreach (BoneNode node in visibleNodes)
                {
                    int x = 10 + node.Depth * 20;
                    if (GUI.Button(new Rect(x + 25, y, contentWidth - x - 25, 22), node.GameObject.name, node.IsDynamicRoot ? dynamicRoot : node.IsDynamic ? dynamic : normal))
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
                GUIStyle goodPath = new GUIStyle(GUI.skin.textField);
                goodPath.normal.textColor = Color.green;
                goodPath.hover.textColor = Color.green;
                goodPath.focused.textColor = Color.green;

                GUIStyle badPath = new GUIStyle(goodPath);
                badPath.normal.textColor = Color.red;
                badPath.hover.textColor = Color.red;
                badPath.focused.textColor = Color.red;

                GUIStyle label = new GUIStyle(GUI.skin.label);
                label.alignment = TextAnchor.MiddleCenter;

                GUIStyle box = new GUIStyle(GUI.skin.box);
                box.alignment = TextAnchor.MiddleCenter;

                if (GUI.Button(new Rect(10, 50, 480, 25), Replacer ? L.Text("◀ Replace in all paths ▶") : L.Text("◀ Replace common or full Path: ▶"), GUI.skin.button))
                {
                    Replacer = !Replacer;
                }
                if (!Replacer)
                {
                    CommonPathText = GUI.TextField(new Rect(10, 75, 335, 25), CommonPathText, Directory.Exists(CommonPathText) ? goodPath : badPath);

                    if (!Directory.Exists(CommonPathText)) GUI.enabled = false;
                    if (GUI.Button(new Rect(350, 75, 480 - 350 - 70, 25), L.Text("Common")))
                    {
                        Main.currentLoadProcess.Import.CommonPath = CommonPathText;
                    }
                    if (GUI.Button(new Rect(350+70, 75, 480 - 350 - 70, 25), L.Text("Full")))
                    {
                        Main.currentLoadProcess.Import.ReplacePathInAllTextures(CommonPathText);
                    }
                    GUI.enabled = true;
                }
                else
                {
                    if (ReplaceString == null) ReplaceString = CommonPathText;
                    ReplaceString = GUI.TextField(new Rect(10, 75, 215, 25), ReplaceString, GUI.skin.textField);
                    if (GUI.Button(new Rect(230, 75, 40, 25), L.Text("with")))
                    {
                        foreach (Material mat in Main.currentLoadProcess.Import.MaterialTextures.Keys)
                        {
                            foreach (TexturePath tPath in Main.currentLoadProcess.Import.MaterialTextures[mat])
                            {
                                tPath.Path = tPath.Path.Replace(ReplaceString, ReplaceWith);
                            }
                        }
                    }
                    ReplaceWith = GUI.TextField(new Rect(275, 75, 215, 25), ReplaceWith, GUI.skin.textField);
                }

                Rect scrollContentRect = new Rect(0, 0, 470, 0);
                foreach (Material mat in Main.currentLoadProcess.Import.MaterialTextures.Keys)
                {
                    scrollContentRect.height += 30;
                    scrollContentRect.height += 30 * Main.currentLoadProcess.Import.MaterialTextures[mat].Count(tPath => tPath.Type == Assimp.TextureType.Diffuse || tPath.Type == Assimp.TextureType.Normals);
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
                        if (CheckButton(new Rect(10, y, 25, 25), tPath.Use, ""))
                        {
                            tPath.Use = !tPath.Use;
                        }
                        GUI.enabled = true;
                        GUI.Box(new Rect(35, y, 100, 25), "");
                        GUI.Label(new Rect(35, y, 100, 25), L.Text(tPath.Type.ToString()), label);
                        tPath.Path = GUI.TextField(
                            new Rect(135, y, 470 - 160, 25),
                            tPath.Path,
                            tPath.PathOkay() ? goodPath : badPath
                        );
                        if (!GUI.Button(new Rect(470 - 25, y, 25, 25), "...")) continue;
                        tPath.Path = tPath.Path.Replace("\\", "/");
                        string[] file = KKAPI.Utilities.OpenFileDialog.ShowDialog(L.Text("Select Texture"), Main.currentLoadProcess.Import.SourceIdentifier,
                            L.Text("Image files") + " (*.png; *.jpg; *.jpeg; *.tga; *.bmp; *.dds) |*.png; *.jpg; *.jpeg; *.tga; *.bmp; *.dds |" + L.Text("All files") + " (*.*)|*.*", "png", SingleFileFlags);
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
                    L.Text("Finish"))) return;
            if (KoikatuAPI.GetCurrentGameMode() == GameMode.Studio)
            {
                Main.asc.FinishLoadProcess(Main.currentLoadProcess);
                _mmddStatus = Main.currentLoadProcess.Import.LastMmddExportStatus;
            }
            else if (KoikatuAPI.GetCurrentGameMode() == GameMode.Maker)
                MakerAPI.GetCharacterControl().gameObject.GetComponentInChildren<AssetCharaController>()?.FinishLoadProcess(Main.currentLoadProcess);
        }
    }
}
