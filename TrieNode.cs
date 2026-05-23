using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenUtau.Core.Vogen {
    class TrieNode {
        public Dictionary<string, TrieNode> children = new Dictionary<string, TrieNode>();
        public string[]? pinyins = null;

        public static TrieNode LoadDictionary(IEnumerable<string> lines) {
            TrieNode root = new TrieNode();
            foreach (var line in lines.Skip(1).Reverse()) {
                var parts = line.Trim().Split(',');
                if (parts.Length >= 2) {
                    var etor = StringInfo.GetTextElementEnumerator(parts[0]);
                    BuildTrie(root, etor, parts[1]);
                }
            }
            return root;
        }

        private static void BuildTrie(TrieNode node, TextElementEnumerator etor, string pinyin) {
            if (!etor.MoveNext()) {
                node.pinyins = pinyin.Split();
                return;
            }
            string hanzi = etor.GetTextElement();
            if (!node.children.TryGetValue(hanzi, out var child)) {
                node.children[hanzi] = child = new TrieNode();
            }
            BuildTrie(child, etor, pinyin);
        }

        public string[]? Query(Span<string> text) {
            string[]? pinyins = null;
            int index = 0;
            TrieNode node = this;
            while (index < text.Length && node.children.TryGetValue(text[index], out var child)) {
                if (child.pinyins != null) {
                    pinyins = child.pinyins;
                }
                node = child;
                index++;
            }
            return pinyins;
        }
    }
}
