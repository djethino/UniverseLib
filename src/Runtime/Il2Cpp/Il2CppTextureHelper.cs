#if CPP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UniverseLib.Config;

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
            => NewTexture(width, height, TextureFormat.RGBA32, false);

        protected internal override Texture2D Internal_NewTexture2D(int width, int height, TextureFormat textureFormat, bool mipChain)
            => NewTexture(width, height, textureFormat, mipChain);

        /// <summary>
        /// The four-argument constructor, and only that one.
        ///
        /// 🔴 <c>Texture2D(int, int, TextureFormat, int, bool, IntPtr)</c> is NOT present in every
        /// Unity version an IL2CPP game may be built with, and it was called unconditionally. On
        /// Unity 2022.3.62f2 the first texture threw <c>MissingMethodException</c> — thrown while a
        /// consumer was building its UI, so the whole construction aborted: panels made before it
        /// stayed on screen, panels after it never existed.
        ///
        /// 🔴 **And it cannot be probed for.** Reaching for it through reflection is worse than
        /// calling it: Il2CppInterop GENERATES the constructors the metadata declares, so
        /// <c>GetConstructor</c> finds one the native side does not have, and invoking it corrupts
        /// memory — an AccessViolationException that takes the game down with no message, where the
        /// direct call at least threw something catchable. Tried, and reverted, on the same game.
        ///
        /// The four-argument constructor has been there since Unity 4, is what the two-argument one
        /// resolves to anyway, and does the same thing: mipChain false means one mip level.
        /// </summary>
        private static Texture2D NewTexture(int width, int height, TextureFormat format, bool mipChain)
            => new Texture2D(width, height, format, mipChain);

        protected internal override void Internal_Blit(Texture tex, RenderTexture rt)
        {
            if (ConfigManager.Bypass_UniverseLib_ICall)
            {
                Graphics.Blit(tex, rt);
            }
            else
            {
                ICallManager.GetICall<d_Blit2>("UnityEngine.Graphics::Blit2")
                    .Invoke(tex.ToIl2CppPointer(), rt.ToIl2CppPointer());
            }
        }

        protected internal override byte[] Internal_EncodeToPNG(Texture2D tex)
        {
            IntPtr arrayPtr = ICallManager.GetICall<d_EncodeToPNG>("UnityEngine.ImageConversion::EncodeToPNG")
                .Invoke(tex.ToIl2CppPointer());

            return arrayPtr == IntPtr.Zero ? null : new Il2CppStructArray<byte>(arrayPtr);
        }

        protected internal override Sprite Internal_CreateSprite(Texture2D texture)
        {
            var rect = new Rect(0, 0, texture.width, texture.height);
            return
                ConfigManager.Bypass_UniverseLib_ICall ?
                Sprite.Create(texture, rect, Vector2.zero, 100f, 0u, SpriteMeshType.Tight, Vector4.zero) :
                CreateSpriteImpl(texture, rect, Vector2.zero, 100f, 0u, Vector4.zero);
        }

        protected internal override Sprite Internal_CreateSprite(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, Vector4 border)
        {
            return
                ConfigManager.Bypass_UniverseLib_ICall ?
                Sprite.Create(texture, rect, pivot, pixelsPerUnit, extrude, SpriteMeshType.Tight, border) :
                CreateSpriteImpl(texture, rect, pivot, pixelsPerUnit, extrude, border);
        }

        internal static Sprite CreateSpriteImpl(Texture texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, Vector4 border)
        {
            IntPtr spritePtr = ICallManager.GetICall<d_CreateSprite>("UnityEngine.Sprite::CreateSprite_Injected")
                .Invoke(texture.ToIl2CppPointer(), ref rect, ref pivot, pixelsPerUnit, extrude, 1, ref border, false);

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
        // Cached native stride of CharacterInfo (detected at runtime, -1 = not yet detected)
        private static int _characterInfoNativeStride = -1;
        // Offset from array Pointer to first element's advance field
        private static int _arrayHeaderSize = -1;

        /// <summary>
        /// Detect the native stride of CharacterInfo by writing known values
        /// to a 2-element array and scanning memory for the pattern.
        /// </summary>
        private static int DetectCharacterInfoStride()
        {
            if (_characterInfoNativeStride > 0) return _characterInfoNativeStride;

            try
            {
                // Use a real font to detect the stride — create a temp font with 2 chars
                // via RequestCharactersInTexture on a known dynamic font
                var testArr = new Il2CppStructArray<CharacterInfo>(2);

                // Write DIFFERENT markers for each field to identify which offset is which
                var ci0 = new CharacterInfo();
                ci0.index = 11111;
                ci0.advance = 22222;
                ci0.minX = 33333;
                ci0.maxX = 44444;
                testArr[0] = ci0;

                // Scan from array Pointer to find our marker in native memory.
                // Header size varies by IL2CPP version, so scan broadly.
                IntPtr arrayPtr = testArr.Pointer;

                // Scan for ALL markers to identify field positions
                int indexOff = -1, advanceOff = -1, minXOff = -1, maxXOff = -1;
                for (int off = 0; off < 1024; off += 4)
                {
                    int val = System.Runtime.InteropServices.Marshal.ReadInt32(arrayPtr + off);
                    if (val == 11111 && indexOff < 0) indexOff = off;
                    else if (val == 22222 && advanceOff < 0) advanceOff = off;
                    else if (val == 33333 && minXOff < 0) minXOff = off;
                    else if (val == 44444 && maxXOff < 0) maxXOff = off;
                }

                Universe.Log($"[FontHelper] Field scan: index=+0x{indexOff:X}, advance=+0x{advanceOff:X}, " +
                    $"minX=+0x{minXOff:X}, maxX=+0x{maxXOff:X}");

                // The smallest offset is the start of element 0
                int marker0Offset = -1;
                if (indexOff >= 0) marker0Offset = indexOff;
                if (advanceOff >= 0 && (marker0Offset < 0 || advanceOff < marker0Offset)) marker0Offset = advanceOff;
                if (minXOff >= 0 && (marker0Offset < 0 || minXOff < marker0Offset)) marker0Offset = minXOff;
                if (maxXOff >= 0 && (marker0Offset < 0 || maxXOff < marker0Offset)) marker0Offset = maxXOff;

                if (marker0Offset < 0)
                {
                    var dump = new System.Text.StringBuilder();
                    for (int off = 0; off < 256; off += 4)
                    {
                        int val = System.Runtime.InteropServices.Marshal.ReadInt32(arrayPtr + off);
                        if (val != 0) dump.Append($"+0x{off:X}={val} ");
                    }
                    Universe.LogWarning($"[FontHelper] No markers found. Dump: {dump}");
                    return -1;
                }

                // Calculate field offsets relative to element start
                int elemStart = marker0Offset; // smallest offset = first field
                _arrayHeaderSize = elemStart;
                Universe.Log($"[FontHelper] Element 0 starts at arrayPtr+0x{elemStart:X}");

                // arr[1] = ci1 doesn't work (IL2CPP struct array indexer broken for index > 0).
                // Instead, probe the stride by writing directly to memory at candidate offsets
                // and reading back via the managed indexer.
                // Compute stride from field positions:
                // index (int) is at elemStart. advance (float) should be at elemStart + 36
                // in the deprecated layout. Verify by scanning for advance marker (22222 as float).
                int advanceBits = BitConverter.ToInt32(BitConverter.GetBytes(22222.0f), 0);
                int advanceOffset = -1;
                for (int off = elemStart + 4; off < elemStart + 128; off += 4)
                {
                    int val = System.Runtime.InteropServices.Marshal.ReadInt32(arrayPtr + off);
                    if (val == advanceBits)
                    {
                        advanceOffset = off - elemStart; // offset within struct
                        break;
                    }
                }

                if (advanceOffset < 0)
                {
                    var dump = new System.Text.StringBuilder();
                    for (int off = elemStart; off < elemStart + 128; off += 4)
                    {
                        int val = System.Runtime.InteropServices.Marshal.ReadInt32(arrayPtr + off);
                        if (val != 0) dump.Append($"+0x{off:X}={val} ");
                    }
                    Universe.LogWarning($"[FontHelper] Cannot find advance field. Dump: {dump}");
                    return -1;
                }

                // Stride = advance offset + sizeof(advance) + sizeof(flipped) = advanceOffset + 8
                _characterInfoNativeStride = advanceOffset + 4 + 4; // advance(4) + flipped(4)
                Universe.Log($"[FontHelper] Detected layout: elemStart=+0x{elemStart:X}, " +
                    $"advance at struct+{advanceOffset}, stride={_characterInfoNativeStride}");
                return _characterInfoNativeStride;
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"[FontHelper] DetectCharacterInfoStride failed: {ex.Message}");
                return -1;
            }
        }

        public static bool SetFontCharacterInfoFromRaw(Font font, int count,
            int[] indices, int[] advances,
            float[] uvL, float[] uvR, float[] uvT, float[] uvB,
            int[] minXs, int[] maxXs, int[] minYs, int[] maxYs,
            int[] glyphWs, int[] glyphHs)
        {
            if (font == null || count <= 0) return false;
            try
            {
                // Detect native struct stride and element start offset
                int stride = DetectCharacterInfoStride();
                if (stride <= 0)
                {
                    // Stride was probed during DetectCharacterInfoStride — use _characterInfoNativeStride
                    stride = _characterInfoNativeStride;
                    if (stride <= 0 || _arrayHeaderSize <= 0)
                    {
                        Universe.LogWarning($"[FontHelper] Cannot determine CharacterInfo layout (stride={stride}, header={_arrayHeaderSize})");
                        return false;
                    }
                }

                // Create the array and write directly with deprecated CharacterInfo layout.
                // The deprecated layout (44 bytes per element) is:
                //   +0:  index (int) - character code
                //   +4:  uv.x (float) - UV left
                //   +8:  uv.y (float) - UV top
                //   +12: uv.width (float) - UV width
                //   +16: uv.height (float) - UV height (negative if flipped)
                //   +20: vert.x (float) - minX (bearing)
                //   +24: vert.y (float) - maxY (top of glyph, positive up)
                //   +28: vert.width (float) - glyph width (maxX - minX)
                //   +32: vert.height (float) - NEGATIVE glyph height
                //   +36: width (float) - advance width (FLOAT not int!)
                //   +40: flipped (int) - 0

                var il2cppArray = new Il2CppStructArray<CharacterInfo>(count);
                IntPtr arrayPtr = il2cppArray.Pointer;
                // Element 0 starts at _arrayHeaderSize (detected during stride detection)
                IntPtr dataStart = arrayPtr + _arrayHeaderSize;

                // The native setter reads elements at managed stride (52), not native stride (44).
                int managedSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(CharacterInfo));
                int writeStride = managedSize; // Use managed size as the stride

                // DISCOVER field layout within the 52-byte managed struct.
                // Write known values via managed API (arr[0] = ci), scan memory.
                var discoveryArr = new Il2CppStructArray<CharacterInfo>(1);
                IntPtr discPtr = discoveryArr.Pointer + _arrayHeaderSize;

                // Write truly unique values — no mathematical relationships between them
                var discCi = new CharacterInfo();
                discCi.index = 10001;
                discCi.advance = 20002;
                discCi.minX = 30003;
                discCi.maxX = 40004;
                discCi.minY = 50005;
                discCi.maxY = 60006;
                discCi.glyphWidth = 70007;
                discCi.glyphHeight = 80008;
                discCi.uvBottomLeft = new Vector2(0.101f, 0.202f);
                discCi.uvBottomRight = new Vector2(0.303f, 0.404f);
                discCi.uvTopLeft = new Vector2(0.505f, 0.606f);
                discCi.uvTopRight = new Vector2(0.707f, 0.808f);
                discoveryArr[0] = discCi;

                // Dump all 52+ bytes to find each field
                var layoutDump = new System.Text.StringBuilder();
                layoutDump.Append($"[FontHelper] Layout dump ({writeStride} bytes): ");
                for (int off = 0; off < writeStride + 8; off += 4)
                {
                    int ival = System.Runtime.InteropServices.Marshal.ReadInt32(discPtr + off);
                    float fval = BitConverter.ToSingle(BitConverter.GetBytes(ival), 0);
                    string label = "";
                    if (ival == 10001) label = " [index]";
                    else if (Math.Abs(fval - 20002f) < 1f) label = " [advance]";
                    else if (Math.Abs(fval - 30003f) < 1f) label = " [minX]";
                    else if (Math.Abs(fval - 40004f) < 1f) label = " [maxX]";
                    else if (Math.Abs(fval - 50005f) < 1f) label = " [minY]";
                    else if (Math.Abs(fval - 60006f) < 1f) label = " [maxY]";
                    else if (Math.Abs(fval - 70007f) < 1f) label = " [glyphW]";
                    else if (Math.Abs(fval - 80008f) < 1f) label = " [glyphH]";
                    else if (Math.Abs(fval - 0.101f) < 0.001f) label = " [uvBL.x]";
                    else if (Math.Abs(fval - 0.202f) < 0.001f) label = " [uvBL.y]";
                    else if (Math.Abs(fval - 0.303f) < 0.001f) label = " [uvBR.x]";
                    else if (Math.Abs(fval - 0.404f) < 0.001f) label = " [uvBR.y]";
                    else if (Math.Abs(fval - 0.505f) < 0.001f) label = " [uvTL.x]";
                    else if (Math.Abs(fval - 0.606f) < 0.001f) label = " [uvTL.y]";
                    else if (Math.Abs(fval - 0.707f) < 0.001f) label = " [uvTR.x]";
                    else if (Math.Abs(fval - 0.808f) < 0.001f) label = " [uvTR.y]";
                    else if (ival != 0) label = $" (f={fval:F4})";

                    if (ival != 0 || !string.IsNullOrEmpty(label))
                        layoutDump.Append($"+{off}{label} ");
                }
                Universe.Log(layoutDump.ToString());

                Universe.Log($"[FontHelper] Writing {count} chars at stride {writeStride}, dataStart=arrayPtr+0x{_arrayHeaderSize:X}");

                // Write each element using the managed setter at index 0 (which works),
                // then copy the raw bytes to the correct position.
                // This is universal: the managed setter handles all internal conversions
                // (modern API → deprecated layout), we just relocate the bytes.
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

                    // Write to index 0 (the only index that works via managed setter)
                    il2cppArray[0] = ci;

                    // Copy the bytes from element 0 to element i's correct position
                    if (i > 0)
                    {
                        IntPtr src = dataStart;
                        IntPtr dst = dataStart + (i * writeStride);
                        for (int b = 0; b < writeStride; b += 4)
                        {
                            int val = System.Runtime.InteropServices.Marshal.ReadInt32(src + b);
                            System.Runtime.InteropServices.Marshal.WriteInt32(dst + b, val);
                        }
                    }
                }

                // Verify element 0 in memory before assigning
                {
                    IntPtr elem0 = dataStart;
                    int idx0 = System.Runtime.InteropServices.Marshal.ReadInt32(elem0 + 0);
                    float uvx0 = BitConverter.ToSingle(BitConverter.GetBytes(System.Runtime.InteropServices.Marshal.ReadInt32(elem0 + 4)), 0);
                    float adv0 = BitConverter.ToSingle(BitConverter.GetBytes(System.Runtime.InteropServices.Marshal.ReadInt32(elem0 + 36)), 0);
                    Universe.Log($"[FontHelper] Elem0 verify: index={idx0} ('{(char)idx0}'), uv.x={uvx0:F4}, advance={adv0:F1}");
                }

                font.characterInfo = il2cppArray;

                // Verify
                CharacterInfo testCi;
                bool testResult = font.GetCharacterInfo((char)indices[0], out testCi);
                Universe.Log($"[FontHelper] After set (stride {writeStride}): GetCharacterInfo('{(char)indices[0]}')={testResult}, advance={testCi.advance}");

                if (!testResult)
                    Universe.LogWarning($"[FontHelper] CharacterInfo not accepted by font");
                return true;
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"[FontHelper] SetFontCharacterInfoFromRaw failed: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private static void WriteFloat(IntPtr addr, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, addr, 4);
        }

        private class CharInfoFieldOffsets
        {
            public int Index, Advance;
            public int UvBLx, UvBLy, UvBRx, UvBRy;
            public int UvTLx, UvTLy, UvTRx, UvTRy;
            public int MinX, MaxX, MinY, MaxY;
            public int GlyphW, GlyphH;
        }

        /// <summary>
        /// Discover field offsets within CharacterInfo relative to the advance field.
        /// Writes known marker values to element 0 and scans native memory.
        /// All offsets are relative to the advance field position (advance = 0).
        /// </summary>
        private static CharInfoFieldOffsets DiscoverFieldOffsets(
            Il2CppStructArray<CharacterInfo> arr, IntPtr arrayPtr, int stride)
        {
            var offsets = new CharInfoFieldOffsets();
            int advBase = _arrayHeaderSize; // offset of advance field from arrayPtr

            // Scan range: from advBase - stride to advBase + stride (covers the full struct)
            int scanStart = Math.Max(0, advBase - stride);
            int scanEnd = advBase + stride;

            // Helper: write a struct with ONE field set, find its offset relative to advance
            Action<Action<CharacterInfo>, int, Action<CharInfoFieldOffsets, int>> findInt =
                (setter, marker, storeFn) =>
                {
                    var ci = new CharacterInfo();
                    setter(ci);
                    arr[0] = ci;
                    for (int off = scanStart; off < scanEnd; off += 4)
                    {
                        int val = System.Runtime.InteropServices.Marshal.ReadInt32(arrayPtr + off);
                        if (val == marker)
                        {
                            storeFn(offsets, off - advBase); // relative to advance
                            return;
                        }
                    }
                };

            findInt(ci => ci.index = 99887, 99887, (o, off) => o.Index = off);
            findInt(ci => ci.advance = 77665, 77665, (o, off) => { }); // advance = 0 by definition
            findInt(ci => ci.minX = 55443, 55443, (o, off) => o.MinX = off);
            findInt(ci => ci.maxX = 33221, 33221, (o, off) => o.MaxX = off);
            findInt(ci => ci.minY = 11009, 11009, (o, off) => o.MinY = off);
            findInt(ci => ci.maxY = 88776, 88776, (o, off) => o.MaxY = off);
            findInt(ci => ci.glyphWidth = 66554, 66554, (o, off) => o.GlyphW = off);
            findInt(ci => ci.glyphHeight = 44332, 44332, (o, off) => o.GlyphH = off);

            float uvMarker = 0.123456f;
            int uvMarkerBits = BitConverter.ToInt32(BitConverter.GetBytes(uvMarker), 0);

            Action<Action<CharacterInfo>, Action<CharInfoFieldOffsets, int>> findFloat =
                (setter, storeFn) =>
                {
                    var ci = new CharacterInfo();
                    setter(ci);
                    arr[0] = ci;
                    for (int off = scanStart; off < scanEnd; off += 4)
                    {
                        int val = System.Runtime.InteropServices.Marshal.ReadInt32(arrayPtr + off);
                        if (val == uvMarkerBits)
                        {
                            storeFn(offsets, off - advBase); // relative to advance
                            return;
                        }
                    }
                };

            findFloat(ci => ci.uvBottomLeft = new Vector2(uvMarker, 0), (o, off) => o.UvBLx = off);
            findFloat(ci => ci.uvBottomLeft = new Vector2(0, uvMarker), (o, off) => o.UvBLy = off);
            findFloat(ci => ci.uvBottomRight = new Vector2(uvMarker, 0), (o, off) => o.UvBRx = off);
            findFloat(ci => ci.uvBottomRight = new Vector2(0, uvMarker), (o, off) => o.UvBRy = off);
            findFloat(ci => ci.uvTopLeft = new Vector2(uvMarker, 0), (o, off) => o.UvTLx = off);
            findFloat(ci => ci.uvTopLeft = new Vector2(0, uvMarker), (o, off) => o.UvTLy = off);
            findFloat(ci => ci.uvTopRight = new Vector2(uvMarker, 0), (o, off) => o.UvTRx = off);
            findFloat(ci => ci.uvTopRight = new Vector2(0, uvMarker), (o, off) => o.UvTRy = off);

            return offsets;
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
            if (ConfigManager.Bypass_UniverseLib_ICall)
            {
                Graphics.CopyTexture(
                    src, srcElement, srcMip, srcX, srcY, srcWidth, srcHeight,
                    dst, dstElement, dstMip, dstX, dstY);
            }
            else
            {
                ICallManager.GetICall<d_CopyTexture_Region>("UnityEngine.Graphics::CopyTexture_Region")
                    .Invoke(src.ToIl2CppPointer(), srcElement, srcMip, srcX, srcY, srcWidth, srcHeight, dst.ToIl2CppPointer(), dstElement, dstMip, dstX, dstY);
            }
            return dst;
        }
    }
}
#endif