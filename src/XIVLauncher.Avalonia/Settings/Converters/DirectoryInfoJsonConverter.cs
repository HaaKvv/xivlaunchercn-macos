using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XIVLauncher.Settings.Converters;

public sealed class DirectoryInfoJsonConverter : JsonConverter<DirectoryInfo>
{
    public override DirectoryInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var path = reader.GetString();
        // 空/空白字符串不应解析为当前目录（new DirectoryInfo("") 会抛 ArgumentException），
        // 按"未配置"处理返回 null，由调用方回退默认路径
        return string.IsNullOrWhiteSpace(path) ? null : new DirectoryInfo(path);
    }

    public override void Write(Utf8JsonWriter writer, DirectoryInfo value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.FullName);
}
