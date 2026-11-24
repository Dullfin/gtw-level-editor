using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// Reads and writes level files. Unity's JsonUtility in this game drops lists of our own
    /// classes (a level's objects, a ramp's points), so the lists are written and split by hand
    /// and JsonUtility only handles single flat objects.
    /// </summary>
    public static class LevelJson
    {
        public static string LevelToJson(LevelData level)
        {
            var sb = new StringBuilder();
            sb.Append(OpenObject(JsonUtility.ToJson(level)));
            sb.Append(",\n  \"objects\": [");
            for (int i = 0; i < level.objects.Count; i++)
            {
                sb.Append(i == 0 ? "\n    " : ",\n    ");
                sb.Append(ObjectToJson(level.objects[i]));
            }
            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }

        public static LevelData LevelFromJson(string json)
        {
            var level = JsonUtility.FromJson<LevelData>(json);
            if (level == null) throw new FormatException("empty level file");
            level.objects = new List<LevelObjectData>();
            if (Members(json).TryGetValue("objects", out var raw))
                foreach (var item in Items(raw)) level.objects.Add(ObjectFromJson(item));
            return level;
        }

        /// <summary>One object on one line, ramp points included.</summary>
        public static string ObjectToJson(LevelObjectData d)
        {
            var sb = new StringBuilder(OpenObject(JsonUtility.ToJson(d)));
            if (d.points != null && d.points.Count > 0)
            {
                sb.Append(",\"points\":[");
                for (int i = 0; i < d.points.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(JsonUtility.ToJson(d.points[i]));
                }
                sb.Append(']');
            }
            sb.Append('}');
            return sb.ToString();
        }

        public static LevelObjectData ObjectFromJson(string json)
        {
            var d = JsonUtility.FromJson<LevelObjectData>(json);
            d.points = new List<RampPoint>();
            if (Members(json).TryGetValue("points", out var raw))
                foreach (var item in Items(raw)) d.points.Add(JsonUtility.FromJson<RampPoint>(item));
            return d;
        }

        /// <summary>A JSON object's text without its closing brace, so more members can be appended.</summary>
        private static string OpenObject(string json)
        {
            int end = json.LastIndexOf('}');
            return json.Substring(0, end).TrimEnd();
        }

        // ---------------- a tiny scanner: raw member values and array items ----------------

        /// <summary>The raw text of each member of a JSON object, by name.</summary>
        private static Dictionary<string, string> Members(string json)
        {
            var result = new Dictionary<string, string>();
            int i = SkipSpace(json, 0);
            if (i >= json.Length || json[i] != '{') return result;
            i++;
            while (true)
            {
                i = SkipSpace(json, i);
                if (i >= json.Length || json[i] == '}') break;
                int keyEnd = SkipValue(json, i);
                string key = json.Substring(i + 1, keyEnd - i - 2);
                i = SkipSpace(json, keyEnd);
                i++; // ':'
                i = SkipSpace(json, i);
                int valueEnd = SkipValue(json, i);
                result[key] = json.Substring(i, valueEnd - i);
                i = SkipSpace(json, valueEnd);
                if (i < json.Length && json[i] == ',') i++;
            }
            return result;
        }

        /// <summary>The raw text of each item of a JSON array.</summary>
        private static List<string> Items(string json)
        {
            var result = new List<string>();
            int i = SkipSpace(json, 0);
            if (i >= json.Length || json[i] != '[') return result;
            i++;
            while (true)
            {
                i = SkipSpace(json, i);
                if (i >= json.Length || json[i] == ']') break;
                int end = SkipValue(json, i);
                result.Add(json.Substring(i, end - i));
                i = SkipSpace(json, end);
                if (i < json.Length && json[i] == ',') i++;
            }
            return result;
        }

        private static int SkipSpace(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            return i;
        }

        /// <summary>Index just past the value (string, object, array, number or literal) starting at i.</summary>
        private static int SkipValue(string s, int i)
        {
            if (s[i] == '"')
            {
                i++;
                while (i < s.Length && s[i] != '"') i += s[i] == '\\' ? 2 : 1;
                return i + 1;
            }
            if (s[i] == '{' || s[i] == '[')
            {
                int depth = 0;
                for (; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '"') { i = SkipValue(s, i) - 1; continue; }
                    if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') { depth--; if (depth == 0) return i + 1; }
                }
                return s.Length;
            }
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i])) i++;
            return i;
        }
    }
}
