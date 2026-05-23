using System.Collections.Generic;

namespace OpenUtau.Core.Render {
    class RenderCache {
        class Node {
            public uint hash;
            public byte[] data;

            public Node prev;
            public Node next;
        }

        private readonly int capacity;
        private int size;
        private readonly Node dummyHead;
        private readonly Node dummyTail;
        private readonly Dictionary<uint, Node> dict;
        private readonly object lockObj = new object();

        public RenderCache(int capacity) {
            this.capacity = capacity;
            size = 0;
            dummyHead = new Node();
            dummyTail = new Node();
            dummyHead.next = dummyTail;
            dummyTail.prev = dummyHead;
            dict = new Dictionary<uint, Node>();
        }

        public byte[] Get(uint hash) {
            lock (lockObj) {
                if (dict.TryGetValue(hash, out Node node)) {
                    Remove(node);
                    AddToLast(node);
                    return node.data;
                }
                return null;
            }
        }

        public void Put(uint hash, byte[] data) {
            lock (lockObj) {
                if (dict.TryGetValue(hash, out Node node)) {
                    node.data = data;
                    Remove(node);
                    AddToLast(node);
                } else {
                    while (size >= capacity) {
                        dict.Remove(dummyHead.next.hash);
                        Remove(dummyHead.next);
                        --size;
                    }
                    Node newNode = new Node {
                        hash = hash,
                        data = data,
                    };
                    dict.Add(hash, newNode);
                    AddToLast(newNode);
                    ++size;
                }
            }
        }

        public void Clear() {
            size = 0;
            dummyHead.next = dummyTail;
            dummyTail.prev = dummyHead;
            dict.Clear();
        }

        private void Remove(Node node) {
            node.next.prev = node.prev;
            node.prev.next = node.next;
        }

        private void AddToLast(Node node) {
            node.next = dummyTail;
            node.prev = dummyTail.prev;
            dummyTail.prev.next = node;
            dummyTail.prev = node;
        }
    }
}
