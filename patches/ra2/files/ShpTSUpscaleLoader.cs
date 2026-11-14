#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.IO;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.SpriteLoaders;
using OpenRA.Primitives;

namespace OpenRA.Mods.RA2.Graphics
{
	/// <summary>
	/// Loads sidebar icons (*icon.shp) like ShpTS, but converts them to true colour
	/// and enlarges them 2x with smoothing, so they stay readable on high-DPI screens.
	/// Sequences using these icons must set Scale: 0.5 to keep the original on-screen size.
	/// </summary>
	public class ShpTSUpscaleLoader : ISpriteLoader
	{
		const int Factor = 2;

		sealed class UpscaledFrame : ISpriteFrame
		{
			public SpriteFrameType Type => SpriteFrameType.Bgra32;
			public Size Size { get; }
			public Size FrameSize { get; }
			public Vector2 Offset { get; }
			public byte[] Data { get; }
			public bool DisableExportPadding => false;

			public UpscaledFrame(ISpriteFrame src, uint[] palette)
			{
				Size = new Size(src.Size.Width * Factor, src.Size.Height * Factor);
				FrameSize = new Size(src.FrameSize.Width * Factor, src.FrameSize.Height * Factor);
				Offset = src.Offset * Factor;
				Data = new byte[Size.Width * Size.Height * 4];

				var sw = src.Size.Width;
				var sh = src.Size.Height;
				if (sw == 0 || sh == 0 || src.Data == null || src.Data.Length < sw * sh)
					return;

				for (var y = 0; y < Size.Height; y++)
				{
					var fy = (y + 0.5f) / Factor - 0.5f;
					var y0 = (int)MathF.Floor(fy);
					var ty = fy - y0;
					for (var x = 0; x < Size.Width; x++)
					{
						var fx = (x + 0.5f) / Factor - 0.5f;
						var x0 = (int)MathF.Floor(fx);
						var tx = fx - x0;

						float r = 0, g = 0, b = 0, a = 0;
						for (var j = 0; j < 2; j++)
						{
							for (var i = 0; i < 2; i++)
							{
								var sx = Math.Clamp(x0 + i, 0, sw - 1);
								var sy = Math.Clamp(y0 + j, 0, sh - 1);
								var idx = src.Data[sy * sw + sx];
								if (idx == 0)
									continue;

								var w = (i == 0 ? 1 - tx : tx) * (j == 0 ? 1 - ty : ty);
								var c = palette[idx];
								r += ((c >> 16) & 0xFF) * w;
								g += ((c >> 8) & 0xFF) * w;
								b += (c & 0xFF) * w;
								a += w;
							}
						}

						if (a <= 0.001f)
							continue;

						var o = (y * Size.Width + x) * 4;
						Data[o] = (byte)Math.Clamp((int)MathF.Round(b / a), 0, 255);
						Data[o + 1] = (byte)Math.Clamp((int)MathF.Round(g / a), 0, 255);
						Data[o + 2] = (byte)Math.Clamp((int)MathF.Round(r / a), 0, 255);
						Data[o + 3] = (byte)Math.Clamp((int)MathF.Round(a * 255), 0, 255);
					}
				}
			}
		}

		static uint[] palette;
		readonly ShpTSLoader inner = new();

		static uint[] LoadPalette()
		{
			if (palette != null)
				return palette;

			using var s = Game.ModData.DefaultFileSystem.Open("cameo.pal");
			var colors = new uint[256];
			for (var i = 0; i < 256; i++)
			{
				// Palette files store 6-bit VGA colour components
				var r = s.ReadUInt8() << 2;
				var g = s.ReadUInt8() << 2;
				var b = s.ReadUInt8() << 2;
				r |= r >> 6;
				g |= g >> 6;
				b |= b >> 6;
				colors[i] = 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
			}

			return palette = colors;
		}

		public bool TryParseSprite(Stream s, string filename, out ISpriteFrame[] frames, out TypeDictionary metadata)
		{
			frames = null;
			metadata = null;

			if (filename == null || !filename.EndsWith("icon.shp", StringComparison.OrdinalIgnoreCase))
				return false;

			if (!inner.TryParseSprite(s, filename, out var original, out metadata))
				return false;

			uint[] pal;
			try
			{
				pal = LoadPalette();
			}
			catch (Exception)
			{
				// No palette available: fall back to the original indexed frames
				frames = original;
				return true;
			}

			frames = new ISpriteFrame[original.Length];
			for (var i = 0; i < original.Length; i++)
				frames[i] = new UpscaledFrame(original[i], pal);

			return true;
		}
	}
}
