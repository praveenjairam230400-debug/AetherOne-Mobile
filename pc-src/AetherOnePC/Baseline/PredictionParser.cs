using System.Text.Json;
namespace AetherOnePC;
internal static class PredictionParser
{
    internal static string[] Parse(string value){if(value.Length>65536)throw new JsonException("Prediction response too large");using var doc=JsonDocument.Parse(value);if(doc.RootElement.ValueKind!=JsonValueKind.Array||doc.RootElement.GetArrayLength()<2||doc.RootElement[1].ValueKind!=JsonValueKind.Array)return [];return doc.RootElement[1].EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()??"").Where(x=>x.Length is >0 and <200).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();}
}
