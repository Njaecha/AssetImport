# PMX guide

AssetImport reads PMX 2.0 directly and imports the result as a Studio item or accessory.

## Import

1. Extract the model with its companion texture directories intact.
2. Open **Left Alt + I**, choose the `.pmx` file, and select **PMX / VMD preset**. Start with **Scaling-factor: 1**.
3. Enable **Import Armature** for animation and **Import Blendshapes** for vertex expressions. Leave **Material per Renderer** off initially.
4. Inspect the preview, texture paths, and selected physics roots, then finish importing.
5. Wait for **Textures ready** before saving, copying, or switching coordinates.

The complete skeleton is imported. Selecting bones in the physics preview changes the DynamicBone roots, not which bones are imported. Avoid selecting the whole model, head, or waist as a physics root.

## VMD playback with MMDD 3

In Studio, open MMDD's **Load VMD Advanced**, select the imported item, and choose its **AssetImport PMX Auto** definition. The regular character wizard does not list item definitions.

Mappings use the original PMX bone names to match VMD tracks and the imported hierarchy to locate Unity transforms. Supported features include IK limits, fixed axes, non-local rotation/position grants, and vertex expressions. Physics chains and their dependants are excluded from animation synchronization where needed.

The AssetImport runtime extension evaluates each mapped bone's animation, then its append transform, then IK. Enabled IK runs even when its control bone has no VMD animation track; VMD IK switches still apply. Non-local rotation grants compose after the bone's own rotation, and position grants inherit displacement relative to the source's rest pose.

If the definition is missing, select the item and click **Repair selected PMX Auto** in AssetImport, then reopen MMDD's definition selector and bind the regenerated definition. The panel reports whether installation succeeded or only a backup was written.

After updating AssetImport, restart Studio and MMDD to load the updated runtime extension. Regenerate and rebind existing PMX Auto definitions when applying animation-mapping fixes.

AssetImport detects a unique MMDD library under `BepInEx/plugins`. For another location, set **PMX / MMDD Library Path** to the MMDD `Lib` directory and regenerate the mapping. Mapping and script backups are stored under `UserData/AssetImport/MMDD/`; additional `.report.txt` files are written only with **Debug** enabled.

## Physics

- **Auto PMX physics chains** selects usable chains, including supported skirt and accessory chains.
- **Enable physics after import** controls the initial enabled state.
- **PMX physics preset** applies initial settings for hair, skirts, and accessories.
- **Physics ON / OFF** changes the selected item's or accessory's physics state.
- **Reset physics motion** resets the simulation after seeking or repositioning.
- **Ground collision** adds a horizontal ground plane at the model origin. **Ground offset** raises or lowers it in model units; scaling the model also scales the offset. Rotating the model does not tilt the plane.
- **Downward force** adds a constant downward load. Start with `0.001`; set it to `0` to disable this additional load. This is a simulation-strength setting, not acceleration in metres per second squared. Existing gravity and force settings in physics editors remain additive.
- **Keep shape** retains elasticity and stiffness while the chain responds to load. Supported PMX joints also contribute angular limits and an approximate spring response.
- **Physics chains** opens per-chain settings. Search by root name and select one or several chains. With **Debug** enabled, **Show selected roots** marks their attachment points on the model.
- **Settle on load / reset** lets enabled chains settle after loading or resetting. **Settle physics** starts settling manually. Pause motion first; moving a chain's anchor stops its extra settling steps. Settling is bounded and may not reach equilibrium on every model.

New PMX imports use **Keep shape**, ground collision, downward force (`0.001`), and automatic settling. To change a model, select one Studio item or the accessory slot, edit **Selected model physics**, and click **Apply physics settings**. Settings persist in scenes, character cards, and coordinate cards, including copied objects and accessories. Assets saved without per-chain settings load in **Keep shape** mode; their ground, force, and settling settings are retained.

In **Physics chains**, choose a mode and click **Apply to selected chains**:

| Mode | Behavior |
| --- | --- |
| Model default | Follow the selected model's default mode. |
| Keep shape | Respond to load while retaining shape support and supported PMX angular constraints. |
| Free hanging | Release native shape attraction and PMX angular support for these chains. Lengths, damping, collisions, frozen axes, and physics weight remain active. |
| No simulation | Stop secondary motion on these chains and restore their input pose. |

In the **Motion** tab, **Added downward force** multiplies the model's additional load by `0–4`; `0` does not disable gravity supplied by another editor. **Shape retention** scales native elasticity and stiffness from `0–100%`; it does not loosen PMX hard angle limits. Lower it gradually for cloth that should sag further. Use **Free hanging** only for parts that should hang freely, since stiff ribbons and styled hair can collapse in that mode. **Reset motion settings** restores inheritance and both multipliers while preserving breast contact settings. Editor parameters and the original rest pose are not overwritten. Pause motion before adjusting or settling.

PMX stores rigid-body modes, masses, joint limits, and springs; it has no per-part “natural drape” flag. AssetImport reads these records and applies angular constraints where one rigid body per bone connects an ancestor to a descendant. Spring response uses mass and an approximate inertia. DynamicBone cannot reproduce joint translation, cross-linked rigid bodies, or full twist dynamics; ambiguous bindings retain native shape support and are reported in the log. Branches without a unique simulated direction cannot receive angular correction. Editor-frozen axes take priority, and contact corrections can relax angular limits. This remains an approximation of MMD physics, so static shapes and motion can differ. Reload existing models or scenes after upgrading to reconstruct joint bindings.

Free hanging and the shape-retention multiplier require compatible DynamicBone constraint hooks. If unavailable, native attraction remains active and the log reports the limitation. Settling advances the existing simulation; a chain with no load or strong shape support may move very little.

The ground belongs to each model and follows its origin height, rather than following animated feet. Set the offset if the model's origin is not at foot level. Ground collision acts on DynamicBone particles, not every mesh vertex; suitable chain roots and radii are still needed. It does not collide with stage geometry or prevent penetration when a fixed chain anchor is too far below ground. Disable downward force if a physics editor already supplies the desired load.

Static PMX body shapes can become DynamicBone colliders. PMX collision groups determine which chains use them. **Hair body collision assist** can add body collisions for hair excluded by those groups; it is optional and takes effect on a new import. **PMX / Enable Body Collisions** controls collider generation and requires reloading the model or scene after a change.

DynamicBone approximates secondary motion; it is not MMD's rigid-body/joint solver. Box colliders are approximated, and fast motion can still cause penetration. Adjust chain roots and parameters for the model through AssetImport or a compatible physics editor. Saved scenes retain their stored settings.

### Experimental breast contacts

For a selected model, enable **Breast contacts (experimental)** under **Selected model physics**, then click **Apply physics settings**. The option defaults to off and saves with the model. Turn it off and apply to remove the contact displacement. **Physics OFF** and per-chain **No simulation** also suppress contact motion for their associated chains.

This feature uses the game's `DynamicBone_Ver02` on private proxy bones. Ordinary imported physics continues running; only an additional, bounded positional offset is applied to recognized breast bones in a late `LateUpdate`, before mesh skinning. The offset is removed early in the next `Update` before animation and ordinary physics. Camera callbacks only observe the resulting pose and never advance the simulation. It does not require Colliders or a pose-editor plugin. Bone rotation and scale remain controlled by the existing animation and physics.

Automatic recognition requires weighted breast bones with matching names and PMX dynamic bodies. To override recognition, open **Physics chains**, select one or more chains, and switch to the **Breast contacts** tab:

| Setting | Effect |
| --- | --- |
| Automatic | Use the model's automatic breast recognition. This is the default for existing scenes. |
| Breast | Include weighted branch endpoints in the selected chain regardless of their names. Independently selected nested chains use their own setting. |
| Exclude | Remove the selected chain from breast contacts, including automatically recognized bones. Ordinary physics continues. |
| Sphere radius (%) | Scale each sphere's radius from 10% to 400%; 100% uses its original size. |
| Source volumes | Use PMX bodies, or bone-spacing spheres where bodies are missing. |
| Fit to mesh | Estimate each endpoint sphere from that endpoint's weighted mesh region. |
| Radius (0 = scaled) | Set an absolute radius in model units, up to 2, independent of the source size. Zero restores percentage scaling. |
| Center offset | Move each volume along its bone's local X/Y/Z axes, from -2 to 2 model units. |
| Sphere / Capsule | Choose the contact shape. Existing scenes keep spheres until changed. |
| Total length | Capsule length including both caps, from 0 to 4 model units. The effective length is at least twice the radius; at that minimum the shape is spherical. |
| Rotation (deg) | Rotate the capsule's local Y axis using X/Y/Z Euler angles, from -180 to 180 degrees. |
| Mirror across X | Reflect X offsets and Y/Z rotations for endpoints on the negative-X side of the original model. With both sides selected, negative X moves them inward and positive X moves them outward. |

Select just one chain to edit either side independently. Mirror side is determined from the fitted or source center in the original model coordinates; animation does not change it. For models whose origin is not between the two sides, leave mirroring off and adjust each chain separately. To extend contact coverage without widening it equally in all directions, choose **Capsule**, set its total length above the diameter, then rotate and position it using the preview.

Automatic and manual selection both use the last weighted target of each branch; intermediate targets do not receive contact volumes. PMX append followers use their driving bones to identify branch endpoints. Fitting changes an endpoint's volume without moving the contact target to the chain root.

When several weighted followers share an eligible endpoint driver, only that driver receives a contact volume and native spring state. Its added displacement propagates through supported PMX position grants in the same physics chain, at the model's authored rates. These followers share the driver's response and recovery; they do not run separate contact springs. Ordinary animation and physics remain the baseline. Rotation-only grants do not inherit position, local grants are unsupported, and propagation does not cross into independently configured nested chains. This is not a soft-body volume or a complete MMD deformation solver.

A capsule also uses one native spring per endpoint. Before each step, the adapter finds the closest point on its center segment to each allowed body obstacle and translates a private copy of that obstacle so the native spherical particle has the same sampled contact gap and normal. This provides a longer contact region with one shared positional response. It is a discrete approximation: closest points are refreshed between steps, not during every internal solver iteration, and fast or deep intersections can remain imperfect. Capsule orientation follows the bone and configured rotation; contacts do not add angular motion.

**Source volumes** and **Fit to mesh** reset size to 100%, absolute radius to zero, and center offsets to zero. Existing scenes retain saved sizes and offsets. For small endpoint spheres, select **Fit to mesh**, inspect the preview, then adjust size and center. Fitting uses the endpoint's original mesh bounds for vertex weights of at least 25%, with a radius equal to half the longest bound dimension. It is an approximate starting volume, not a skin-surface collision mesh. Missing or degenerate mesh regions retain their source volumes.

Enable **Debug** in the main panel to use the preview controls. **Show contact volumes** displays up to 16 selected spheres or capsules. **Draft (cyan)** includes unapplied edits; **Applied (green)** shows the geometry configured for the live contact layer. **Body obstacles** adds up to 64 eligible PMX body colliders in yellow. These displays do not advance simulation or establish that contacts are enabled; check the model's contact status. Scroll within the editor to reach the preview and apply controls. Click **Apply breast settings** to activate and save the edits with the model. **Reset breast settings** restores automatic recognition, source spheres, 100% radius, zero offsets/rotation, and mirroring off without changing motion settings. The model's **Breast contacts (experimental)** switch must also be enabled. All contact settings and simulation remain available with Debug off.

Manual selection uses dynamic PMX body geometry where available. In source-volume mode, a weighted bone without a suitable body receives a sphere centered on that bone, sized from its original parent spacing. Short bone spacing can make these spheres much smaller than the visible part; use mesh fitting or an absolute radius. Unweighted helper bones are not contact targets. Stable PMX body obstacles are still required. Reload models or scenes after upgrading to populate manual candidates and mesh bounds. An endpoint's maximum displacement is its radius, including in capsule mode; increasing length does not increase that bound. Radius, length and offsets follow model scale; positive uniform scale is required. This is not full MMD rigid-body physics, and some rigs may produce unsuitable contact motion.

Each endpoint uses its own obstacle list. Volumes carried by the target or its physics chain are excluded, including sibling deformation bones. Other volumes that already overlap the configured sphere or capsule in the original PMX pose are also excluded, preventing existing support overlap from continuously pushing the breast. Separate arms remain eligible when they start outside that volume in the original pose, even if touching when the feature is enabled. An oversized or misplaced volume can exclude an arm that overlaps it in the original pose; inspect the pair diagnostics when adjusting size, center or rotation. Enabling the option in a different pose does not change this filtering. Reload existing models or scenes after upgrading to rebuild reference geometry and append dependencies.

The panel reports eligible and active breasts. Activation first checks a hidden proxy at rest and against a temporary sphere; capsules with a nonzero center segment also check a temporary obstacle at one end. The model is not changed during these checks. An initialization, self-check, or update failure disables the contact layer and leaves ordinary physics active. To retry after correcting the model or settings, click **Apply physics settings**. Diagnostic lines start with `PMX native breast candidates`, `PMX native breast initialized`, `PMX native breast response`, or `PMX native breast adapter stopped`. A reported response establishes that the native proxy moved, not that visible mesh contact is correct.

### Debug mode

**Debug** is a single global setting, off by default. Toggle it in the main panel or through **_General_ / Debug** in the plugin configuration. Both controls use the same saved value; it is not part of scene or model data. Changes take effect without reloading a model or restarting its physics.

With Debug off, detailed logs, physics overlays, automatic/manual diagnostic exports, geometry sampling, and render-writeback observation are disabled. Import failures, missing resources, unsupported-feature warnings and native physics safety checks remain active. Disabling Debug stops pending and periodic writes; existing files are preserved and may describe an earlier session.

With Debug on, the plugin creates `UserData/AssetImport/Diagnostics/breast-contacts.txt`, even without an imported model. Model state changes and active contact summaries refresh this file at most once every five seconds. For missing or unexpected motion, reproduce the pose for at least 10 seconds, then click **Open diagnostics folder** at the top of the panel. **Save contact diagnostics** also refreshes the report immediately. Debug exposes the loaded build ID and report errors. If the game directory cannot be written, the report uses a temporary directory and the panel shows the error; the folder button opens the actual destination.

Debug also enables detailed import/physics logs, PMX mapping reports, and source-asset dumps on subsequent import/load operations. Dumps are placed under `UserData/AssetImport/` in scene/card-specific folders and may occupy substantial disk space. To collect import-time details, enable Debug before importing or reloading; **Repair selected PMX Auto** regenerates its mapping report. Turn Debug off when finished.

The report includes the loaded DLL path and build, game directory, applied and pending settings, recognition/rejection reasons, loaded models, chain state, native initialization stages recorded while Debug was on, last failure, update/write counters, displacement and sampled obstacle distances. Endpoint candidates show each weighted bone, its parent, driving bone, chain and selection result; `shared-driver` identifies followers merged into an eligible driver. `Position grant` records bound sources and rates; `Follower writeback` records inherited contact offsets. Per-endpoint pair decisions show excluded obstacles and both reference-pose and live-pose gaps. Raw displacement and bound-reset counts distinguish native motion from the bounded result applied to the model. It does not advance physics. Send `breast-contacts.txt` with the current session's `BepInEx/LogOutput.log` when reporting a problem. If the build label and folder button are missing, first enable Debug. If Debug itself is missing, verify the installed DLL and restart Studio. An old `output_log.txt` may not correspond to that session.

While Debug and contacts are enabled, `PMX breast diagnostics` lines record a summary every five seconds, including zero displacement or missing late updates. State changes are logged immediately. `Contact selection` records shape, effective length, radius and capsule half-axis; `Chain` records saved mirror, offset and rotation settings. `selfCheck.capsuleEndDisplacement` records the capsule end calibration. `Writeback` records input/output centers; `Contact geometry` compares gaps in the same obstacle pose at the indicated frame. `renderWriteError` and `followerRenderWriteError` measure subsequent changes to endpoint and follower writes observed by cameras. `Skin binding result` checks exact transform references in the model's skinned renderers; `NO_RENDERER_REFERENCE` means that probe cannot drive those renderers. These checks do not measure rendered pixels or prove visible contact. Surface gaps and overlap counts are geometric estimates; changed-after-write counts may also include manual edits.

## Textures and color conversion

PNG/JPEG are supported along with BMP, TGA, and common DDS formats. Supported DDS data includes BC1/2/3 and common RGB(A) layouts; BC7, HDR, cubemaps, arrays, and volume textures are not supported. Missing or unsupported textures are reported separately.

**Convert diffuse to KK color** applies a 3D `.cube` LUT to diffuse textures. Set **Textures / KK Color LUT** to the desired PS-to-KK LUT; the default path is `UserData/AssetImport/PS转KK色.cube`. LUT files are supplied separately. Conversion is disabled by default and leaves the original texture files unchanged.

Texture preparation runs in the background with bounded buffers; Unity and MaterialEditor updates run on the game thread. Large images still require decoding, encoding, and GPU upload. Replacing or deleting the target cancels pending work.

## Supported data and limitations

| Data | Support |
| --- | --- |
| PMX version | 2.0; 2.1 is rejected |
| Geometry | Material meshes, UVs, normals, vertex weights, bind pose, and full bone hierarchy |
| Skinning | Linear; SDEF is approximated |
| Morphs | Vertex morphs and groups containing only supported vertex morphs |
| Animation mapping | Supported IK, fixed axes, and non-local rotation/position grants |
| Physics | DynamicBone chains, supported static body colliders, and supported ancestor-linked joint angular constraints |
| Not implemented | Full rigid-body/joint simulation, soft bodies, local grants, bone/UV/material morphs, toon/sphere shading |

Invalid optional IK or cyclic grant/group-morph dependencies can be disabled with diagnostics while usable data is retained. Unsupported mixed morph groups are skipped as a whole. Necessary geometry corruption and cyclic parent hierarchies still fail validation. Check the log and mapping report when a model imports but a motion or expression differs from MMD.

Matching an IK target does not guarantee the same intermediate joint pose as MMD. Multi-axis IK limits, fixed-axis projection, and bone evaluation order can still produce differences. PMX operations marked for evaluation after rigid-body physics are not reproduced as a separate MMD physics stage.

## Troubleshooting

- **Dark colors:** check the material and lighting; if the model requires PS-to-KK conversion, enable the diffuse LUT option before importing.
- **Missing expressions:** enable blend shapes and inspect the mapping report for unsupported or cyclic morphs.
- **Unexpected foot motion:** confirm the matching model definition, scale, and physics roots. Report the affected VMD frame range with the mapping report.
- **Textures absent after saving:** wait for texture processing to complete and inspect missing-file diagnostics.

Include the build identifier and relevant `BepInEx/LogOutput.log` or `output_log.txt` when reporting a problem.
