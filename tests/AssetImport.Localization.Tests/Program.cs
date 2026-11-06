using System.Globalization;
using System.Text.RegularExpressions;
using AssetImport;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
}

string Fields(string format) => string.Join(",", Regex.Matches(format, @"\{(\d+)(?:[^{}]*)\}")
    .Select(m => m.Groups[1].Value).OrderBy(s => s));

Check(L.Language == InterfaceLanguage.English, "Default interface language is English.");
foreach (var pair in L.Chinese)
{
    Check(!string.IsNullOrEmpty(pair.Value), "Missing translation: " + pair.Key);
    Check(Fields(pair.Key) == Fields(pair.Value), "Mismatched placeholders: " + pair.Key);
    var values = Enumerable.Range(0, 12).Select(i => (object)(i + .25)).ToArray();
    _ = string.Format(CultureInfo.InvariantCulture, pair.Key, values);
    _ = string.Format(CultureInfo.InvariantCulture, pair.Value, values);
    Check(L.Text(pair.Key) == pair.Key, "English fallback changed: " + pair.Key);
}

var path = "D:/Models/{0}/Import/模型.pmx";
var message = L.Message("Saved: {0}", path);
var status = L.Message("Physics {0}: {1} item(s), {2} chain(s), {3} enabled.", L.Message("ON"), 1, 2, 2)
    + L.Message("\nWarnings: {0}. See the log for details.", 3);
string english = status;
Check((string)message == "Saved: " + path, "Path must stay a literal argument, including braces.");
L.Language = InterfaceLanguage.ChineseSimplified;
Check(L.Text("Import") == "导入", "Chinese interface labels.");
Check((string)message == "已保存：" + path, "Existing messages follow a language switch without changing paths.");
Check(status.ToString().Contains("物理开启") && status.ToString().Contains("警告：3"), "Nested and appended messages switch together.");
Check(status.English == english, "Diagnostic text stays English after switching.");
Check(L.Text("A custom model name") == "A custom model name", "Unknown keys have an English fallback.");
Check(L.Message("Report: {0}", "Import").ToString() == "报告：Import", "Model names that look like resource keys are not translated.");
L.Language = InterfaceLanguage.English;
Check((string)status == english, "Cached messages switch back to English.");
Check((string)(LocalizedText)null == null && (LocalizedText)(string)null == null, "Null message compatibility.");

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "src/AssetImport/AssetImport.cs"))) root = root.Parent;
Check(root != null, "Locate repository sources for translation coverage.");
foreach (string file in Directory.GetFiles(Path.Combine(root!.FullName, "src/AssetImport"), "*.cs"))
{
    if (Path.GetFileName(file).StartsWith("AssetImport.Localization")) continue;
    string source = File.ReadAllText(file);
    foreach (Match match in Regex.Matches(source, "L\\.(?:Text|Format|Message)\\(\"((?:[^\"\\\\]|\\\\.)*)\""))
    {
        string key = Regex.Unescape(match.Groups[1].Value);
        Check(L.Chinese.ContainsKey(key), "Missing catalogue entry in " + file + ": " + key);
    }
}
Console.WriteLine($"Localization checks passed: {checks}; {L.Chinese.Count} translated messages.");
