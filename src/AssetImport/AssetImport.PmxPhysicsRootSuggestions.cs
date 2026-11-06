using System;
using System.Collections.Generic;
using System.Text;

namespace AssetImport
{
    internal sealed class PmxPhysicsRootCandidate
    {
        internal int Bone, DynamicBoneCount;
        internal string Name, Reason;
        internal bool IsHair, IsUsable;
    }

    // Suggestions only: applying or merging these roots is an explicit UI action.
    // PMX has no standard "hair" tag. Body modes and hierarchy identify physics
    // roots; names supply conservative evidence about which roots belong to hair.
    internal sealed class PmxPhysicsRootSuggestions
    {
        internal int[] HairRoots = new int[0], PhysicsRoots = new int[0];
        internal int SkippedRootCount, IgnoredBodyCount;
        internal readonly List<PmxPhysicsRootCandidate> Candidates = new List<PmxPhysicsRootCandidate>();
        internal readonly List<string> Diagnostics = new List<string>();

        internal static PmxPhysicsRootSuggestions Build(PmxModel model)
        {
            if (model == null) throw new ArgumentNullException("model");
            var result = new PmxPhysicsRootSuggestions();
            var bones = model.Bones ?? new PmxModel.Bone[0];
            var bodies = model.RigidBodyData ?? new PmxModel.RigidBody[0];
            var dynamic = new bool[bones.Length];
            var bodyNames = new List<string>[bones.Length];
            int ignored = 0;
            foreach (var body in bodies)
            {
                if (body == null || (body.Mode != 1 && body.Mode != 2)) continue;
                if (body.Bone < 0 || body.Bone >= bones.Length || bones[body.Bone] == null)
                { ignored++; continue; }
                dynamic[body.Bone] = true;
                if (bodyNames[body.Bone] == null) bodyNames[body.Bone] = new List<string>();
                bodyNames[body.Bone].Add(body.Name ?? "");
            }
            var actionBones = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                actionBones[i] |= ProtectedActionBone(bones[i]);
                // Accessory chains may also have their own IK (HAKU's numeric
                // tail chain does). A generic IK must not override explicit
                // physics metadata. Standard action IK still protects its
                // target/links even if their individual names are unusual.
                bool actionIk = ProtectedSkeleton(bones[i].Name), hasPhysics = false;
                int target = bones[i].IkTarget;
                if (ValidBone(target, bones))
                { actionIk |= ProtectedSkeleton(bones[target].Name); hasPhysics |= dynamic[target]; }
                if (bones[i].IkLinks != null)
                    foreach (var link in bones[i].IkLinks)
                        if (link != null && ValidBone(link.Bone, bones))
                        { actionIk |= ProtectedSkeleton(bones[link.Bone].Name); hasPhysics |= dynamic[link.Bone]; }
                if (actionIk || !hasPhysics)
                {
                    if (ValidBone(target, bones)) actionBones[target] = true;
                    if (bones[i].IkLinks != null)
                        foreach (var link in bones[i].IkLinks)
                            if (link != null && ValidBone(link.Bone, bones)) actionBones[link.Bone] = true;
                }
            }
            var safe = new bool[bones.Length];
            var reasons = new string[bones.Length];
            var descendants = new int[bones.Length];
            var dynamicCounts = new int[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!dynamic[i]) continue;
                if (!ValidAncestry(i, bones)) reasons[i] = "Invalid or cyclic parent hierarchy.";
                else if (bones[i].Parent < 0 || actionBones[i])
                    reasons[i] = "Global root or animation skeleton bone; keep this bone controlled by animation.";
                int protectedDescendant = -1;
                for (int b = 0; b < bones.Length; b++)
                    if (Below(b, i, bones))
                    {
                        if (dynamic[b]) dynamicCounts[i]++;
                        if (b == i) continue;
                        descendants[i]++;
                        if (actionBones[b]) protectedDescendant = b;
                    }
                if (reasons[i] == null && descendants[i] == 0)
                    reasons[i] = "No descendant bones for a Dynamic Bone chain; leaf bones cannot move on their own.";
                if (reasons[i] == null && protectedDescendant >= 0)
                    reasons[i] = "Contains animation skeleton/IK bone " + PmxModel.BoneName(protectedDescendant, bones[protectedDescendant].Name) + "; simulating this whole subtree is unsafe.";
                safe[i] = reasons[i] == null;
            }

            var roots = new List<int>();
            for (int i = 0; i < bones.Length; i++)
            {
                if (!safe[i]) continue;
                bool hasDynamicAncestor = false;
                int parent = bones[i].Parent, steps = 0;
                // A static helper in the middle of a chain is still covered by
                // a safe upper Dynamic Bone root: do not suggest it twice.
                // Unsafe animation roots do not hide independent safe branches.
                while (ValidBone(parent, bones) && steps++ < bones.Length)
                {
                    if (safe[parent]) { hasDynamicAncestor = true; break; }
                    parent = bones[parent].Parent;
                }
                if (!hasDynamicAncestor && steps <= bones.Length) roots.Add(i);
            }

            var hair = new List<int>();
            foreach (int root in roots)
            {
                var candidate = new PmxPhysicsRootCandidate {
                    Bone = root, Name = PmxModel.BoneName(root, bones[root].Name), DynamicBoneCount = dynamicCounts[root], IsUsable = true
                };
                candidate.IsHair = IdentifyHair(root, bones, bodyNames[root], dynamicCounts[root], descendants[root], out candidate.Reason);
                result.Candidates.Add(candidate);
                if (candidate.IsHair) hair.Add(root);
            }
            // A leaf already covered by a selected parent is part of that chain,
            // not a skipped root. Report only unsupported, uncovered body bones.
            for (int i = 0; i < bones.Length; i++)
            {
                if (!dynamic[i] || safe[i]) continue;
                bool covered = false;
                foreach (int root in roots) if (Below(i, root, bones)) { covered = true; break; }
                if (covered) continue;
                var candidate = new PmxPhysicsRootCandidate {
                    Bone = i, Name = PmxModel.BoneName(i, bones[i].Name), DynamicBoneCount = dynamicCounts[i], Reason = reasons[i]
                };
                result.Candidates.Add(candidate);
                result.SkippedRootCount++;
                result.Diagnostics.Add("Skipped " + candidate.Name + ": " + candidate.Reason);
            }
            result.PhysicsRoots = roots.ToArray();
            result.HairRoots = hair.ToArray();
            result.IgnoredBodyCount = ignored;
            if (ignored != 0)
                result.Diagnostics.Add("Ignored " + ignored + " dynamic bodies without a valid bone; no chain can be selected for them.");
            result.Diagnostics.Add("PMX physics roots: " + roots.Count + "; suggested hair roots: " + hair.Count + "; unsupported roots skipped: " + result.SkippedRootCount + ". All-chain selection uses PMX dynamic bodies; hair names are heuristic.");
            if (roots.Count == 0)
                result.Diagnostics.Add("No usable PMX dynamic chains found. Static bodies or bone names alone do not create physics chains.");
            if (hair.Count == 0)
                result.Diagnostics.Add("No confident hair roots found. Select hair roots manually; unknown head attachments are not assumed to be hair.");
            return result;
        }

        private static bool IdentifyHair(int root, PmxModel.Bone[] bones, List<string> bodyNames,
            int dynamicCount, int descendants, out string reason)
        {
            reason = "PMX dynamic chain; no confident hair name.";
            if (descendants == 0)
            { reason = "No descendant bones for a Dynamic Bone chain; choose a root manually."; return false; }
            if (NonHair(bones[root].Name) || AnyName(bodyNames, NonHair))
            { reason = "Clothing, body or accessory name; not selected as hair."; return false; }

            bool namedHair = HairName(bones[root].Name) || AnyName(bodyNames, HairName);
            bool belowHead = false;
            int parent = bones[root].Parent, steps = 0;
            while (ValidBone(parent, bones) && steps++ < bones.Length)
            {
                string name = bones[parent].Name;
                if (HeadName(name)) { belowHead = true; break; }
                if (ProtectedSkeleton(name)) break;
                if (NonHair(name))
                { reason = "Clothing/body/accessory ancestor; not selected as hair."; return false; }
                namedHair |= HairName(name);
                parent = bones[parent].Parent;
            }
            if (namedHair)
            {
                reason = belowHead ? "Hair name and head ancestor, with PMX dynamic bodies." : "Hair name with PMX dynamic bodies.";
                return true;
            }
            // Some YYB hair rigs use left/right HZ1..HZn with HA/HB branches.
            // This is a naming convention, not a PMX tag: require both a head
            // ancestor and more than one dynamic bone, never just "under head".
            if (belowHead && dynamicCount >= 2 && NumberedHzName(bones[root].Name))
            { reason = "Head-attached numbered HZ hair chain with multiple PMX dynamic bodies."; return true; }
            return false;
        }

        private static bool AnyName(List<string> names, Predicate<string> predicate)
        {
            if (names != null) foreach (string name in names) if (predicate(name)) return true;
            return false;
        }
        private static bool ValidBone(int bone, PmxModel.Bone[] bones)
        { return bone >= 0 && bone < bones.Length && bones[bone] != null; }
        private static bool ValidAncestry(int bone, PmxModel.Bone[] bones)
        {
            for (int steps = 0; steps < bones.Length; steps++)
            {
                if (!ValidBone(bone, bones)) return false;
                bone = bones[bone].Parent;
                if (bone == -1) return true;
            }
            return false;
        }
        private static bool Below(int bone, int ancestor, PmxModel.Bone[] bones)
        {
            for (int steps = 0; ValidBone(bone, bones) && steps < bones.Length; steps++, bone = bones[bone].Parent)
                if (bone == ancestor) return true;
            return false;
        }
        private static string Stem(string name)
        {
            var text = new StringBuilder();
            foreach (char ch in (name ?? "").ToLowerInvariant()) if (char.IsLetter(ch)) text.Append(ch);
            return text.ToString();
        }
        private static bool HeadName(string name)
        {
            string stem = Stem(name);
            return stem == "頭" || stem == "头" || stem == "head" || stem == "biphead" || stem == "jbipchead" || stem == "mixamorighead";
        }
        private static bool ProtectedSkeleton(string name)
        {
            if (HeadName(name)) return true;
            switch (Stem(name))
            {
                case "全ての親": case "操作中心": case "センター": case "グルーブ": case "上半身": case "下半身":
                case "腰": case "首": case "両目": case "root": case "rootbone": case "armature": case "global":
                case "center": case "centre": case "groove": case "hips": case "pelvis": case "waist":
                case "spine": case "chest": case "neck": return true;
                default: return ProtectedLimb(Stem(name));
            }
        }
        private static bool ProtectedActionBone(PmxModel.Bone bone)
        {
            return bone != null && (ProtectedSkeleton(bone.Name) || (bone.Flags & 0x0020) != 0 || bone.IkTarget >= 0 || bone.IkLinks != null && bone.IkLinks.Count > 0);
        }
        private static bool ProtectedLimb(string stem)
        {
            // Standard MMD action bones and common imported English skeletons.
            // Exact stems avoid mistaking breast, skirt or accessory names for
            // the chest/limb skeleton. Number suffixes were removed by Stem().
            if (stem.StartsWith("左") || stem.StartsWith("右")) stem = stem.Substring(1);
            switch (stem)
            {
                case "肩": case "肩p": case "肩c": case "腕": case "腕捩": case "ひじ": case "肘": case "手捩": case "手首":
                case "親指": case "人指": case "人差指": case "中指": case "薬指": case "小指":
                case "足": case "足d": case "ひざ": case "膝": case "ひざd": case "足首": case "足首d": case "つま先": case "足先ex":
                case "足ik": case "つま先ik": case "足ｉｋ": case "つま先ｉｋ": case "目": return true;
            }
            foreach (string prefix in new[] { "mixamorig", "bip", "jbip" })
                if (stem.StartsWith(prefix)) { stem = stem.Substring(prefix.Length); break; }
            if (stem.StartsWith("left")) stem = stem.Substring(4);
            else if (stem.StartsWith("right")) stem = stem.Substring(5);
            else if (stem.Length > 1 && (stem[0] == 'l' || stem[0] == 'r' || stem[0] == 'c'))
            {
                string withoutSide = stem.Substring(1);
                if (EnglishActionStem(withoutSide)) return true;
            }
            if (EnglishActionStem(stem)) return true;
            // Blender convention: upper_arm.L / hand.R, after normalization.
            return stem.Length > 1 && (stem.EndsWith("l") || stem.EndsWith("r")) && EnglishActionStem(stem.Substring(0, stem.Length - 1));
        }
        private static bool EnglishActionStem(string stem)
        {
            switch (stem)
            {
                case "root": case "hips": case "pelvis": case "spine": case "chest": case "neck": case "head":
                case "shoulder": case "clavicle": case "arm": case "upperarm": case "forearm": case "lowerarm": case "elbow": case "wrist": case "hand":
                case "thumb": case "index": case "middle": case "ring": case "pinky": case "little": case "finger":
                case "handthumb": case "handindex": case "handmiddle": case "handring": case "handpinky":
                case "upleg": case "thigh": case "leg": case "upperleg": case "lowerleg": case "calf": case "knee": case "ankle": case "foot": case "toe": case "toes": case "toebase": case "eye": return true;
                default: return false;
            }
        }
        private static bool ContainsAny(string text, params string[] parts)
        {
            foreach (string part in parts) if (text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
        private static bool LatinWordStart(string text, params string[] words)
        {
            // Accept HairTail and backHair, but not "chair" or "crosshair".
            foreach (string word in words)
                for (int at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0;
                    at = text.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
                    if (at == 0 || !IsLatin(text[at - 1]) || char.IsUpper(text[at]) && char.IsLower(text[at - 1])) return true;
            return false;
        }
        private static bool IsLatin(char value)
        { return value >= 'A' && value <= 'Z' || value >= 'a' && value <= 'z'; }
        private static bool HairName(string name)
        {
            string text = name ?? "";
            return ContainsAny(text, "髪", "髮", "头发", "頭髮", "馬尾", "马尾", "辫", "辮", "もみあげ", "アホ毛", "ポニーテール", "ツインテール") ||
                LatinWordStart(text, "hair", "ponytail", "pigtail", "twintail", "bang", "braid", "ahoge", "sideburn");
        }
        private static bool NonHair(string name)
        {
            string text = name ?? "";
            return ContainsAny(text, "胸", "乳", "おっぱい", "スカート", "裙", "袖", "披", "服", "リボン", "飾", "饰", "イヤリング", "角", "耳", "眼", "目", "口", "尻尾", "帽") ||
                LatinWordStart(text, "breast", "bust", "boob", "skirt", "dress", "sleeve", "cape", "ribbon", "earring", "jewel", "accessory", "horn", "ear", "eye", "mouth", "hat");
        }
        private static bool NumberedHzName(string name)
        {
            string text = (name ?? "").Replace(" ", "").Replace("_", "").Replace(".", "").Replace("-", "").ToLowerInvariant();
            if (text.StartsWith("左") || text.StartsWith("右")) text = text.Substring(1);
            else if (text.StartsWith("left")) text = text.Substring(4);
            else if (text.StartsWith("right")) text = text.Substring(5);
            if (!text.StartsWith("hz") || text.Length == 2) return false;
            for (int i = 2; i < text.Length; i++) if (!char.IsDigit(text[i])) return false;
            return true;
        }
    }
}
