using System.Collections.Generic;

namespace AssetImport
{
    // Parent transforms are essential. Append/IK edges are optional operations:
    // isolate cyclic operations without deleting their bones or inventing motion.
    internal sealed class PmxAnimationPlan
    {
        internal bool[] Grant, Ik;
        internal string[] InactiveIk, GrantWarning, IkWarning;

        internal static PmxAnimationPlan Build(PmxModel model)
        {
            int count=model.Bones.Length;
            var plan=new PmxAnimationPlan { Grant=new bool[count], Ik=new bool[count],
                InactiveIk=new string[count], GrantWarning=new string[count], IkWarning=new string[count] };
            var edges=new List<int>[count];var reverse=new List<int>[count];
            for (int i=0;i<count;i++) { edges[i]=new List<int>(); reverse[i]=new List<int>(); }
            for (int i=0;i<count;i++)
            {
                var bone=model.Bones[i];
                plan.Grant[i]=(bone.Flags&0x300)!=0 && bone.GrantParent>=0 && bone.GrantWeight!=0;
                if ((bone.Flags&0x20)!=0)
                {
                    plan.InactiveIk[i]=bone.IkIterations==0 ? "zero iterations" : bone.IkAngle==0 ? "zero step angle"
                        : bone.IkLinks.Count==0 ? "empty chain" : null;
                    plan.Ik[i]=plan.InactiveIk[i]==null;
                }
                AddEdge(edges,reverse,i,bone.Parent);
                if (plan.Grant[i]) AddEdge(edges,reverse,i,bone.GrantParent);
                if (plan.Ik[i])
                {
                    AddEdge(edges,reverse,i,bone.IkTarget);
                    foreach (var link in bone.IkLinks) AddEdge(edges,reverse,i,link.Bone);
                }
            }
            // Iterative Kosaraju: a deep but valid hierarchy must not overflow the stack.
            var visited=new bool[count];var order=new List<int>();var stack=new Stack<int>();
            for (int root=0;root<count;root++)
            {
                if (visited[root]) continue;
                stack.Push(root);
                while (stack.Count>0)
                {
                    int node=stack.Pop();
                    if (node<0) { order.Add(~node); continue; }
                    if (visited[node]) continue;
                    visited[node]=true;stack.Push(~node);
                    foreach (int child in edges[node]) if (!visited[child]) stack.Push(child);
                }
            }
            var component=new int[count];for (int i=0;i<count;i++)component[i]=-1;
            for (int i=order.Count-1;i>=0;i--)
            {
                int root=order[i];if (component[root]>=0) continue;
                component[root]=root;stack.Push(root);
                while (stack.Count>0)
                    foreach (int parent in reverse[stack.Pop()])
                        if (component[parent]<0) { component[parent]=root;stack.Push(parent); }
            }
            for (int i=0;i<count;i++)
            {
                var bone=model.Bones[i];
                if (plan.Grant[i] && InComponent(component,i,bone.GrantParent))
                { plan.Grant[i]=false;plan.GrantWarning[i]="cyclic append dependency"; }
                if (plan.Ik[i])
                {
                    bool cyclic=InComponent(component,i,bone.IkTarget);
                    foreach (var link in bone.IkLinks) cyclic|=InComponent(component,i,link.Bone);
                    if (cyclic) { plan.Ik[i]=false;plan.IkWarning[i]="cyclic IK dependency"; }
                }
            }
            return plan;
        }
        private static bool InComponent(int[] components,int from,int to)
        { return to>=0 && to<components.Length && components[from]==components[to]; }
        private static void AddEdge(List<int>[] edges,List<int>[] reverse,int from,int to)
        {
            // The binary reader validates bounds. Sentinel -1 is not a graph edge;
            // malformed active operations are still validated by the exporter.
            if (to<0 || to>=edges.Length) return;
            edges[from].Add(to);reverse[to].Add(from);
        }
    }
}
