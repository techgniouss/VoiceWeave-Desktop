using System.Text;

namespace VoiceWeave.Engine;

/// <summary>
/// ONNX metadata is a repeated field at the end of a serialised model (field 14: a key string and a value string), so
/// entries can be added to a model by appending these bytes without rewriting the file. sherpa-onnx refuses a NeMo CTC
/// model that lacks them; the published Hindi export ships without.
/// </summary>
public static class OnnxMetadata
{
    public static byte[] Encode(IEnumerable<(string key, string value)> entries)
    {
        var output = new List<byte>();
        foreach (var (key, value) in entries)
        {
            var k = Encoding.UTF8.GetBytes(key);
            var v = Encoding.UTF8.GetBytes(value);
            // One-byte lengths only: protobuf needs a varint beyond 127 bytes.
            if (k.Length + v.Length + 4 > 127) throw new ArgumentException("Metadata entry too long: " + key);
            output.Add(0x72);
            output.Add((byte)(4 + k.Length + v.Length));
            output.Add(0x0A);
            output.Add((byte)k.Length);
            output.AddRange(k);
            output.Add(0x12);
            output.Add((byte)v.Length);
            output.AddRange(v);
        }
        return output.ToArray();
    }

    public static async Task AppendAsync(string path, IEnumerable<(string key, string value)> entries)
    {
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(Encode(entries));
    }
}
