using System;
using System.Collections.Generic;

namespace SimCore
{
    /// <summary>
    /// Display text by key ("ui.build" -> "Build"), read from a flat JSON file { "key": "text", ... }.
    /// Texts may hold {0}, {1}... placeholders. A missing key shows the key itself (and is reported once), so a gap is visible but never fatal.
    /// To add a language, add another file with the same keys: no code changes.
    /// </summary>
    public sealed class StringTable
    {
        private readonly Dictionary<string, string> _texts;
        private readonly HashSet<string> _reported = new HashSet<string>();

        /// <summary>Called once for every key that was asked for but is missing.</summary>
        public Action<string> OnMissing;

        public StringTable(string json)
        {
            var map = MiniJson.Parse(json) as Dictionary<string, object>;
            if (map == null) throw new GameDataException("strings: expected an object { \"key\": \"text\", ... }");
            _texts = new Dictionary<string, string>(map.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in map)
            {
                if (!(pair.Value is string text)) throw new GameDataException($"strings.{pair.Key}: expected a text value");
                _texts[pair.Key] = text;
            }
        }

        public int Count => _texts.Count;

        public bool Has(string key) => _texts.ContainsKey(key);

        public IEnumerable<string> Keys => _texts.Keys;

        public string Get(string key)
        {
            if (_texts.TryGetValue(key, out string text)) return text;
            if (_reported.Add(key)) OnMissing?.Invoke(key);
            return key;
        }

        public string Get(string key, params object[] args)
        {
            string text = Get(key);
            if (args == null || args.Length == 0) return text;
            try { return string.Format(text, args); }
            catch (FormatException) { return text; }
        }
    }
}
