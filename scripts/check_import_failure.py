#!/usr/bin/env python3
"""Execute the actual fresh-import/cleanup methods against inert Studio doubles.

Requires Python 3.9+ and .NET SDK 8. No Unity/game runtime is executed.
Run from the repository root: python scripts/check_import_failure.py
"""
import argparse
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
PREAMBLE = r'''
using System;
using System.Collections.Generic;
using System.IO;
using Studio;
using AssetImport;
class Vector3 {}
class FakeObject { public bool destroyed; public static implicit operator bool(FakeObject o) => o != null && !o.destroyed; }
class Material : FakeObject {}
class MeshRenderer { public Material material=new Material(); }
class GameObject : FakeObject { public string name="temp"; public bool active=true; public void SetActive(bool v)=>active=v; public T GetComponentInChildren<T>() where T:new()=>new T(); }
class DynamicBoneCollider { public GameObject gameObject=new GameObject(); }
static class Resources { public static T[] FindObjectsOfTypeAll<T>()=>new T[0]; }
class Log { public List<string> messages=new List<string>(); public void LogInfo(object o){} public void LogError(object o)=>messages.Add(o.ToString()); public void LogWarning(object o)=>messages.Add(o.ToString()); public void LogMessage(object o)=>messages.Add(o.ToString()); }
static class Main { public static LoadProcess currentLoadProcess; }
static class AssetUI { public static bool ArmatureMode,PreloadUI; public static string CommonPathText; }
class LoadProcess { public enum LoadProcessKind { Normal } public Import Import; public OCIItem Item; public LoadProcess(GameObject b,OCIItem item,Import import,Vector3 scale,LoadProcessKind kind){Import=import;Item=item;} }
static class RamCacheUtility { public static string ToCache(string path){if(Test.Mode=="cache")throw new IOException("Cache denied");return "hash";} public static byte[] GetFileBlob(string hash)=>new byte[0]; }
class Import { public static Import Last; public bool IsLoaded,HasBones; public string LoadError,CommonPath="textures"; public GameObject GameObject; public Import(string hash,bool armature,Material material,bool perRenderer,bool blends){Last=this;}
 public void Load(){ if(Test.Mode=="parse"){return;} GameObject=new GameObject(); if(Test.Mode=="load")throw new Exception("Conversion failed"); IsLoaded=true; }

 public void ResolveTextureFiles(string path){if(Test.Mode=="texture")throw new IOException("Texture lookup failed");}
}
namespace Studio { class TreeNodeObject{} class ItemInfo {public List<int> bones=new List<int>();} class OCIItem {public GameObject objectItem=new GameObject();public ItemInfo itemInfo=new ItemInfo();public TreeNodeObject treeNodeObject=new TreeNodeObject();}
 static class AddObjectItem {public static OCIItem Last;public static OCIItem Add(int a,int b,int c){Last=new OCIItem();Studio.Live.Add(Last);return Last;}}
 static class Studio {public static List<OCIItem> Live=new List<OCIItem>(); public static int DeleteCalls;public static void DeleteNode(TreeNodeObject node){DeleteCalls++;Live.RemoveAll(x=>ReferenceEquals(x.treeNodeObject,node));}}
}
class AssetSceneController { public static Log Logger=new Log(); public static List<FakeObject> Destroyed=new List<FakeObject>(); static T Instantiate<T>(T x) where T:new()=>new T(); static void Destroy(FakeObject o){o.destroyed=true;Destroyed.Add(o);} static string GetDumpPath()=>Path.GetTempPath();
'''
POSTAMBLE = r'''
}
static class Test {
 public static string Mode;static int assertions;
 static void Check(bool okay,string name){assertions++;if(!okay)throw new Exception(name);}
 static void Reset(string mode){Mode=mode;Studio.Studio.Live.Clear();Studio.Studio.DeleteCalls=0;AssetSceneController.Destroyed.Clear();AssetSceneController.Logger.messages.Clear();Import.Last=null;AssetUI.PreloadUI=true;}
 static void Main(){var path=Path.Combine(Path.GetTempPath(),"assetimport-import-"+Guid.NewGuid().ToString("N")+".fbx");File.WriteAllBytes(path,new byte[]{1});
 foreach(string mode in new[]{"parse","cache","load","texture","success"}){
  Reset(mode); var existing=new OCIItem();Studio.Studio.Live.Add(existing);var previous=new LoadProcess(existing.objectItem,existing,null,new Vector3(),LoadProcess.LoadProcessKind.Normal);global::Main.currentLoadProcess=previous;
  new AssetSceneController().Import(path,new Vector3(),true,false,true);
  Check(Studio.Studio.Live.Contains(existing),mode+": existing scene item preserved");
  if(mode=="success") {Check(Studio.Studio.Live.Count==2,"success item survives");Check(Studio.Studio.DeleteCalls==0,"success no delete");Check(AssetSceneController.Destroyed.Count==0,"success no destroys");Check(!ReferenceEquals(global::Main.currentLoadProcess,previous),"success new preload");Check(AssetUI.PreloadUI,"success preload visible");}
  else {Check(Studio.Studio.Live.Count==1,mode+": temp removed");Check(Studio.Studio.DeleteCalls==1,mode+": native delete once");Check(ReferenceEquals(global::Main.currentLoadProcess,previous),mode+": previous preload preserved");Check(AssetUI.PreloadUI,mode+": previous preload visibility preserved");Check(AssetSceneController.Logger.messages.Exists(x=>x.StartsWith("Import failed:")),mode+": visible failure");Check(AssetSceneController.Destroyed.Exists(x=>x is Material),mode+": base material destroyed");if(mode=="load"||mode=="texture")Check(Import.Last.GameObject.destroyed,mode+": partial imported gameobject destroyed");}
 }
 Reset("parse");new AssetSceneController().Import(path+".missing",new Vector3(),true,false,true);Check(Studio.Studio.Live.Count==0,"missing file creates no placeholder");Check(Studio.Studio.DeleteCalls==0,"missing file does not delete anything");
 File.Delete(path);
 Console.WriteLine("PASS "+assertions+" production Import rollback assertions");
 }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    source = (ROOT / "src/AssetImport/AssetImport.SceneController.cs").read_text(encoding="utf-8-sig")
    start = source.index("        public void Import(string path, Vector3 scale")
    end = source.index("        /// <summary>\n        /// Second part of the import", start)
    with tempfile.TemporaryDirectory(prefix="assetimport-import-check-") as temp:
        work = Path(temp)
        (work / "Program.cs").write_text(PREAMBLE + source[start:end] + POSTAMBLE, encoding="utf-8")
        (work / "Debug.cs").write_text((ROOT / "src/AssetImport/AssetImport.Debug.cs").read_text(encoding="utf-8"), encoding="utf-8")
        for localization in (ROOT / "src/AssetImport").glob("AssetImport.Localization*.cs"):
            (work / localization.name).write_text(localization.read_text(encoding="utf-8"), encoding="utf-8")
        (work / "check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>')
        subprocess.run([args.dotnet, "run", "--project", str(work / "check.csproj"), "-c", "Release", "--disable-build-servers", "-p:UseSharedCompilation=false", "-v:q"], cwd=ROOT, check=True)


if __name__ == "__main__":
    main()
