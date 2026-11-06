using System.Globalization;
using UnityEngine;

namespace AssetImport
{
    internal partial class AssetUI
    {
        private Asset _environmentAsset;
        private AssetPhysicsEnvironment _environment;
        private PhysicsEnvironmentSettings _environmentDraft;
        private string _floorOffsetText, _downwardForceText;

        private void DrawPhysicsEnvironment(ref int y, AssetPhysicsEnvironment environment, Asset asset)
        {
            if (!environment || asset == null || environment.Chains == null || environment.Chains.Length == 0)
            { _environmentAsset = null; _environment = null; _chainPhysicsOpen = false; return; }
            if (_environmentAsset != asset || _environment != environment)
            {
                _environmentAsset = asset; _environment = environment;
                _environmentDraft = environment.Settings.Copy();
                _floorOffsetText = _environmentDraft.FloorOffset.ToString(CultureInfo.InvariantCulture);
                _downwardForceText = _environmentDraft.DownwardForce.ToString(CultureInfo.InvariantCulture);
                _chainSelection.Clear(); _chainPhysicsOpen = false;
            }
            GUI.enabled = !PreloadUI;
            GUI.Label(new Rect(10, y += 8, 220, 22), L.Text("Selected model physics"));
            var contacts = environment.GetComponent<NativeContactController>();
            if (contacts)
            {
                if (CheckButton(new Rect(10, y += 25, 220, 25), _environmentDraft.NativeBreastContacts, L.Text("Breast contacts (experimental)")))
                    _environmentDraft.NativeBreastContacts = !_environmentDraft.NativeBreastContacts;
                var contactStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                string contactStatus = contacts.Status;
                if (AssetDebug.Enabled) contactStatus += "\n" + contacts.DiagnosticSummary;
                if (_environmentDraft.NativeBreastContacts != environment.Settings.NativeBreastContacts)
                    contactStatus += L.Text("\nPending change: click Apply physics settings.");
                float contactHeight = contactStyle.CalcHeight(new GUIContent(contactStatus), 220);
                GUI.Label(new Rect(10, y += 27, 220, contactHeight), contactStatus, contactStyle);
                y += (int)contactHeight - 25;
            }
            if (CheckButton(new Rect(10, y += 25, 220, 25), _environmentDraft.Floor, L.Text("Ground collision")))
                _environmentDraft.Floor = !_environmentDraft.Floor;
            GUI.Label(new Rect(10, y += 28, 140, 22), L.Text("Ground offset"));
            _floorOffsetText = GUI.TextField(new Rect(153, y, 77, 22), _floorOffsetText);
            GUI.Label(new Rect(10, y += 25, 140, 22), L.Text("Downward force"));
            _downwardForceText = GUI.TextField(new Rect(153, y, 77, 22), _downwardForceText);
            if (GUI.Button(new Rect(10, y += 25, 220, 25), L.Format("Default: {0}", PhysicsModeName(_environmentDraft.DefaultMode))))
                _environmentDraft.NaturalDrape = !_environmentDraft.NaturalDrape;
            if (GUI.Button(new Rect(10, y += 28, 220, 25), L.Format("Physics chains ({0})...", environment.Chains.Length)))
                _chainPhysicsOpen = !_chainPhysicsOpen;
            if (CheckButton(new Rect(10, y += 25, 220, 25), _environmentDraft.Settle, L.Text("Settle on load / reset")))
                _environmentDraft.Settle = !_environmentDraft.Settle;
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
            string help = L.Text("Ground offset is relative to the model origin. Force: 0 to 0.02 (0 = off). Pause motion before settling.");
            if (_environmentDraft.NaturalDrape)
                help += NaturalDrapePatch.Available ? L.Text(" Free hanging removes shape retention and PMX angular support.")
                    : L.Text(" Free hanging is unavailable with this physics runtime. Check the log.");
            float height = style.CalcHeight(new GUIContent(help), 220);
            GUI.Label(new Rect(10, y += 28, 220, height), help, style);
            y += (int)height;
            if (GUI.Button(new Rect(10, y += 4, 220, 25), L.Text("Apply physics settings")))
                ApplyPhysicsDraft();
            if (GUI.Button(new Rect(10, y += 30, 220, 25), environment.IsSettling ? L.Text("Restart settling") : L.Text("Settle physics")))
                _physicsActionStatus = environment.RequestSettle() > 0 ? L.Message("Settling physics.") : L.Message("Enable physics before settling.");
            y += 25;
            if (environment.IsSettling)
            {
                GUI.Label(new Rect(10, y += 4, 220, 22), L.Text("Settling physics..."));
                y += 22;
            }
            GUI.enabled = true;
        }

        private void ApplyPhysicsDraft()
        {
            if (!_environment || _environmentAsset == null) return;
            float offset, force;
            if (!float.TryParse(_floorOffsetText, NumberStyles.Float, CultureInfo.InvariantCulture, out offset) ||
                !float.TryParse(_downwardForceText, NumberStyles.Float, CultureInfo.InvariantCulture, out force) ||
                !PhysicsEnvironmentMath.Finite(offset) || !PhysicsEnvironmentMath.Finite(force) ||
                offset < -100f || offset > 100f || force < 0f || force > .02f)
                _physicsActionStatus = L.Message("Enter ground offset from -100 to 100 and force from 0 to 0.02.");
            else
            {
                _environmentDraft.FloorOffset = offset; _environmentDraft.DownwardForce = force;
                _environment.Apply(_environmentDraft);
                var contacts = _environment.GetComponent<NativeContactController>();
                if (contacts) { contacts.ResetContacts(); contacts.ReportSettingsApplied(); }
                PhysicsEnvironmentPersistence.Write(_environmentAsset, _environmentDraft);
                _physicsActionStatus = L.Message("Physics settings applied.");
            }
        }
    }
}
