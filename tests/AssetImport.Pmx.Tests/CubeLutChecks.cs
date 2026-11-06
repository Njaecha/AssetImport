using AssetImport;
using System.Globalization;

internal static class CubeLutChecks
{
    internal static void Run(Action<bool, string> check)
    {
        string Cube(Func<int,int,int,string> row, string domain = "") => "TITLE \"test\"\nLUT_3D_SIZE 2\n" + domain + string.Join("\n",
            from b in Enumerable.Range(0,2) from g in Enumerable.Range(0,2) from r in Enumerable.Range(0,2) select row(r,g,b));
        var source = new byte[] {0,0,0,0, 255,255,255,255, 12,123,240,64, 127,128,129,7};
        string identity = Cube((r,g,b)=>$"{r} {g} {b}");
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var copy=(byte[])source.Clone(); CubeLut.Read(new StringReader(identity)).Apply(copy);
            check(copy.SequenceEqual(source), "identity LUT preserves endpoints, interpolation and every alpha byte in non-English locale");
            copy=(byte[])source.Clone(); CubeLut.Read(new StringReader(Cube((r,g,b)=>$"{b} {r} {g}"))).Apply(copy);
            check(Enumerable.Range(0, source.Length/4).All(i=>copy[i*4]==source[i*4+2] && copy[i*4+1]==source[i*4] && copy[i*4+2]==source[i*4+1] && copy[i*4+3]==source[i*4+3]), "red-fastest cube axis order and cross-channel interpolation");
            copy=new byte[]{0,127,255,37}; CubeLut.Read(new StringReader(Cube((r,g,b)=>$"{r} {g} {b}","DOMAIN_MIN 0.25 0.25 0.25\nDOMAIN_MAX 0.75 0.75 0.75\n"))).Apply(copy);
            check(copy[0]==0 && Math.Abs(copy[1]-127)<=1 && copy[2]==255 && copy[3]==37,"domain remapping/clamping preserves alpha");
            foreach(string invalid in new[]{"LUT_3D_SIZE 256",identity+"\n0 0 0",identity.Replace("1 1 1","NaN 1 1"),identity.Replace("1 1 1",""),"LUT_1D_SIZE 2\n0 0 0",identity+"\nDOMAIN_MIN 0 0 0",identity.Replace("TITLE \"test\"","DOMAIN_MIN 1 1 1")})
            {
                try { CubeLut.Read(new StringReader(invalid)); check(false,"invalid LUT accepted"); }
                catch(FormatException) { check(true,"invalid or incomplete LUT rejected before import"); }
            }
            CheckOptimizedInterpolation(check);
            var lut=CubeLut.Read(new StringReader(identity)); AssetUI.ImportColorLut=lut;
            var import=new Import(new PmxModel { Bones=Array.Empty<PmxModel.Bone>() },false);
            var normal=new LoadProcess(null,(Studio.OCIItem)null,import,new UnityEngine.Vector3(),LoadProcess.LoadProcessKind.Normal);
            AssetUI.ImportColorLut=null;
            check(ReferenceEquals(normal.ColorLut,lut),"import snapshots selected LUT before preload UI can change");
            AssetUI.ImportColorLut=lut;
            foreach(var kind in new[]{LoadProcess.LoadProcessKind.Load,LoadProcess.LoadProcessKind.Import,LoadProcess.LoadProcessKind.Copy})
                check(new LoadProcess(null,(Studio.OCIItem)null,import,new UnityEngine.Vector3(),kind).ColorLut==null,"saved/copy texture never converted twice: "+kind);
        }
        finally { CultureInfo.CurrentCulture=oldCulture; AssetUI.ImportColorLut=null; }
    }

    private static void CheckOptimizedInterpolation(Action<bool,string> check)
    {
        // Retain the old nested-loop reference to catch channel strides, domain
        // lookup errors and changes to rounding at arbitrary interpolation points.
        var random = new Random(8452);
        foreach (int size in new[] { 3, 7, 16 })
        {
            var values = Enumerable.Range(0,size*size*size*3).Select(_=>(float)(random.NextDouble()*2-.5)).ToArray();
            float[] min = { -.2f, .1f, .25f }, max = { .8f, 1.1f, .75f };
            string Number(float value) => value.ToString("R",CultureInfo.InvariantCulture);
            var text = new System.Text.StringBuilder($"LUT_3D_SIZE {size}\nDOMAIN_MIN {string.Join(" ",min.Select(Number))}\nDOMAIN_MAX {string.Join(" ",max.Select(Number))}\n");
            for(int i=0;i<values.Length;i+=3)text.AppendLine(string.Join(" ",values.Skip(i).Take(3).Select(Number)));
            var source = new byte[65536*4]; random.NextBytes(source);
            var expected = (byte[])source.Clone();
            for (int p=0;p<expected.Length;p+=4)
            {
                float Coord(int axis) => Math.Max(0,Math.Min(1,(source[p+axis]/255f-min[axis])/(max[axis]-min[axis])))*(size-1);
                float r=Coord(0),g=Coord(1),b=Coord(2);
                int x=Math.Min((int)r,size-2),y=Math.Min((int)g,size-2),z=Math.Min((int)b,size-2);
                r-=x;g-=y;b-=z;
                for(int c=0;c<3;c++)
                {
                    double result=0;
                    for(int dz=0;dz<2;dz++)for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
                        result+=values[(((z+dz)*size+y+dy)*size+x+dx)*3+c]*(dx==0?1-r:r)*(dy==0?1-g:g)*(dz==0?1-b:b);
                    expected[p+c]=(byte)Math.Max(0,Math.Min(255,Math.Round(result*255)));
                }
            }
            var lut=CubeLut.Read(new StringReader(text.ToString()));
            lut.Apply(source);
            check(source.SequenceEqual(expected),"optimized interpolation exactly matches reference, including alpha/domain/clamp/rounding: size "+size);
        }
    }
}
