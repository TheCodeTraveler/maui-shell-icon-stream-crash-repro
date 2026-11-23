namespace Repro;

static class Log
{
    public const string Tag = "ShellIconRepro";

    public static void Line(string marker, string caseName, params (string Name, string Value)[] fields)
    {
        var text = $"{marker} case={caseName}";
        foreach (var (name, value) in fields)
        {
            var clean = value.Replace('"', '\'').ReplaceLineEndings(" | ");
            text += clean.Contains(' ') ? $" {name}=\"{clean}\"" : $" {name}={clean}";
        }

        Android.Util.Log.Info(Tag, text);
    }
}
