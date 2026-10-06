using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace ClassIsland.Core.Converters;

/// <summary>
/// 适用于 <see cref="Color"/> 的值转换器。
/// </summary>
public class ColorHexJsonConverter : JsonConverter<Color>
{
    /// <inheritdoc />
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return ParseHex(reader.GetString());
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            byte a = 0;
            byte r = 0;
            byte g = 0;
            byte b = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    return default;

                var propertyName = reader.GetString();

                reader.Read(); // 读取属性值

                switch (propertyName)
                {
                    case "A":
                        reader.TryGetByte(out a);
                        break;
                    case "R":
                        reader.TryGetByte(out r);
                        break;
                    case "G":
                        reader.TryGetByte(out g);
                        break;
                    case "B":
                        reader.TryGetByte(out b);
                        break;
                    default:
                        reader.Skip(); // 忽略未知字段
                        break;
                }
            }

            return Color.FromArgb(a, r, g, b);
        }

        reader.Skip();
        return default;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStringValue($"#{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}");
    }

    private static Color ParseHex(string? str)
    {
        if (string.IsNullOrWhiteSpace(str))
            return default;

        var s = str.TrimStart('#');
        try
        {
            return s.Length switch
            {
                8 => Color.FromArgb(FromHex(s, 6), FromHex(s, 0), FromHex(s, 2), FromHex(s, 4)),
                6 => Color.FromArgb(255, FromHex(s, 0), FromHex(s, 2), FromHex(s, 4)),
                _ => default
            };
        }
        catch
        {
            return default;
        }
    }

    private static byte FromHex(string s, int index)
    {
        return System.Convert.ToByte(s.Substring(index, 2), 16);
    }
}
