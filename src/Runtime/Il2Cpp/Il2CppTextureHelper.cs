#if CPP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
#if INTEROP
using Il2CppInterop.Runtime.InteropTypes.Arrays;
#else
using UnhollowerBaseLib;
#endif

namespace UniverseLib.Runtime.Il2Cpp
{
    internal class Il2CppTextureHelper : TextureHelper
    {
        internal delegate IntPtr d_EncodeToPNG(IntPtr tex);

        internal delegate void d_Blit2(IntPtr source, IntPtr dest);

        internal delegate IntPtr d_CreateSprite(IntPtr texture, ref Rect rect, ref Vector2 pivot, float pixelsPerUnit,
            uint extrude, int meshType, ref Vector4 border, bool generateFallbackPhysicsShape);

        internal delegate void d_CopyTexture_Region(IntPtr src, int srcElement, int srcMip, int srcX, int srcY, 
            int srcWidth, int srcHeight, IntPtr dst, int dstElement, int dstMip, int dstX, int dstY);

        protected internal override Texture2D Internal_NewTexture2D(int width, int height)
        {
            return new(width, height, TextureFormat.RGBA32, 1, false, IntPtr.Zero);
        }

        protected internal override Texture2D Internal_NewTexture2D(int width, int height, TextureFormat textureFormat, bool mipChain)
        {
            return new(width, height, textureFormat, mipChain ? -1 : 1, false, IntPtr.Zero);
        }

        protected internal override void Internal_Blit(Texture tex, RenderTexture rt)
        {
            ICallManager.GetICall<d_Blit2>("UnityEngine.Graphics::Blit2")
                .Invoke(tex.Pointer, rt.Pointer);
        }

        protected internal override byte[] Internal_EncodeToPNG(Texture2D tex)
        {
            IntPtr arrayPtr = ICallManager.GetICall<d_EncodeToPNG>("UnityEngine.ImageConversion::EncodeToPNG")
                .Invoke(tex.Pointer);

            return arrayPtr == IntPtr.Zero ? null : new Il2CppStructArray<byte>(arrayPtr);
        }

        protected internal override Sprite Internal_CreateSprite(Texture2D texture)
            => CreateSpriteImpl(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero, 100f, 0u, Vector4.zero);

        protected internal override Sprite Internal_CreateSprite(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, Vector4 border)
             => CreateSpriteImpl(texture, rect, pivot, pixelsPerUnit, extrude, border);

        internal static Sprite CreateSpriteImpl(Texture texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, Vector4 border)
        {
            IntPtr spritePtr = ICallManager.GetICall<d_CreateSprite>("UnityEngine.Sprite::CreateSprite_Injected")
                .Invoke(texture.Pointer, ref rect, ref pivot, pixelsPerUnit, extrude, 1, ref border, false);

            return spritePtr == IntPtr.Zero ? null : new Sprite(spritePtr);
        }

        // ICall delegate for Font.CreateDynamicFontFromOSFont
        internal delegate IntPtr d_CreateDynamicFontFromOSFont(IntPtr fontname, int size);

        /// <summary>
        /// Create a dynamic Font from a system font name via ICall.
        /// Bypasses managed method stripping on IL2CPP.
        /// Returns null if the ICall is not available.
        /// </summary>
        public static Font CreateDynamicFontFromOSFontICall(string fontName, int size)
        {
            try
            {
                var func = ICallManager.GetICallUnreliable<d_CreateDynamicFontFromOSFont>(
                    "UnityEngine.Font::Internal_CreateDynamicFont");

                // Convert managed string to Il2Cpp string pointer
#if INTEROP
                IntPtr strPtr = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(fontName);
#else
                IntPtr strPtr = UnhollowerBaseLib.IL2CPP.ManagedStringToIl2Cpp(fontName);
#endif
                IntPtr result = func(strPtr, size);

                if (result != IntPtr.Zero)
                    return new Font(result);
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"[Il2CppTextureHelper] CreateDynamicFontICall failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Set pixels on a texture using IL2CPP-compatible SetPixels32.
        /// </summary>
        public static void SetPixels32IL2CPP(Texture2D texture, Color32[] colors)
        {
            if (texture == null || colors == null) return;
            var il2cppColors = new Il2CppStructArray<Color32>(colors.Length);
            for (int i = 0; i < colors.Length; i++)
                il2cppColors[i] = colors[i];
            texture.SetPixels32(il2cppColors);
        }

        /// <summary>
        /// Set Font.characterInfo using proper IL2CPP struct array.
        /// This is impossible from .NET Standard 2.0 code (struct marshaling fails).
        /// From IL2CPP-compiled code, Il2CppStructArray handles it correctly.
        /// </summary>
        /// <summary>
        /// Set Font.characterInfo from raw glyph data arrays.
        /// CharacterInfo properties don't marshal between managed/.NET Standard and IL2CPP,
        /// so we pass raw float/int arrays and build IL2CPP CharacterInfo structs here.
        /// Arrays: index, advance, uvL, uvR, uvT, uvB, minX, maxX, minY, maxY, glyphW, glyphH
        /// </summary>
        public static bool SetFontCharacterInfoFromRaw(Font font, int count,
            int[] indices, int[] advances,
            float[] uvL, float[] uvR, float[] uvT, float[] uvB,
            int[] minXs, int[] maxXs, int[] minYs, int[] maxYs,
            int[] glyphWs, int[] glyphHs)
        {
            if (font == null || count <= 0) return false;
            try
            {
                var il2cppArray = new Il2CppStructArray<CharacterInfo>(count);
                for (int i = 0; i < count; i++)
                {
                    var ci = new CharacterInfo();
                    ci.index = indices[i];
                    ci.advance = advances[i];
                    ci.uvBottomLeft = new Vector2(uvL[i], uvB[i]);
                    ci.uvBottomRight = new Vector2(uvR[i], uvB[i]);
                    ci.uvTopLeft = new Vector2(uvL[i], uvT[i]);
                    ci.uvTopRight = new Vector2(uvR[i], uvT[i]);
                    ci.minX = minXs[i];
                    ci.maxX = maxXs[i];
                    ci.minY = minYs[i];
                    ci.maxY = maxYs[i];
                    ci.glyphWidth = glyphWs[i];
                    ci.glyphHeight = glyphHs[i];

                    // Debug: check values BEFORE writing to array
                    if (i == 0)
                    {
                        Universe.Log($"[FontHelper] Before array write CI[0]: advance={ci.advance}, " +
                            $"minX={ci.minX}, maxX={ci.maxX}, uvBL={ci.uvBottomLeft}, uvTR={ci.uvTopRight}, " +
                            $"raw uvL={uvL[0]}, uvR={uvR[0]}, uvT={uvT[0]}, uvB={uvB[0]}");
                    }

                    il2cppArray[i] = ci;

                    // Debug: read back immediately
                    if (i == 0)
                    {
                        var rb = il2cppArray[0];
                        Universe.Log($"[FontHelper] After array write CI[0]: advance={rb.advance}, " +
                            $"minX={rb.minX}, maxX={rb.maxX}, uvBL={rb.uvBottomLeft}, uvTR={rb.uvTopRight}");
                    }
                }
                font.characterInfo = il2cppArray;

                var readBack = font.characterInfo;
                Universe.Log($"[FontHelper] Set {count} chars, readback={readBack?.Length ?? -1}");

                // Verify with a non-space character (find first with maxX > 0)
                if (readBack != null && readBack.Length > 1)
                {
                    for (int v = 0; v < readBack.Length && v < 50; v++)
                    {
                        if (readBack[v].maxX != 0)
                        {
                            var c = readBack[v];
                            Universe.Log($"[FontHelper] CI[{v}]: advance={c.advance}, minX={c.minX}, maxX={c.maxX}, " +
                                $"uvBL={c.uvBottomLeft}, uvTR={c.uvTopRight}");
                            return true;
                        }
                    }
                }
                return readBack != null && readBack.Length > 0;
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"[FontHelper] SetFontCharacterInfoFromRaw failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Set Font.fontNames using IL2CPP string array.
        /// </summary>
        public static bool SetFontNames(Font font, string[] names)
        {
            if (font == null || names == null) return false;
            try
            {
                var il2cppNames = new Il2CppStringArray(names);
                font.fontNames = il2cppNames;
                Universe.Log($"[FontHelper] Set fontNames: [{string.Join(", ", names)}]");
                return true;
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"[FontHelper] SetFontNames failed: {ex.Message}");
                return false;
            }
        }

        internal override bool Internal_CanForceReadCubemaps => true;

        internal override Texture Internal_CopyTexture(Texture src, int srcElement, int srcMip, int srcX, int srcY, 
            int srcWidth, int srcHeight, Texture dst, int dstElement, int dstMip, int dstX, int dstY)
        {
            ICallManager.GetICall<d_CopyTexture_Region>("UnityEngine.Graphics::CopyTexture_Region")
                .Invoke(src.Pointer, srcElement, srcMip, srcX, srcY, srcWidth, srcHeight, dst.Pointer, dstElement, dstMip, dstX, dstY);

            return dst;
        }
    }
}
#endif