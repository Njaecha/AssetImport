using AssetImport;

// Exercise production path/selection code without executing game or Unity DLLs.
string temp = Path.Combine(Path.GetTempPath(), "AssetImportTextureTests_" + Guid.NewGuid().ToString("N"));
string oldWorkingDirectory = Environment.CurrentDirectory;
int checks = 0;
void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
try
{
    string models = Path.Combine(temp, "模型 with spaces"), game = Path.Combine(temp, "Game");
    Directory.CreateDirectory(models); Directory.CreateDirectory(game);
    Environment.CurrentDirectory = game;
    string model = Path.Combine(models, "house.fbx");
    string png = Path.Combine(models, "checker.png");
    File.WriteAllText(model, "fixture model"); File.WriteAllText(png, "fixture image");
    File.WriteAllText(Path.Combine(game, "checker.png"), "wrong texture in game working directory");
    string Resolve(string reference) => TextureFileResolver.Resolve(reference, model);
    Check(Resolve("checker.png") == png.Replace('\\', '/'), "Relative textures must use the model directory, not game CWD");
    string nested = Path.Combine(models, "textures"); Directory.CreateDirectory(nested);
    string normal = Path.Combine(nested, "NORMAL.PNG"); File.WriteAllText(normal, "normal image");
    Check(Resolve("textures\\NORMAL.PNG") == normal.Replace('\\', '/'), "Windows separators and nested texture directories");
    Check(Resolve(png) == png.Replace('\\', '/'), "Existing absolute reference");
    Check(Resolve("Z:/author/export/checker.png") == png.Replace('\\', '/'), "Stale author path with same-name texture beside model");
    Check(Resolve("missing/checker.png") == png.Replace('\\', '/'), "Relocated companion beside model");
    Check(!File.Exists(Resolve("missing.png")), "Missing texture must remain visibly missing");
    Check(Resolve("*0") == "*0", "Embedded texture marker must not become a guessed disk path");
    Check(TextureFileResolver.Resolve("textures/checker.png", null) == "textures/checker.png", "Scene restores do not resolve author's disk files");
    var texture = new TexturePath(new UnityEngine.Material(), Assimp.TextureType.Diffuse, "textures\\NORMAL.PNG");
    Check(!texture.Use, "Unresolved texture starts unchecked");
    texture.Path = Resolve(texture.Path);
    Check(texture.PathOkay() && texture.Use, "Resolved texture is selected automatically before preload");
    texture.Use = false; texture.Path = texture.Path;
    Check(!texture.Use, "UI redraw must retain a deliberate unchecked state");
    Check(TextureFileResolver.IsSupportedImage("x.JpEg") && TextureFileResolver.IsSupportedImage("x.JPG"), "JPEG/uppercase extensions");
    Check(TextureFileResolver.IsSupportedImage("x.tga"), "TGA selection enabled with managed decoder");
    Check(TextureFileResolver.IsSupportedImage("x.BMP") && TextureFileResolver.IsSupportedImage("x.dds"), "BMP/DDS selection enabled with managed decoders");
    Console.WriteLine($"PASS: {checks} production texture path/selection checks; no image decoding or Unity runtime.");
}
finally { Environment.CurrentDirectory = oldWorkingDirectory; Directory.Delete(temp, true); }

namespace UnityEngine { public sealed class Material { } }
namespace Assimp { public enum TextureType { Diffuse, Normals } }
