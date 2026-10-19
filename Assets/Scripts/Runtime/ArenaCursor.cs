using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Hardware pointers keep their response independent of rendering and network snapshots.
    public sealed class ArenaCursor : IDisposable
    {
        public enum Kind { Arrow, Hand, Inspect, Grab, Grabbing, Move, Text }
        const int Size = 48;
        readonly Dictionary<int, Texture2D> textures = new Dictionary<int, Texture2D>();
        readonly List<Burst> bursts = new List<Burst>();
        readonly Color32 ink = new Color32(19, 34, 37, 255);
        readonly Color32 gold = new Color32(230, 182, 83, 255);
        readonly Color32 soul = new Color32(107, 242, 209, 255);
        readonly Color32 light = new Color32(239, 255, 237, 255);
        Texture2D ripple;
        int current = -1;
        float releasedAt = -10;
        bool wasHeld, active;
        struct Burst { public Vector2 position; public float started; }

        public void Update(Kind kind, Vector2 pointer, bool pressed, bool held)
        {
            bool inside = pointer.x >= 0 && pointer.y >= 0 && pointer.x < Screen.width && pointer.y < Screen.height;
            if (!Application.isFocused || !inside) { Suspend(); return; }
            active = true;
            if (pressed)
            {
                if (bursts.Count >= 8) bursts.RemoveAt(0);
                bursts.Add(new Burst { position = pointer, started = Time.unscaledTime });
            }
            if (wasHeld && !held) releasedAt = Time.unscaledTime;
            wasHeld = held;
            int pose = held ? 1 : Time.unscaledTime - releasedAt < .11f ? 2 : 0;
            Apply(kind, pose);
        }

        void Apply(Kind kind, int pose)
        {
            int key = (int)kind * 3 + pose;
            if (key == current) return;
            Texture2D texture;
            if (!textures.TryGetValue(key, out texture))
            {
                texture = Create(kind, pose);
                textures.Add(key, texture);
            }
            Vector2 hotspot = kind == Kind.Inspect || kind == Kind.Move ? new Vector2(16, 16) :
                kind == Kind.Text ? new Vector2(10, 17) : new Vector2(4, 4);
            Cursor.SetCursor(texture, hotspot, CursorMode.Auto);
            Cursor.visible = true;
            current = key;
        }

        internal Texture2D VerifyAndCreatePreview()
        {
            const int cell = 64, columns = 7, rows = 3;
            var previewPixels = new Color32[cell * columns * cell * rows];
            for (int pose = 0; pose < rows; pose++) for (int mode = 0; mode < columns; mode++)
            {
                Apply((Kind)mode, pose);
                var pixels = textures[mode * 3 + pose].GetPixels32();
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                    previewPixels[((rows - 1 - pose) * cell + y + 8) * columns * cell + mode * cell + x + 8] = pixels[y * Size + x];
            }
            var preview = new Texture2D(cell * columns, cell * rows, TextureFormat.RGBA32, false) { name = "Soulforge cursor preview", filterMode = FilterMode.Point };
            preview.SetPixels32(previewPixels); preview.Apply(false, false);
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); current = -1;
            return preview;
        }

        public void Draw()
        {
            if (!active || Event.current.type != EventType.Repaint) return;
            if (ripple == null) ripple = CreateRipple();
            Color old = GUI.color;
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                Burst b = bursts[i];
                float age = (Time.unscaledTime - b.started) / .42f;
                if (age >= 1) { bursts.RemoveAt(i); continue; }
                float size = Mathf.Lerp(12, 58, 1 - Mathf.Pow(1 - age, 3));
                GUI.color = new Color(.64f, 1, .86f, (1 - age) * .62f);
                GUI.DrawTexture(new Rect(b.position.x - size / 2, Screen.height - b.position.y - size / 2, size, size), ripple);
            }
            GUI.color = old;
        }

        public void Suspend()
        {
            if (active) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Cursor.visible = true;
            active = false; current = -1; wasHeld = false; bursts.Clear();
        }

        public void Dispose()
        {
            Suspend();
            foreach (Texture2D texture in textures.Values) UnityEngine.Object.Destroy(texture);
            textures.Clear();
            if (ripple != null) UnityEngine.Object.Destroy(ripple);
        }

        Texture2D Create(Kind kind, int pose)
        {
            var pixels = new Color32[Size * Size];
            Action<int, int, int, int, Color32> box = (x, y, w, h, color) =>
            {
                for (int yy = Mathf.Max(0, y); yy < Mathf.Min(Size, y + h); yy++)
                    for (int xx = Mathf.Max(0, x); xx < Mathf.Min(Size, x + w); xx++)
                        pixels[(Size - 1 - yy) * Size + xx] = color;
            };
            Action<int, int, int, int, Color32, int> line = (x, y, tx, ty, color, thickness) =>
            {
                int dx = Math.Abs(tx - x), sx = x < tx ? 1 : -1, dy = -Math.Abs(ty - y), sy = y < ty ? 1 : -1, error = dx + dy;
                for (;;) { box(x, y, thickness, thickness, color); if (x == tx && y == ty) break; int e = error * 2; if (e >= dy) { error += dy; x += sx; } if (e <= dx) { error += dx; y += sy; } }
            };
            Color32 face = pose == 1 ? gold : soul;
            if (kind == Kind.Arrow)
            {
                for (int y = 4; y <= 29; y++)
                {
                    int span = Mathf.RoundToInt((y - 4) * .82f);
                    box(4, y, Mathf.Min(span + 3, 23), 1, ink);
                    if (y < 24) box(6, y + 1, Mathf.Max(1, Mathf.Min(span - 1, 17)), 1, gold);
                    if (y < 20) box(8, y + 3, Mathf.Max(1, Mathf.Min(span - 4, 11)), 1, face);
                }
                line(15, 23, 21, 35, ink, 6); line(16, 23, 22, 34, gold, 3);
                line(8, 10, 9, 20, light, 2); box(5, 27, 7, 5, ink);
            }
            else if (kind == Kind.Hand || kind == Kind.Grab || kind == Kind.Grabbing)
            {
                bool closed = kind == Kind.Grabbing || pose == 1;
                box(4, 4, 8, closed ? 18 : 24, ink); box(6, 6, 4, closed ? 13 : 19, face);
                box(10, closed ? 11 : 17, 21, 18, ink); box(12, closed ? 13 : 19, 17, 13, gold);
                for (int i = 0; i < 3; i++)
                {
                    int yy = closed ? 10 + i : 12 + i * 3;
                    box(12 + i * 6, yy, 7, 15, ink); box(14 + i * 6, yy + 2, 3, 11, face);
                }
                box(12, 30, 16, 6, ink); box(14, 31, 12, 3, gold); box(17, 25, 6, 4, light);
                if (kind == Kind.Grab && !closed) { box(33, 12, 3, 9, soul); box(30, 15, 9, 3, soul); }
            }
            else if (kind == Kind.Inspect)
            {
                for (int y = 2; y < 31; y++) for (int x = 2; x < 31; x++)
                {
                    int d = (x - 16) * (x - 16) + (y - 16) * (y - 16);
                    if (d <= 169 && d >= 81) box(x, y, 1, 1, d > 121 ? ink : gold);
                }
                box(9, 13, 14, 6, ink); box(14, 10, 5, 12, face); box(15, 12, 2, 4, light);
                line(26, 26, 35, 35, ink, 7); line(27, 27, 35, 35, gold, 3);
            }
            else if (kind == Kind.Move)
            {
                line(16, 3, 16, 30, ink, 4); line(3, 16, 30, 16, ink, 4);
                line(17, 5, 17, 29, face, 2); line(5, 17, 29, 17, face, 2);
                line(16, 3, 10, 9, gold, 3); line(16, 3, 22, 9, gold, 3);
                line(3, 16, 9, 10, gold, 3); line(3, 16, 9, 22, gold, 3);
                line(30, 16, 24, 10, gold, 3); line(30, 16, 24, 22, gold, 3);
                line(16, 30, 10, 24, gold, 3); line(16, 30, 22, 24, gold, 3);
                box(13, 13, 8, 8, ink); box(15, 15, 4, 4, light);
            }
            else
            {
                box(7, 3, 9, 3, ink); box(10, 6, 3, 24, ink); box(7, 30, 9, 3, ink);
                box(8, 4, 7, 1, gold); box(11, 6, 1, 24, face); box(8, 31, 7, 1, gold);
            }
            if (pose == 2) { box(32, 3, 2, 6, gold); box(30, 5, 6, 2, light); }
            if (pose != 0)
            {
                var bounced = new Color32[pixels.Length];
                Vector2 pivot = kind == Kind.Inspect || kind == Kind.Move ? new Vector2(16, 16) : kind == Kind.Text ? new Vector2(10, 17) : new Vector2(4, 4);
                float stretchX = pose == 1 ? .9f : 1.06f, stretchY = pose == 1 ? .87f : 1.09f;
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                {
                    int sourceX = Mathf.RoundToInt(pivot.x + (x - pivot.x) / stretchX);
                    int sourceY = Mathf.RoundToInt(pivot.y + (y - pivot.y) / stretchY);
                    if (sourceX >= 0 && sourceX < Size && sourceY >= 0 && sourceY < Size)
                        bounced[(Size - 1 - y) * Size + x] = pixels[(Size - 1 - sourceY) * Size + sourceX];
                }
                pixels = bounced;
            }
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Soulforge cursor " + kind + " " + pose, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(pixels); texture.Apply(false, false);
            return texture;
        }

        static Texture2D CreateRipple()
        {
            const int edge = 64;
            var pixels = new Color32[edge * edge];
            for (int y = 0; y < edge; y++) for (int x = 0; x < edge; x++)
            {
                int distance = Math.Abs(x - 32) + Math.Abs(y - 32);
                if (distance >= 26 && distance <= 28) pixels[y * edge + x] = Color.white;
            }
            var texture = new Texture2D(edge, edge, TextureFormat.RGBA32, false) { name = "Soulforge click ripple", filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(pixels); texture.Apply(false, true); return texture;
        }
    }
}
