using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace AssetImport
{
    internal partial class AssetUI
    {
        private bool _chainBreastTab;
        private int _chainBreastRole;
        private float _chainBreastRadiusScale = 1f;
        private string _chainBreastRadiusText = "100";
        private bool _chainBreastMeshVolume, _breastPreviewApplied, _breastShowObstacles;
        private float _chainBreastRadiusOverride;
        private Vector3 _chainBreastOffset;
        private string _breastAbsoluteText = "0", _breastXText = "0", _breastYText = "0", _breastZText = "0";
        private bool _chainBreastCapsule, _chainBreastMirror;
        private float _chainBreastLength;
        private Vector3 _chainBreastRotation;
        private Vector2 _breastEditorScroll;
        private string _breastLengthText = "0", _breastRotXText = "0", _breastRotYText = "0", _breastRotZText = "0";

        private static string BreastRoleName(int role)
        { return role == 1 ? L.Text("Breast") : role == 2 ? L.Text("Exclude") : L.Text("Automatic"); }

        private void LoadChainBreastSettings(PhysicsChainSettings settings)
        {
            var clean = settings == null ? new PhysicsChainSettings() : settings.Copy();
            _chainBreastRole = clean.BreastRole; _chainBreastRadiusScale = clean.BreastRadiusScale;
            _chainBreastRadiusText = (100f * _chainBreastRadiusScale).ToString("0.##", CultureInfo.InvariantCulture);
            _chainBreastMeshVolume = clean.BreastMeshVolume; _chainBreastRadiusOverride = clean.BreastRadiusOverride;
            _chainBreastOffset = new Vector3(clean.BreastOffsetX, clean.BreastOffsetY, clean.BreastOffsetZ);
            _breastAbsoluteText = clean.BreastRadiusOverride.ToString("0.####", CultureInfo.InvariantCulture);
            _breastXText = clean.BreastOffsetX.ToString("0.####", CultureInfo.InvariantCulture);
            _breastYText = clean.BreastOffsetY.ToString("0.####", CultureInfo.InvariantCulture);
            _breastZText = clean.BreastOffsetZ.ToString("0.####", CultureInfo.InvariantCulture);
            _chainBreastCapsule = clean.BreastCapsule; _chainBreastMirror = clean.BreastMirrorX;
            _chainBreastLength = clean.BreastLength;
            _chainBreastRotation = new Vector3(clean.BreastRotationX, clean.BreastRotationY, clean.BreastRotationZ);
            _breastLengthText = clean.BreastLength.ToString("0.####", CultureInfo.InvariantCulture);
            _breastRotXText = clean.BreastRotationX.ToString("0.##", CultureInfo.InvariantCulture);
            _breastRotYText = clean.BreastRotationY.ToString("0.##", CultureInfo.InvariantCulture);
            _breastRotZText = clean.BreastRotationZ.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void WriteBreastFields(PhysicsChainSettings settings)
        {
            settings.BreastRole = _chainBreastRole; settings.BreastRadiusScale = _chainBreastRadiusScale;
            settings.BreastMeshVolume = _chainBreastMeshVolume; settings.BreastRadiusOverride = _chainBreastRadiusOverride;
            settings.BreastOffsetX = _chainBreastOffset.x; settings.BreastOffsetY = _chainBreastOffset.y; settings.BreastOffsetZ = _chainBreastOffset.z;
            settings.BreastCapsule = _chainBreastCapsule; settings.BreastLength = _chainBreastLength; settings.BreastMirrorX = _chainBreastMirror;
            settings.BreastRotationX = _chainBreastRotation.x; settings.BreastRotationY = _chainBreastRotation.y; settings.BreastRotationZ = _chainBreastRotation.z;
        }

        private NativeContactShape[] PreviewBreastSettings(NativeContactController contacts)
        {
            var preview = _environmentDraft.Copy();
            foreach (var root in _chainSelection)
            {
                WriteBreastFields(preview.EditRoot(root));
            }
            return contacts.Preview(preview).Where(s => _chainSelection.Contains(s.ChainRoot)).ToArray();
        }

        private NativeContactShape[] DisplayedBreastShapes(NativeContactController contacts)
        {
            return AssetDebug.Enabled && _breastPreviewApplied ? contacts.ContactShapes.Where(s => _chainSelection.Contains(s.ChainRoot)).ToArray()
                : PreviewBreastSettings(contacts);
        }

        private void DrawChainBreastEditor(int y)
        {
            _breastEditorScroll = GUI.BeginScrollView(new Rect(0, y + 29, 638, _chainPhysicsRect.height - y - 35),
                _breastEditorScroll, new Rect(0, 0, 610, 380));
            DrawBreastVolumeFields();
            GUI.EndScrollView();
        }

        private void DrawBreastVolumeFields()
        {
            var contacts = _environment.GetComponent<NativeContactController>();
            bool editable = contacts && _chainSelection.Count > 0;
            GUI.enabled = editable;
            for (int role = 0; role < 3; role++)
                if (CheckButton(new Rect(12 + role * 200, 0, 192, 25), _chainBreastRole == role, BreastRoleName(role))) _chainBreastRole = role;
            if (CheckButton(new Rect(12, 30, 190, 25), !_chainBreastMeshVolume, L.Text("Source volumes"))) ResetBreastVolume(false);
            if (CheckButton(new Rect(212, 30, 190, 25), _chainBreastMeshVolume, L.Text("Fit to mesh"))) ResetBreastVolume(true);
            GUI.Label(new Rect(410, 32, 195, 22), L.Text("Resets size and center"));
            if (CheckButton(new Rect(12, 60, 100, 25), !_chainBreastCapsule, L.Text("Sphere"))) _chainBreastCapsule = false;
            if (CheckButton(new Rect(120, 60, 110, 25), _chainBreastCapsule, L.Text("Capsule"))) _chainBreastCapsule = true;
            GUI.enabled = editable && _chainBreastCapsule;
            GUI.Label(new Rect(245, 62, 110, 22), L.Text("Total length"));
            _breastLengthText = GUI.TextField(new Rect(360, 60, 100, 23), _breastLengthText);
            GUI.Label(new Rect(470, 62, 135, 22), L.Text("Min: 2 x radius"));
            GUI.enabled = editable;
            GUI.Label(new Rect(12, 92, 160, 22), L.Text("Radius (%)"));
            GUI.enabled = editable && _chainBreastRadiusOverride == 0;
            float value = GUI.HorizontalSlider(new Rect(180, 100, 315, 20), _chainBreastRadiusScale, .1f, 4f);
            if (value != _chainBreastRadiusScale)
            { _chainBreastRadiusScale = value; _chainBreastRadiusText = (value * 100f).ToString("0.##", CultureInfo.InvariantCulture); }
            _chainBreastRadiusText = GUI.TextField(new Rect(510, 92, 95, 23), _chainBreastRadiusText);
            float percent;
            bool valid = ReadContactValue(_chainBreastRadiusText, 10, 400, out percent);
            if (valid) _chainBreastRadiusScale = percent / 100f;
            GUI.enabled = editable;
            GUI.Label(new Rect(12, 122, 185, 22), L.Text("Radius (0 = scaled)"));
            _breastAbsoluteText = GUI.TextField(new Rect(200, 120, 90, 23), _breastAbsoluteText);
            GUI.Label(new Rect(302, 122, 300, 22), L.Text("Model units; 0 uses percentage"));
            DrawContactVectorFields(150, L.Text("Center offset"), ref _breastXText, ref _breastYText, ref _breastZText);
            GUI.enabled = editable && _chainBreastCapsule;
            DrawContactVectorFields(180, L.Text("Rotation (deg)"), ref _breastRotXText, ref _breastRotYText, ref _breastRotZText);
            GUI.enabled = editable;
            if (CheckButton(new Rect(12, 210, 158, 25), _chainBreastMirror, L.Text("Mirror across X"))) _chainBreastMirror = !_chainBreastMirror;
            GUI.Label(new Rect(180, 208, 425, 32), L.Text("When mirrored: negative X moves inward, positive outward; Y/Z rotations reflect."), new GUIStyle(GUI.skin.label) { wordWrap = true });
            float absolute, x, z, offsetY, length, rx, ry, rz;
            bool volumeValid = ReadContactValue(_breastAbsoluteText, 0, 2, out absolute) &
                ReadContactValue(_breastXText, -2, 2, out x) & ReadContactValue(_breastYText, -2, 2, out offsetY) &
                ReadContactValue(_breastZText, -2, 2, out z);
            bool capsuleValid = ReadContactValue(_breastLengthText, 0, 4, out length) &
                ReadContactValue(_breastRotXText, -180, 180, out rx) & ReadContactValue(_breastRotYText, -180, 180, out ry) &
                ReadContactValue(_breastRotZText, -180, 180, out rz);
            if (volumeValid) { _chainBreastRadiusOverride = absolute; _chainBreastOffset = new Vector3(x, offsetY, z); }
            if (capsuleValid) { _chainBreastLength = length; _chainBreastRotation = new Vector3(rx, ry, rz); }
            valid &= volumeValid && (!_chainBreastCapsule || capsuleValid);
            GUI.enabled = true;
            if (AssetDebug.Enabled)
            {
                if (CheckButton(new Rect(12, 244, 190, 25), !_breastPreviewApplied, L.Text("Draft (cyan)"))) _breastPreviewApplied = false;
                if (CheckButton(new Rect(212, 244, 190, 25), _breastPreviewApplied, L.Text("Applied (green)"))) _breastPreviewApplied = true;
                if (CheckButton(new Rect(412, 244, 190, 25), _breastShowObstacles, L.Text("Body obstacles"))) _breastShowObstacles = !_breastShowObstacles;
            }
            else GUI.Label(new Rect(12, 244, 590, 25), L.Text("Enable Debug in the main panel to preview contact volumes."));
            string summary = L.Text("Select one or more physics chains.");
            if (contacts && _chainSelection.Count > 0)
            {
                var shapes = DisplayedBreastShapes(contacts);
                summary = L.Format("{0} endpoint(s), radius {1}", shapes.Length, shapes.Length == 0 ? "--" :
                    shapes.Min(s => s.Radius).ToString("0.####", CultureInfo.InvariantCulture) + " - " + shapes.Max(s => s.Radius).ToString("0.####", CultureInfo.InvariantCulture)) +
                    (AssetDebug.Enabled && _breastPreviewApplied ? (_environment.Settings.NativeBreastContacts ? L.Text(" (applied)") : L.Text(" (contacts OFF)")) : L.Text(" (draft; click Apply)"));
            }
            GUI.Label(new Rect(12, 274, 590, 23), summary);
            GUI.enabled = editable && valid;
            if (GUI.Button(new Rect(12, 302, 218, 27), L.Text("Apply breast settings")))
            {
                foreach (string root in _chainSelection) WriteBreastFields(_environmentDraft.EditRoot(root));
                ApplyPhysicsDraft();
            }
            GUI.enabled = editable;
            if (GUI.Button(new Rect(240, 302, 185, 27), L.Text("Reset breast settings")))
            {
                LoadChainBreastSettings(null);
                foreach (string root in _chainSelection) WriteBreastFields(_environmentDraft.EditRoot(root));
                ApplyPhysicsDraft();
            }
            GUI.enabled = true;
            string help = !contacts ? L.Text("Breast contacts require an imported PMX model.") : !valid ? L.Text("Radius: 0-2. Length: 0-4. Offset: -2 to 2. Rotation: -180 to 180.") :
                !_environmentDraft.NativeBreastContacts ? L.Text("Enable Breast contacts in Selected model physics to use these settings.") :
                L.Text("Select one chain to adjust each side separately. Capsule length includes both caps. Apply activates edits.");
            GUI.Label(new Rect(12, 335, 590, 42), help, new GUIStyle(GUI.skin.label) { wordWrap = true });
        }

        private static void DrawContactVectorFields(int y, string label, ref string x, ref string offsetY, ref string z)
        {
            GUI.Label(new Rect(12, y + 2, 120, 22), label);
            GUI.Label(new Rect(135, y + 2, 20, 22), "X"); x = GUI.TextField(new Rect(155, y, 110, 23), x);
            GUI.Label(new Rect(300, y + 2, 20, 22), "Y"); offsetY = GUI.TextField(new Rect(320, y, 110, 23), offsetY);
            GUI.Label(new Rect(465, y + 2, 20, 22), "Z"); z = GUI.TextField(new Rect(485, y, 120, 23), z);
        }

        private void ResetBreastVolume(bool mesh)
        {
            int role = _chainBreastRole;
            LoadChainBreastSettings(new PhysicsChainSettings { BreastRole = role, BreastMeshVolume = mesh,
                BreastCapsule = _chainBreastCapsule, BreastLength = _chainBreastLength, BreastMirrorX = _chainBreastMirror,
                BreastRotationX = _chainBreastRotation.x, BreastRotationY = _chainBreastRotation.y, BreastRotationZ = _chainBreastRotation.z });
            _breastPreviewApplied = false;
        }
        private static bool ReadContactValue(string text, float min, float max, out float value)
        { return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && PhysicsEnvironmentMath.Finite(value) && value >= min && value <= max; }

        private void DrawBreastContactPreview()
        {
            if (!AssetDebug.Enabled || Event.current.type != EventType.Repaint || _chainSelection.Count == 0) return;
            var contacts = _environment.GetComponent<NativeContactController>(); var camera = Camera.main;
            if (!contacts || !camera) return;
            Color old = GUI.color;
            try
            {
                var shapes = DisplayedBreastShapes(contacts);
                GUI.color = _breastPreviewApplied ? Color.green : Color.cyan;
                foreach (var shape in shapes.Take(16))
                {
                    if (!shape.Bone) continue;
                    var center = shape.Bone.TransformPoint(shape.Center);
                    float radius = shape.Radius * Math.Abs(shape.Bone.lossyScale.x);
                    DrawContactVolume(camera, center, radius, shape.Bone.rotation * NativeContactGeometry.ContactHalfAxis(shape) * Math.Abs(shape.Bone.lossyScale.x));
                }
                if (_breastShowObstacles && contacts.Definition != null)
                {
                    GUI.color = Color.yellow;
                    foreach (var body in contacts.Definition.Obstacles.Where(o => shapes.Any(s => NativeContactPairPolicy.Exclusion(s, o) == null)).Take(64))
                    {
                        if (!body.Bone) continue;
                        var center = body.Bone.TransformPoint(body.Center);
                        float scale = Math.Abs(body.Bone.lossyScale.x), radius = body.Radius * scale;
                        var half = NativeContactGeometry.HalfAxis(body.Radius, body.Height, body.Direction, body.Bone.rotation * body.Rotation) * scale;
                        DrawContactVolume(camera, center, radius, half);
                    }
                }
            }
            finally { GUI.color = old; }
        }
        private static void DrawContactVolume(Camera camera, Vector3 center, float radius, Vector3 half)
        {
            if (half.sqrMagnitude <= 1e-12f) { DrawContactSphere(camera, center, radius); return; }
            var axis = half.normalized;
            var side = Vector3.Cross(axis, Math.Abs(axis.y) < .8f ? Vector3.up : Vector3.right).normalized * radius;
            var other = Vector3.Cross(axis, side);
            foreach (var v in new[] { side, -side, other, -other })
                DrawContactArc(camera, center + half + v, center - half + v);
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI / 12, b = (i + 1) * Mathf.PI / 12;
                var va = side * Mathf.Cos(a) + other * Mathf.Sin(a);
                var vb = side * Mathf.Cos(b) + other * Mathf.Sin(b);
                DrawContactArc(camera, center + half + va, center + half + vb);
                DrawContactArc(camera, center - half + va, center - half + vb);
                if (i >= 12) continue;
                for (int plane = 0; plane < 2; plane++)
                {
                    var radial = plane == 0 ? side : other;
                    var ra = radial * Mathf.Cos(a); var rb = radial * Mathf.Cos(b);
                    var aa = axis * (radius * Mathf.Sin(a)); var ab = axis * (radius * Mathf.Sin(b));
                    DrawContactArc(camera, center + half + ra + aa, center + half + rb + ab);
                    DrawContactArc(camera, center - half + ra - aa, center - half + rb - ab);
                }
            }
        }

        private static void DrawContactSphere(Camera camera, Vector3 center, float radius)
        {
            for (int axis = 0; axis < 3; axis++)
                for (int i = 0; i < 24; i++)
                {
                    float a = i * 2f * Mathf.PI / 24, b = (i + 1) * 2f * Mathf.PI / 24;
                    DrawContactArc(camera, center + CirclePoint(axis, a) * radius, center + CirclePoint(axis, b) * radius);
                }
        }
        private static Vector3 CirclePoint(int axis, float angle)
        {
            float a = Mathf.Cos(angle), b = Mathf.Sin(angle);
            return axis == 0 ? new Vector3(0, a, b) : axis == 1 ? new Vector3(a, 0, b) : new Vector3(a, b, 0);
        }
        private static void DrawContactArc(Camera camera, Vector3 a, Vector3 b)
        {
            a = camera.WorldToScreenPoint(a); b = camera.WorldToScreenPoint(b);
            if (a.z <= 0 || b.z <= 0) return;
            var start = new Vector2(a.x, Screen.height - a.y); var delta = new Vector2(b.x - a.x, a.y - b.y);
            var matrix = GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
                GUI.DrawTexture(new Rect(start.x, start.y, delta.magnitude, 1), Texture2D.whiteTexture);
            }
            finally { GUI.matrix = matrix; }
        }
    }
}
