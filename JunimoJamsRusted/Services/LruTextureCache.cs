using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;

namespace JunimoJamsRusted.Services
{
    public sealed class LruTextureCache : IDisposable
    {
        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<CacheItem>> _map;
        private readonly LinkedList<CacheItem> _list;
        private readonly object _lock = new();

        private sealed class CacheItem
        {
            public string Key { get; }
            public Texture2D Texture { get; }

            public CacheItem(string key, Texture2D texture)
            {
                this.Key = key;
                this.Texture = texture;
            }
        }

        public LruTextureCache(int capacity = 50)
        {
            this._capacity = Math.Max(5, capacity);
            this._map = new Dictionary<string, LinkedListNode<CacheItem>>(this._capacity, StringComparer.Ordinal);
            this._list = new LinkedList<CacheItem>();
        }

        public bool TryGet(string key, out Texture2D? texture)
        {
            lock (this._lock)
            {
                if (this._map.TryGetValue(key, out var node))
                {
                    if (node.Value.Texture.IsDisposed)
                    {
                        this._list.Remove(node);
                        this._map.Remove(key);
                        texture = null;
                        return false;
                    }

                    // Move to front (most recently used)
                    this._list.Remove(node);
                    this._list.AddFirst(node);
                    texture = node.Value.Texture;
                    return true;
                }
            }

            texture = null;
            return false;
        }

        public void Put(string key, Texture2D texture)
        {
            Texture2D? evictedTexture = null;

            lock (this._lock)
            {
                if (this._map.TryGetValue(key, out var existingNode))
                {
                    this._list.Remove(existingNode);
                    if (!ReferenceEquals(existingNode.Value.Texture, texture))
                    {
                        evictedTexture = existingNode.Value.Texture;
                    }
                }
                else if (this._map.Count >= this._capacity)
                {
                    // Evict least recently used (last item)
                    var last = this._list.Last;
                    if (last != null)
                    {
                        this._list.RemoveLast();
                        this._map.Remove(last.Value.Key);
                        evictedTexture = last.Value.Texture;
                    }
                }

                var newNode = new LinkedListNode<CacheItem>(new CacheItem(key, texture));
                this._list.AddFirst(newNode);
                this._map[key] = newNode;
            }

            // Dispose evicted texture outside lock to avoid blocking
            evictedTexture?.Dispose();
        }

        public void Clear()
        {
            List<Texture2D> toDispose;
            lock (this._lock)
            {
                toDispose = new List<Texture2D>(this._map.Count);
                foreach (var node in this._list)
                {
                    toDispose.Add(node.Texture);
                }
                this._map.Clear();
                this._list.Clear();
            }

            foreach (var tex in toDispose)
            {
                tex.Dispose();
            }
        }

        public void Dispose()
        {
            this.Clear();
        }
    }
}
