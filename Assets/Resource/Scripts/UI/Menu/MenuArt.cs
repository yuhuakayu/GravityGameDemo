using System;
using System.Collections.Generic;
using UnityEngine;

namespace Resource.Scripts.UI.Menu
{
    public sealed class MenuArt : ScriptableObject
    {
        [Serializable]
        public struct Entry { public string key; public Sprite[] frames; }
        public Entry[] sprites;
        public TextAsset logoLetters;
        public TextAsset blobs;
        public Shader blobShader;
        private Dictionary<string, Sprite[]> _lookup;

        public Sprite Get(string key, int frame = 0)
        {
            if (_lookup == null)
            {
                _lookup = new Dictionary<string, Sprite[]>();
                foreach (var entry in sprites) _lookup[entry.key] = entry.frames;
            }
            var frames = _lookup[key];
            return frames[frame % frames.Length];
        }
    }
}
