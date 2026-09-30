using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Newtonsoft.Json converters for Unity math types.
/// All Write/Read paths are NaN/Infinity safe — bad floats are replaced with 0.
/// </summary>

// Internal helper ─ sanitise floats and safely read from JToken
internal static class Sf
{
    internal static float S(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
    internal static float F(JToken t, string k) => t?[k] == null ? 0f : S((float)t[k]);
    internal static int   I(JToken t, string k) => t?[k] == null ? 0  : (int)  t[k];
}

// ─────────────────────────────────────────────────────────────
// Vector2Int
// ─────────────────────────────────────────────────────────────
public class Vector2IntConverter : JsonConverter<Vector2Int>
{
    public override void WriteJson(JsonWriter w, Vector2Int v, JsonSerializer s)
    {
        w.WriteStartObject();
        w.WritePropertyName("x"); w.WriteValue(v.x);
        w.WritePropertyName("y"); w.WriteValue(v.y);
        w.WriteEndObject();
    }
    public override Vector2Int ReadJson(JsonReader r, Type t, Vector2Int e, bool has, JsonSerializer s)
    {
        var o = JObject.Load(r);
        return new Vector2Int(Sf.I(o, "x"), Sf.I(o, "y"));
    }
}

// ─────────────────────────────────────────────────────────────
// Vector2
// ─────────────────────────────────────────────────────────────
public class Vector2Converter : JsonConverter<Vector2>
{
    public override void WriteJson(JsonWriter w, Vector2 v, JsonSerializer s)
    {
        w.WriteStartObject();
        w.WritePropertyName("x"); w.WriteValue(Sf.S(v.x));
        w.WritePropertyName("y"); w.WriteValue(Sf.S(v.y));
        w.WriteEndObject();
    }
    public override Vector2 ReadJson(JsonReader r, Type t, Vector2 e, bool has, JsonSerializer s)
    {
        var o = JObject.Load(r);
        return new Vector2(Sf.F(o, "x"), Sf.F(o, "y"));
    }
}

// ─────────────────────────────────────────────────────────────
// Vector3
// ─────────────────────────────────────────────────────────────
public class Vector3Converter : JsonConverter<Vector3>
{
    public override void WriteJson(JsonWriter w, Vector3 v, JsonSerializer s)
    {
        w.WriteStartObject();
        w.WritePropertyName("x"); w.WriteValue(Sf.S(v.x));
        w.WritePropertyName("y"); w.WriteValue(Sf.S(v.y));
        w.WritePropertyName("z"); w.WriteValue(Sf.S(v.z));
        w.WriteEndObject();
    }
    public override Vector3 ReadJson(JsonReader r, Type t, Vector3 e, bool has, JsonSerializer s)
    {
        var o = JObject.Load(r);
        return new Vector3(Sf.F(o, "x"), Sf.F(o, "y"), Sf.F(o, "z"));
    }
}

// ─────────────────────────────────────────────────────────────
// Quaternion — guards against the (0,0,0,0) degenerate case
// ─────────────────────────────────────────────────────────────
public class QuaternionConverter : JsonConverter<Quaternion>
{
    public override void WriteJson(JsonWriter w, Quaternion q, JsonSerializer s)
    {
        // Normalise degenerate quaternions before writing
        if (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) q = Quaternion.identity;
        w.WriteStartObject();
        w.WritePropertyName("x"); w.WriteValue(Sf.S(q.x));
        w.WritePropertyName("y"); w.WriteValue(Sf.S(q.y));
        w.WritePropertyName("z"); w.WriteValue(Sf.S(q.z));
        w.WritePropertyName("w"); w.WriteValue(Sf.S(q.w));
        w.WriteEndObject();
    }
    public override Quaternion ReadJson(JsonReader r, Type t, Quaternion e, bool has, JsonSerializer s)
    {
        var o = JObject.Load(r);
        var q = new Quaternion(Sf.F(o, "x"), Sf.F(o, "y"), Sf.F(o, "z"), Sf.F(o, "w"));
        return (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) ? Quaternion.identity : q;
    }
}

// ─────────────────────────────────────────────────────────────
// Matrix4x4 — stored as named row-major fields e00..e33
// e.g. { "e00": m[0,0], "e01": m[0,1], ..., "e33": m[3,3] }
// ─────────────────────────────────────────────────────────────
public class Matrix4x4Converter : JsonConverter<Matrix4x4>
{
    public override void WriteJson(JsonWriter w, Matrix4x4 m, JsonSerializer s)
    {
        w.WriteStartObject();
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
            {
                w.WritePropertyName($"e{row}{col}");
                w.WriteValue(Sf.S(m[row, col]));
            }
        w.WriteEndObject();
    }
    public override Matrix4x4 ReadJson(JsonReader r, Type t, Matrix4x4 e, bool has, JsonSerializer s)
    {
        var o = JObject.Load(r);
        Matrix4x4 m = Matrix4x4.identity;
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
                m[row, col] = Sf.F(o, $"e{row}{col}");
        return m;
    }
}

// ─────────────────────────────────────────────────────────────
// Shared serializer factory
// ─────────────────────────────────────────────────────────────
public static class UnityJsonSerializer
{
    private static readonly JsonConverter[] _converters = new JsonConverter[]
    {
        new Vector2IntConverter(),
        new Vector2Converter(),
        new Vector3Converter(),
        new QuaternionConverter(),
        new Matrix4x4Converter(),
    };

    public static JsonSerializerSettings Settings { get; } = new JsonSerializerSettings
    {
        Converters = _converters,
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
        FloatFormatHandling = FloatFormatHandling.DefaultValue,  // NaN/Inf → 0 (fallback safety)
    };

    public static string Serialize(object obj, bool pretty = false)
    {
        var s = new JsonSerializerSettings
        {
            Converters = _converters,
            Formatting = pretty ? Formatting.Indented : Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            FloatFormatHandling = FloatFormatHandling.DefaultValue,
        };
        return JsonConvert.SerializeObject(obj, s);
    }

    public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
}
