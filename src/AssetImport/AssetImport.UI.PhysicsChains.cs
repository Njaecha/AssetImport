using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetImport
{
    internal partial class AssetUI
    {
        private bool _chainPhysicsOpen, _chainMarkers = true;
        private Rect _chainPhysicsRect = new Rect(780, 80, 640, 560);
        private Vector2 _chainPhysicsScroll;
        private readonly HashSet<string> _chainSelection = new HashSet<string>(StringComparer.Ordinal);
        private string _chainSearch = "";
        private int _chainEditMode;
        private float _chainForceScale = 1f, _chainShapeRetention = 1f;

        private static string PhysicsModeName(int mode)
        {
            switch ((PhysicsChainMode)mode)
            {
                case PhysicsChainMode.KeepShape: return L.Text("Keep shape");
                case PhysicsChainMode.FreeHanging: return L.Text("Free hanging");
                case PhysicsChainMode.Animation: return L.Text("No simulation");
                default: return L.Text("Model default");
            }
        }

        private void DrawChainPhysicsWindow()
        {
            if (!_chainPhysicsOpen || !_environment || _environmentDraft == null || PreloadUI) return;
            if (AssetDebug.Enabled && _chainMarkers) { DrawChainMarkers(); if (_chainBreastTab) DrawBreastContactPreview(); }
            _chainPhysicsRect.height = Math.Min(560, Math.Max(430, Screen.height - 50));
            _chainPhysicsRect.x = Mathf.Clamp(_chainPhysicsRect.x, 0, Math.Max(0, Screen.width - _chainPhysicsRect.width));
            _chainPhysicsRect.y = Mathf.Clamp(_chainPhysicsRect.y, 0, Math.Max(0, Screen.height - _chainPhysicsRect.height));
            _chainPhysicsRect = GUI.Window(33363, _chainPhysicsRect, ChainPhysicsWindow, L.Text("Physics chains"));
            KKAPI.Utilities.IMGUIUtils.EatInputInRect(_chainPhysicsRect);
        }

        private void ChainPhysicsWindow(int id)
        {
            if (GUI.Button(new Rect(610, 1, 25, 20), "X")) _chainPhysicsOpen = false;
            GUI.Label(new Rect(12, 30, 55, 22), L.Text("Search"));
            _chainSearch = GUI.TextField(new Rect(68, 30, 326, 22), _chainSearch);
            if (AssetDebug.Enabled && CheckButton(new Rect(408, 28, 220, 25), _chainMarkers, _chainBreastTab ? L.Text("Show contact volumes") : L.Text("Show selected roots"))) _chainMarkers = !_chainMarkers;
            var visible = _environment.Chains.Where(c => c && c.m_Root &&
                c.m_Root.name.IndexOf(_chainSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (GUI.Button(new Rect(12, 60, 120, 24), L.Text("Select visible")))
            { foreach (var chain in visible) _chainSelection.Add(chain.m_Root.name); LoadSingleChainSettings(); }
            if (GUI.Button(new Rect(140, 60, 95, 24), L.Text("Clear"))) _chainSelection.Clear();
            GUI.Label(new Rect(250, 62, 370, 22), L.Format("{0} selected / {1} chains", _chainSelection.Count, _environment.Chains.Length));
            float bottom = _chainPhysicsRect.height - (_chainBreastTab ? 315 : 193);
            _chainPhysicsScroll = GUI.BeginScrollView(new Rect(10, 91, 620, bottom - 91), _chainPhysicsScroll,
                new Rect(0, 0, 600, Math.Max(bottom - 91, visible.Length * 29)));
            for (int i = 0; i < visible.Length; i++)
            {
                var chain = visible[i]; string root = chain.m_Root.name;
                var settings = _environmentDraft.ForRoot(root);
                string mode = PhysicsModeName(settings == null ? 0 : settings.Mode);
                if (CheckButton(new Rect(0, i * 29, 407, 26), _chainSelection.Contains(root), root))
                {
                    if (!_chainSelection.Remove(root)) _chainSelection.Add(root);
                    LoadSingleChainSettings();
                }
                string contactRole = BreastRoleName(settings == null ? 0 : settings.BreastRole);
                GUI.Label(new Rect(414, i * 29 + 2, 182, 24), new GUIContent(_chainBreastTab ? contactRole : mode,
                    L.Format("PMX joint bindings: {0}; Breast contacts: {1}", _environment.JointCount(chain), contactRole)));
            }
            GUI.EndScrollView();
            int y = (int)bottom + 6;
            if (CheckButton(new Rect(12, y, 190, 24), !_chainBreastTab, L.Text("Motion"))) _chainBreastTab = false;
            if (CheckButton(new Rect(210, y, 200, 24), _chainBreastTab, L.Text("Breast contacts"))) _chainBreastTab = true;
            if (_chainBreastTab)
            {
                DrawChainBreastEditor(y); GUI.enabled = true; GUI.DragWindow(new Rect(0, 0, 606, 22)); return;
            }
            GUI.enabled = _chainSelection.Count > 0;
            for (int mode = 0; mode < 4; mode++)
                if (CheckButton(new Rect(12 + mode * 156, y + 25, 150, 25), _chainEditMode == mode, PhysicsModeName(mode))) _chainEditMode = mode;
            GUI.Label(new Rect(12, y + 57, 230, 22), L.Format("Added downward force: {0:0.00}x", _chainForceScale));
            _chainForceScale = GUI.HorizontalSlider(new Rect(250, y + 63, 375, 20), _chainForceScale, 0, 4);
            GUI.Label(new Rect(12, y + 85, 230, 22), L.Format("Shape retention: {0:0}%", _chainShapeRetention * 100));
            GUI.enabled = _chainSelection.Count > 0 && NaturalDrapePatch.Available &&
                _chainEditMode != (int)PhysicsChainMode.FreeHanging && _chainEditMode != (int)PhysicsChainMode.Animation;
            _chainShapeRetention = GUI.HorizontalSlider(new Rect(250, y + 91, 375, 20), _chainShapeRetention, 0, 1);
            GUI.enabled = _chainSelection.Count > 0;
            if (GUI.Button(new Rect(12, y + 116, 218, 27), L.Text("Apply to selected chains")))
            {
                foreach (string root in _chainSelection)
                {
                    var settings = _environmentDraft.EditRoot(root);
                    settings.Mode = _chainEditMode; settings.ForceScale = _chainForceScale; settings.ShapeRetention = _chainShapeRetention;
                }
                ApplyPhysicsDraft();
            }
            if (GUI.Button(new Rect(240, y + 116, 170, 27), L.Text("Reset motion settings")))
            {
                foreach (string root in _chainSelection)
                { var settings = _environmentDraft.EditRoot(root); settings.Mode = 0; settings.ForceScale = settings.ShapeRetention = 1f; }
                _chainEditMode = 0; _chainForceScale = _chainShapeRetention = 1; ApplyPhysicsDraft();
            }
            GUI.enabled = true;
            string help = NaturalDrapePatch.Available
                ? L.Text("Keep shape uses elasticity and supported PMX angular limits. Free hanging releases both. Pause motion before adjusting.")
                : L.Text("Free hanging and shape adjustment are unavailable with this physics runtime. Native shape retention remains active. Check the log.");
            GUI.Label(new Rect(12, y + 149, 615, 36), help, new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUI.DragWindow(new Rect(0, 0, 606, 22));
        }

        private void LoadSingleChainSettings()
        {
            if (_chainSelection.Count != 1) return;
            var settings = _environmentDraft.ForRoot(_chainSelection.First()) ?? new PhysicsChainSettings();
            _chainEditMode = settings.Mode; _chainForceScale = settings.ForceScale; _chainShapeRetention = settings.ShapeRetention;
            LoadChainBreastSettings(settings);
        }

        private void DrawChainMarkers()
        {
            var camera = Camera.main; if (!camera) return;
            Color previous = GUI.color;
            try
            {
                int shown = 0;
                foreach (var chain in _environment.Chains)
                {
                    if (!chain || !chain.m_Root || !_chainSelection.Contains(chain.m_Root.name)) continue;
                    var point = camera.WorldToScreenPoint(chain.m_Root.position); if (point.z <= 0) continue;
                    GUI.color = Color.yellow;
                    GUI.DrawTexture(new Rect(point.x - 4, Screen.height - point.y - 4, 8, 8), Texture2D.whiteTexture);
                    if (shown++ < 8) GUI.Label(new Rect(point.x + 8, Screen.height - point.y - 12, 280, 24), chain.m_Root.name);
                }
            }
            finally { GUI.color = previous; }
        }
    }
}
