using System.Globalization;
using System.Text;

namespace TouchMirror.AirPlay;

public static class PlistCodec
{
    public static object? Read(byte[] data)
    {
        if (data.Length >= 8 && data[0] == 'b' && data[1] == 'p')
            return ReadBinary(data);
        var text = Encoding.UTF8.GetString(data).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (text.StartsWith("<?xml") || text.StartsWith("<plist"))
            return ReadXml(text);
        return Encoding.UTF8.GetString(data);
    }

    public static byte[] WriteXml(Dictionary<string, object?> dict)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
        sb.Append("<plist version=\"1.0\">\n");
        WriteXmlValue(sb, dict, 0);
        sb.Append("</plist>\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void WriteXmlValue(StringBuilder sb, object? value, int indent)
    {
        var pad = new string('\t', indent);
        switch (value)
        {
            case Dictionary<string, object?> dict:
                sb.Append(pad).Append("<dict>\n");
                foreach (var (k, v) in dict)
                {
                    sb.Append(pad).Append('\t').Append("<key>").Append(Escape(k)).Append("</key>\n");
                    WriteXmlValue(sb, v, indent + 1);
                }
                sb.Append(pad).Append("</dict>\n");
                break;
            case List<object?> list:
                sb.Append(pad).Append("<array>\n");
                foreach (var item in list)
                    WriteXmlValue(sb, item, indent + 1);
                sb.Append(pad).Append("</array>\n");
                break;
            case string s:
                sb.Append(pad).Append("<string>").Append(Escape(s)).Append("</string>\n");
                break;
            case bool b:
                sb.Append(pad).Append(b ? "<true/>\n" : "<false/>\n");
                break;
            case byte[] bytes:
                sb.Append(pad).Append("<data>").Append(Convert.ToBase64String(bytes)).Append("</data>\n");
                break;
            case long or int:
                sb.Append(pad).Append("<integer>").Append(Convert.ToInt64(value, CultureInfo.InvariantCulture)).Append("</integer>\n");
                break;
            case double or float:
                sb.Append(pad).Append("<real>").Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)).Append("</real>\n");
                break;
            case DateTime dt:
                sb.Append(pad).Append("<date>").Append(dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append("</date>\n");
                break;
            case null:
                sb.Append(pad).Append("<string></string>\n");
                break;
            default:
                sb.Append(pad).Append("<string>").Append(Escape(value.ToString() ?? "")).Append("</string>\n");
                break;
        }
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static byte[] Write(Dictionary<string, object?> dict)
    {
        var objects = new List<object?>();
        void Collect(object? v)
        {
            objects.Add(v);
            if (v is Dictionary<string, object?> d)
            {
                foreach (var k in d.Keys) Collect(k);
                foreach (var val in d.Values) Collect(val);
            }
            else if (v is List<object?> l)
            {
                foreach (var item in l) Collect(item);
            }
        }
        Collect(dict);

        var body = new List<byte>();
        var offsets = new List<int>();
        var objBytes = objects.Select(o => EncodeObj(o, objects)).ToList();
        var refSize = objects.Count > 255 ? 2 : 1;
        foreach (var b in objBytes)
        {
            offsets.Add(8 + body.Count);
            body.AddRange(b);
        }
        var offsetTableOffset = 8 + body.Count;
        var offsetSize = offsetTableOffset > ushort.MaxValue ? 4 : (offsetTableOffset > byte.MaxValue ? 2 : 1);

        var result = new List<byte>(Encoding.ASCII.GetBytes("bplist00"));
        result.AddRange(body);
        foreach (var off in offsets)
            result.AddRange(BigBytes(off, offsetSize));
        var trailer = new byte[32];
        trailer[6] = (byte)offsetSize;
        trailer[7] = (byte)refSize;
        BigBytes(objects.Count, 8).CopyTo(trailer, 8);
        BigBytes(0, 8).CopyTo(trailer, 16);
        BigBytes(offsetTableOffset, 8).CopyTo(trailer, 24);
        result.AddRange(trailer);
        return result.ToArray();
    }

    private static byte[] EncodeObj(object? value, List<object?> objects)
    {
        switch (value)
        {
            case Dictionary<string, object?> dict:
            {
                var bytes = new List<byte>(CountMarker(0xD0, dict.Count));
                foreach (var k in dict.Keys) bytes.Add((byte)objects.IndexOf(k));
                foreach (var v in dict.Values) bytes.Add((byte)objects.IndexOf(v));
                return bytes.ToArray();
            }
            case List<object?> list:
            {
                var bytes = new List<byte>(CountMarker(0xA0, list.Count));
                foreach (var item in list) bytes.Add((byte)objects.IndexOf(item));
                return bytes.ToArray();
            }
            case string s:
            {
                var bytes = new List<byte>(CountMarker(0x50, s.Length));
                bytes.AddRange(Encoding.ASCII.GetBytes(s));
                return bytes.ToArray();
            }
            case byte[] data:
            {
                var bytes = new List<byte>(CountMarker(0x40, data.Length));
                bytes.AddRange(data);
                return bytes.ToArray();
            }
            case long or int:
            {
                var v = Convert.ToInt64(value);
                var size = v <= byte.MaxValue ? 1 : v <= ushort.MaxValue ? 2 : v <= uint.MaxValue ? 4 : 8;
                var marker = (byte)(0x10 | (size == 1 ? 0 : size == 2 ? 1 : size == 4 ? 2 : 3));
                return new[] { marker }.Concat(BigBytes(v, size)).ToArray();
            }
            case bool b:
                return new[] { b ? (byte)0x09 : (byte)0x08 };
            case double d:
            {
                var bytes = new List<byte> { 0x23 };
                bytes.AddRange(BigBytes(BitConverter.DoubleToInt64Bits(d), 8));
                return bytes.ToArray();
            }
            default:
                return new byte[] { 0x00 };
        }
    }

    private static byte[] CountMarker(int type, int count)
    {
        if (count < 15)
            return new[] { (byte)(type | count) };
        var size = count <= byte.MaxValue ? 1 : count <= ushort.MaxValue ? 2 : 4;
        var intMarker = (byte)(0x10 | (size == 1 ? 0 : size == 2 ? 1 : 2));
        return new[] { (byte)(type | 0x0F), intMarker }.Concat(BigBytes(count, size)).ToArray();
    }

    private static byte[] BigBytes(long value, int size)
    {
        var b = new byte[size];
        for (var i = size - 1; i >= 0; i--)
        {
            b[i] = (byte)(value & 0xFF);
            value >>= 8;
        }
        return b;
    }

    public static string Dump(object? value, int indent = 0)
    {
        var pad = new string(' ', indent * 2);
        var sb = new StringBuilder();
        switch (value)
        {
            case Dictionary<string, object?> dict:
                foreach (var (k, v) in dict)
                {
                    sb.Append(pad).Append(k).Append(": ");
                    if (v is Dictionary<string, object?> or List<object?>)
                    {
                        sb.Append('\n').Append(Dump(v, indent + 1));
                    }
                    else if (v is byte[] bytes)
                    {
                        sb.Append("<").Append(bytes.Length).Append("b ");
                        sb.Append(Convert.ToHexString(bytes, 0, Math.Min(24, bytes.Length)));
                        if (bytes.Length > 24)
                            sb.Append('…');
                        sb.Append(">\n");
                    }
                    else
                    {
                        sb.Append(v?.ToString() ?? "null").Append('\n');
                    }
                }
                break;
            case List<object?> list:
                foreach (var item in list)
                    sb.Append(pad).Append("- ").Append(Dump(item, indent + 1).TrimStart());
                break;
            default:
                sb.Append(pad).Append(value?.ToString() ?? "null").Append('\n');
                break;
        }
        return sb.ToString();
    }

    private static object? ReadXml(string xml)
    {
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xml);
        var plist = doc.DocumentElement;
        if (plist == null)
            return null;
        foreach (System.Xml.XmlNode node in plist.ChildNodes)
            if (node.Name == "dict")
                return XmlValue(node);
        return null;
    }

    private static object? XmlValue(System.Xml.XmlNode node)
    {
        switch (node.Name)
        {
            case "dict":
                var dict = new Dictionary<string, object?>();
                for (var i = 0; i + 1 < node.ChildNodes.Count; i += 2)
                    if (node.ChildNodes[i]?.Name == "key")
                        dict[node.ChildNodes[i]!.InnerText] = XmlValue(node.ChildNodes[i + 1]!);
                return dict;
            case "array":
                var list = new List<object?>();
                foreach (System.Xml.XmlNode child in node.ChildNodes)
                    list.Add(XmlValue(child));
                return list;
            case "string":
                return node.InnerText;
            case "true":
                return true;
            case "false":
                return false;
            case "integer":
                return long.TryParse(node.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0L;
            case "real":
                return double.TryParse(node.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;
            case "data":
                try { return Convert.FromBase64String(node.InnerText.Trim()); }
                catch { return Array.Empty<byte>(); }
            case "date":
                return DateTime.TryParse(node.InnerText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal, out var dt) ? dt : DateTime.UnixEpoch;
            default:
                return node.InnerText;
        }
    }

    private sealed class BReader
    {
        private readonly byte[] _data;
        private int _objRefSize, _offsetSize;
        private long _numObjects, _topObject, _offsetTableOffset;
        private long[] _offsets = Array.Empty<long>();

        public BReader(byte[] data) => _data = data;

        public object? Parse()
        {
            var trailer = _data.AsSpan(_data.Length - 32);
            _offsetSize = trailer[6];
            _objRefSize = trailer[7];
            _numObjects = ReadBig(trailer, 8, 8);
            _topObject = ReadBig(trailer, 16, 8);
            _offsetTableOffset = ReadBig(trailer, 24, 8);

            _offsets = new long[_numObjects];
            for (var i = 0; i < _numObjects; i++)
                _offsets[i] = ReadBig(_data, (int)_offsetTableOffset + i * _offsetSize, _offsetSize);

            return ReadObj((int)_offsets[_topObject]);
        }

        private long ReadBig(ReadOnlySpan<byte> buf, int off, int size)
        {
            long v = 0;
            for (var i = 0; i < size; i++)
                v = (v << 8) | buf[off + i];
            return v;
        }

        private object? ReadObj(int offset)
        {
            var marker = _data[offset];
            var type = marker & 0xF0;
            var info = marker & 0x0F;
            var pos = offset + 1;
            long count = info;
            if (info == 0x0F)
            {
                var countMarker = _data[pos];
                var countSize = 1 << (countMarker & 0x0F);
                count = ReadBig(_data, pos + 1, countSize);
                pos += 1 + countSize;
            }

            switch (type)
            {
                case 0x00:
                    return info switch
                    {
                        0x00 => null,
                        0x08 => false,
                        0x09 => true,
                        _ => null
                    };
                case 0x10:
                    return ReadBig(_data, pos, 1 << info);
                case 0x20:
                    var realSize = 1 << info;
                    return realSize == 4
                        ? (double)BitConverter.Int32BitsToSingle((int)ReadBig(_data, pos, 4))
                        : BitConverter.Int64BitsToDouble(ReadBig(_data, pos, 8));
                case 0x30:
                    var secs = BitConverter.Int64BitsToDouble(ReadBig(_data, pos, 8));
                    return DateTime.UnixEpoch.AddYears(1).AddSeconds(secs);
                case 0x40:
                    return _data.AsSpan(pos, (int)count).ToArray();
                case 0x50:
                    return Encoding.ASCII.GetString(_data, pos, (int)count);
                case 0x60:
                    return Encoding.BigEndianUnicode.GetString(_data, pos, (int)count * 2);
                case 0x70:
                    return Encoding.UTF8.GetString(_data, pos, (int)count);
                case 0x80:
                    var uid = ReadBig(_data, pos, (int)count + 1);
                    return uid;
                case 0xA0:
                    var list = new List<object?>((int)count);
                    for (var i = 0; i < count; i++)
                    {
                        var refIdx = (int)ReadBig(_data, pos + i * _objRefSize, _objRefSize);
                        list.Add(ReadObj((int)_offsets[refIdx]));
                    }
                    return list;
                case 0xD0:
                    var dict = new Dictionary<string, object?>();
                    var keyBase = pos;
                    var valBase = pos + (int)count * _objRefSize;
                    for (var i = 0; i < count; i++)
                    {
                        var keyRef = (int)ReadBig(_data, keyBase + i * _objRefSize, _objRefSize);
                        var valRef = (int)ReadBig(_data, valBase + i * _objRefSize, _objRefSize);
                        var key = ReadObj((int)_offsets[keyRef])?.ToString() ?? $"k{keyRef}";
                        dict[key] = ReadObj((int)_offsets[valRef]);
                    }
                    return dict;
                default:
                    return null;
            }
        }
    }

    private static object? ReadBinary(byte[] data) => new BReader(data).Parse();
}
